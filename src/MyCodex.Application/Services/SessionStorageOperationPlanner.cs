using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public static class SessionStorageOperationPlanner
    {
        public static List<ChatSession> SelectFileDeletionSessions(IEnumerable<ChatSession> sessions)
        {
            return (sessions ?? Enumerable.Empty<ChatSession>())
                .Where(session => LocalSessionRemovalEligibility.CanPermanentlyDeleteSession(session) &&
                    string.IsNullOrWhiteSpace(session.ThreadId))
                .ToList();
        }

        public static List<ChatSession> SelectArchivableSessions(IEnumerable<ChatSession> sessions)
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

        public static string ResolveSessionPathFromThreadSummaries(IEnumerable<AppServerThreadSummary> threads, string threadId)
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
    }
}
