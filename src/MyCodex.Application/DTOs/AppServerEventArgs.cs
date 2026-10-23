using System;
using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerEventArgs : EventArgs
    {
        public AppServerEventArgs(
            string eventType,
            string content,
            string threadId = null,
            string turnId = null,
            string itemId = null,
            string requestId = null,
            string commandText = null,
            string workingDirectory = null,
            string reason = null,
            string details = null,
            AppServerApprovalRequestKind? approvalKind = null,
            bool canApproveForSession = false,
            long? totalTokens = null,
            long? modelContextWindow = null,
            IReadOnlyList<AppServerToolUserInputQuestion> toolQuestions = null,
            string processId = null,
            string outputStream = null,
            string explanation = null,
            IReadOnlyList<AppServerTurnPlanStep> planSteps = null,
            string diffText = null,
            string planDeltaText = null,
            IReadOnlyList<string> changedFiles = null,
            string mcpServerName = null,
            string mcpToolName = null,
            string itemStatus = null,
            string url = null,
            AppServerThreadGoal threadGoal = null,
            AppServerFileSystemChangedEvent fileSystemChanged = null,
            AppServerFuzzyFileSearchSessionEvent fuzzyFileSearchSession = null,
            AppServerWindowsSandboxSetupCompletedEvent windowsSandboxSetupCompleted = null,
            AppServerWindowsWorldWritableWarningEvent windowsWorldWritableWarning = null,
            AppServerGuardianApprovalReviewEvent guardianApprovalReview = null,
            AppServerModerationMetadataEvent moderationMetadata = null,
            AppServerThreadTokenUsageSnapshot threadTokenUsage = null,
            ApprovalRequestClassification approvalClassification = null,
            bool isMcpElicitationConfirmation = false,
            AppServerThreadSummary threadSummary = null,
            AppServerThreadStatus threadStatus = null,
            AppServerCollaborationActivity collaboration = null,
            AppServerSubAgentActivity subAgentActivity = null,
            bool isHistorical = false,
            string messagePhase = null)
        {
            EventType = eventType;
            Content = content;
            ThreadId = threadId;
            TurnId = turnId;
            ItemId = itemId;
            RequestId = requestId;
            CommandText = commandText;
            WorkingDirectory = workingDirectory;
            Reason = reason;
            Details = details;
            ProcessId = processId;
            OutputStream = outputStream;
            ApprovalKind = approvalKind;
            ApprovalClassification = approvalClassification;
            CanApproveForSession = canApproveForSession;
            TotalTokens = totalTokens;
            ModelContextWindow = modelContextWindow;
            ThreadTokenUsage = threadTokenUsage;
            ToolQuestions = toolQuestions;
            Explanation = explanation;
            PlanSteps = planSteps;
            DiffText = diffText;
            PlanDeltaText = planDeltaText;
            ChangedFiles = changedFiles;
            McpServerName = mcpServerName;
            McpToolName = mcpToolName;
            ItemStatus = itemStatus;
            Url = url;
            ThreadGoal = threadGoal;
            FileSystemChanged = fileSystemChanged;
            FuzzyFileSearchSession = fuzzyFileSearchSession;
            WindowsSandboxSetupCompleted = windowsSandboxSetupCompleted;
            WindowsWorldWritableWarning = windowsWorldWritableWarning;
            GuardianApprovalReview = guardianApprovalReview;
            ModerationMetadata = moderationMetadata;
            IsMcpElicitationConfirmation = isMcpElicitationConfirmation;
            ThreadSummary = threadSummary;
            ThreadStatus = threadStatus;
            Collaboration = collaboration;
            SubAgentActivity = subAgentActivity;
            IsHistorical = isHistorical;
            MessagePhase = messagePhase;
            Timestamp = DateTime.Now;
        }

        public string EventType { get; }
        public string MessagePhase { get; }

        public string Content { get; }

        public string ThreadId { get; }

        public string TurnId { get; }

        public string ItemId { get; }

        public string RequestId { get; }

        public string CommandText { get; }

        public string WorkingDirectory { get; }

        public string Reason { get; }

        public string Details { get; }

        public string ProcessId { get; }

        public string OutputStream { get; }

        public AppServerApprovalRequestKind? ApprovalKind { get; }

        public ApprovalRequestClassification ApprovalClassification { get; }

        public bool CanApproveForSession { get; }

        public long? TotalTokens { get; }

        public long? ModelContextWindow { get; }

        public AppServerThreadTokenUsageSnapshot ThreadTokenUsage { get; }

        public IReadOnlyList<AppServerToolUserInputQuestion> ToolQuestions { get; }

        public string Explanation { get; }

        public IReadOnlyList<AppServerTurnPlanStep> PlanSteps { get; }

        public string DiffText { get; }

        public string PlanDeltaText { get; }

        public IReadOnlyList<string> ChangedFiles { get; }

        public string McpServerName { get; }

        public bool IsMcpElicitationConfirmation { get; }

        public string McpToolName { get; }

        public string ItemStatus { get; }

        public string Url { get; }

        public AppServerThreadGoal ThreadGoal { get; }

        public AppServerFileSystemChangedEvent FileSystemChanged { get; }

        public AppServerFuzzyFileSearchSessionEvent FuzzyFileSearchSession { get; }

        public AppServerWindowsSandboxSetupCompletedEvent WindowsSandboxSetupCompleted { get; }

        public AppServerWindowsWorldWritableWarningEvent WindowsWorldWritableWarning { get; }

        public AppServerGuardianApprovalReviewEvent GuardianApprovalReview { get; }

        public AppServerModerationMetadataEvent ModerationMetadata { get; }

        public AppServerThreadSummary ThreadSummary { get; }
        public AppServerThreadStatus ThreadStatus { get; }
        public AppServerCollaborationActivity Collaboration { get; }
        public AppServerSubAgentActivity SubAgentActivity { get; }
        public bool IsHistorical { get; }

        public DateTime Timestamp { get; }
    }
}
