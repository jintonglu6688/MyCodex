using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.Persistence
{
    public sealed partial class TargetCatalogStore
    {
        public void ValidateLocalRemoval(string id)
        {
            WithLock(() => { GetValidatedRemovalPaths(ReadCatalog(), id); return true; });
        }

        // Caller holds this target's offline instance reservation for the whole operation.
        // A retained archive is never an input to Add or migration and cannot revive its identity.
        public string RemoveLocalData(string id)
        {
            return WithLock(() =>
            {
                var catalog = ReadCatalog();
                var paths = GetValidatedRemovalPaths(catalog, id);
                var target = Find(catalog, id);
                var directory = paths.GetAppDataDirectory();
                var archive = Path.Combine(_targetsRoot, ".removed-" + Guid.NewGuid().ToString("N"));
                RejectReparsePath(archive);
                var originalCatalog = File.ReadAllBytes(_catalogPath);
                var catalogAttempted = false;
                Directory.Move(directory, archive);
                try
                {
                    _saveCheckpoint?.Invoke("removal-moved");
                    catalog.Targets.Remove(target);
                    if (catalog.LastOpenedTargetId == id) catalog.LastOpenedTargetId = "windows";
                    catalogAttempted = true;
                    WriteAtomic(_catalogPath, catalog);
                    _saveCheckpoint?.Invoke("removal-catalog");
                }
                catch (Exception failure)
                {
                    var errors = new List<Exception> { failure };
                    try { Directory.Move(archive, directory); }
                    catch (Exception restoreFailure) { errors.Add(restoreFailure); }
                    if (catalogAttempted)
                    {
                        try { WriteAtomic(_catalogPath, stream => stream.Write(originalCatalog, 0, originalCatalog.Length)); }
                        catch (Exception restoreFailure) { errors.Add(restoreFailure); }
                    }
                    if (errors.Count > 1)
                        throw new AggregateException("Target removal failed and its local data could not be fully restored. Inspect the removal archive before retrying.", errors);
                    throw;
                }

                // Publish before destructive cleanup: a failure must never resurrect a partly deleted identity.
                try
                {
                    _saveCheckpoint?.Invoke("removal-cleanup");
                    RejectReparsePath(archive);
                    ValidateOwnedTree(archive);
                    Directory.Delete(archive, true);
                    return null;
                }
                catch (IOException) { return archive; }
                catch (UnauthorizedAccessException) { return archive; }
            });
        }

        private AppConfigService GetValidatedRemovalPaths(TargetCatalogSnapshot catalog, string id)
        {
            var target = Find(catalog, id);
            if (target.Kind == "Windows") throw new InvalidOperationException("The local Windows target cannot be removed.");
            var paths = CreatePaths(target.Id);
            ValidateTargetData(paths, target);
            ValidateRemovalData(paths, paths.GetAppDataDirectory());
            return paths;
        }

        private void ValidateRemovalData(AppConfigService paths, string directory)
        {
            for (var ancestor = Path.GetDirectoryName(_sharedRoot); ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
                if (Directory.Exists(ancestor) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Target removal cannot follow a reparse point above its local data root.");
            RejectReparsePath(directory);
            ValidateOwnedTree(directory);
            foreach (var entry in Directory.GetFileSystemEntries(directory))
            {
                var name = Path.GetFileName(entry);
                if (!Directory.Exists(entry) ||
                    !(name == "config" || name == "logs" || name == "temp" || name == "automations"))
                    throw new IOException("Unrecognized files in the target data directory were preserved. Move them outside this directory before removing the target.");
            }
            var ownedConfigNames = new[] { "config.json", "app-state.json", "automations.json", "mode-settings.json", "remote-service.json", "remote-bindings.json" };
            foreach (var entry in Directory.GetFileSystemEntries(paths.GetConfigDirectory()))
            {
                var name = Path.GetFileName(entry);
                if (Directory.Exists(entry) || !ownedConfigNames.Any(owned => IsOwnedRemovalConfigFile(name, owned)))
                    throw new IOException("Unrecognized files in the target config directory were preserved. Move them outside this directory before removing the target.");
            }
            if (Directory.EnumerateFileSystemEntries(directory, ".git", SearchOption.AllDirectories).Any())
                throw new IOException("A Git repository exists inside this target's local data. Move it outside this directory before removing the target.");
            var workspace = paths.GetAutomationWorkspaceDirectory();
            if (Directory.Exists(workspace) && Directory.EnumerateFileSystemEntries(workspace).Any())
                throw new IOException("The target's automation workspace contains files. Move them outside this directory before removing the target.");
            var state = Read<AppStateSnapshot>(paths.GetAppStateFilePath());
            var roots = (state.RecentWorkspaces ?? new List<WorkspaceInfo>()).Where(w => w != null).Select(w => w.RootPath)
                .Concat((state.RuntimeProfiles ?? new List<RuntimeProfileStateSnapshot>()).Where(p => p != null)
                    .SelectMany(p => (p.RecentWorkspaces ?? new List<WorkspaceInfo>()).Where(w => w != null).Select(w => w.RootPath)))
                .Concat((state.OfficeRuntimeStates ?? new List<OfficeRuntimeStateSnapshot>()).Where(p => p != null)
                    .SelectMany(p => (p.RecentWorkspaces ?? new List<WorkspaceInfo>()).Where(w => w != null).Select(w => w.RootPath)));
            var automations = Read<AutomationStateSnapshot>(paths.GetAutomationsFilePath());
            roots = roots.Concat((automations.Definitions ?? new List<AutomationDefinitionSnapshot>()).Where(a => a?.Context != null)
                .SelectMany(a => new[] { a.Context.WorkspacePath, a.Context.OfficeRootPath }));
            var config = JsonAppConfigStore.NormalizeSnapshot(Read<AppConfigSnapshot>(paths.GetConfigFilePath()));
            roots = roots.Concat(new[] { config.GetSelectedRemoteUnixSshConfig().PrivateKeyPath });
            if (File.Exists(paths.GetModeSettingsFilePath()))
                roots = roots.Concat(new[] { Read<ModeSettingsSnapshot>(paths.GetModeSettingsFilePath()).OfficeRootPath });
            foreach (var root in roots)
            {
                // Unix paths describe remote data and must not be interpreted as local drive paths.
                if (string.IsNullOrWhiteSpace(root) ||
                    !(root.StartsWith(@"\\", StringComparison.Ordinal) || root.Length > 2 && root[1] == ':' && Path.IsPathRooted(root))) continue;
                var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var targetDirectory = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(full, targetDirectory, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(targetDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    targetDirectory.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("A user file or project/Office directory overlaps this target's local data. Its files were preserved; move the data before removing the target.");
            }
        }

        private static bool IsOwnedRemovalConfigFile(string name, string owned)
        {
            if (name == owned || name == owned + ".bak" || name == owned + ".tmp") return true;
            Guid temporaryId;
            return name.StartsWith(owned + ".", StringComparison.Ordinal) && name.EndsWith(".tmp", StringComparison.Ordinal) &&
                Guid.TryParseExact(name.Substring(owned.Length + 1, name.Length - owned.Length - 5), "N", out temporaryId);
        }
    }
}
