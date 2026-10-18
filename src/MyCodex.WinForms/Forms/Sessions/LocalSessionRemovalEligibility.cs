using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal static class LocalSessionRemovalEligibility
    {
        internal static bool HasThreadSessions(IEnumerable<ChatSession> sessions, string workspaceId)
        {
            foreach (var session in SessionCollectionQueries.GetSessionsForWorkspace(sessions, workspaceId))
            {
                if (session != null && !string.IsNullOrWhiteSpace(session.ThreadId))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool CanPermanentlyDeleteWorkspace(IEnumerable<ChatSession> sessions, string workspaceId)
        {
            foreach (var session in SessionCollectionQueries.GetSessionsForWorkspace(sessions, workspaceId))
            {
                if (CanPermanentlyDeleteSession(session))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool CanPermanentlyDeleteSession(ChatSession session)
        {
            return session != null &&
                   (!string.IsNullOrWhiteSpace(session.SessionPath) || !string.IsNullOrWhiteSpace(session.ThreadId));
        }
    }
}
