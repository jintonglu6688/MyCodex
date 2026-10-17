using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class SessionArchiveResult
    {
        public HashSet<string> ArchivedThreadIds { get; } = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, string> Failures { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static class SessionArchiveService
    {
        public static async Task<SessionArchiveResult> ArchiveAsync(IAppServerThreadClient client,
            IEnumerable<ChatSession> sessions, CancellationToken cancellationToken, Func<bool> isCurrent = null)
        {
            var targets = sessions.Where(session => session != null && !string.IsNullOrWhiteSpace(session.ThreadId))
                .GroupBy(session => session.ThreadId, StringComparer.Ordinal).Select(group => group.First())
                .ToDictionary(session => session.ThreadId, StringComparer.Ordinal);
            var roots = targets.Values.Where(session => !HasSelectedAncestor(session, targets)).ToList();
            var result = new SessionArchiveResult();
            EnsureCurrent(cancellationToken, isCurrent);
            var archived = await client.ListArchivedThreadsAsync(null).ConfigureAwait(false);
            EnsureCurrent(cancellationToken, isCurrent);
            result.ArchivedThreadIds.UnionWith(archived.Where(thread => targets.ContainsKey(thread.ThreadId)).Select(thread => thread.ThreadId));
            foreach (var root in roots)
            {
                EnsureCurrent(cancellationToken, isCurrent);
                if (result.ArchivedThreadIds.Contains(root.ThreadId)) continue;
                try
                {
                    await client.ArchiveThreadAsync(root.ThreadId).ConfigureAwait(false);
                    EnsureCurrent(cancellationToken, isCurrent);
                    // A successful response confirms the root, not every spawned descendant.
                    result.ArchivedThreadIds.Add(root.ThreadId);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    result.Failures[root.ThreadId] = ex.Message;
                }
            }

            EnsureCurrent(cancellationToken, isCurrent);
            try
            {
                archived = await client.ListArchivedThreadsAsync(null).ConfigureAwait(false);
                EnsureCurrent(cancellationToken, isCurrent);
                result.ArchivedThreadIds.Clear();
                result.ArchivedThreadIds.UnionWith(archived.Where(thread => targets.ContainsKey(thread.ThreadId)).Select(thread => thread.ThreadId));
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                // Keep only roots confirmed by a response and threads already known archived.
                foreach (var id in targets.Keys.Where(id => !result.ArchivedThreadIds.Contains(id)))
                    if (!result.Failures.ContainsKey(id)) result.Failures[id] = ex.Message;
            }
            foreach (var id in targets.Keys)
            {
                if (result.ArchivedThreadIds.Contains(id)) result.Failures.Remove(id);
                else if (!result.Failures.ContainsKey(id)) result.Failures[id] = SharedLocalization.Text(
                    "SessionArchive.Unconfirmed", "The runtime has not confirmed that this conversation was archived. It remains in the list.");
            }
            return result;
        }

        private static bool HasSelectedAncestor(ChatSession session, IDictionary<string, ChatSession> targets)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { session.ThreadId };
            var parentId = session.ParentThreadId;
            var hasAncestor = false;
            while (!string.IsNullOrWhiteSpace(parentId) && targets.TryGetValue(parentId, out var parent))
            {
                if (!visited.Add(parentId)) throw new InvalidOperationException("Cyclic archive scope.");
                hasAncestor = true;
                parentId = parent.ParentThreadId;
            }
            return hasAncestor;
        }

        private static void EnsureCurrent(CancellationToken cancellationToken, Func<bool> isCurrent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isCurrent != null && !isCurrent()) throw new OperationCanceledException();
        }
    }
}
