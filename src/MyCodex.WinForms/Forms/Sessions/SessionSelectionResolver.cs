using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionSelectionResult
    {
        public SessionSelectionResult(WorkspaceInfo workspace, ChatSession session)
        {
            Workspace = workspace;
            Session = session;
        }

        public WorkspaceInfo Workspace { get; }

        public ChatSession Session { get; }
    }

    internal static class SessionSelectionResolver
    {
        internal static ChatSession ResolveCurrentSession(
            WorkspaceInfo currentWorkspace,
            ChatSession currentSession,
            IEnumerable<ChatSession> sessions)
        {
            if (currentWorkspace == null)
            {
                return null;
            }

            var preferredSessionId = currentSession != null ? currentSession.Id : null;
            if (!string.IsNullOrWhiteSpace(preferredSessionId))
            {
                var preferredSession = FindSessionById(sessions, preferredSessionId);
                if (preferredSession != null &&
                    string.Equals(preferredSession.WorkspaceId, currentWorkspace.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveMainSession(preferredSession, sessions);
                }
            }

            if (sessions == null)
            {
                return null;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    string.Equals(session.WorkspaceId, currentWorkspace.Id, StringComparison.OrdinalIgnoreCase) &&
                    !ShouldHideSubAgentInMainSelection(session, sessions))
                {
                    return session;
                }
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    string.Equals(session.WorkspaceId, currentWorkspace.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return session;
                }
            }

            return null;
        }

        internal static ChatSession ResolveCurrentSession(
            WorkspaceInfo currentWorkspace,
            ChatSession currentSession,
            IEnumerable<ChatSession> sessions,
            AppInteractionMode mode)
        {
            if (currentWorkspace == null)
            {
                return null;
            }

            var preferredSessionId = currentSession != null ? currentSession.Id : null;
            if (!string.IsNullOrWhiteSpace(preferredSessionId))
            {
                var preferredSession = FindSessionById(sessions, preferredSessionId);
                if (preferredSession != null &&
                    preferredSession.Mode == mode &&
                    string.Equals(preferredSession.WorkspaceId, currentWorkspace.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return ResolveMainSession(preferredSession, sessions, mode);
                }
            }

            if (sessions == null)
            {
                return null;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    session.Mode == mode &&
                    string.Equals(session.WorkspaceId, currentWorkspace.Id, StringComparison.OrdinalIgnoreCase) &&
                    !ShouldHideSubAgentInMainSelection(session, sessions, mode))
                {
                    return session;
                }
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    session.Mode == mode &&
                    string.Equals(session.WorkspaceId, currentWorkspace.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return session;
                }
            }

            return null;
        }

        internal static SessionSelectionResult RestoreSelection(
            IList<WorkspaceInfo> recentWorkspaces,
            IEnumerable<ChatSession> sessions,
            WorkspaceInfo currentWorkspace,
            ChatSession currentSession,
            string workspaceId,
            string sessionId)
        {
            var selectedWorkspace = currentWorkspace;
            var selectedSession = currentSession;

            var restoredWorkspace = FindWorkspaceById(recentWorkspaces, workspaceId);
            if (restoredWorkspace != null)
            {
                selectedWorkspace = restoredWorkspace;
            }

            var restoredSession = FindSessionById(sessions, sessionId);
            if (restoredSession != null)
            {
                selectedSession = restoredSession;
            }

            if (selectedWorkspace == null && recentWorkspaces != null && recentWorkspaces.Count > 0)
            {
                selectedWorkspace = recentWorkspaces[0];
            }

            selectedSession = ResolveCurrentSession(selectedWorkspace, selectedSession, sessions);

            return new SessionSelectionResult(selectedWorkspace, selectedSession);
        }

        internal static SessionSelectionResult RestoreOfficeSelection(
            IEnumerable<WorkspaceInfo> workspaces,
            IEnumerable<ChatSession> sessions,
            string workspaceId,
            string sessionId)
        {
            var selectedSession = FindSessionById(sessions, sessionId);
            if (selectedSession != null && selectedSession.Mode == AppInteractionMode.Office)
            {
                var sessionWorkspace = FindWorkspaceById(workspaces, selectedSession.WorkspaceId);
                if (sessionWorkspace != null && sessionWorkspace.Kind == WorkspaceKind.Office)
                {
                    return new SessionSelectionResult(sessionWorkspace, selectedSession);
                }
            }

            var selectedWorkspace = FindWorkspaceById(workspaces, workspaceId);
            if (selectedWorkspace == null || selectedWorkspace.Kind != WorkspaceKind.Office)
            {
                selectedWorkspace = FindFirstOfficeWorkspace(workspaces);
            }

            return new SessionSelectionResult(selectedWorkspace, null);
        }

        private static WorkspaceInfo FindWorkspaceById(IEnumerable<WorkspaceInfo> workspaces, string workspaceId)
        {
            if (string.IsNullOrWhiteSpace(workspaceId) || workspaces == null)
            {
                return null;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace != null &&
                    string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    return workspace;
                }
            }

            return null;
        }

        private static ChatSession FindSessionById(IEnumerable<ChatSession> sessions, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || sessions == null)
            {
                return null;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    return session;
                }
            }

            return null;
        }

        private static WorkspaceInfo FindFirstOfficeWorkspace(IEnumerable<WorkspaceInfo> workspaces)
        {
            if (workspaces == null)
            {
                return null;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace != null && workspace.Kind == WorkspaceKind.Office)
                {
                    return workspace;
                }
            }

            return null;
        }

        private static ChatSession ResolveMainSession(ChatSession session, IEnumerable<ChatSession> sessions)
        {
            if (!ShouldHideSubAgentInMainSelection(session, sessions))
            {
                return session;
            }

            var parentSession = FindParentSession(session, sessions);
            return parentSession ?? session;
        }

        private static ChatSession ResolveMainSession(ChatSession session, IEnumerable<ChatSession> sessions, AppInteractionMode mode)
        {
            if (!ShouldHideSubAgentInMainSelection(session, sessions, mode))
            {
                return session;
            }

            var parentSession = FindParentSession(session, sessions, mode);
            return parentSession ?? session;
        }

        private static bool ShouldHideSubAgentInMainSelection(ChatSession session, IEnumerable<ChatSession> sessions)
        {
            return SessionListProjection.IsSubAgent(session) &&
                   !string.IsNullOrWhiteSpace(session.ParentThreadId) &&
                   FindParentSession(session, sessions) != null;
        }

        private static bool ShouldHideSubAgentInMainSelection(ChatSession session, IEnumerable<ChatSession> sessions, AppInteractionMode mode)
        {
            return SessionListProjection.IsSubAgent(session) &&
                   !string.IsNullOrWhiteSpace(session.ParentThreadId) &&
                   FindParentSession(session, sessions, mode) != null;
        }

        private static ChatSession FindParentSession(ChatSession session, IEnumerable<ChatSession> sessions)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ParentThreadId) || sessions == null)
            {
                return null;
            }

            foreach (var candidate in sessions)
            {
                if (candidate != null &&
                    string.Equals(candidate.WorkspaceId, session.WorkspaceId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(candidate.ThreadId) &&
                    string.Equals(candidate.ThreadId, session.ParentThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static ChatSession FindParentSession(ChatSession session, IEnumerable<ChatSession> sessions, AppInteractionMode mode)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ParentThreadId) || sessions == null)
            {
                return null;
            }

            foreach (var candidate in sessions)
            {
                if (candidate != null &&
                    candidate.Mode == mode &&
                    string.Equals(candidate.WorkspaceId, session.WorkspaceId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(candidate.ThreadId) &&
                    string.Equals(candidate.ThreadId, session.ParentThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
