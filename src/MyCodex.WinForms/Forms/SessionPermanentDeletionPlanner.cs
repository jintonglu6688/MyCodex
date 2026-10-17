using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionPermanentDeletionPlan
    {
        public List<AppServerThreadSummary> Roots { get; } = new List<AppServerThreadSummary>();
        public List<AppServerThreadSummary> Threads { get; } = new List<AppServerThreadSummary>();
        public HashSet<string> MissingThreadIds { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    internal sealed class SessionPermanentDeletionResult
    {
        public List<string> DeletedThreadIds { get; } = new List<string>();
        public List<string> FailedThreadIds { get; } = new List<string>();
        public Dictionary<string, string> FailureReasons { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    internal static class SessionPermanentDeletionPlanner
    {
        internal static async Task<AppServerThreadDetails> ReadDeletionRootAsync(ChatSession session,
            IReadOnlyList<AppServerThreadSummary> descendants, Func<string, Task<AppServerThreadDetails>> read,
            Func<string, string, Task<bool>> confirmMissing)
        {
            try
            {
                var root = await read(session.ThreadId);
                if (root == null) throw new InvalidOperationException("Missing deletion root response.");
                return root;
            }
            catch (InvalidOperationException ex) when (string.Equals(ex.Message,
                "thread not loaded: " + session.ThreadId, StringComparison.Ordinal))
            {
                if (descendants.Count == 0 && confirmMissing != null &&
                    await confirmMissing(session.ThreadId, session.SessionPath)) return null;
                throw;
            }
        }

        internal static SessionPermanentDeletionPlan Build(IEnumerable<ChatSession> selected, IEnumerable<AppServerThreadSummary> knownThreads)
        {
            var byId = new Dictionary<string, AppServerThreadSummary>(StringComparer.Ordinal);
            foreach (var thread in knownThreads ?? Enumerable.Empty<AppServerThreadSummary>())
            {
                if (thread != null && !string.IsNullOrWhiteSpace(thread.ThreadId)) byId[thread.ThreadId] = thread;
            }
            var selectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var session in selected ?? Enumerable.Empty<ChatSession>())
            {
                if (session == null) continue;
                if (string.IsNullOrWhiteSpace(session.ThreadId) || !byId.ContainsKey(session.ThreadId))
                    throw new InvalidOperationException("Deletion scope is unavailable.");
                selectedIds.Add(session.ThreadId);
            }
            var plan = new SessionPermanentDeletionPlan();
            foreach (var id in selectedIds)
            {
                var visited = new HashSet<string>(StringComparer.Ordinal) { id };
                var cursor = byId[id];
                var hasSelectedAncestor = false;
                while (!string.IsNullOrWhiteSpace(cursor.ParentThreadId))
                {
                    if (!visited.Add(cursor.ParentThreadId)) throw new InvalidOperationException("Cyclic deletion scope.");
                    if (selectedIds.Contains(cursor.ParentThreadId)) hasSelectedAncestor = true;
                    if (!byId.TryGetValue(cursor.ParentThreadId, out cursor)) break;
                }
                if (!hasSelectedAncestor) plan.Roots.Add(byId[id]);
            }
            var included = new HashSet<string>(StringComparer.Ordinal);
            foreach (var root in plan.Roots)
            {
                foreach (var thread in Descendants(root, byId.Values))
                {
                    if (thread.ParentThreadIdConflict || thread.ThreadStatus == null ||
                        (thread.ThreadStatus.ActiveFlags != null && thread.ThreadStatus.ActiveFlags.Count > 0) ||
                        (thread.ThreadStatus.Type != "idle" && thread.ThreadStatus.Type != "notLoaded"))
                        throw new InvalidOperationException("Deletion scope contains an active or unknown thread.");
                    if (included.Add(thread.ThreadId)) plan.Threads.Add(thread);
                }
            }
            return plan;
        }

        internal static List<AppServerThreadSummary> Descendants(AppServerThreadSummary root, IEnumerable<AppServerThreadSummary> threads)
        {
            var children = threads.ToLookup(thread => thread.ParentThreadId ?? string.Empty, StringComparer.Ordinal);
            var result = new List<AppServerThreadSummary> { root };
            var ids = new HashSet<string>(StringComparer.Ordinal) { root.ThreadId };
            for (var index = 0; index < result.Count; index++)
            {
                foreach (var candidate in children[result[index].ThreadId])
                {
                    if (ids.Add(candidate.ThreadId))
                        result.Add(candidate);
                }
            }
            return result;
        }

        internal static void EnsureCompleteDescendants(AppServerThreadSummary root, IEnumerable<AppServerThreadSummary> discovered)
        {
            if (root == null) throw new InvalidOperationException("Missing deletion root.");
            var threads = discovered.ToList();
            if (threads.Any(thread => thread == null || string.IsNullOrWhiteSpace(thread.ThreadId)))
                throw new InvalidOperationException("Incomplete deletion scope.");
            var reachable = new HashSet<string>(Descendants(root, threads).Select(thread => thread.ThreadId), StringComparer.Ordinal);
            if (threads.Any(thread => !reachable.Contains(thread.ThreadId)))
                throw new InvalidOperationException("Unresolved descendant relationship.");
        }

        internal static bool SameScope(SessionPermanentDeletionPlan first, SessionPermanentDeletionPlan second)
        {
            return first.MissingThreadIds.SetEquals(second.MissingThreadIds) &&
                new HashSet<string>(first.Threads.Select(thread => thread.ThreadId), StringComparer.Ordinal)
                .SetEquals(second.Threads.Select(thread => thread.ThreadId)) &&
                new HashSet<string>(first.Roots.Select(thread => thread.ThreadId), StringComparer.Ordinal)
                    .SetEquals(second.Roots.Select(thread => thread.ThreadId));
        }

        internal static async Task<SessionPermanentDeletionResult> ExecuteAsync(
            SessionPermanentDeletionPlan plan, Func<string, Task> deleteThread,
            Action<IReadOnlyList<string>> confirmed, Func<bool> isCurrent, Func<string, bool> wasConfirmed = null)
        {
            var result = new SessionPermanentDeletionResult();
            foreach (var root in plan.Roots)
            {
                var affected = Descendants(root, plan.Threads).Select(thread => thread.ThreadId).ToList();
                if (!isCurrent())
                {
                    result.FailedThreadIds.AddRange(affected);
                    continue;
                }
                try
                {
                    if (!plan.MissingThreadIds.Contains(root.ThreadId)) await deleteThread(root.ThreadId);
                }
                catch (Exception ex)
                {
                    foreach (var id in affected)
                    {
                        if (isCurrent() && wasConfirmed != null && wasConfirmed(id)) result.DeletedThreadIds.Add(id);
                        else result.FailedThreadIds.Add(id);
                    }
                    if (affected.Any(result.FailedThreadIds.Contains) && !string.IsNullOrWhiteSpace(ex.Message))
                        result.FailureReasons[root.ThreadId] = ex.Message;
                    continue;
                }
                if (!isCurrent())
                {
                    result.FailedThreadIds.AddRange(affected);
                    continue;
                }
                result.DeletedThreadIds.AddRange(affected);
                confirmed(affected);
            }
            return result;
        }
    }
}
