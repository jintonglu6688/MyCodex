using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Domain.Enums;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public sealed class SessionListEntry
    {
        public SessionListEntry(ChatSession session, IReadOnlyList<ChatSession> subAgents)
        {
            Session = session;
            SubAgents = subAgents ?? Array.Empty<ChatSession>();
        }

        public ChatSession Session { get; }

        public IReadOnlyList<ChatSession> SubAgents { get; }

        public int SubAgentCount
        {
            get { return SubAgents.Count; }
        }
    }

    public static class SessionListProjection
    {
        public static IReadOnlyList<SessionListEntry> BuildWorkspaceEntries(
            IEnumerable<ChatSession> sessions,
            string workspaceId,
            AppInteractionMode interactionMode)
        {
            var workspaceSessions = new List<ChatSession>();
            if (sessions != null)
            {
                foreach (var session in sessions)
                {
                    if (session == null ||
                        session.Mode != interactionMode ||
                        !string.Equals(session.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    workspaceSessions.Add(session);
                }

                foreach (var root in workspaceSessions.ToArray())
                {
                    foreach (var child in SessionDescendantCollector.Collect(root, sessions.Where(candidate => candidate != null && candidate.Mode == interactionMode)).Skip(1))
                        if (!ContainsSession(workspaceSessions, child)) workspaceSessions.Add(child);
                }
            }
            return BuildMainEntries(workspaceSessions);
        }

        public static IReadOnlyList<SessionListEntry> BuildMainEntries(IEnumerable<ChatSession> sessions)
        {
            var orderedSessions = new List<ChatSession>();
            if (sessions != null)
            {
                foreach (var session in sessions)
                {
                    if (session != null)
                    {
                        orderedSessions.Add(session);
                    }
                }
            }

            var entries = new List<SessionListEntry>();
            foreach (var session in orderedSessions)
            {
                if (ShouldHideSubAgentInMainList(session, orderedSessions))
                {
                    continue;
                }

                var subAgents = SessionDescendantCollector.Collect(session, orderedSessions).Skip(1).ToArray();

                entries.Add(new SessionListEntry(session, subAgents));
            }

            return entries;
        }

        public static bool IsSubAgent(ChatSession session)
        {
            return session != null &&
                   (session.IsSubAgent ||
                    string.Equals(session.ThreadSource, "subagent", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(session.ParentThreadId));
        }

        public static bool IsWorking(SessionListEntry entry, Func<string, bool> isThreadWorking)
        {
            if (entry == null || isThreadWorking == null)
            {
                return false;
            }

            if (IsWorking(entry.Session, isThreadWorking))
            {
                return true;
            }

            foreach (var subAgent in entry.SubAgents)
            {
                if (IsWorking(subAgent, isThreadWorking))
                {
                    return true;
                }
            }

            return false;
        }

        public static int CountWorkingEntries(IEnumerable<SessionListEntry> entries, Func<string, bool> isThreadWorking)
        {
            var count = 0;
            if (entries == null)
            {
                return count;
            }

            foreach (var entry in entries)
            {
                if (IsWorking(entry, isThreadWorking))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsWorking(ChatSession session, Func<string, bool> isThreadWorking)
        {
            return session != null &&
                   !string.IsNullOrWhiteSpace(session.ThreadId) &&
                   isThreadWorking(session.ThreadId);
        }

        private static bool ShouldHideSubAgentInMainList(ChatSession session, IEnumerable<ChatSession> sessions)
        {
            if (!IsSubAgent(session))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(session.ParentThreadId) && HasParentInList(session, sessions);
        }

        private static bool HasParentInList(ChatSession session, IEnumerable<ChatSession> sessions)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ParentThreadId) || sessions == null)
            {
                return false;
            }

            foreach (var candidate in sessions)
            {
                if (candidate != null &&
                    !string.IsNullOrWhiteSpace(candidate.ThreadId) &&
                    string.Equals(candidate.ThreadId, session.ParentThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsSession(IEnumerable<ChatSession> sessions, ChatSession target)
        {
            if (sessions == null || target == null)
            {
                return false;
            }

            foreach (var session in sessions)
            {
                if (ReferenceEquals(session, target) ||
                    (!string.IsNullOrWhiteSpace(session.Id) &&
                     string.Equals(session.Id, target.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
