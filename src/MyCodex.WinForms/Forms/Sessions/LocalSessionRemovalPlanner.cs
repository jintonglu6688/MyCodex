using System;
using System.Collections.Generic;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal sealed class LocalSessionRemovalPlan
    {
        public LocalSessionRemovalPlan(
            IList<WorkspaceInfo> retainedWorkspaces,
            IList<ChatSession> retainedSessions,
            IList<ChatSession> retainedInactiveRuntimeSessions,
            IList<ChatSession> sessionsToClear,
            WorkspaceInfo currentWorkspace,
            ChatSession currentSession,
            bool hasChanges)
        {
            RetainedWorkspaces = new List<WorkspaceInfo>(retainedWorkspaces ?? new WorkspaceInfo[0]);
            RetainedSessions = new List<ChatSession>(retainedSessions ?? new ChatSession[0]);
            RetainedInactiveRuntimeSessions = new List<ChatSession>(retainedInactiveRuntimeSessions ?? new ChatSession[0]);
            SessionsToClear = new List<ChatSession>(sessionsToClear ?? new ChatSession[0]);
            CurrentWorkspace = currentWorkspace;
            CurrentSession = currentSession;
            HasChanges = hasChanges;
        }

        public List<WorkspaceInfo> RetainedWorkspaces { get; }

        public List<ChatSession> RetainedSessions { get; }

        public List<ChatSession> RetainedInactiveRuntimeSessions { get; }

        public List<ChatSession> SessionsToClear { get; }

        public WorkspaceInfo CurrentWorkspace { get; }

        public ChatSession CurrentSession { get; }

        public bool HasChanges { get; }
    }

    internal static class LocalSessionRemovalPlanner
    {
        internal static LocalSessionRemovalPlan BuildWorkspaceRemoval(
            WorkspaceInfo workspace,
            IList<WorkspaceInfo> recentWorkspaces,
            IList<ChatSession> sessions,
            IList<ChatSession> inactiveRuntimeSessions,
            WorkspaceInfo currentWorkspace,
            ChatSession currentSession)
        {
            if (workspace == null)
            {
                return BuildNoChange(recentWorkspaces, sessions, inactiveRuntimeSessions, currentWorkspace, currentSession);
            }

            var retainedWorkspaces = new List<WorkspaceInfo>();
            if (recentWorkspaces != null)
            {
                foreach (var item in recentWorkspaces)
                {
                    if (item == null || string.Equals(item.Id, workspace.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    retainedWorkspaces.Add(item);
                }
            }

            var retainedSessions = new List<ChatSession>();
            var sessionsToClear = new List<ChatSession>();
            SplitSessionsByWorkspace(sessions, workspace.Id, retainedSessions, sessionsToClear);

            var retainedInactiveRuntimeSessions = new List<ChatSession>();
            SplitSessionsByWorkspace(inactiveRuntimeSessions, workspace.Id, retainedInactiveRuntimeSessions, null);

            var selectedWorkspace = currentWorkspace;
            if (selectedWorkspace != null &&
                string.Equals(selectedWorkspace.Id, workspace.Id, StringComparison.OrdinalIgnoreCase))
            {
                selectedWorkspace = retainedWorkspaces.Count > 0 ? retainedWorkspaces[0] : null;
            }

            var selectedSession = selectedWorkspace == null
                ? null
                : SessionSelectionResolver.ResolveCurrentSession(selectedWorkspace, currentSession, retainedSessions);

            return new LocalSessionRemovalPlan(
                retainedWorkspaces,
                retainedSessions,
                retainedInactiveRuntimeSessions,
                sessionsToClear,
                selectedWorkspace,
                selectedSession,
                true);
        }

        internal static LocalSessionRemovalPlan BuildSessionRemoval(
            ChatSession session,
            IList<WorkspaceInfo> recentWorkspaces,
            IList<ChatSession> sessions,
            IList<ChatSession> inactiveRuntimeSessions,
            WorkspaceInfo currentWorkspace,
            ChatSession currentSession,
            bool includeDescendants = true)
        {
            if (session == null)
            {
                return BuildNoChange(recentWorkspaces, sessions, inactiveRuntimeSessions, currentWorkspace, currentSession);
            }

            var sessionsToClear = includeDescendants
                ? SessionDescendantCollector.Collect(session, sessions)
                : new List<ChatSession> { session };
            var removalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in sessionsToClear)
            {
                if (target != null && !string.IsNullOrWhiteSpace(target.Id))
                {
                    removalIds.Add(target.Id);
                }
            }

            var retainedSessions = new List<ChatSession>();
            if (sessions != null)
            {
                foreach (var item in sessions)
                {
                    if (item != null && !string.IsNullOrWhiteSpace(item.Id) && removalIds.Contains(item.Id))
                    {
                        continue;
                    }

                    retainedSessions.Add(item);
                }
            }

            var selectedSession = currentSession;
            if (selectedSession != null && !string.IsNullOrWhiteSpace(selectedSession.Id) && removalIds.Contains(selectedSession.Id))
            {
                selectedSession = null;
            }

            if (currentWorkspace == null ||
                string.Equals(currentWorkspace.Id, session.WorkspaceId, StringComparison.OrdinalIgnoreCase))
            {
                selectedSession = SessionSelectionResolver.ResolveCurrentSession(currentWorkspace, selectedSession, retainedSessions, session.Mode);
            }

            return new LocalSessionRemovalPlan(
                recentWorkspaces,
                retainedSessions,
                inactiveRuntimeSessions,
                sessionsToClear,
                currentWorkspace,
                selectedSession,
                true);
        }

        private static LocalSessionRemovalPlan BuildNoChange(
            IList<WorkspaceInfo> recentWorkspaces,
            IList<ChatSession> sessions,
            IList<ChatSession> inactiveRuntimeSessions,
            WorkspaceInfo currentWorkspace,
            ChatSession currentSession)
        {
            return new LocalSessionRemovalPlan(
                recentWorkspaces,
                sessions,
                inactiveRuntimeSessions,
                new ChatSession[0],
                currentWorkspace,
                currentSession,
                false);
        }

        private static void SplitSessionsByWorkspace(
            IEnumerable<ChatSession> sessions,
            string workspaceId,
            ICollection<ChatSession> retainedSessions,
            ICollection<ChatSession> removedSessions)
        {
            if (sessions == null)
            {
                return;
            }

            foreach (var session in sessions)
            {
                if (session != null && string.Equals(session.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    if (removedSessions != null)
                    {
                        removedSessions.Add(session);
                    }

                    continue;
                }

                retainedSessions.Add(session);
            }
        }
    }
}
