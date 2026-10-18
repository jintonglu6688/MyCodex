using System;

namespace MyCodex.Application.Services
{
    public sealed class ThreadCompactionCompletionPlan
    {
        public bool ShouldClearCompactionState { get; set; }

        public bool ShouldInvalidateHistoryCache { get; set; }

        public bool ShouldRefreshInputState { get; set; }

        public bool ShouldReloadHistory { get; set; }

        public bool ShouldAddChatHistoryMessage { get; set; }

        public bool ShouldAnnounceCompletion { get; set; }
    }

    public static class ThreadCompactionEventPlanner
    {
        public static bool ShouldHandleTurnCompleted(
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

        public static bool ShouldHandleFailure(bool isThreadPendingCompaction, bool isThreadActiveCompaction)
        {
            return isThreadPendingCompaction || isThreadActiveCompaction;
        }

        public static ThreadCompactionCompletionPlan BuildCompletionPlan(bool isCurrentSessionThread)
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
