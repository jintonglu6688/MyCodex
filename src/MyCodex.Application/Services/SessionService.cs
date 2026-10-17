using System;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public class SessionService
    {
        public ChatSession CreateSession(WorkspaceInfo workspace, int index, string title = null)
        {
            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            var now = DateTime.Now;
            return new ChatSession
            {
                Id = Guid.NewGuid().ToString("N"),
                WorkspaceId = workspace.Id,
                Mode = workspace.Kind == WorkspaceKind.Office ? AppInteractionMode.Office : AppInteractionMode.Coding,
                ThreadId = string.Empty,
                Title = string.IsNullOrWhiteSpace(title)
                    ? BuildDefaultSessionTitle(workspace, index)
                    : title.Trim(),
                CreatedAt = now,
                LastActiveAt = now
            };
        }

        public string BuildDefaultSessionTitle(WorkspaceInfo workspace, int index)
        {
            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            return string.Format(
                SharedLocalization.Text("SessionService.Title.DefaultSession.Template", "{0}"),
                workspace.Name);
        }

        public ChatSession CreateImportedSession(WorkspaceInfo workspace, AppServerThreadSummary threadSummary)
        {
            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            if (threadSummary == null)
            {
                throw new ArgumentNullException(nameof(threadSummary));
            }

            return new ChatSession
            {
                Id = Guid.NewGuid().ToString("N"),
                WorkspaceId = workspace.Id,
                Mode = workspace.Kind == WorkspaceKind.Office ? AppInteractionMode.Office : AppInteractionMode.Coding,
                ThreadId = threadSummary.ThreadId,
                Title = BuildImportedSessionTitle(workspace, threadSummary),
                CreatedAt = threadSummary.CreatedAt == default(DateTime) ? DateTime.Now : threadSummary.CreatedAt,
                LastActiveAt = threadSummary.UpdatedAt == default(DateTime) ? DateTime.Now : threadSummary.UpdatedAt,
                IsImported = true,
                ThreadSource = string.IsNullOrWhiteSpace(threadSummary.ThreadSource) ? threadSummary.Source : threadSummary.ThreadSource,
                Preview = threadSummary.Preview,
                SessionPath = threadSummary.SessionPath,
                ModelProviderId = threadSummary.ModelProviderId,
                IsSubAgent = IsSubAgentThread(threadSummary),
                ParentThreadId = threadSummary.ParentThreadId,
                SubAgentDepth = threadSummary.SubagentDepth,
                SubAgentNickname = threadSummary.AgentNickname,
                SubAgentRole = threadSummary.AgentRole
            };
        }

        public ChatSession CreateForkSession(
            WorkspaceInfo workspace,
            ChatSession sourceSession,
            AppServerThreadSummary threadSummary,
            string title,
            int forkIndex,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort)
        {
            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            if (sourceSession == null)
            {
                throw new ArgumentNullException(nameof(sourceSession));
            }

            if (threadSummary == null)
            {
                throw new ArgumentNullException(nameof(threadSummary));
            }

            var now = DateTime.Now;
            return new ChatSession
            {
                Id = Guid.NewGuid().ToString("N"),
                WorkspaceId = workspace.Id,
                ThreadId = threadSummary.ThreadId,
                Title = string.IsNullOrWhiteSpace(title)
                    ? BuildImportedSessionTitle(workspace, threadSummary)
                    : title.Trim(),
                CreatedAt = threadSummary.CreatedAt == default(DateTime) ? now : threadSummary.CreatedAt,
                LastActiveAt = threadSummary.UpdatedAt == default(DateTime) ? now : threadSummary.UpdatedAt,
                ApprovalMode = approvalMode,
                Preview = threadSummary.Preview,
                SessionPath = threadSummary.SessionPath,
                ModelProviderId = threadSummary.ModelProviderId,
                ModelId = modelId,
                ReasoningEffort = reasoningEffort,
                IsPlanModeEnabled = sourceSession.IsPlanModeEnabled,
                IsFork = true,
                ForkSourceThreadId = sourceSession.ThreadId,
                ForkSourceTitle = sourceSession.Title,
                ForkIndex = forkIndex
            };
        }

        public ChatSession CreateReviewSession(
            WorkspaceInfo workspace,
            ChatSession sourceSession,
            AppServerThreadSummary threadSummary,
            string title,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort)
        {
            if (workspace == null)
            {
                throw new ArgumentNullException(nameof(workspace));
            }

            if (sourceSession == null)
            {
                throw new ArgumentNullException(nameof(sourceSession));
            }

            if (threadSummary == null)
            {
                throw new ArgumentNullException(nameof(threadSummary));
            }

            var now = DateTime.Now;
            return new ChatSession
            {
                Id = Guid.NewGuid().ToString("N"),
                WorkspaceId = workspace.Id,
                ThreadId = threadSummary.ThreadId,
                Title = string.IsNullOrWhiteSpace(title)
                    ? BuildImportedSessionTitle(workspace, threadSummary)
                    : title.Trim(),
                CreatedAt = threadSummary.CreatedAt == default(DateTime) ? now : threadSummary.CreatedAt,
                LastActiveAt = threadSummary.UpdatedAt == default(DateTime) ? now : threadSummary.UpdatedAt,
                ApprovalMode = approvalMode,
                Preview = threadSummary.Preview,
                SessionPath = threadSummary.SessionPath,
                ModelProviderId = threadSummary.ModelProviderId,
                ModelId = modelId,
                ReasoningEffort = reasoningEffort,
                IsReview = true,
                ReviewSourceThreadId = sourceSession.ThreadId,
                ReviewSourceTitle = sourceSession.Title
            };
        }

        private static string BuildImportedSessionTitle(WorkspaceInfo workspace, AppServerThreadSummary threadSummary)
        {
            if (!string.IsNullOrWhiteSpace(threadSummary.Title))
            {
                return threadSummary.Title;
            }

            if (!string.IsNullOrWhiteSpace(threadSummary.Preview))
            {
                var preview = threadSummary.Preview.Trim();
                return preview.Length > 30 ? preview.Substring(0, 30) + "..." : preview;
            }

            return string.Format(
                SharedLocalization.Text("SessionService.Title.HistorySession.Template", "{0} History Session"),
                workspace.Name);
        }

        private static bool IsSubAgentThread(AppServerThreadSummary threadSummary)
        {
            return threadSummary != null &&
                   (string.Equals(threadSummary.ThreadSource, "subagent", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(threadSummary.ParentThreadId));
        }
    }
}
