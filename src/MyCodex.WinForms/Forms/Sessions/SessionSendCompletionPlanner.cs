using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionSendCompletionPlan
    {
        public SessionSendCompletionPlan(
            bool requiresThreadRecreationUiRefresh,
            string previousThreadIdToReset,
            string historyMessage,
            string statusText,
            string sessionTitle,
            string sessionPreview)
        {
            RequiresThreadRecreationUiRefresh = requiresThreadRecreationUiRefresh;
            PreviousThreadIdToReset = previousThreadIdToReset ?? string.Empty;
            HistoryMessage = historyMessage ?? string.Empty;
            StatusText = statusText ?? string.Empty;
            SessionTitle = sessionTitle ?? string.Empty;
            SessionPreview = sessionPreview ?? string.Empty;
        }

        public bool RequiresThreadRecreationUiRefresh { get; }

        public string PreviousThreadIdToReset { get; }

        public string HistoryMessage { get; }

        public string StatusText { get; }

        public string SessionTitle { get; }

        public string SessionPreview { get; }

        public bool HasSessionTitleUpdate
        {
            get { return !string.IsNullOrWhiteSpace(SessionTitle); }
        }

        internal bool ApplyLocalSessionTitleUpdate(ChatSession session)
        {
            if (session == null || !HasSessionTitleUpdate)
            {
                return false;
            }

            session.Title = SessionTitle;
            session.Preview = SessionPreview;
            return true;
        }
    }

    internal static class SessionSendCompletionPlanner
    {
        internal static SessionSendCompletionPlan Build(
            SessionSendResult sendResult,
            ChatSession session,
            WorkspaceInfo workspace,
            string text,
            string previewText)
        {
            var recreation = sendResult != null ? sendResult.RetryThreadRecreation : null;
            var repairedInvalidEncryptedContent = sendResult != null && sendResult.RepairedInvalidEncryptedContent;
            var historyMessage = string.Empty;
            var statusText = string.Empty;
            if (recreation != null)
            {
                historyMessage = UiLocalization.Text(
                    "MainForm.Message.RecreatedMissingRemoteThreadAndResent",
                    "The remote thread for current session was missing. A new thread was created automatically and sending continued.");
                statusText = UiLocalization.Text("MainForm.Status.RecreatedThreadAndResent", "Session thread was recreated and message was resent");
            }
            else if (repairedInvalidEncryptedContent)
            {
                historyMessage = UiLocalization.Text(
                    "MainForm.Message.RepairedInvalidEncryptedContentAndResent",
                    "The current session history was repaired automatically and sending continued.");
                statusText = UiLocalization.Text(
                    "MainForm.Status.RepairedInvalidEncryptedContentAndResent",
                    "Session history was repaired and message was resent");
            }

            var sessionTitle = string.Empty;
            var sessionPreview = string.Empty;
            if (SessionTitleStrategy.ShouldReplaceGeneratedTitle(session, workspace))
            {
                sessionTitle = SessionTitleStrategy.BuildTitleFromFirstMessage(text, previewText);
                if (!string.IsNullOrWhiteSpace(sessionTitle))
                {
                    sessionPreview = string.IsNullOrWhiteSpace(text)
                        ? previewText ?? string.Empty
                        : (text ?? string.Empty).Trim();
                }
            }

            return new SessionSendCompletionPlan(
                recreation != null,
                recreation != null ? recreation.PreviousThreadId : null,
                historyMessage,
                statusText,
                sessionTitle,
                sessionPreview);
        }
    }
}
