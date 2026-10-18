using System;
using MyCodex.Application.CodingRemote;

namespace MyCodex.Application.Services
{
    public sealed class RemoteHostMutationRefreshPlan
    {
        public RemoteHostMutationRefreshPlan(
            bool reloadLocalStateForCurrentRuntime,
            string statusText,
            string beginCompactionSessionId = null,
            bool refreshHostSettings = false)
        {
            ReloadLocalStateForCurrentRuntime = reloadLocalStateForCurrentRuntime;
            StatusText = statusText;
            BeginCompactionSessionId = beginCompactionSessionId;
            RefreshHostSettings = refreshHostSettings;
        }

        public bool ReloadLocalStateForCurrentRuntime { get; }

        public string StatusText { get; }

        public string BeginCompactionSessionId { get; }

        public bool RefreshHostSettings { get; }

        public bool ShouldBeginCompaction
        {
            get { return !string.IsNullOrWhiteSpace(BeginCompactionSessionId); }
        }

        public bool HasChanges
        {
            get { return ReloadLocalStateForCurrentRuntime || RefreshHostSettings || !string.IsNullOrWhiteSpace(StatusText) || ShouldBeginCompaction; }
        }
    }

    public static class RemoteHostMutationRefreshPlanner
    {
        public static RemoteHostMutationRefreshPlan Build(RemoteHostMutation mutation)
        {
            if (mutation == null)
            {
                return new RemoteHostMutationRefreshPlan(false, null);
            }

            if (string.Equals(mutation.MutationType, RemoteHostMutationTypes.SessionCompactRequested, StringComparison.OrdinalIgnoreCase))
            {
                return new RemoteHostMutationRefreshPlan(true, mutation.StatusText, mutation.SessionId);
            }

            if (string.Equals(mutation.MutationType, RemoteHostMutationTypes.HostSettingsUpdated, StringComparison.OrdinalIgnoreCase))
            {
                return new RemoteHostMutationRefreshPlan(false, mutation.StatusText, refreshHostSettings: true);
            }

            if (string.Equals(mutation.MutationType, RemoteHostMutationTypes.WorkspaceArchived, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mutation.MutationType, RemoteHostMutationTypes.WorkspaceDeleted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mutation.MutationType, RemoteHostMutationTypes.SessionCreated, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mutation.MutationType, RemoteHostMutationTypes.SessionThreadUpdated, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mutation.MutationType, RemoteHostMutationTypes.SessionArchived, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mutation.MutationType, RemoteHostMutationTypes.SessionDeleted, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mutation.MutationType, RemoteHostMutationTypes.SessionRenamed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mutation.MutationType, RemoteHostMutationTypes.SessionForkCreated, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mutation.MutationType, RemoteHostMutationTypes.SessionSettingsUpdated, StringComparison.OrdinalIgnoreCase))
            {
                return new RemoteHostMutationRefreshPlan(true, mutation.StatusText);
            }

            return new RemoteHostMutationRefreshPlan(false, mutation.StatusText);
        }
    }
}
