using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public static class SessionDescendantCollector
    {
        public static List<ChatSession> Collect(ChatSession root, IEnumerable<ChatSession> candidates)
        {
            var descendants = new List<ChatSession>();
            if (root == null)
            {
                return descendants;
            }

            descendants.Add(root);
            if (candidates == null || string.IsNullOrWhiteSpace(root.ThreadId))
            {
                return descendants;
            }

            List<ChatSession> indexedCandidates;
            var childrenByParentThreadId = IndexChildren(candidates, out indexedCandidates);
            var currentPass = new SortedSet<int>();
            var nextPass = new SortedSet<int>();
            var discoveredThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var discoveredSessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var discoveredBlankIdThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var discoveredSessions = new HashSet<ChatSession>();

            discoveredSessions.Add(root);
            if (!string.IsNullOrWhiteSpace(root.Id))
            {
                discoveredSessionIds.Add(root.Id);
            }
            else
            {
                discoveredBlankIdThreadIds.Add(root.ThreadId);
            }

            discoveredThreadIds.Add(root.ThreadId);
            ScheduleChildren(root.ThreadId, -1, childrenByParentThreadId, currentPass, nextPass);

            while (currentPass.Count > 0 || nextPass.Count > 0)
            {
                if (currentPass.Count == 0)
                {
                    currentPass = nextPass;
                    nextPass = new SortedSet<int>();
                }

                var candidateIndex = currentPass.Min;
                currentPass.Remove(candidateIndex);
                var child = indexedCandidates[candidateIndex];
                if (!TryAddSession(child, discoveredSessionIds, discoveredBlankIdThreadIds, discoveredSessions))
                {
                    continue;
                }

                descendants.Add(child);
                if (!string.IsNullOrWhiteSpace(child.ThreadId) && discoveredThreadIds.Add(child.ThreadId))
                {
                    ScheduleChildren(
                        child.ThreadId,
                        candidateIndex,
                        childrenByParentThreadId,
                        currentPass,
                        nextPass);
                }
            }

            return descendants;
        }

        private static Dictionary<string, List<int>> IndexChildren(
            IEnumerable<ChatSession> candidates,
            out List<ChatSession> indexedCandidates)
        {
            indexedCandidates = new List<ChatSession>();
            var childrenByParentThreadId = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                var candidateIndex = indexedCandidates.Count;
                indexedCandidates.Add(candidate);
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.ParentThreadId))
                {
                    continue;
                }

                List<int> children;
                if (!childrenByParentThreadId.TryGetValue(candidate.ParentThreadId, out children))
                {
                    children = new List<int>();
                    childrenByParentThreadId.Add(candidate.ParentThreadId, children);
                }

                children.Add(candidateIndex);
            }

            return childrenByParentThreadId;
        }

        private static void ScheduleChildren(
            string parentThreadId,
            int currentIndex,
            IDictionary<string, List<int>> childrenByParentThreadId,
            ISet<int> currentPass,
            ISet<int> nextPass)
        {
            List<int> children;
            if (!childrenByParentThreadId.TryGetValue(parentThreadId, out children))
            {
                return;
            }

            foreach (var childIndex in children)
            {
                (childIndex > currentIndex ? currentPass : nextPass).Add(childIndex);
            }
        }

        private static bool TryAddSession(
            ChatSession session,
            ISet<string> discoveredSessionIds,
            ISet<string> discoveredBlankIdThreadIds,
            ISet<ChatSession> discoveredSessions)
        {
            if (!discoveredSessions.Add(session))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(session.Id))
            {
                return discoveredSessionIds.Add(session.Id);
            }

            return string.IsNullOrWhiteSpace(session.ThreadId) || discoveredBlankIdThreadIds.Add(session.ThreadId);
        }
    }
}
