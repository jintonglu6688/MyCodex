using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    public enum HookStateChangeStatus
    {
        Saved,
        Changed,
        VersionUnavailable,
        NothingToWrite
    }

    public enum HookRemoveStatus
    {
        Removed,
        Changed,
        RemovedStateNotUpdated
    }

    public sealed class HookStateChangeResult
    {
        public HookStateChangeStatus Status { get; set; }

        public bool Overridden { get; set; }

        /// <summary>Saved, yet the runtime still reports the old state, as it does for built-in cleanup hooks.</summary>
        public bool Ignored { get; set; }

        public AppServerHooksListResult List { get; set; }
    }

    /// <summary>Lists hooks and writes reviewed trust or enablement to the user config, then re-lists.</summary>
    public sealed class HookManagementService
    {
        private readonly IAppServerHookClient _hookClient;
        private readonly IAppServerConfigClient _configClient;
        private readonly IAppServerFileSystemClient _fileClient;

        public HookManagementService(
            IAppServerHookClient hookClient,
            IAppServerConfigClient configClient,
            IAppServerFileSystemClient fileClient = null)
        {
            _hookClient = hookClient ?? throw new ArgumentNullException(nameof(hookClient));
            _configClient = configClient ?? throw new ArgumentNullException(nameof(configClient));
            _fileClient = fileClient;
        }

        public bool CanEditFiles
        {
            get { return _fileClient != null; }
        }

        /// <summary>The user hooks.json sits next to the user config.toml, in the target's CODEX_HOME.</summary>
        public async Task<string> GetUserHooksPathAsync(string workspacePath)
        {
            var config = await _configClient.ReadConfigAsync(workspacePath, true).ConfigureAwait(false);
            var userLayer = config == null ? null : config.Layers.FirstOrDefault(layer =>
                string.Equals(layer.SourceType, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(layer.FilePath));
            return userLayer == null ? null : HookSourceFileEditor.GetSiblingPath(userLayer.FilePath, "hooks.json");
        }

        public async Task<byte[]> ReadSourceAsync(string path)
        {
            var result = await RequireFileClient().ReadFileAsync(path).ConfigureAwait(false);
            return string.IsNullOrEmpty(result == null ? null : result.DataBase64) ? new byte[0] : Convert.FromBase64String(result.DataBase64);
        }

        public Task WriteSourceAsync(string path, byte[] data)
        {
            return RequireFileClient().WriteFileAsync(path, data);
        }

        // shortcut: read-modify-write without a file version check; fs/writeFile has no expectedVersion, add one if a bridge gains it.
        public async Task AddHookAsync(string path, string eventName, string matcher, Dictionary<string, object> handler)
        {
            var original = await ReadExistingOrNullAsync(path).ConfigureAwait(false);
            var updated = HookSourceFileEditor.AddHandler(original, eventName, matcher, handler);
            if (original == null)
            {
                await RequireFileClient().CreateDirectoryAsync(HookSourceFileEditor.GetSiblingPath(path, string.Empty).TrimEnd('/', '\\'), true).ConfigureAwait(false);
            }

            await RequireFileClient().WriteFileAsync(path, updated).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes the definition, then moves the state of later handlers of that event onto their new positions,
        /// since hook state is keyed by position. Changed when the file no longer holds the listed definition.
        /// </summary>
        public async Task<HookRemoveStatus> RemoveHookAsync(string workspacePath, AppServerHookInfo hook, IEnumerable<AppServerHookInfo> listed)
        {
            if (!HookSourceFileEditor.CanRemove(hook))
            {
                return HookRemoveStatus.Changed;
            }

            var updated = HookSourceFileEditor.RemoveHandler(await ReadSourceAsync(hook.SourcePath).ConfigureAwait(false), hook);
            if (updated == null)
            {
                return HookRemoveStatus.Changed;
            }

            await RequireFileClient().WriteFileAsync(hook.SourcePath, updated).ConfigureAwait(false);
            var edits = HookStateEditPlanner.BuildRemapAfterRemoval(hook, listed);
            var config = await _configClient.ReadConfigAsync(workspacePath, true).ConfigureAwait(false);
            var guard = AppServerConfigWriteGuard.EvaluateUserConfig(config, edits);
            if (!guard.CanWrite)
            {
                return HookRemoveStatus.RemovedStateNotUpdated;
            }

            await _hookClient.WriteHookStateAsync(edits, guard.ExpectedVersion).ConfigureAwait(false);
            return HookRemoveStatus.Removed;
        }

        private async Task<byte[]> ReadExistingOrNullAsync(string path)
        {
            try
            {
                return await ReadSourceAsync(path).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Only a file its folder does not list starts a new one; an existing file that cannot be read is never
                // replaced. A folder that cannot be listed counts as missing, and creating it then reports any real error.
                List<AppServerFileSystemEntry> entries = null;
                try
                {
                    entries = await RequireFileClient().ReadDirectoryAsync(HookSourceFileEditor.GetSiblingPath(path, string.Empty)).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }

                var fileName = HookSourceFileEditor.GetFileName(path);
                if (entries != null && entries.Any(entry => string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    throw;
                }

                return null;
            }
        }

        private IAppServerFileSystemClient RequireFileClient()
        {
            return _fileClient ?? throw new InvalidOperationException("Hook source files are unavailable without a file system client.");
        }

        public async Task<AppServerHooksListResult> ListAsync(string workspacePath, string threadId)
        {
            if (!string.IsNullOrWhiteSpace(threadId))
            {
                try
                {
                    var threadView = await _hookClient.ListHooksAsync(null, threadId).ConfigureAwait(false);
                    // Older runtimes ignore threadId and answer for their own cwd, so the cwd must match.
                    if (threadView != null && SamePath(threadView.Cwd, workspacePath))
                    {
                        threadView.IsThreadView = true;
                        return threadView;
                    }
                }
                catch (Exception)
                {
                    // An unloaded thread is rejected by the server; the workspace view still answers.
                }
            }

            return await _hookClient.ListHooksAsync(workspacePath, null).ConfigureAwait(false);
        }

        public Task<HookStateChangeResult> TrustAsync(string workspacePath, string threadId, IReadOnlyCollection<AppServerHookInfo> reviewed)
        {
            return ApplyAsync(workspacePath, threadId, reviewed, HookStateEditPlanner.BuildTrust(reviewed),
                hook => string.Equals(hook.TrustStatus, "trusted", StringComparison.Ordinal));
        }

        public Task<HookStateChangeResult> SetEnabledAsync(string workspacePath, string threadId, AppServerHookInfo hook, bool enabled)
        {
            return ApplyAsync(workspacePath, threadId, new[] { hook }, HookStateEditPlanner.BuildEnabled(hook, enabled),
                current => current.Enabled == enabled);
        }

        public Task<HookStateChangeResult> RevokeAsync(string workspacePath, string threadId, AppServerHookInfo hook)
        {
            return ApplyAsync(workspacePath, threadId, new[] { hook }, HookStateEditPlanner.BuildRevoke(hook),
                current => !string.Equals(current.TrustStatus, "trusted", StringComparison.Ordinal));
        }

        private async Task<HookStateChangeResult> ApplyAsync(
            string workspacePath,
            string threadId,
            IReadOnlyCollection<AppServerHookInfo> reviewed,
            List<AppServerConfigWriteEdit> edits,
            Func<AppServerHookInfo, bool> applied)
        {
            if (edits.Count == 0)
            {
                return new HookStateChangeResult { Status = HookStateChangeStatus.NothingToWrite };
            }

            var current = await ListAsync(workspacePath, threadId).ConfigureAwait(false);
            if (!StillMatches(reviewed, current))
            {
                return new HookStateChangeResult { Status = HookStateChangeStatus.Changed, List = current };
            }

            var config = await _configClient.ReadConfigAsync(workspacePath, true).ConfigureAwait(false);
            var guard = AppServerConfigWriteGuard.EvaluateUserConfig(config, edits);
            if (!guard.CanWrite)
            {
                return new HookStateChangeResult { Status = HookStateChangeStatus.VersionUnavailable, List = current };
            }

            var write = await _hookClient.WriteHookStateAsync(edits, guard.ExpectedVersion).ConfigureAwait(false);
            var after = await ListAsync(workspacePath, threadId).ConfigureAwait(false);
            return new HookStateChangeResult
            {
                Status = HookStateChangeStatus.Saved,
                Overridden = write != null && write.IsOverridden,
                Ignored = after != null && after.Hooks.Any(hook =>
                    reviewed.Any(seen => string.Equals(seen.Key, hook.Key, StringComparison.Ordinal)) && !applied(hook)),
                List = after
            };
        }

        /// <summary>A write only approves the exact definitions the user reviewed.</summary>
        private static bool StillMatches(IEnumerable<AppServerHookInfo> reviewed, AppServerHooksListResult current)
        {
            var hooks = current == null ? new List<AppServerHookInfo>() : current.Hooks;
            return reviewed.All(seen => hooks.Any(hook =>
                string.Equals(hook.Key, seen.Key, StringComparison.Ordinal) &&
                string.Equals(hook.CurrentHash, seen.CurrentHash, StringComparison.Ordinal)));
        }

        private static bool SamePath(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
                string.Equals(
                    WorkspacePathUtilities.NormalizeLocalOrUnixPath(left).TrimEnd('\\', '/'),
                    WorkspacePathUtilities.NormalizeLocalOrUnixPath(right).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
