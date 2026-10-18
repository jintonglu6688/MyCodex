using System;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionHistoryAnchorPlanner
    {
        internal static bool TryGetForkTrimTurns(
            ChatSession currentSession,
            ChatHistoryItem item,
            Func<string, bool> isUserSender,
            out int trimTurnsAfterAnchor)
        {
            trimTurnsAfterAnchor = 0;
            if (!IsValidTurnAnchor(currentSession, item))
            {
                return false;
            }

            trimTurnsAfterAnchor = item.TurnCount - item.TurnIndex;
            return trimTurnsAfterAnchor >= 0;
        }

        private static bool IsValidTurnAnchor(ChatSession currentSession, ChatHistoryItem item)
        {
            return currentSession != null &&
                   item != null &&
                   !string.IsNullOrWhiteSpace(item.ThreadId) &&
                   !string.IsNullOrWhiteSpace(currentSession.ThreadId) &&
                   string.Equals(item.ThreadId, currentSession.ThreadId, StringComparison.OrdinalIgnoreCase) &&
                   !string.IsNullOrWhiteSpace(item.TurnId) &&
                   item.TurnIndex > 0 &&
                   item.TurnCount >= item.TurnIndex;
        }
    }
}
