using System;

namespace MyCodex.Infrastructure.AppServer
{
    internal enum AppServerNotificationAction
    {
        Ignore,
        ThreadStarted,
        ThreadArchived,
        ThreadDeleted,
        ThreadClosed,
        ThreadUnarchived,
        ThreadCompacted,
        ThreadStatusChanged,
        ThreadNameUpdated,
        ThreadGoalUpdated,
        ThreadGoalCleared,
        TurnStarted,
        TurnPlanUpdated,
        TurnDiffUpdated,
        ItemStarted,
        AgentMessageDelta,
        PlanDelta,
        CommandExecutionOutputDelta,
        CommandTerminalInteraction,
        FileChangeOutputDelta,
        FileChangePatchUpdated,
        AutoApprovalReviewStarted,
        AutoApprovalReviewCompleted,
        McpToolCallProgress,
        ReasoningSummaryTextDelta,
        ReasoningSummaryPartAdded,
        ReasoningTextDelta,
        StandaloneCommandOutputDelta,
        ItemCompleted,
        TurnCompleted,
        AccountUpdated,
        AccountLoginCompleted,
        McpServerOauthLoginCompleted,
        McpServerStartupStatusUpdated,
        SkillsChanged,
        ExternalAgentConfigImportCompleted,
        FileSystemChanged,
        FuzzyFileSearchSessionUpdated,
        FuzzyFileSearchSessionCompleted,
        WindowsSandboxSetupCompleted,
        RateLimitsUpdated,
        ThreadTokenUsageUpdated,
        TurnModerationMetadata,
        WindowsWorldWritableWarning,
        ConfigWarning,
        DeprecationNotice,
        GuardianWarning,
        RuntimeWarning,
        ModelRerouted,
        ModelVerification,
        ServerRequestResolved,
        Error,
        Unhandled
    }

    internal static class AppServerNotificationDispatcher
    {
        internal static AppServerNotificationAction Classify(string method)
        {
            if (IsLowNoiseCompatibilityNotification(method))
            {
                return AppServerNotificationAction.Ignore;
            }

            switch (method)
            {
                case "thread/started":
                    return AppServerNotificationAction.ThreadStarted;
                case "thread/archived":
                    return AppServerNotificationAction.ThreadArchived;
                case "thread/deleted":
                    return AppServerNotificationAction.ThreadDeleted;
                case "thread/closed":
                    return AppServerNotificationAction.ThreadClosed;
                case "thread/unarchived":
                    return AppServerNotificationAction.ThreadUnarchived;
                case "thread/compacted":
                    return AppServerNotificationAction.ThreadCompacted;
                case "thread/status/changed":
                    return AppServerNotificationAction.ThreadStatusChanged;
                case "thread/name/updated":
                    return AppServerNotificationAction.ThreadNameUpdated;
                case "thread/goal/updated":
                    return AppServerNotificationAction.ThreadGoalUpdated;
                case "thread/goal/cleared":
                    return AppServerNotificationAction.ThreadGoalCleared;
                case "thread/settings/updated":
                case "thread/readState/changed":
                    return AppServerNotificationAction.Ignore;
                case "turn/started":
                    return AppServerNotificationAction.TurnStarted;
                case "turn/plan/updated":
                    return AppServerNotificationAction.TurnPlanUpdated;
                case "turn/diff/updated":
                    return AppServerNotificationAction.TurnDiffUpdated;
                case "item/started":
                    return AppServerNotificationAction.ItemStarted;
                case "item/agentMessage/delta":
                    return AppServerNotificationAction.AgentMessageDelta;
                case "item/plan/delta":
                    return AppServerNotificationAction.PlanDelta;
                case "item/commandExecution/outputDelta":
                    return AppServerNotificationAction.CommandExecutionOutputDelta;
                case "item/commandExecution/terminalInteraction":
                    return AppServerNotificationAction.CommandTerminalInteraction;
                case "item/fileChange/outputDelta":
                    return AppServerNotificationAction.FileChangeOutputDelta;
                case "item/fileChange/patchUpdated":
                    return AppServerNotificationAction.FileChangePatchUpdated;
                case "item/autoApprovalReview/started":
                    return AppServerNotificationAction.AutoApprovalReviewStarted;
                case "item/autoApprovalReview/completed":
                    return AppServerNotificationAction.AutoApprovalReviewCompleted;
                case "item/mcpToolCall/progress":
                    return AppServerNotificationAction.McpToolCallProgress;
                case "item/reasoning/summaryTextDelta":
                    return AppServerNotificationAction.ReasoningSummaryTextDelta;
                case "item/reasoning/summaryPartAdded":
                    return AppServerNotificationAction.ReasoningSummaryPartAdded;
                case "item/reasoning/textDelta":
                    return AppServerNotificationAction.ReasoningTextDelta;
                case "command/exec/outputDelta":
                    return AppServerNotificationAction.StandaloneCommandOutputDelta;
                case "item/completed":
                    return AppServerNotificationAction.ItemCompleted;
                case "turn/completed":
                    return AppServerNotificationAction.TurnCompleted;
                case "account/updated":
                    return AppServerNotificationAction.AccountUpdated;
                case "account/login/completed":
                    return AppServerNotificationAction.AccountLoginCompleted;
                case "mcpServer/oauthLogin/completed":
                    return AppServerNotificationAction.McpServerOauthLoginCompleted;
                case "mcpServer/startupStatus/updated":
                    return AppServerNotificationAction.McpServerStartupStatusUpdated;
                case "skills/changed":
                    return AppServerNotificationAction.SkillsChanged;
                case "externalAgentConfig/import/completed":
                    return AppServerNotificationAction.ExternalAgentConfigImportCompleted;
                case "fs/changed":
                    return AppServerNotificationAction.FileSystemChanged;
                case "fuzzyFileSearch/sessionUpdated":
                    return AppServerNotificationAction.FuzzyFileSearchSessionUpdated;
                case "fuzzyFileSearch/sessionCompleted":
                    return AppServerNotificationAction.FuzzyFileSearchSessionCompleted;
                case "windowsSandbox/setupCompleted":
                    return AppServerNotificationAction.WindowsSandboxSetupCompleted;
                case "account/rateLimits/updated":
                    return AppServerNotificationAction.RateLimitsUpdated;
                case "thread/tokenUsage/updated":
                    return AppServerNotificationAction.ThreadTokenUsageUpdated;
                case "turn/moderationMetadata":
                    return AppServerNotificationAction.TurnModerationMetadata;
                case "windows/worldWritableWarning":
                    return AppServerNotificationAction.WindowsWorldWritableWarning;
                case "configWarning":
                    return AppServerNotificationAction.ConfigWarning;
                case "deprecationNotice":
                    return AppServerNotificationAction.DeprecationNotice;
                case "guardianWarning":
                    return AppServerNotificationAction.GuardianWarning;
                case "warning":
                    return AppServerNotificationAction.RuntimeWarning;
                case "model/rerouted":
                    return AppServerNotificationAction.ModelRerouted;
                case "model/verification":
                    return AppServerNotificationAction.ModelVerification;
                case "serverRequest/resolved":
                    return AppServerNotificationAction.ServerRequestResolved;
                case "error":
                    return AppServerNotificationAction.Error;
                default:
                    return AppServerNotificationAction.Unhandled;
            }
        }

        internal static bool IsLowNoiseCompatibilityNotification(string method)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                return false;
            }

            return string.Equals(method, "hook/started", StringComparison.Ordinal) ||
                   string.Equals(method, "hook/completed", StringComparison.Ordinal) ||
                   string.Equals(method, "app/list/updated", StringComparison.Ordinal) ||
                   string.Equals(method, "process/outputDelta", StringComparison.Ordinal) ||
                   string.Equals(method, "process/exited", StringComparison.Ordinal) ||
                   string.Equals(method, "rawResponseItem/completed", StringComparison.Ordinal) ||
                   string.Equals(method, "remoteControl/status/changed", StringComparison.Ordinal) ||
                   method.StartsWith("thread/realtime/", StringComparison.Ordinal);
        }
    }
}
