using System;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ThreadCompactionCompletionPlan
    {
        public bool ShouldClearCompactionState { get; set; }

        public bool ShouldInvalidateHistoryCache { get; set; }

        public bool ShouldRefreshInputState { get; set; }

        public bool ShouldReloadHistory { get; set; }

        public bool ShouldAddChatHistoryMessage { get; set; }

        public bool ShouldAnnounceCompletion { get; set; }
    }

    internal static class ThreadCompactionEventPlanner
    {
        internal static bool ShouldHandleTurnCompleted(
            bool hasRecentCompletionTurn,
            bool isThreadPendingCompaction,
            string activeTurnId,
            string eventTurnId)
        {
            if (hasRecentCompletionTurn)
            {
                return true;
            }

            if (isThreadPendingCompaction)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(activeTurnId))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(eventTurnId) ||
                   string.Equals(activeTurnId, eventTurnId, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool ShouldHandleFailure(bool isThreadPendingCompaction, bool isThreadActiveCompaction)
        {
            return isThreadPendingCompaction || isThreadActiveCompaction;
        }

        internal static ThreadCompactionCompletionPlan BuildCompletionPlan(bool isCurrentSessionThread)
        {
            return new ThreadCompactionCompletionPlan
            {
                ShouldClearCompactionState = true,
                ShouldInvalidateHistoryCache = true,
                ShouldRefreshInputState = true,
                ShouldReloadHistory = false,
                ShouldAddChatHistoryMessage = false,
                ShouldAnnounceCompletion = isCurrentSessionThread
            };
        }
    }
}
