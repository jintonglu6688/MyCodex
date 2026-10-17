using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal static class LocalSubAgentThreadSyncPlanner
    {
        internal static List<AppServerThreadSummary> FilterCurrentParentSubAgents(
            IEnumerable<AppServerThreadSummary> threads,
            string parentThreadId,
            ISet<string> ignoredThreadIds)
        {
            if (threads == null || string.IsNullOrWhiteSpace(parentThreadId)) return new List<AppServerThreadSummary>();
            var indexed = threads.Where(thread => IsUserSubAgent(thread) && !string.IsNullOrWhiteSpace(thread.ThreadId) &&
                    (ignoredThreadIds == null || !ignoredThreadIds.Contains(thread.ThreadId)))
                .GroupBy(thread => thread.ThreadId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(thread => thread.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);
            return SessionDescendantCollector.Collect(new ChatSession { ThreadId = parentThreadId },
                    indexed.Values.Select(thread => new ChatSession { ThreadId = thread.ThreadId, ParentThreadId = thread.ParentThreadId }))
                .Skip(1).Select(session => indexed[session.ThreadId]).ToList();
        }

        internal static bool IsUserSubAgent(AppServerThreadSummary thread)
        {
            return thread != null && !string.Equals(thread.SubAgentSourceKind, "review", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(thread.SubAgentSourceKind, "compact", StringComparison.OrdinalIgnoreCase);
        }
    }
}
