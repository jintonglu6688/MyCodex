using System;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal enum AppServerEventRoute
    {
        Default = 0,
        AuthenticationChanged,
        Ignore,
        StandaloneCommandOutput,
        SystemNotice,
        ProtocolNotice,
        ProtocolWarning,
        AppServerDisconnected,
        SummaryOnly,
        TurnStarted,
        TurnRetrying,
        TurnCompleted,
        TurnInterrupted,
        TurnFailed,
        ContextCompactionCompleted,
        ApprovalRequested,
        ApprovalResolved,
        ToolUserInputRequested,
        AsyncInputRequested,
        McpElicitationUrlRequested,
        ToolUserInputResolved,
        ThreadArchivedChanged,
        SkillFilesChanged,
        ExternalAgentConfigImportCompleted,
        Assistant,
        CommandOutput,
        CommandTerminalInteraction,
        CommandCompleted,
        ThreadStarted,
        ThreadStatusChanged,
        ThreadNameChanged,
        ThreadGoalChanged,
        ThreadTokenUsageChanged,
        McpStartupStatusUpdated,
        InternalToolStatus,
        Error
    }

    internal static class AppServerEventRouting
    {
        internal static AppServerEventRoute Resolve(AppServerEventArgs e)
        {
            switch (NormalizeEventType(e))
            {
                case "auth_changed":
                case "auth_login_completed":
                    return AppServerEventRoute.AuthenticationChanged;
                case "rate_limits_changed":
                    return AppServerEventRoute.Ignore;
                case "mcp_startup_status_updated":
                    return AppServerEventRoute.McpStartupStatusUpdated;
                case "token_usage":
                    return AppServerEventRoute.ThreadTokenUsageChanged;
                case "command_exec_output":
                    return AppServerEventRoute.StandaloneCommandOutput;
                case "system_notice":
                case "file_system_changed":
                case "internal_tools_registered":
                    return AppServerEventRoute.SystemNotice;
                case "fuzzy_file_search_session_updated":
                case "fuzzy_file_search_session_completed":
                case "windows_sandbox_setup_completed":
                    return AppServerEventRoute.Ignore;
                case "protocol_notice":
                    return AppServerEventRoute.ProtocolNotice;
                case "protocol_warning":
                case "windows_world_writable_warning":
                    return AppServerEventRoute.ProtocolWarning;
                case "app_server_disconnected":
                    return AppServerEventRoute.AppServerDisconnected;
                case "turn_plan_updated":
                case "plan_delta":
                case "turn_diff_updated":
                case "reasoning_started":
                case "reasoning_summary_delta":
                case "reasoning_text_delta":
                case "reasoning_completed":
                case "file_change_progress":
                case "file_change_patch_updated":
                case "file_change_completed":
                case "mcp_tool_progress":
                case "mcp_tool_completed":
                case "network_activity_started":
                case "network_activity_completed":
                case "image_generation_started":
                case "image_generation_completed":
                case "subagent_activity":
                    return AppServerEventRoute.SummaryOnly;
                case "turn_started":
                    return AppServerEventRoute.TurnStarted;
                case "turn_retrying":
                    return AppServerEventRoute.TurnRetrying;
                case "turn_completed":
                    return AppServerEventRoute.TurnCompleted;
                case "turn_interrupted":
                    return AppServerEventRoute.TurnInterrupted;
                case "turn_failed":
                    return AppServerEventRoute.TurnFailed;
                case "context_compaction_completed":
                    return AppServerEventRoute.ContextCompactionCompleted;
                case "approval_requested":
                    return AppServerEventRoute.ApprovalRequested;
                case "approval_resolved":
                    return AppServerEventRoute.ApprovalResolved;
                case "tool_user_input_requested":
                case "mcp_elicitation_requested":
                    return AppServerEventRoute.ToolUserInputRequested;
                case "async_input_requested":
                    return AppServerEventRoute.AsyncInputRequested;
                case "mcp_elicitation_url_requested":
                    return AppServerEventRoute.McpElicitationUrlRequested;
                case "tool_user_input_resolved":
                    return AppServerEventRoute.ToolUserInputResolved;
                case "thread_archived":
                case "thread_deleted":
                case "thread_closed":
                case "thread_unarchived":
                    return AppServerEventRoute.ThreadArchivedChanged;
                case "skills_changed":
                    return AppServerEventRoute.SkillFilesChanged;
                case "external_agent_config_import_completed":
                    return AppServerEventRoute.ExternalAgentConfigImportCompleted;
                case "assistant":
                case "plan":
                    return AppServerEventRoute.Assistant;
                case "command_output":
                    return AppServerEventRoute.CommandOutput;
                case "command_terminal_interaction":
                    return AppServerEventRoute.CommandTerminalInteraction;
                case "command_completed":
                    return AppServerEventRoute.CommandCompleted;
                case "thread_started":
                    return AppServerEventRoute.ThreadStarted;
                case "thread_status_changed":
                    return AppServerEventRoute.ThreadStatusChanged;
                case "thread_name_updated":
                    return AppServerEventRoute.ThreadNameChanged;
                case "thread_goal_updated":
                case "thread_goal_cleared":
                    return AppServerEventRoute.ThreadGoalChanged;
                case "internal_tool_running":
                case "internal_tool_completed":
                case "internal_tool_failed":
                case "auto_approval_review_started":
                case "auto_approval_review_completed":
                    return AppServerEventRoute.InternalToolStatus;
                case "turn_moderation_metadata":
                    return AppServerEventRoute.ProtocolNotice;
                case "error":
                    return AppServerEventRoute.Error;
                default:
                    return AppServerEventRoute.Default;
            }
        }

        internal static bool IsBufferedCommandOutputEvent(AppServerEventArgs e)
        {
            var route = Resolve(e);
            return route == AppServerEventRoute.CommandOutput ||
                   route == AppServerEventRoute.StandaloneCommandOutput;
        }

        internal static bool IsBufferedTurnSummaryEvent(AppServerEventArgs e)
        {
            switch (NormalizeEventType(e))
            {
                case "plan_delta":
                case "turn_plan_updated":
                case "turn_diff_updated":
                case "reasoning_summary_delta":
                case "reasoning_text_delta":
                case "file_change_progress":
                case "file_change_patch_updated":
                case "file_change_completed":
                case "mcp_tool_progress":
                case "mcp_tool_completed":
                case "network_activity_started":
                case "network_activity_completed":
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsTurnCompleted(AppServerEventRoute route)
        {
            return route == AppServerEventRoute.TurnCompleted;
        }

        internal static bool ShouldRequestThreadListRefresh(AppServerEventRoute route)
        {
            return route == AppServerEventRoute.ThreadStarted ||
                   route == AppServerEventRoute.ThreadStatusChanged ||
                   route == AppServerEventRoute.ThreadArchivedChanged ||
                   route == AppServerEventRoute.TurnStarted ||
                   route == AppServerEventRoute.TurnCompleted;
        }

        internal static bool ShouldScheduleThreadListRefreshFollowUp(AppServerEventRoute route)
        {
            return route == AppServerEventRoute.ThreadStarted ||
                   route == AppServerEventRoute.ThreadStatusChanged ||
                   route == AppServerEventRoute.TurnStarted;
        }

        internal static bool IsTurnTerminal(AppServerEventRoute route)
        {
            return route == AppServerEventRoute.TurnCompleted ||
                   route == AppServerEventRoute.TurnInterrupted ||
                   route == AppServerEventRoute.TurnFailed;
        }

        internal static bool IsErrorDisplayEvent(AppServerEventRoute route)
        {
            return route == AppServerEventRoute.Error ||
                   route == AppServerEventRoute.TurnFailed;
        }

        internal static ChatHistoryCategory GetFallbackCategory(AppServerEventRoute route)
        {
            switch (route)
            {
                case AppServerEventRoute.TurnStarted:
                case AppServerEventRoute.TurnRetrying:
                case AppServerEventRoute.TurnCompleted:
                case AppServerEventRoute.TurnInterrupted:
                case AppServerEventRoute.TurnFailed:
                case AppServerEventRoute.ThreadStarted:
                case AppServerEventRoute.ThreadStatusChanged:
                case AppServerEventRoute.ThreadGoalChanged:
                case AppServerEventRoute.ThreadTokenUsageChanged:
                case AppServerEventRoute.InternalToolStatus:
                    return ChatHistoryCategory.Processing;
                case AppServerEventRoute.CommandCompleted:
                    return ChatHistoryCategory.Command;
                case AppServerEventRoute.ApprovalRequested:
                case AppServerEventRoute.ApprovalResolved:
                    return ChatHistoryCategory.Approval;
                case AppServerEventRoute.ToolUserInputRequested:
                case AppServerEventRoute.AsyncInputRequested:
                case AppServerEventRoute.McpElicitationUrlRequested:
                case AppServerEventRoute.ToolUserInputResolved:
                    return ChatHistoryCategory.System;
                case AppServerEventRoute.SkillFilesChanged:
                case AppServerEventRoute.ExternalAgentConfigImportCompleted:
                    return ChatHistoryCategory.FileChange;
                case AppServerEventRoute.AuthenticationChanged:
                    return ChatHistoryCategory.Account;
                case AppServerEventRoute.ProtocolWarning:
                    return ChatHistoryCategory.ProtocolWarning;
                case AppServerEventRoute.ProtocolNotice:
                    return ChatHistoryCategory.ProtocolNotice;
                default:
                    return ChatHistoryCategory.System;
            }
        }

        private static string NormalizeEventType(AppServerEventArgs e)
        {
            return (e != null ? e.EventType : string.Empty).Trim().ToLowerInvariant();
        }
    }
}
