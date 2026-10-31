using System;
using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class ChatSession
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string WorkspaceId { get; set; }

        [DataMember]
        public AppInteractionMode Mode { get; set; } = AppInteractionMode.Coding;

        [DataMember]
        public string RuntimeProfileId { get; set; }

        [DataMember]
        public string OfficeRuntimeScopeId { get; set; }

        [DataMember]
        public string ThreadId { get; set; }

        [DataMember]
        public string Title { get; set; }

        [DataMember]
        public DateTime CreatedAt { get; set; }

        [DataMember]
        public DateTime LastActiveAt { get; set; }

        [DataMember]
        public ApprovalMode? ApprovalMode { get; set; }

        [DataMember]
        public bool IsImported { get; set; }

        [DataMember]
        public string ThreadSource { get; set; }

        [DataMember]
        public string Preview { get; set; }

        [DataMember]
        public string SessionPath { get; set; }

        [DataMember]
        public string ModelId { get; set; }

        [DataMember]
        public string ModelProviderId { get; set; }

        [DataMember]
        public string ReasoningEffort { get; set; }

        // A later manual model choice or follow-global reset must survive automation synchronization.
        [DataMember(EmitDefaultValue = false)]
        public bool AutomationModelSettingsInitialized { get; set; }

        [DataMember]
        public bool IsPlanModeEnabled { get; set; }

        [DataMember]
        public string ThreadGoalObjective { get; set; }

        [DataMember]
        public string ThreadGoalStatus { get; set; }

        [DataMember]
        public long? ThreadGoalTokenBudget { get; set; }

        [DataMember]
        public long? ThreadGoalTokensUsed { get; set; }

        [DataMember]
        public long? ThreadGoalTimeUsedSeconds { get; set; }

        [DataMember]
        public long? ThreadGoalCreatedAt { get; set; }

        [DataMember]
        public long? ThreadGoalUpdatedAt { get; set; }

        [DataMember]
        public long? ThreadTokenUsageModelContextWindow { get; set; }

        [DataMember]
        public long? ThreadTokenUsageTotalTokens { get; set; }

        [DataMember]
        public long? ThreadTokenUsageInputTokens { get; set; }

        [DataMember]
        public long? ThreadTokenUsageCachedInputTokens { get; set; }

        [DataMember]
        public long? ThreadTokenUsageOutputTokens { get; set; }

        [DataMember]
        public long? ThreadTokenUsageReasoningOutputTokens { get; set; }

        [DataMember]
        public long? ThreadTokenUsageLastTotalTokens { get; set; }

        [DataMember]
        public long? ThreadTokenUsageLastInputTokens { get; set; }

        [DataMember]
        public long? ThreadTokenUsageLastCachedInputTokens { get; set; }

        [DataMember]
        public long? ThreadTokenUsageLastOutputTokens { get; set; }

        [DataMember]
        public long? ThreadTokenUsageLastReasoningOutputTokens { get; set; }

        [DataMember]
        public bool IsFork { get; set; }

        [DataMember]
        public string ForkSourceThreadId { get; set; }

        [DataMember]
        public string ForkSourceTitle { get; set; }

        [DataMember]
        public int? ForkIndex { get; set; }

        [DataMember]
        public bool IsReview { get; set; }

        [DataMember]
        public string ReviewSourceThreadId { get; set; }

        [DataMember]
        public string ReviewSourceTitle { get; set; }

        [DataMember]
        public bool IsSubAgent { get; set; }

        [DataMember]
        public string ParentThreadId { get; set; }

        [DataMember]
        public int? SubAgentDepth { get; set; }

        [DataMember]
        public string SubAgentNickname { get; set; }

        [DataMember]
        public string SubAgentRole { get; set; }
    }
}
