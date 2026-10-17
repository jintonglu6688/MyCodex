using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionStorageOperationPlanner
    {
        internal static List<ChatSession> SelectFileDeletionSessions(IEnumerable<ChatSession> sessions)
        {
            return (sessions ?? Enumerable.Empty<ChatSession>())
                .Where(session => LocalSessionRemovalEligibility.CanPermanentlyDeleteSession(session) &&
                    string.IsNullOrWhiteSpace(session.ThreadId))
                .ToList();
        }

        internal static List<ChatSession> SelectArchivableSessions(IEnumerable<ChatSession> sessions)
        {
            var result = new List<ChatSession>();
            if (sessions == null)
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (session != null && !string.IsNullOrWhiteSpace(session.ThreadId))
                {
                    result.Add(session);
                }
            }

            return result;
        }

        internal static int CountNonNullSessions(IEnumerable<ChatSession> sessions)
        {
            if (sessions == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var session in sessions)
            {
                if (session != null)
                {
                    count++;
                }
            }

            return count;
        }

        internal static string ResolveSessionPathFromThreadSummaries(IEnumerable<AppServerThreadSummary> threads, string threadId)
        {
            if (threads == null || string.IsNullOrWhiteSpace(threadId))
            {
                return null;
            }

            foreach (var thread in threads)
            {
                if (thread != null &&
                    string.Equals(thread.ThreadId, threadId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(thread.SessionPath))
                {
                    return thread.SessionPath;
                }
            }

            return null;
        }

        internal static string ResolveSessionPathFromThreadDetails(AppServerThreadDetails details)
        {
            return details == null || string.IsNullOrWhiteSpace(details.SessionPath)
                ? null
                : details.SessionPath;
        }

        internal static bool ShouldIgnoreThreadReadInvalidOperation(InvalidOperationException exception)
        {
            if (exception == null || string.IsNullOrWhiteSpace(exception.Message))
            {
                return false;
            }

            return exception.Message.IndexOf("materialized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   exception.Message.IndexOf("includeTurns", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   exception.Message.IndexOf("not loaded", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   exception.Message.IndexOf("failed to locate rollout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   exception.Message.IndexOf("no rollout found", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
