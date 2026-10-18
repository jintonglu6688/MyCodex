using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public enum ThreadAssociationStatus
    {
        Unassociated,
        AssociatedElsewhere,
        AssociatedCurrentWorkspace
    }

    public enum ThreadAssociationFilterMode
    {
        Linkable,
        All,
        Unassociated,
        AssociatedElsewhere,
        AssociatedCurrentWorkspace
    }

    public sealed class ThreadAssociationCandidate
    {
        public ThreadAssociationCandidate(AppServerThreadSummary thread, ThreadAssociationStatus status)
        {
            Thread = thread;
            Status = status;
        }

        public AppServerThreadSummary Thread { get; }

        public ThreadAssociationStatus Status { get; }

        public bool IsLinkable
        {
            get { return Status != ThreadAssociationStatus.AssociatedCurrentWorkspace; }
        }
    }

    public static class ThreadAssociationPlanner
    {
        public static List<ThreadAssociationCandidate> BuildCandidates(
            IEnumerable<AppServerThreadSummary> threads,
            IEnumerable<ChatSession> sessions,
            string targetWorkspaceId,
            ISet<string> ignoredThreadIds = null)
        {
            var candidates = new List<ThreadAssociationCandidate>();
            if (threads == null)
            {
                return candidates;
            }

            var seenThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var thread in threads)
            {
                if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId))
                {
                    continue;
                }

                if (ignoredThreadIds != null && ignoredThreadIds.Contains(thread.ThreadId))
                {
                    continue;
                }

                if (!seenThreadIds.Add(thread.ThreadId))
                {
                    continue;
                }

                candidates.Add(new ThreadAssociationCandidate(
                    thread,
                    Classify(thread.ThreadId, sessions, targetWorkspaceId)));
            }

            return candidates;
        }

        public static bool IsVisible(ThreadAssociationCandidate candidate, ThreadAssociationFilterMode filterMode)
        {
            if (candidate == null)
            {
                return false;
            }

            switch (filterMode)
            {
                case ThreadAssociationFilterMode.All:
                    return true;
                case ThreadAssociationFilterMode.Unassociated:
                    return candidate.Status == ThreadAssociationStatus.Unassociated;
                case ThreadAssociationFilterMode.AssociatedElsewhere:
                    return candidate.Status == ThreadAssociationStatus.AssociatedElsewhere;
                case ThreadAssociationFilterMode.AssociatedCurrentWorkspace:
                    return candidate.Status == ThreadAssociationStatus.AssociatedCurrentWorkspace;
                default:
                    return candidate.IsLinkable;
            }
        }

        private static ThreadAssociationStatus Classify(
            string threadId,
            IEnumerable<ChatSession> sessions,
            string targetWorkspaceId)
        {
            var hasAnyAssociation = false;
            if (sessions == null || string.IsNullOrWhiteSpace(threadId))
            {
                return ThreadAssociationStatus.Unassociated;
            }

            foreach (var session in sessions)
            {
                if (session == null ||
                    !string.Equals(session.ThreadId, threadId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                hasAnyAssociation = true;
                if (!string.IsNullOrWhiteSpace(targetWorkspaceId) &&
                    string.Equals(session.WorkspaceId, targetWorkspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    return ThreadAssociationStatus.AssociatedCurrentWorkspace;
                }
            }

            return hasAnyAssociation
                ? ThreadAssociationStatus.AssociatedElsewhere
                : ThreadAssociationStatus.Unassociated;
        }
    }
}
