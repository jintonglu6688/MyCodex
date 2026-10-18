using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ThreadAssociationMergeResult
    {
        public ThreadAssociationMergeResult(int associatedThreadCount, int skippedThreadCount, ChatSession firstAssociatedSession)
        {
            AssociatedThreadCount = associatedThreadCount;
            SkippedThreadCount = skippedThreadCount;
            FirstAssociatedSession = firstAssociatedSession;
        }

        public int AssociatedThreadCount { get; }

        public int SkippedThreadCount { get; }

        public ChatSession FirstAssociatedSession { get; }

        public bool HasAssociatedSessions
        {
            get { return AssociatedThreadCount > 0; }
        }
    }

    internal static class ThreadAssociationSessionMerger
    {
        internal static async System.Threading.Tasks.Task<ThreadAssociationMergeResult> ImportThreadsAsync(
            IEnumerable<AppServerThreadSummary> threads,
            IList<ChatSession> sessions,
            WorkspaceInfo targetWorkspace,
            SessionService sessionService,
            IAppServerThreadClient threadClient,
            SessionThreadService sessionThreadService)
        {
            if (threads == null ||
                sessions == null ||
                targetWorkspace == null ||
                sessionService == null ||
                threadClient == null)
            {
                return new ThreadAssociationMergeResult(0, 0, null);
            }

            var targetThreadIds = BuildTargetWorkspaceLinkedThreadIds(sessions, targetWorkspace.Id);
            var selectedThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var associatedCount = 0;
            var skippedCount = 0;
            ChatSession firstAssociatedSession = null;

            foreach (var thread in threads)
            {
                if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId))
                {
                    continue;
                }

                if (!selectedThreadIds.Add(thread.ThreadId) || targetThreadIds.Contains(thread.ThreadId))
                {
                    skippedCount++;
                    continue;
                }

                var forkSummary = await threadClient.ForkThreadAsync(thread.ThreadId).ConfigureAwait(true);
                if (forkSummary == null || string.IsNullOrWhiteSpace(forkSummary.ThreadId))
                {
                    skippedCount++;
                    continue;
                }

                await TrySetCopiedThreadNameAsync(threadClient, forkSummary.ThreadId, thread.Title).ConfigureAwait(true);
                if (sessionThreadService != null)
                {
                    await sessionThreadService.TryUpdateThreadGitMetadataAsync(forkSummary.ThreadId, targetWorkspace).ConfigureAwait(true);
                }

                var associatedSession = sessionService.CreateImportedSession(
                    targetWorkspace,
                    BuildTargetWorkspaceThreadSummary(thread, forkSummary, targetWorkspace));
                associatedSession.ForkSourceThreadId = thread.ThreadId;
                sessions.Add(associatedSession);
                targetThreadIds.Add(thread.ThreadId);
                targetThreadIds.Add(associatedSession.ThreadId);
                associatedCount++;
                if (firstAssociatedSession == null)
                {
                    firstAssociatedSession = associatedSession;
                }
            }

            return new ThreadAssociationMergeResult(associatedCount, skippedCount, firstAssociatedSession);
        }

        private static HashSet<string> BuildTargetWorkspaceLinkedThreadIds(IEnumerable<ChatSession> sessions, string targetWorkspaceId)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (sessions == null || string.IsNullOrWhiteSpace(targetWorkspaceId))
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (session == null ||
                    string.IsNullOrWhiteSpace(session.ThreadId) ||
                    !string.Equals(session.WorkspaceId, targetWorkspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(session.ThreadId);
                if (!string.IsNullOrWhiteSpace(session.ForkSourceThreadId))
                {
                    result.Add(session.ForkSourceThreadId);
                }
            }

            return result;
        }

        private static async System.Threading.Tasks.Task TrySetCopiedThreadNameAsync(
            IAppServerThreadClient threadClient,
            string threadId,
            string title)
        {
            if (threadClient == null || string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            try
            {
                await threadClient.SetThreadNameAsync(threadId, title.Trim()).ConfigureAwait(true);
            }
            catch
            {
                // The copied thread already exists; keep the local title when remote renaming fails.
            }
        }

        private static AppServerThreadSummary BuildTargetWorkspaceThreadSummary(
            AppServerThreadSummary source,
            AppServerThreadSummary forkSummary,
            WorkspaceInfo targetWorkspace)
        {
            return new AppServerThreadSummary
            {
                ThreadId = forkSummary.ThreadId,
                Title = string.IsNullOrWhiteSpace(source.Title) ? forkSummary.Title : source.Title,
                Preview = string.IsNullOrWhiteSpace(source.Preview) ? forkSummary.Preview : source.Preview,
                WorkingDirectory = targetWorkspace.RootPath,
                Source = string.IsNullOrWhiteSpace(source.Source) ? forkSummary.Source : source.Source,
                ThreadSource = source.ThreadSource,
                ParentThreadId = source.ParentThreadId,
                SubagentDepth = source.SubagentDepth,
                AgentNickname = source.AgentNickname,
                AgentRole = source.AgentRole,
                CreatedAt = source.CreatedAt == default(DateTime) ? forkSummary.CreatedAt : source.CreatedAt,
                UpdatedAt = source.UpdatedAt == default(DateTime) ? forkSummary.UpdatedAt : source.UpdatedAt,
                SessionPath = forkSummary.SessionPath
            };
        }
    }
}
