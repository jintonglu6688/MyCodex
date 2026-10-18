using MyCodex.Application.CodingRemote;
using MyCodex.Application.Services;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void HandleRemoteHostMutation(RemoteHostMutation mutation)
        {
            var plan = RemoteHostMutationRefreshPlanner.Build(mutation);
            if (plan == null || !plan.HasChanges)
            {
                return;
            }

            if (plan.ShouldBeginCompaction)
            {
                BeginRemoteHostCompaction(plan.BeginCompactionSessionId);
            }

            if (plan.ReloadLocalStateForCurrentRuntime)
            {
                ReloadLocalStateForCurrentRuntimeProfile();
            }

            if (plan.RefreshHostSettings)
            {
                this.BeginInvokeSafe(async () => await RefreshAfterRemoteHostSettingsUpdateAsync().ConfigureAwait(true));
            }

            if (!string.IsNullOrWhiteSpace(plan.StatusText))
            {
                SetLowPriorityStatus(plan.StatusText);
            }
        }

        private void BeginRemoteHostCompaction(string sessionId)
        {
            var session = FindSessionById(sessionId);
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return;
            }

            TryBeginThreadCompaction(session.ThreadId);
            RefreshInputState();
        }
    }
}
