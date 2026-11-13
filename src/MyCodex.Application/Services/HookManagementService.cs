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

    public sealed class HookStateChangeResult
    {
        public HookStateChangeStatus Status { get; set; }

        public bool Overridden { get; set; }

        public AppServerHooksListResult List { get; set; }
    }

    /// <summary>Lists hooks and writes reviewed trust or enablement to the user config, then re-lists.</summary>
    public sealed class HookManagementService
    {
        private readonly IAppServerHookClient _hookClient;
        private readonly IAppServerConfigClient _configClient;

        public HookManagementService(IAppServerHookClient hookClient, IAppServerConfigClient configClient)
        {
            _hookClient = hookClient ?? throw new ArgumentNullException(nameof(hookClient));
            _configClient = configClient ?? throw new ArgumentNullException(nameof(configClient));
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
            return ApplyAsync(workspacePath, threadId, reviewed, HookStateEditPlanner.BuildTrust(reviewed));
        }

        public Task<HookStateChangeResult> SetEnabledAsync(string workspacePath, string threadId, AppServerHookInfo hook, bool enabled)
        {
            return ApplyAsync(workspacePath, threadId, new[] { hook }, HookStateEditPlanner.BuildEnabled(hook, enabled));
        }

        public Task<HookStateChangeResult> RevokeAsync(string workspacePath, string threadId, AppServerHookInfo hook)
        {
            return ApplyAsync(workspacePath, threadId, new[] { hook }, HookStateEditPlanner.BuildRevoke(hook));
        }

        private async Task<HookStateChangeResult> ApplyAsync(
            string workspacePath,
            string threadId,
            IReadOnlyCollection<AppServerHookInfo> reviewed,
            List<AppServerConfigWriteEdit> edits)
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
            return new HookStateChangeResult
            {
                Status = HookStateChangeStatus.Saved,
                Overridden = write != null && write.IsOverridden,
                List = await ListAsync(workspacePath, threadId).ConfigureAwait(false)
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
