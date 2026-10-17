using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ThreadImportMergeResult
    {
        public ThreadImportMergeResult(int importedThreadCount, WorkspaceInfo preferredWorkspace, ChatSession preferredSession)
        {
            ImportedThreadCount = importedThreadCount;
            PreferredWorkspace = preferredWorkspace;
            PreferredSession = preferredSession;
        }

        public int ImportedThreadCount { get; }

        public WorkspaceInfo PreferredWorkspace { get; }

        public ChatSession PreferredSession { get; }

        public bool HasPreferredSelection
        {
            get { return PreferredWorkspace != null && PreferredSession != null; }
        }
    }

    internal static class ThreadImportSessionMerger
    {
        internal static List<ChatSession> ApplyThreadTitles(
            IEnumerable<AppServerThreadSummary> threads,
            IList<ChatSession> sessions)
        {
            var updatedSessions = new List<ChatSession>();
            if (threads == null || sessions == null)
            {
                return updatedSessions;
            }

            foreach (var thread in threads)
            {
                if (thread == null ||
                    string.IsNullOrWhiteSpace(thread.ThreadId) ||
                    string.IsNullOrWhiteSpace(thread.Title))
                {
                    continue;
                }

                var title = thread.Title.Trim();
                for (var i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session == null ||
                        !string.Equals(session.ThreadId, thread.ThreadId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(session.Title, title, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    session.Title = title;
                    updatedSessions.Add(session);
                    break;
                }
            }

            return updatedSessions;
        }

        internal static ThreadImportMergeResult ImportThreads(
            IEnumerable<AppServerThreadSummary> threads,
            IList<ChatSession> sessions,
            WorkspaceService workspaceService,
            SessionService sessionService,
            Func<string, bool> isWorkspacePathAvailable,
            Action<WorkspaceInfo> upsertRecentWorkspace,
            bool allowMissingWorkspaces,
            Func<string, bool, WorkspaceInfo> createWorkspace = null)
        {
            return ImportThreads(
                threads,
                sessions,
                workspaceService,
                sessionService,
                isWorkspacePathAvailable,
                upsertRecentWorkspace,
                allowMissingWorkspaces,
                createWorkspace,
                AppInteractionMode.Coding,
                null);
        }

        internal static ThreadImportMergeResult ImportThreads(
            IEnumerable<AppServerThreadSummary> threads,
            IList<ChatSession> sessions,
            WorkspaceService workspaceService,
            SessionService sessionService,
            Func<string, bool> isWorkspacePathAvailable,
            Action<WorkspaceInfo> upsertRecentWorkspace,
            bool allowMissingWorkspaces,
            Func<string, bool, WorkspaceInfo> createWorkspace,
            AppInteractionMode interactionMode)
        {
            return ImportThreads(
                threads,
                sessions,
                workspaceService,
                sessionService,
                isWorkspacePathAvailable,
                upsertRecentWorkspace,
                allowMissingWorkspaces,
                createWorkspace,
                interactionMode,
                null);
        }

        internal static ThreadImportMergeResult ImportThreads(
            IEnumerable<AppServerThreadSummary> threads,
            IList<ChatSession> sessions,
            WorkspaceService workspaceService,
            SessionService sessionService,
            Func<string, bool> isWorkspacePathAvailable,
            Action<WorkspaceInfo> upsertRecentWorkspace,
            bool allowMissingWorkspaces,
            Func<string, bool, WorkspaceInfo> createWorkspace,
            AppInteractionMode interactionMode,
            string officeRootPath,
            Func<AppServerThreadSummary, string> titleFallbackResolver = null)
        {
            if (threads == null || sessions == null || workspaceService == null || sessionService == null)
            {
                return new ThreadImportMergeResult(0, null, null);
            }

            createWorkspace = createWorkspace ?? workspaceService.CreateFromPath;
            WorkspaceInfo preferredWorkspace = null;
            ChatSession preferredSession = null;
            var importedThreadCount = 0;
            foreach (var thread in threads)
            {
                if (!string.IsNullOrWhiteSpace(officeRootPath) &&
                    ThreadImportWorkflowPlanner.FilterThreadsForInteractionMode(new[] { thread }, interactionMode, officeRootPath).Count == 0)
                {
                    continue;
                }

                if (!CanImportThread(thread, allowMissingWorkspaces, isWorkspacePathAvailable))
                {
                    continue;
                }

                try
                {
                    var workspace = createWorkspace(thread.WorkingDirectory, !allowMissingWorkspaces);
                    workspace.LastOpenedAt = thread.UpdatedAt == default(DateTime) ? DateTime.Now : thread.UpdatedAt;
                    if (upsertRecentWorkspace != null)
                    {
                        upsertRecentWorkspace(workspace);
                    }

                    var session = UpsertImportedSession(
                        sessions,
                        sessionService,
                        workspace,
                        ApplyTitleFallback(thread, titleFallbackResolver),
                        interactionMode);
                    importedThreadCount++;
                    if (preferredWorkspace == null)
                    {
                        preferredWorkspace = workspace;
                        preferredSession = session;
                    }
                }
                catch
                {
                    // Historical archived threads may point at workspaces that cannot be resolved anymore.
                }
            }

            return new ThreadImportMergeResult(importedThreadCount, preferredWorkspace, preferredSession);
        }

        private static AppServerThreadSummary ApplyTitleFallback(
            AppServerThreadSummary thread,
            Func<AppServerThreadSummary, string> titleFallbackResolver)
        {
            if (thread == null ||
                !string.IsNullOrWhiteSpace(thread.Title) ||
                titleFallbackResolver == null)
            {
                return thread;
            }

            var fallbackTitle = titleFallbackResolver(thread);
            if (string.IsNullOrWhiteSpace(fallbackTitle))
            {
                return thread;
            }

            return new AppServerThreadSummary
            {
                ThreadId = thread.ThreadId,
                Title = fallbackTitle.Trim(),
                Preview = thread.Preview,
                ModelProviderId = thread.ModelProviderId,
                WorkingDirectory = thread.WorkingDirectory,
                Source = thread.Source,
                ThreadSource = thread.ThreadSource,
                ParentThreadId = thread.ParentThreadId,
                SubagentDepth = thread.SubagentDepth,
                AgentNickname = thread.AgentNickname,
                AgentRole = thread.AgentRole,
                CreatedAt = thread.CreatedAt,
                UpdatedAt = thread.UpdatedAt,
                SessionPath = thread.SessionPath
            };
        }

        private static bool CanImportThread(
            AppServerThreadSummary thread,
            bool allowMissingWorkspaces,
            Func<string, bool> isWorkspacePathAvailable)
        {
            if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId) || string.IsNullOrWhiteSpace(thread.WorkingDirectory))
            {
                return false;
            }

            return allowMissingWorkspaces ||
                   (isWorkspacePathAvailable != null && isWorkspacePathAvailable(thread.WorkingDirectory));
        }

        private static ChatSession UpsertImportedSession(
            IList<ChatSession> sessions,
            SessionService sessionService,
            WorkspaceInfo workspace,
            AppServerThreadSummary thread,
            AppInteractionMode interactionMode)
        {
            var importedSession = sessionService.CreateImportedSession(workspace, thread);
            importedSession.Mode = interactionMode;
            var placeholderIndex = -1;
            for (var i = 0; i < sessions.Count; i++)
            {
                var existing = sessions[i];
                if (existing == null || existing.Mode != interactionMode)
                {
                    continue;
                }

                if (string.Equals(existing.ThreadId, thread.ThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyImportedSession(existing, workspace, importedSession);
                    return existing;
                }

                if (placeholderIndex < 0 && IsMatchingEmptyPlaceholder(existing, workspace, importedSession))
                {
                    placeholderIndex = i;
                }
            }

            if (placeholderIndex >= 0)
            {
                ApplyImportedSession(sessions[placeholderIndex], workspace, importedSession);
                return sessions[placeholderIndex];
            }

            sessions.Add(importedSession);
            return importedSession;
        }

        private static void ApplyImportedSession(ChatSession target, WorkspaceInfo workspace, ChatSession importedSession)
        {
            target.WorkspaceId = workspace.Id;
            target.Mode = importedSession.Mode;
            target.ThreadId = importedSession.ThreadId;
            target.Title = importedSession.Title;
            target.CreatedAt = importedSession.CreatedAt;
            target.LastActiveAt = importedSession.LastActiveAt;
            target.IsImported = true;
            target.ThreadSource = importedSession.ThreadSource;
            target.Preview = importedSession.Preview;
            target.SessionPath = importedSession.SessionPath;
            target.IsSubAgent = importedSession.IsSubAgent;
            target.ParentThreadId = importedSession.ParentThreadId;
            target.SubAgentDepth = importedSession.SubAgentDepth;
            target.SubAgentNickname = importedSession.SubAgentNickname;
            target.SubAgentRole = importedSession.SubAgentRole;
        }

        private static bool IsMatchingEmptyPlaceholder(ChatSession existing, WorkspaceInfo workspace, ChatSession importedSession)
        {
            return existing != null &&
                   workspace != null &&
                   importedSession != null &&
                   string.IsNullOrWhiteSpace(existing.ThreadId) &&
                   string.Equals(existing.WorkspaceId, workspace.Id, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       NormalizeTitle(existing.Title),
                       NormalizeTitle(importedSession.Title),
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeTitle(string title)
        {
            return string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
        }
    }
}
