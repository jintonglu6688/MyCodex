using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Constants;

namespace MyCodex.Infrastructure.Persistence
{
    public sealed partial class TargetCatalogStore
    {
        private readonly string _sharedRoot;
        private readonly string _installationRoot;
        private readonly string _appDirectoryName;
        private readonly string _targetsRoot;
        private readonly string _catalogPath;
        private readonly string _mutexName;
        private readonly Action<string> _saveCheckpoint;
        private readonly TargetRuntimeSettingsPlanner _planner = new TargetRuntimeSettingsPlanner();

        public TargetCatalogStore(AppConfigService paths, string appDirectoryName = "MyCodex")
            : this((paths ?? throw new ArgumentNullException(nameof(paths))).GetSharedDataDirectory(),
                paths.GetInstallationDirectory(), appDirectoryName)
        {
        }

        public TargetCatalogStore(string sharedRoot, string installationRoot, string appDirectoryName)
            : this(sharedRoot, installationRoot, appDirectoryName, null)
        {
        }

        internal TargetCatalogStore(string sharedRoot, string installationRoot, string appDirectoryName, Action<string> saveCheckpoint)
        {
            _sharedRoot = Path.GetFullPath(sharedRoot ?? throw new ArgumentNullException(nameof(sharedRoot)));
            _installationRoot = Path.GetFullPath(installationRoot ?? throw new ArgumentNullException(nameof(installationRoot)));
            _appDirectoryName = appDirectoryName;
            _saveCheckpoint = saveCheckpoint;
            _targetsRoot = Path.Combine(_sharedRoot, "targets");
            _catalogPath = Path.Combine(_targetsRoot, "catalog.json");
            using (var hash = SHA256.Create())
                _mutexName = @"Local\MyCodex.TargetCatalog." + BitConverter.ToString(
                    hash.ComputeHash(Encoding.UTF8.GetBytes(_catalogPath.ToUpperInvariant()))).Replace("-", string.Empty);
        }

        public TargetCatalogSnapshot Load()
        {
            return WithLock(ReadCatalog);
        }

        public string GetSharedRoot()
        {
            return _sharedRoot;
        }

        public TargetProfileSnapshot GetTarget(string id)
        {
            return WithLock(() => Find(ReadCatalog(), id));
        }

        public AppConfigService GetPaths(string id)
        {
            return WithLock(() =>
            {
                var target = Find(ReadCatalog(), id);
                var paths = CreatePaths(target.Id);
                ValidateTargetData(paths, target);
                return paths;
            });
        }

        public TargetRuntimeSettings LoadRuntimeSettings(string id)
        {
            return WithLock(() =>
            {
                var target = Find(ReadCatalog(), id);
                var paths = CreatePaths(target.Id);
                ValidateTargetData(paths, target);
                return _planner.Build(target, new JsonAppConfigStore(paths).Load());
            });
        }

        public void RememberOpened(string id)
        {
            WithLock(() =>
            {
                var catalog = ReadCatalog();
                Find(catalog, id);
                catalog.LastOpenedTargetId = id;
                WriteAtomic(_catalogPath, catalog);
                return true;
            });
        }

        public void ResetLastOpenedAfterFailure(string id)
        {
            WithLock(() =>
            {
                var catalog = ReadCatalog();
                if (id == "windows" || !string.Equals(catalog.LastOpenedTargetId, id, StringComparison.Ordinal)) return false;
                catalog.LastOpenedTargetId = "windows";
                WriteAtomic(_catalogPath, catalog);
                return true;
            });
        }

        public TargetProfileSnapshot AddWsl(string name, WslAppServerConfigSnapshot wsl)
        {
            TargetRuntimeSettingsPlanner.ValidateName(name);
            if (wsl == null) throw new ArgumentNullException(nameof(wsl));
            var distribution = wsl.DistributionName;
            if (distribution.Any(char.IsControl)) throw new ArgumentException("Invalid WSL distribution name.", nameof(wsl));
            return WithLock(() =>
            {
                var catalog = ReadCatalog();
                var existing = catalog.Targets.FirstOrDefault(t => t.Kind == "Wsl" &&
                    string.Equals(t.WslDistributionName, distribution, StringComparison.OrdinalIgnoreCase));
                if (existing != null) throw new DuplicateTargetException(existing.Id);
                var scope = "wsl|" + distribution.ToLowerInvariant();
                var target = new TargetProfileSnapshot
                {
                    Id = TargetMigrationPlanner.GetTargetId("Wsl", scope), Kind = "Wsl", Name = name.Trim(),
                    RuntimeScopeId = scope, WslDistributionName = distribution
                };
                var settings = new TargetRuntimeSettings
                {
                    Name = name, CodexRuntimeSource = CodexRuntimeSources.Wsl, WslAppServer = wsl
                };
                return Add(catalog, target, settings);
            });
        }

        public TargetProfileSnapshot AddSsh(string name, RemoteMacSshConfigSnapshot ssh)
        {
            TargetRuntimeSettingsPlanner.ValidateName(name);
            TargetRuntimeSettingsPlanner.ValidateSsh(ssh);
            ValidateProtectedPassword(ssh);
            return WithLock(() =>
            {
                var catalog = ReadCatalog();
                ThrowIfDuplicateSsh(catalog, null, ssh);
                var profileId = Guid.NewGuid().ToString("N");
                var scope = "remote-unix|" + profileId;
                var target = new TargetProfileSnapshot
                {
                    Id = TargetMigrationPlanner.GetTargetId("Ssh", scope), Kind = "Ssh", Name = name.Trim(),
                    RuntimeScopeId = scope, SshProfileId = profileId
                };
                return Add(catalog, target, new TargetRuntimeSettings
                {
                    Name = name, CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh, RemoteSsh = ssh
                });
            });
        }

        // Caller must hold this target's instance reservation or execute inside its live IPC owner.
        // The catalog mutex only protects metadata; it cannot prove a target process is absent.
        // SSH host changes require the offline reservation, never a live IPC owner.
        public TargetProfileSnapshot SaveRuntimeSettings(string id, TargetRuntimeSettings settings)
        {
            return WithLock(() =>
            {
                var catalog = ReadCatalog();
                var target = Find(catalog, id);
                var paths = CreatePaths(id);
                ValidateTargetData(paths, target);
                var configStore = new JsonAppConfigStore(paths);
                var config = configStore.Load();
                _planner.ValidateRevision(target, config, settings, true);
                _planner.Apply(target, config, settings);
                if (target.Kind == "Ssh")
                {
                    ThrowIfDuplicateSsh(catalog, id, settings.RemoteSsh);
                    ValidateProtectedPassword(settings.RemoteSsh);
                }
                RejectReparsePath(paths.GetConfigFilePath() + ".tmp");
                RejectReparsePath(paths.GetConfigFilePath() + ".bak");
                var identityStore = new JsonRemoteServiceConfigStore(paths);
                return identityStore.WithTargetIdentityLock(() => SaveTransaction(catalog, target, paths, configStore, config, identityStore, settings));
            });
        }

        private TargetProfileSnapshot SaveTransaction(TargetCatalogSnapshot catalog, TargetProfileSnapshot target,
            AppConfigService paths, JsonAppConfigStore configStore, AppConfigSnapshot config,
            JsonRemoteServiceConfigStore identityStore, TargetRuntimeSettings settings)
        {
            var files = new[] { paths.GetConfigFilePath(), paths.GetRemoteServiceConfigFilePath(), _catalogPath };
            foreach (var path in files) { RejectReparsePath(path); RejectReparsePath(path + ".bak"); }
            var originals = files.Select(File.ReadAllBytes).ToArray();
            var attempted = 0;
            try
            {
                attempted = 1;
                configStore.Save(config);
                _saveCheckpoint?.Invoke("config");
                var identity = identityStore.LoadTargetIdentity();
                identity.ThisPcName = settings.Name.Trim();
                attempted = 2;
                identityStore.SaveTargetIdentity(identity);
                _saveCheckpoint?.Invoke("identity");
                target.Name = settings.Name.Trim();
                attempted = 3;
                WriteAtomic(_catalogPath, catalog);
                _saveCheckpoint?.Invoke("catalog");
                return target;
            }
            catch (Exception failure)
            {
                var errors = new List<Exception> { failure };
                for (var index = attempted - 1; index >= 0; index--)
                {
                    try
                    {
                        var bytes = originals[index];
                        WriteAtomic(files[index], stream => stream.Write(bytes, 0, bytes.Length));
                    }
                    catch (Exception rollbackFailure) { errors.Add(rollbackFailure); }
                }
                if (errors.Count > 1)
                    throw new AggregateException("The settings save failed and could not be fully restored. Inspect this target before retrying.", errors);
                throw;
            }
        }

        private TargetProfileSnapshot Add(TargetCatalogSnapshot catalog, TargetProfileSnapshot target, TargetRuntimeSettings settings)
        {
            var destination = Path.Combine(_targetsRoot, target.Id);
            RejectReparsePath(destination);
            if (Directory.Exists(destination))
                throw new IOException("A target data directory already exists without a catalog entry. It was preserved.");
            var windowsTarget = Find(catalog, "windows");
            var windowsPaths = CreatePaths(windowsTarget.Id);
            ValidateTargetData(windowsPaths, windowsTarget);
            var windowsConfig = new JsonAppConfigStore(windowsPaths).Load();
            var staging = Path.Combine(_targetsRoot, ".adding-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var moved = false;
            try
            {
                var paths = new AppConfigService(staging, _installationRoot, _appDirectoryName, _sharedRoot);
                var config = new AppConfigSnapshot
                {
                    MainWindowHotKeyEnabled = false,
                    EnableAutomaticChatReading = false,
                    ScreenReaderIndependentChannelName = windowsConfig.ScreenReaderIndependentChannelName
                };
                if (target.Kind == "Ssh")
                {
                    config.RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                    {
                        new RemoteUnixSshProfileConfigSnapshot { Id = target.SshProfileId, Name = target.Name, Ssh = settings.RemoteSsh }
                    };
                    config.SelectedRemoteUnixSshProfileId = target.SshProfileId;
                }
                _planner.Apply(target, config, settings);
                new JsonAppConfigStore(paths).Save(config);
                WriteAtomic(paths.GetAppStateFilePath(), new AppStateSnapshot());
                WriteAtomic(paths.GetAutomationsFilePath(), new AutomationStateSnapshot());
                WriteAtomic(paths.GetRemoteServiceConfigFilePath(), new TargetRemoteIdentitySnapshot
                {
                    HostId = Guid.NewGuid().ToString("D"), ThisPcName = target.Name, RemoteEnabled = false,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                });
                ValidateTargetData(paths, target);
                // Complete local data appears before its catalog entry. No remote home or runtime is touched.
                Directory.Move(staging, destination);
                moved = true;
                catalog.Targets.Add(target);
                WriteAtomic(_catalogPath, catalog);
                return target;
            }
            finally
            {
                // After publication failure retain the complete orphan for explicit recovery, never overwrite it.
                if (!moved && Directory.Exists(staging)) DeleteOwnedStaging(staging);
            }
        }

        private static void ValidateProtectedPassword(RemoteMacSshConfigSnapshot ssh)
        {
            if (!string.IsNullOrWhiteSpace(ssh.ProtectedPassword)) RemoteSshPasswordProtector.Unprotect(ssh.ProtectedPassword);
        }

        private void ThrowIfDuplicateSsh(TargetCatalogSnapshot catalog, string ownId, RemoteMacSshConfigSnapshot requested)
        {
            foreach (var candidate in catalog.Targets.Where(t => t.Kind == "Ssh" && t.Id != ownId))
            {
                var paths = CreatePaths(candidate.Id);
                ValidateTargetData(paths, candidate);
                var saved = _planner.Build(candidate, new JsonAppConfigStore(paths).Load());
                if (TargetRuntimeSettingsPlanner.IsSameSshEndpoint(saved.RemoteSsh, requested))
                    throw new DuplicateTargetException(candidate.Id);
            }
        }

        private TargetCatalogSnapshot ReadCatalog()
        {
            // A completed migration must retain the shared service; never regenerate credentials here.
            Read<SharedRelayServiceConfigSnapshot>(Path.Combine(_sharedRoot, "config", "relay-service.json"));
            return Read<TargetCatalogSnapshot>(_catalogPath, ValidateCatalog);
        }

        private static void ValidateCatalog(TargetCatalogSnapshot catalog)
        {
            if (catalog.SchemaVersion != 1 || catalog.Targets == null || catalog.Targets.Any(t => t == null) ||
                catalog.Targets.Count(t => t.Kind == "Windows") != 1 ||
                catalog.Targets.Any(t => !TargetMigrationPlanner.IsSafeTargetId(t.Id) ||
                    (t.Kind != "Windows" && t.Kind != "Wsl" && t.Kind != "Ssh") ||
                    (t.Kind == "Windows" && (t.Id != "windows" || t.RuntimeScopeId != "windows")) ||
                    string.IsNullOrWhiteSpace(t.RuntimeScopeId) || string.IsNullOrWhiteSpace(t.Name)) ||
                catalog.Targets.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Targets.Count ||
                catalog.Targets.Select(t => t.RuntimeScopeId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Targets.Count ||
                !catalog.Targets.Any(t => t.Id == catalog.LastOpenedTargetId))
                throw new InvalidDataException("The target catalog is invalid. No fallback target was selected.");
            foreach (var target in catalog.Targets)
            {
                if (target.Kind == "Wsl" && (string.IsNullOrWhiteSpace(target.WslDistributionName) ||
                    !string.Equals(target.RuntimeScopeId, "wsl|" + target.WslDistributionName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("The WSL target identity is invalid.");
                if (target.Kind == "Ssh" && !string.IsNullOrWhiteSpace(target.SshProfileId) &&
                    !string.Equals(target.RuntimeScopeId, "remote-unix|" + target.SshProfileId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The SSH target identity is invalid.");
            }
        }

        private static TargetProfileSnapshot Find(TargetCatalogSnapshot catalog, string id)
        {
            if (!TargetMigrationPlanner.IsSafeTargetId(id)) throw new ArgumentException("Unsafe target identifier.", nameof(id));
            return catalog.Targets.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException("The selected target does not exist.");
        }

        private AppConfigService CreatePaths(string id)
        {
            var path = Path.Combine(_targetsRoot, id);
            RejectReparsePath(path);
            return new AppConfigService(path, _installationRoot, _appDirectoryName, _sharedRoot);
        }

        private void ValidateTargetData(AppConfigService paths, TargetProfileSnapshot target)
        {
            Read<AppConfigSnapshot>(paths.GetConfigFilePath(), config =>
            {
                config = JsonAppConfigStore.NormalizeSnapshot(config);
                if (!string.Equals(new RuntimeProfileService().GetCurrentRuntimeProfileId(config), target.RuntimeScopeId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The target runtime settings do not match its identity.");
                _planner.Build(target, config);
            });
            Read<AppStateSnapshot>(paths.GetAppStateFilePath());
            Read<AutomationStateSnapshot>(paths.GetAutomationsFilePath());
            Read<TargetRemoteIdentitySnapshot>(paths.GetRemoteServiceConfigFilePath(), identity =>
            {
                Guid hostId;
                if (!Guid.TryParseExact(identity.HostId, "D", out hostId) || identity.HostId != hostId.ToString("D"))
                    throw new InvalidDataException("The target remote identity is invalid.");
            });
        }

        private T Read<T>(string path, Action<T> validate = null) where T : class
        {
            RejectReparsePath(path);
            RejectReparsePath(path + ".bak");
            return PersistenceFileRecovery.Load(
                path,
                path + ".bak",
                candidate =>
                {
                    RejectReparsePath(candidate);
                    using (var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    {
                        var value = new DataContractJsonSerializer(typeof(T)).ReadObject(stream) as T ?? throw new InvalidDataException("Empty target data.");
                        validate?.Invoke(value);
                        return value;
                    }
                },
                () => throw new FileNotFoundException("Required target data is missing.", path),
                () => { }); // Each mutation rereads its required data before writing.
        }

        private void WriteAtomic<T>(string path, T value)
        {
            WriteAtomic(path, stream => new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value));
        }

        private void WriteAtomic(string path, Action<Stream> write)
        {
            RejectReparsePath(path);
            RejectReparsePath(path + ".bak");
            AtomicFileWriter.Write(path, write, path + ".bak");
        }

        private void RejectReparsePath(string path)
        {
            var full = Path.GetFullPath(path);
            if (!string.Equals(full, _sharedRoot, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(_sharedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Target data path is outside the shared root.");
            var current = full;
            while (current != null)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Target data cannot follow reparse points.");
                if (string.Equals(current, _sharedRoot, StringComparison.OrdinalIgnoreCase)) break;
                current = Path.GetDirectoryName(current);
            }
        }

        private void DeleteOwnedStaging(string staging)
        {
            if (!string.Equals(Path.GetDirectoryName(staging), _targetsRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(staging).StartsWith(".adding-", StringComparison.Ordinal)) throw new IOException("Unsafe staging cleanup path.");
            RejectReparsePath(staging);
            ValidateOwnedTree(staging);
            Directory.Delete(staging, true);
        }

        private void ValidateOwnedTree(string directory)
        {
            foreach (var child in Directory.GetFileSystemEntries(directory))
            {
                RejectReparsePath(child);
                if (Directory.Exists(child)) ValidateOwnedTree(child);
            }
        }

        private T WithLock<T>(Func<T> action)
        {
            using (var mutex = new Mutex(false, _mutexName))
            {
                var taken = false;
                try
                {
                    try { taken = mutex.WaitOne(TimeSpan.FromSeconds(15)); }
                    catch (AbandonedMutexException) { taken = true; }
                    if (!taken) throw new TimeoutException("The target catalog is currently in use.");
                    RejectReparsePath(_targetsRoot);
                    return action();
                }
                finally { if (taken) mutex.ReleaseMutex(); }
            }
        }
    }

    public sealed class DuplicateTargetException : InvalidOperationException
    {
        public DuplicateTargetException(string existingTargetId)
            : base("This environment already has a configured target.")
        {
            ExistingTargetId = existingTargetId;
        }

        public string ExistingTargetId { get; }
    }
}
