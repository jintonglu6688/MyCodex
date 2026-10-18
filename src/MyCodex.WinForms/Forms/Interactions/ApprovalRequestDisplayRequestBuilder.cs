using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal static class ApprovalRequestDisplayRequestBuilder
    {
        internal static AppServerEventArgs Build(AppServerEventArgs request, TurnProgressSummaryState turnState)
        {
            if (request == null || request.ApprovalKind != AppServerApprovalRequestKind.FileChange || turnState == null)
            {
                return request;
            }

            var details = !string.IsNullOrWhiteSpace(request.Details)
                ? request.Details
                : turnState.FileChangeDetailText;
            var changedFiles = request.ChangedFiles != null && request.ChangedFiles.Count > 0
                ? request.ChangedFiles
                : turnState.ChangedFiles.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();

            if (string.Equals(details, request.Details, System.StringComparison.Ordinal) &&
                ReferenceEquals(changedFiles, request.ChangedFiles))
            {
                return request;
            }

            return new AppServerEventArgs(
                request.EventType,
                request.Content,
                threadId: request.ThreadId,
                turnId: request.TurnId,
                itemId: request.ItemId,
                requestId: request.RequestId,
                commandText: request.CommandText,
                workingDirectory: request.WorkingDirectory,
                reason: request.Reason,
                details: details,
                approvalKind: request.ApprovalKind,
                canApproveForSession: request.CanApproveForSession,
                totalTokens: request.TotalTokens,
                modelContextWindow: request.ModelContextWindow,
                toolQuestions: request.ToolQuestions,
                processId: request.ProcessId,
                outputStream: request.OutputStream,
                explanation: request.Explanation,
                planSteps: request.PlanSteps,
                diffText: request.DiffText,
                planDeltaText: request.PlanDeltaText,
                changedFiles: changedFiles,
                mcpServerName: request.McpServerName,
                mcpToolName: request.McpToolName,
                itemStatus: request.ItemStatus,
                url: request.Url,
                threadGoal: request.ThreadGoal,
                fileSystemChanged: request.FileSystemChanged,
                fuzzyFileSearchSession: request.FuzzyFileSearchSession,
                windowsSandboxSetupCompleted: request.WindowsSandboxSetupCompleted,
                windowsWorldWritableWarning: request.WindowsWorldWritableWarning,
                guardianApprovalReview: request.GuardianApprovalReview,
                moderationMetadata: request.ModerationMetadata,
                approvalClassification: request.ApprovalClassification);
        }
    }
}
