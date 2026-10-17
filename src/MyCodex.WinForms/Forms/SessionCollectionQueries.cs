using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionCollectionQueries
    {
        internal static int CountSessionsInWorkspace(IEnumerable<ChatSession> sessions, string workspaceId)
        {
            if (sessions == null || string.IsNullOrWhiteSpace(workspaceId))
            {
                return 0;
            }

            var count = 0;
            foreach (var session in sessions)
            {
                if (session != null &&
                    string.Equals(session.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }

        internal static int CountSessionsInWorkspace(IEnumerable<ChatSession> sessions, string workspaceId, AppInteractionMode mode)
        {
            return CountSessionsInWorkspace(GetSessionsForWorkspace(sessions, workspaceId, mode), workspaceId);
        }

        internal static ChatSession FindSessionById(IEnumerable<ChatSession> sessions, string sessionId)
        {
            if (sessions == null || string.IsNullOrWhiteSpace(sessionId))
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

        internal static ChatSession FindSessionByThreadId(IEnumerable<ChatSession> sessions, string threadId)
        {
            if (sessions == null || string.IsNullOrWhiteSpace(threadId))
            {
                return null;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    string.Equals(session.ThreadId, threadId, StringComparison.OrdinalIgnoreCase))
                {
                    return session;
                }
            }

            return null;
        }

        internal static List<ChatSession> GetSessionsForWorkspace(IEnumerable<ChatSession> sessions, string workspaceId)
        {
            var result = new List<ChatSession>();
            if (sessions == null || string.IsNullOrWhiteSpace(workspaceId))
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    string.Equals(session.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(session);
                }
            }

            return result;
        }

        internal static List<ChatSession> GetSessionsForWorkspace(IEnumerable<ChatSession> sessions, string workspaceId, AppInteractionMode mode)
        {
            var result = new List<ChatSession>();
            if (sessions == null || string.IsNullOrWhiteSpace(workspaceId))
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    session.Mode == mode &&
                    string.Equals(session.WorkspaceId, workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(session);
                }
            }

            return result;
        }

        internal static List<ChatSession> GetSessionsForActiveRuntimeContexts(
            IEnumerable<ChatSession> sessions,
            string codingRuntimeProfileId,
            string officeRuntimeScopeId)
        {
            var result = new List<ChatSession>();
            if (sessions == null)
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (IsSessionInActiveRuntimeContexts(
                    session,
                    codingRuntimeProfileId,
                    officeRuntimeScopeId))
                {
                    result.Add(session);
                }
            }

            return result;
        }

        internal static bool IsSessionInActiveRuntimeContexts(
            ChatSession session,
            string codingRuntimeProfileId,
            string officeRuntimeScopeId)
        {
            if (session == null)
            {
                return false;
            }

            return session.Mode == AppInteractionMode.Office
                ? string.Equals(
                    session.OfficeRuntimeScopeId,
                    officeRuntimeScopeId,
                    StringComparison.OrdinalIgnoreCase)
                : string.Equals(
                    session.RuntimeProfileId,
                    codingRuntimeProfileId,
                    StringComparison.OrdinalIgnoreCase);
        }

        internal static WorkspaceInfo FindWorkspaceById(IEnumerable<WorkspaceInfo> workspaces, string workspaceId)
        {
            if (workspaces == null || string.IsNullOrWhiteSpace(workspaceId))
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

        internal static List<string> GetThreadIds(IEnumerable<ChatSession> sessions)
        {
            var result = new List<string>();
            if (sessions == null)
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (session != null && !string.IsNullOrWhiteSpace(session.ThreadId))
                {
                    result.Add(session.ThreadId);
                }
            }

            return result;
        }

        internal static List<string> GetThreadIds(IEnumerable<ChatSession> sessions, AppInteractionMode mode)
        {
            var result = new List<string>();
            if (sessions == null)
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (session != null && session.Mode == mode && !string.IsNullOrWhiteSpace(session.ThreadId))
                {
                    result.Add(session.ThreadId);
                }
            }

            return result;
        }

        internal static bool HasImportedSessions(IEnumerable<ChatSession> sessions)
        {
            if (sessions == null)
            {
                return false;
            }

            foreach (var session in sessions)
            {
                if (session != null && session.IsImported && !string.IsNullOrWhiteSpace(session.ThreadId))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool HasImportedSessions(IEnumerable<ChatSession> sessions, AppInteractionMode mode)
        {
            if (sessions == null)
            {
                return false;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    session.Mode == mode &&
                    session.IsImported &&
                    !string.IsNullOrWhiteSpace(session.ThreadId))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
