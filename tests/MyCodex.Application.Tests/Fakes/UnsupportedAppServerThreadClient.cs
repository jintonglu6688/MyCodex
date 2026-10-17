using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Tests.Fakes
{
    internal abstract class UnsupportedAppServerThreadClient : IAppServerThreadClient
    {
        public virtual Task<string> CreateThreadAsync(string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort, AppServerThreadToolContext toolContext = null) { throw new NotSupportedException(); }
        public virtual Task SendUserMessageAsync(string threadId, string workspacePath, IReadOnlyCollection<AppServerUserInputItem> inputItems, ApprovalMode? approvalMode, string modelId, string reasoningEffort, AppServerCollaborationMode collaborationMode = null) { throw new NotSupportedException(); }
        public virtual Task<List<AppServerThreadSummary>> ListThreadsAsync() { throw new NotSupportedException(); }
        public virtual Task<List<AppServerThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken) { throw new NotSupportedException(); }
        public virtual Task<List<string>> ListLoadedThreadIdsAsync(int? limit = null) { throw new NotSupportedException(); }
        public virtual Task<List<string>> ListLoadedThreadIdsAsync(int? limit, CancellationToken cancellationToken) { throw new NotSupportedException(); }
        public virtual Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath) { throw new NotSupportedException(); }
        public virtual Task<AppServerConversationSummary> GetConversationSummaryAsync(AppServerConversationSummaryRequest request, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }
        public virtual Task<AppServerThreadDetails> ReadThreadAsync(string threadId) { throw new NotSupportedException(); }
        public virtual Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken) { throw new NotSupportedException(); }
        public virtual Task ArchiveThreadAsync(string threadId) { throw new NotSupportedException(); }
        public virtual Task DeleteThreadAsync(string threadId) { throw new NotSupportedException(); }
        public virtual Task UnarchiveThreadAsync(string threadId) { throw new NotSupportedException(); }
        public virtual Task<AppServerThreadSummary> ForkThreadAsync(string threadId) { throw new NotSupportedException(); }

        public virtual Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId, string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort)
        {
            return ForkThreadAsync(threadId, lastTurnId);
        }

        public virtual Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId) { throw new NotSupportedException(); }
        public virtual Task CompactThreadAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new NotSupportedException(); }
        public virtual Task<AppServerTurnSteerResult> TrySteerTurnAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, string clientUserMessageId = null) { throw new NotSupportedException(); }
        public virtual Task<AppServerTurnSteerResult> TrySteerTurnAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, string clientUserMessageId, string expectedTurnId) { return TrySteerTurnAsync(threadId, inputItems, clientUserMessageId); }
        public virtual Task<AppServerThreadInjectItemsResult> InjectThreadItemsAsync(string threadId, IReadOnlyCollection<IDictionary<string, object>> items, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }
        public virtual Task ApproveGuardianDeniedActionAsync(AppServerGuardianDeniedActionApprovalRequest request, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }
        public virtual Task<AppServerReviewStartResult> StartReviewAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, AppServerReviewTarget target, bool runDetached, string reasoningEffort = null, string reviewModelId = null) { throw new NotSupportedException(); }
        public virtual Task UnsubscribeThreadAsync(string threadId) { throw new NotSupportedException(); }
        public virtual Task DeleteThreadFileAsync(string sessionPath) { throw new NotSupportedException(); }
        public virtual Task SetThreadNameAsync(string threadId, string name) { throw new NotSupportedException(); }
        public virtual Task<AppServerThreadGoal> SetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, string objective, long? tokenBudget = null) { throw new NotSupportedException(); }
        public virtual Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new NotSupportedException(); }
        public virtual Task ClearThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new NotSupportedException(); }
        public virtual Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo) { throw new NotSupportedException(); }
    }
}
