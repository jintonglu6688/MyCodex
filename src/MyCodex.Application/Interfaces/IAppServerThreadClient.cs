using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerThreadClient
    {
        Task<string> CreateThreadAsync(
            string workspacePath,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort,
            AppServerThreadToolContext toolContext = null);

        Task SendUserMessageAsync(
            string threadId,
            string workspacePath,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort,
            AppServerCollaborationMode collaborationMode = null);

        Task<List<AppServerThreadSummary>> ListThreadsAsync();

        Task<List<AppServerThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken);

        Task<List<string>> ListLoadedThreadIdsAsync(int? limit = null);

        Task<List<string>> ListLoadedThreadIdsAsync(int? limit, CancellationToken cancellationToken);

        Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath);

        Task<AppServerConversationSummary> GetConversationSummaryAsync(
            AppServerConversationSummaryRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<AppServerThreadDetails> ReadThreadAsync(string threadId);

        Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken);

        Task ArchiveThreadAsync(string threadId);

        Task DeleteThreadAsync(string threadId);

        Task UnarchiveThreadAsync(string threadId);

        Task<AppServerThreadSummary> ForkThreadAsync(string threadId);

        Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId);

        Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId, string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort);


        Task CompactThreadAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId);

        Task<AppServerTurnSteerResult> TrySteerTurnAsync(
            string threadId,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            string clientUserMessageId = null);

        Task<AppServerTurnSteerResult> TrySteerTurnAsync(
            string threadId,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            string clientUserMessageId,
            string expectedTurnId);

        Task ApproveGuardianDeniedActionAsync(
            AppServerGuardianDeniedActionApprovalRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        Task<AppServerReviewStartResult> StartReviewAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, AppServerReviewTarget target, bool runDetached, string reasoningEffort = null, string reviewModelId = null);

        Task UnsubscribeThreadAsync(string threadId);

        Task DeleteThreadFileAsync(string sessionPath);

        Task SetThreadNameAsync(string threadId, string name);

        Task<AppServerThreadGoal> SetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, string objective, long? tokenBudget = null);

        Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId);

        Task ClearThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId);

        Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo);
    }
}
