using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class DeletionUiStatePlan
    {
        public DeletionUiStatePlan(
            bool shouldClearHistory,
            bool shouldMarkPendingReload,
            string primaryMessage,
            string secondaryMessage)
        {
            ShouldClearHistory = shouldClearHistory;
            ShouldMarkPendingReload = shouldMarkPendingReload;
            PrimaryMessage = primaryMessage ?? string.Empty;
            SecondaryMessage = secondaryMessage ?? string.Empty;
        }

        public bool ShouldClearHistory { get; }

        public bool ShouldMarkPendingReload { get; }

        public string PrimaryMessage { get; }

        public string SecondaryMessage { get; }

        public bool HasPrimaryMessage
        {
            get { return !string.IsNullOrWhiteSpace(PrimaryMessage); }
        }

        public bool HasSecondaryMessage
        {
            get { return !string.IsNullOrWhiteSpace(SecondaryMessage); }
        }
    }

    internal static class DeletionUiStatePlanner
    {
        internal static DeletionUiStatePlan Build(ChatSession currentSession, WorkspaceInfo currentWorkspace)
        {
            if (currentWorkspace == null)
            {
                return new DeletionUiStatePlan(true, false, string.Empty, string.Empty);
            }

            if (currentSession == null)
            {
                return new DeletionUiStatePlan(
                    true,
                    false,
                    UiLocalization.Text("MainForm.Message.NoSessionsInWorkspace", "There are no sessions in the current workspace."),
                    string.Empty);
            }

            return new DeletionUiStatePlan(
                false,
                true,
                string.Format(
                    UiLocalization.Text("MainForm.Message.SwitchedToSession.Template", "Switched to session: {0}"),
                    currentSession.Title),
                UiLocalization.Text("MainForm.Message.PressEnterToReloadFullHistory", "Press Enter to reopen full history."));
        }
    }
}
