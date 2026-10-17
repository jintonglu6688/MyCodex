using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.Persistence
{
    public sealed class TargetDataMigration
    {
        private readonly AppConfigService _legacyPaths;
        private readonly Action<string> _checkpoint;
        private readonly string _legacyConfigDirectory;

        public TargetDataMigration(AppConfigService legacyPaths)
            : this(legacyPaths, null)
        {
        }

        internal TargetDataMigration(AppConfigService legacyPaths, Action<string> checkpoint, string legacyConfigDirectory = null)
        {
            _legacyPaths = legacyPaths ?? throw new ArgumentNullException(nameof(legacyPaths));
            _checkpoint = checkpoint;
            if (legacyPaths.HasSeparateDataRoot) throw new ArgumentException("Migration requires the legacy shared root.", nameof(legacyPaths));
            _legacyConfigDirectory = legacyConfigDirectory ?? (legacyPaths.HasAppDataDirectoryOverride
                ? Path.Combine(legacyPaths.GetAppDataDirectory(), "legacy-config") : legacyPaths.GetLegacyConfigDirectory());
        }

        public bool HasLegacyData
        {
            get { return Directory.Exists(_legacyPaths.GetConfigDirectory()) || Directory.Exists(_legacyConfigDirectory); }
        }

        public TargetCatalogSnapshot Run()
        {
            var root = _legacyPaths.GetAppDataDirectory();
            Directory.CreateDirectory(root);
            RejectReparsePoint(root);
            // FileShare.None is a short cross-process migration lock; never continue on a partial migration.
            using (var migrationLock = new FileStream(Path.Combine(root, "target-migration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var targetsDirectory = Path.Combine(root, "targets");
                var catalogPath = Path.Combine(targetsDirectory, "catalog.json");
                if (File.Exists(catalogPath))
                {
                    var completed = Read<TargetCatalogSnapshot>(catalogPath);
                    ValidateCompleted(completed, targetsDirectory);
                    Read<SharedRelayServiceConfigSnapshot>(Path.Combine(_legacyPaths.GetConfigDirectory(), "relay-service.json"));
                    return completed;
                }
                if (Directory.Exists(targetsDirectory))
                {
                    throw new InvalidOperationException("The target directory exists without a complete migration. Preserve it and inspect the migration details before retrying.");
                }

                var staging = Path.Combine(root, ".target-migration-staging");
                var backup = Path.Combine(root, "migration-backup");
                var sharedPath = Path.Combine(_legacyPaths.GetConfigDirectory(), "relay-service.json");
                var previousHashPath = Path.Combine(staging, "prepared-service-sha256.txt");
                RejectReparsePoint(staging);
                RejectReparsePoint(previousHashPath);
                var ownsPreparedShared = File.Exists(previousHashPath) && File.Exists(sharedPath) &&
                    File.ReadAllText(previousHashPath) == HashFile(sharedPath);
                if (Directory.Exists(staging)) SafeDeleteStaging(root, staging);
                Directory.CreateDirectory(staging);
                Directory.CreateDirectory(backup);
                RejectReparsePoint(backup);
                CopyDirectory(_legacyPaths.GetConfigDirectory(), Path.Combine(backup, "config"));
                CopyDirectory(_legacyConfigDirectory, Path.Combine(backup, "legacy-config"));
                Checkpoint("backup");

                var config = ReadOrDefault<AppConfigSnapshot>(ResolveLegacyConfigFile("config.json"));
                var appState = ReadOrDefault<AppStateSnapshot>(ResolveLegacyConfigFile("app-state.json"));
                var automations = ReadOrDefault<AutomationStateSnapshot>(_legacyPaths.GetAutomationsFilePath());
                MigrationDateNormalization.Apply(appState, automations);
                var oldSshScope = config.RemoteUnixSshProfiles.Count == 0 && !string.IsNullOrWhiteSpace(config.RemoteMacSsh.HostName)
                    ? new RuntimeProfileService().GetCurrentRuntimeProfileId(new AppConfigSnapshot { CodexRuntimeSource = "RemoteUnixSsh", RemoteMacSsh = config.RemoteMacSsh }) : null;
                config = JsonAppConfigStore.NormalizeSnapshot(config);
                var remote = ReadOrDefault<RemoteServiceConfigSnapshot>(_legacyPaths.GetRemoteServiceConfigFilePath());
                if (File.Exists(_legacyPaths.GetRemoteServiceConfigFilePath()) || File.Exists(_legacyPaths.GetRemoteServiceConfigFilePath() + ".bak"))
                {
                    RemoteServiceConfigPlanner.NormalizeProfiles(remote);
                    Guid hostId;
                    if (!Guid.TryParseExact(remote.HostId, "D", out hostId) || remote.HostId != hostId.ToString("D"))
                    {
                        throw new InvalidDataException("The legacy remote identity is invalid. It must be recovered before migration.");
                    }
                    if (remote.CreatedAt == default(DateTime)) remote.CreatedAt = DateTime.UtcNow;
                }
                else
                {
                    RemoteServiceConfigPlanner.EnsureDefaults(remote, Environment.MachineName);
                    remote.RemoteEnabled = false;
                }
                if (remote.UpdatedAt == default(DateTime)) remote.UpdatedAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
                var planner = new TargetMigrationPlanner();
                var catalog = planner.CreateCatalog(config, appState);
                if (oldSshScope != null)
                {
                    catalog.Targets.Single(t => t.SshProfileId == config.GetSelectedRemoteUnixSshProfile().Id).LegacyRuntimeScopeIds.Add(oldSshScope);
                }
                var scopes = new HashSet<string>(catalog.Targets.SelectMany(t => new[] { t.RuntimeScopeId }.Concat(t.LegacyRuntimeScopeIds)), StringComparer.OrdinalIgnoreCase);
                foreach (var target in catalog.Targets)
                {
                    var inherits = target.Id == catalog.LastOpenedTargetId;
                    var paths = new AppConfigService(Path.Combine(staging, target.Id),
                        _legacyPaths.GetInstallationDirectory(), "MyCodex", root);
                    paths.EnsureConfigDirectory();
                    var preferences = inherits ? Clone(config) : planner.CreateTargetDefaults(Clone(config), target);
                    planner.ConfigureTarget(preferences, target, inherits);
                    Write(paths.GetConfigFilePath(), preferences);
                    var state = Clone(appState);
                    TargetMigrationStatePlanner.SelectState(state, target.RuntimeScopeId, scopes, target.LegacyRuntimeScopeIds);
                    Write(paths.GetAppStateFilePath(), state);
                    var jobs = Clone(automations);
                    TargetMigrationStatePlanner.SelectAutomations(jobs, target.RuntimeScopeId, scopes, target.LegacyRuntimeScopeIds);
                    Write(paths.GetAutomationsFilePath(), jobs);
                    var identity = inherits ? RemoteServiceConfigProjection.SplitTargetIdentity(remote) : new TargetRemoteIdentitySnapshot
                    {
                        HostId = Guid.NewGuid().ToString("D"), ThisPcName = target.Name, RemoteEnabled = false,
                        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
                    };
                    Write(paths.GetRemoteServiceConfigFilePath(), identity);
                    if (inherits && File.Exists(_legacyPaths.GetRemoteBindingsFilePath()))
                    {
                        File.Copy(_legacyPaths.GetRemoteBindingsFilePath(), paths.GetRemoteBindingsFilePath());
                        new JsonRemoteBindingStore(paths.GetRemoteBindingsFilePath()).ListActive(remote.TenantId, remote.HostId);
                    }
                    if (target.Kind == "Windows" && File.Exists(_legacyPaths.GetModeSettingsFilePath()))
                    {
                        File.Copy(_legacyPaths.GetModeSettingsFilePath(), paths.GetModeSettingsFilePath());
                        Read<ModeSettingsSnapshot>(paths.GetModeSettingsFilePath());
                    }
                    Checkpoint("target:" + target.Id);
                }
                var pendingState = Clone(appState);
                TargetMigrationStatePlanner.SelectState(pendingState, null, scopes);
                Write(Path.Combine(staging, "pending-state.json"), pendingState);
                var pendingJobs = Clone(automations);
                TargetMigrationStatePlanner.SelectAutomations(pendingJobs, null, scopes);
                Write(Path.Combine(staging, "pending-automations.json"), pendingJobs);
                Write(Path.Combine(staging, "catalog.json"), catalog);
                ValidateCompleted(Read<TargetCatalogSnapshot>(Path.Combine(staging, "catalog.json")), staging);
                Checkpoint("validated");

                // Keep Relay database/certificate paths in their existing shared root. Publish target layout last.
                var preparedSharedPath = Path.Combine(staging, "prepared-service.json");
                Write(preparedSharedPath, RemoteServiceConfigProjection.SplitShared(remote));
                var preparedHash = HashFile(preparedSharedPath);
                if (File.Exists(sharedPath) && !ownsPreparedShared && HashFile(sharedPath) != preparedHash)
                    throw new InvalidDataException("An unrelated shared Relay configuration already exists. It was not overwritten.");
                File.WriteAllText(Path.Combine(staging, "prepared-service-sha256.txt"), preparedHash);
                WriteAtomic(sharedPath, RemoteServiceConfigProjection.SplitShared(remote));
                Checkpoint("shared");
                File.Delete(preparedSharedPath);
                Directory.Move(staging, targetsDirectory);
                return catalog;
            }
        }

        public AppConfigService GetStartupPaths(TargetCatalogSnapshot catalog, string appDirectoryName)
        {
            var root = _legacyPaths.GetAppDataDirectory();
            ValidateCompleted(catalog, Path.Combine(root, "targets"));
            return new AppConfigService(Path.Combine(root, "targets", catalog.LastOpenedTargetId),
                _legacyPaths.GetInstallationDirectory(), appDirectoryName, root);
        }

        private static void ValidateCompleted(TargetCatalogSnapshot catalog, string directory)
        {
            if (catalog == null || catalog.SchemaVersion != 1 || catalog.Targets == null ||
                catalog.Targets.Count(t => t.Kind == "Windows" && t.Id == "windows") != 1 ||
                catalog.Targets.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Targets.Count ||
                !catalog.Targets.Any(t => t.Id == catalog.LastOpenedTargetId))
            {
                throw new InvalidDataException("The target catalog is incomplete or invalid.");
            }
            RejectReparsePoint(directory);
            foreach (var target in catalog.Targets)
            {
                if (!TargetMigrationPlanner.IsSafeTargetId(target.Id)) throw new InvalidDataException("Unsafe target identifier.");
                var configDirectory = Path.Combine(directory, target.Id, "config");
                RejectReparsePoint(Path.Combine(directory, target.Id));
                RejectReparsePoint(configDirectory);
                Read<AppConfigSnapshot>(Path.Combine(configDirectory, "config.json"));
                Read<AppStateSnapshot>(Path.Combine(configDirectory, "app-state.json"));
                Read<AutomationStateSnapshot>(Path.Combine(configDirectory, "automations.json"));
                var identity = Read<TargetRemoteIdentitySnapshot>(Path.Combine(configDirectory, "remote-service.json"));
                if (string.IsNullOrWhiteSpace(identity.HostId)) throw new InvalidDataException("Target identity is missing.");
            }
        }

        private void Checkpoint(string name) { _checkpoint?.Invoke(name); }

        private string ResolveLegacyConfigFile(string fileName)
        {
            var current = Path.Combine(_legacyPaths.GetConfigDirectory(), fileName);
            return File.Exists(current) || File.Exists(current + ".bak") ? current : Path.Combine(_legacyConfigDirectory, fileName);
        }

        private static T ReadOrDefault<T>(string path) where T : new()
        {
            if (File.Exists(path))
            {
                try { return Read<T>(path); }
                catch (Exception) when (File.Exists(path + ".bak")) { return Read<T>(path + ".bak"); }
            }
            return File.Exists(path + ".bak") ? Read<T>(path + ".bak") : new T();
        }

        private static T Read<T>(string path)
        {
            RejectReparsePoint(path);
            using (var stream = File.OpenRead(path))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
        }

        private static T Clone<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(stream, value);
                stream.Position = 0;
                return (T)serializer.ReadObject(stream);
            }
        }

        private static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            RejectReparsePoint(Path.GetDirectoryName(path));
            RejectReparsePoint(path);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                stream.Flush(true);
            }
        }

        private static void WriteAtomic<T>(string path, T value)
        {
            var temporary = path + ".migration.tmp";
            Write(temporary, value);
            Read<T>(temporary);
            RejectReparsePoint(path);
            RejectReparsePoint(path + ".migration.bak");
            if (File.Exists(path)) File.Replace(temporary, path, path + ".migration.bak");
            else File.Move(temporary, path);
        }

        private static string HashFile(string path)
        {
            RejectReparsePoint(path);
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(stream));
        }

        private static void CopyDirectory(string source, string destination)
        {
            if (!Directory.Exists(source)) return;
            RejectReparsePoint(source);
            Directory.CreateDirectory(destination);
            RejectReparsePoint(destination);
            foreach (var file in Directory.GetFiles(source))
            {
                RejectReparsePoint(file);
                var backupPath = Path.Combine(destination, Path.GetFileName(file));
                RejectReparsePoint(backupPath);
                if (!File.Exists(backupPath)) File.Copy(file, backupPath);
            }
            foreach (var child in Directory.GetDirectories(source)) CopyDirectory(child, Path.Combine(destination, Path.GetFileName(child)));
        }

        private static void RejectReparsePoint(string path)
        {
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Migration cannot follow a reparse point: " + path);
            }
        }

        private static void SafeDeleteStaging(string root, string staging)
        {
            if (!string.Equals(Path.GetFullPath(staging), Path.Combine(Path.GetFullPath(root), ".target-migration-staging"), StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Unsafe migration staging path.");
            }
            RejectReparsePoint(staging);
            ValidateTree(staging);
            Directory.Delete(staging, true);
        }

        private static void ValidateTree(string directory)
        {
            RejectReparsePoint(directory);
            foreach (var file in Directory.GetFiles(directory)) RejectReparsePoint(file);
            foreach (var child in Directory.GetDirectories(directory)) ValidateTree(child);
        }
    }
}
