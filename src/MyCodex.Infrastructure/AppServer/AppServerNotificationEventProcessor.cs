using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerNotificationEventProcessor
    {
        private readonly object _syncRoot = new object();
        private readonly AppServerNotificationEventContext _context;
        private readonly Dictionary<string, StringBuilder> _agentMessageBuffers = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        private readonly Dictionary<string, StringBuilder> _planItemBuffers = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        private readonly Dictionary<string, StringBuilder> _commandOutputBuffers = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        private readonly Dictionary<string, StringBuilder> _fileChangeOutputBuffers = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingMcpToolCallItem> _mcpToolCalls = new Dictionary<string, PendingMcpToolCallItem>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingReasoningItem> _reasoningItems = new Dictionary<string, PendingReasoningItem>(StringComparer.Ordinal);
        private readonly HashSet<string> _commandOutputDeltaItemIds = new HashSet<string>(StringComparer.Ordinal);
        // Temporary threads such as the voice preview never become sessions.
        private readonly HashSet<string> _ephemeralThreadIds = new HashSet<string>(StringComparer.Ordinal);

        public AppServerNotificationEventProcessor(AppServerNotificationEventContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>Drops buffered streaming items; items of a stopped App Server process never complete.</summary>
        public void ClearPendingItems()
        {
            lock (_syncRoot)
            {
                _agentMessageBuffers.Clear();
                _planItemBuffers.Clear();
                _commandOutputBuffers.Clear();
                _fileChangeOutputBuffers.Clear();
                _mcpToolCalls.Clear();
                _reasoningItems.Clear();
                _commandOutputDeltaItemIds.Clear();
            }
        }

        public void HandleNotification(string method, IDictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                return;
            }

            parameters = parameters ?? new Dictionary<string, object>();
            var action = AppServerNotificationDispatcher.Classify(method);
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            var turnId = AppServerJsonValueReader.GetString(parameters, "turnId");
            bool ephemeral;
            var ended = action == AppServerNotificationAction.ThreadClosed || action == AppServerNotificationAction.ThreadDeleted;
            lock (_syncRoot)
            {
                ephemeral = threadId != null && (ended ? _ephemeralThreadIds.Remove(threadId) : _ephemeralThreadIds.Contains(threadId));
            }

            if (ephemeral)
            {
                if (ended)
                {
                    _context.ForgetThreadState(threadId);
                }

                return;
            }

            switch (action)
            {
                case AppServerNotificationAction.Ignore:
                    break;
                case AppServerNotificationAction.ThreadStarted:
                    HandleThreadStarted(parameters);
                    break;
                case AppServerNotificationAction.ThreadArchived:
                    HandleThreadArchived(parameters);
                    break;
                case AppServerNotificationAction.ThreadDeleted:
                    HandleThreadDeleted(parameters);
                    break;
                case AppServerNotificationAction.ThreadClosed:
                    HandleThreadClosed(parameters);
                    break;
                case AppServerNotificationAction.ThreadUnarchived:
                    HandleThreadUnarchived(parameters);
                    break;
                case AppServerNotificationAction.ThreadCompacted:
                    HandleThreadCompacted(parameters);
                    break;
                case AppServerNotificationAction.ThreadStatusChanged:
                    HandleThreadStatusChanged(parameters);
                    break;
                case AppServerNotificationAction.ThreadNameUpdated:
                    HandleThreadNameUpdated(parameters);
                    break;
                case AppServerNotificationAction.ThreadGoalUpdated:
                    HandleThreadGoalUpdated(parameters);
                    break;
                case AppServerNotificationAction.ThreadGoalCleared:
                    HandleThreadGoalCleared(parameters);
                    break;
                case AppServerNotificationAction.TurnStarted:
                    HandleTurnStarted(parameters);
                    break;
                case AppServerNotificationAction.TurnPlanUpdated:
                    HandleTurnPlanUpdated(parameters);
                    break;
                case AppServerNotificationAction.TurnDiffUpdated:
                    HandleTurnDiffUpdated(parameters);
                    break;
                case AppServerNotificationAction.ItemStarted:
                    HandleItemStarted(threadId, turnId, parameters);
                    break;
                case AppServerNotificationAction.AgentMessageDelta:
                    HandleAgentMessageOutputDelta(threadId, turnId, parameters);
                    break;
                case AppServerNotificationAction.PlanDelta:
                    HandlePlanOutputDelta(threadId, turnId, parameters);
                    break;
                case AppServerNotificationAction.CommandExecutionOutputDelta:
                    HandleCommandOutputDelta(threadId, turnId, parameters);
                    break;
                case AppServerNotificationAction.CommandTerminalInteraction:
                    HandleCommandTerminalInteraction(parameters);
                    break;
                case AppServerNotificationAction.FileChangeOutputDelta:
                    HandleFileChangeOutputDelta(threadId, turnId, parameters);
                    break;
                case AppServerNotificationAction.FileChangePatchUpdated:
                    HandleFileChangePatchUpdated(parameters);
                    break;
                case AppServerNotificationAction.AutoApprovalReviewStarted:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateAutoApprovalReviewStarted(parameters));
                    break;
                case AppServerNotificationAction.AutoApprovalReviewCompleted:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateAutoApprovalReviewCompleted(parameters));
                    break;
                case AppServerNotificationAction.McpToolCallProgress:
                    HandleMcpToolCallProgress(threadId, turnId, parameters);
                    break;
                case AppServerNotificationAction.ReasoningSummaryTextDelta:
                    HandleReasoningSummaryTextDelta(threadId, turnId, parameters);
                    break;
                case AppServerNotificationAction.ReasoningSummaryPartAdded:
                    HandleReasoningSummaryPartAdded(parameters);
                    break;
                case AppServerNotificationAction.ReasoningTextDelta:
                    HandleReasoningTextDelta(threadId, turnId, parameters);
                    break;
                case AppServerNotificationAction.StandaloneCommandOutputDelta:
                    HandleStandaloneCommandOutputDelta(parameters);
                    break;
                case AppServerNotificationAction.ItemCompleted:
                    if (HandleContextCompactionItemCompleted(parameters))
                    {
                        break;
                    }

                    HandleItemCompleted(threadId, turnId, parameters);
                    break;
                case AppServerNotificationAction.TurnCompleted:
                    HandleTurnCompleted(threadId, turnId, parameters);
                    break;
                case AppServerNotificationAction.AccountUpdated:
                    HandleAccountUpdated(parameters);
                    break;
                case AppServerNotificationAction.AccountLoginCompleted:
                    HandleAccountLoginCompleted(parameters);
                    break;
                case AppServerNotificationAction.McpServerOauthLoginCompleted:
                    HandleMcpServerOauthLoginCompleted(parameters);
                    break;
                case AppServerNotificationAction.McpServerStartupStatusUpdated:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateMcpServerStartupStatusUpdated(parameters));
                    break;
                case AppServerNotificationAction.SkillsChanged:
                    HandleSkillsChanged();
                    break;
                case AppServerNotificationAction.ExternalAgentConfigImportCompleted:
                    HandleExternalAgentConfigImportCompleted();
                    break;
                case AppServerNotificationAction.FileSystemChanged:
                    HandleFileSystemChanged(parameters);
                    break;
                case AppServerNotificationAction.FuzzyFileSearchSessionUpdated:
                    HandleFuzzyFileSearchSessionUpdated(parameters);
                    break;
                case AppServerNotificationAction.FuzzyFileSearchSessionCompleted:
                    HandleFuzzyFileSearchSessionCompleted(parameters);
                    break;
                case AppServerNotificationAction.WindowsSandboxSetupCompleted:
                    HandleWindowsSandboxSetupCompleted(parameters);
                    break;
                case AppServerNotificationAction.RateLimitsUpdated:
                    HandleRateLimitsUpdated();
                    break;
                case AppServerNotificationAction.ThreadTokenUsageUpdated:
                    HandleThreadTokenUsageUpdated(parameters);
                    break;
                case AppServerNotificationAction.TurnModerationMetadata:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateTurnModerationMetadata(parameters));
                    break;
                case AppServerNotificationAction.WindowsWorldWritableWarning:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateWindowsWorldWritableWarning(parameters));
                    break;
                case AppServerNotificationAction.ConfigWarning:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateConfigWarning(parameters));
                    break;
                case AppServerNotificationAction.DeprecationNotice:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateDeprecationNotice(parameters));
                    break;
                case AppServerNotificationAction.GuardianWarning:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateGuardianWarning(parameters));
                    break;
                case AppServerNotificationAction.RuntimeWarning:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateRuntimeWarning(parameters));
                    break;
                case AppServerNotificationAction.ModelRerouted:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateModelRerouted(parameters));
                    break;
                case AppServerNotificationAction.ModelVerification:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateModelVerification(parameters));
                    break;
                case AppServerNotificationAction.ServerRequestResolved:
                    HandleServerRequestResolved(parameters);
                    break;
                case AppServerNotificationAction.Error:
                    HandleError(parameters);
                    break;
                case AppServerNotificationAction.Unhandled:
                default:
                    _context.RaiseEvent(AppServerNotificationFactory.CreateUnhandledNotification(method, parameters));
                    break;
            }
        }

        private void HandleExternalAgentConfigImportCompleted()
        {
            _context.RaiseEvent(new AppServerEventArgs(
                "external_agent_config_import_completed",
                L("AppServerClient.ExternalAgentConfig.ImportCompleted", "External agent configuration import completed.")));
        }

        private void HandleFileSystemChanged(IDictionary<string, object> parameters)
        {
            var changedEvent = AppServerFileSystemMapper.BuildChangedEvent(parameters);
            _context.RaiseEvent(new AppServerEventArgs(
                "file_system_changed",
                L("AppServerClient.FileSystem.Changed", "File system changed."),
                requestId: changedEvent.WatchId,
                changedFiles: changedEvent.ChangedPaths,
                fileSystemChanged: changedEvent));
        }

        private void HandleFuzzyFileSearchSessionUpdated(IDictionary<string, object> parameters)
        {
            var session = AppServerFuzzyFileSearchMapper.BuildSessionUpdated(parameters);
            _context.RaiseEvent(new AppServerEventArgs(
                "fuzzy_file_search_session_updated",
                L("AppServerClient.FuzzyFileSearch.SessionUpdated", "File search results updated."),
                requestId: session.SessionId,
                fuzzyFileSearchSession: session));
        }

        private void HandleFuzzyFileSearchSessionCompleted(IDictionary<string, object> parameters)
        {
            var session = AppServerFuzzyFileSearchMapper.BuildSessionCompleted(parameters);
            _context.RaiseEvent(new AppServerEventArgs(
                "fuzzy_file_search_session_completed",
                L("AppServerClient.FuzzyFileSearch.SessionCompleted", "File search completed."),
                requestId: session.SessionId,
                fuzzyFileSearchSession: session));
        }

        private void HandleWindowsSandboxSetupCompleted(IDictionary<string, object> parameters)
        {
            var result = AppServerWindowsSandboxMapper.BuildSetupCompleted(parameters);
            _context.RaiseEvent(new AppServerEventArgs(
                "windows_sandbox_setup_completed",
                result.Success
                    ? L("AppServerClient.WindowsSandbox.SetupCompleted", "Windows Sandbox setup completed.")
                    : (string.IsNullOrWhiteSpace(result.Error)
                        ? L("AppServerClient.WindowsSandbox.SetupFailed", "Windows Sandbox setup failed.")
                        : result.Error),
                itemStatus: result.Success ? "success" : "failed",
                windowsSandboxSetupCompleted: result));
        }

        private void HandleThreadStarted(IDictionary<string, object> parameters)
        {
            var thread = AppServerJsonValueReader.GetDictionary(parameters, "thread");
            var threadId = AppServerJsonValueReader.GetString(thread, "id");
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _context.RememberLoadedThreadId(threadId);
            if (AppServerJsonValueReader.GetNullableBool(thread, "ephemeral") == true)
            {
                lock (_syncRoot)
                {
                    _ephemeralThreadIds.Add(threadId);
                }

                return;
            }

            _context.RaiseEvent(AppServerNotificationFactory.CreateThreadStarted(threadId, AppServerThreadMapper.BuildThreadSummary(thread, _context.MapServerPathToClient)));
        }

        private void HandleThreadArchived(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _context.ForgetThreadState(threadId);
            _context.RaiseEvent(AppServerNotificationFactory.CreateThreadArchived(threadId));
        }

        private void HandleThreadDeleted(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _context.ForgetThreadState(threadId);
            _context.RaiseEvent(AppServerNotificationFactory.CreateThreadDeleted(threadId));
        }

        private void HandleThreadClosed(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _context.ForgetThreadState(threadId);
            _context.RaiseEvent(AppServerNotificationFactory.CreateThreadClosed(threadId));
        }

        private void HandleThreadUnarchived(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _context.RaiseEvent(AppServerNotificationFactory.CreateThreadUnarchived(threadId));
        }

        private void HandleThreadCompacted(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _context.RaiseEvent(AppServerNotificationFactory.CreateThreadCompacted(threadId, AppServerJsonValueReader.GetString(parameters, "turnId")));
        }

        private void HandleThreadStatusChanged(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            var status = AppServerJsonValueReader.GetDictionary(parameters, "status");
            var statusType = AppServerJsonValueReader.GetString(status, "type");
            if (!string.IsNullOrWhiteSpace(statusType))
            {
                Trace.TraceInformation(
                    "App Server thread status changed. ThreadId={0}; Status={1}",
                    threadId,
                    AbbreviateForLog(statusType, 300));
                _context.RaiseEvent(AppServerNotificationFactory.CreateThreadStatusChanged(threadId, statusType, AppServerAgentActivityMapper.MapThreadStatus(status)));
            }
        }

        private void HandleThreadNameUpdated(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            var threadName = AppServerJsonValueReader.GetString(parameters, "threadName");
            if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(threadName))
            {
                return;
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "thread_name_updated",
                threadName.Trim(),
                threadId: threadId));
        }

        private void HandleThreadGoalUpdated(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            var turnId = AppServerJsonValueReader.GetString(parameters, "turnId");
            var goal = AppServerProtocolMetadataMapper.BuildThreadGoalFromResult(parameters, threadId);
            if (goal == null)
            {
                return;
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "thread_goal_updated",
                AppServerProtocolTextComposer.BuildThreadGoalUpdatedMessage(goal),
                threadId: goal.ThreadId,
                turnId: turnId,
                threadGoal: goal));
        }

        private void HandleThreadGoalCleared(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "thread_goal_cleared",
                L("AppServerClient.ThreadGoal.Cleared", "Thread goal cleared."),
                threadId: threadId));
        }

        private void HandleTurnStarted(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            var turn = AppServerJsonValueReader.GetDictionary(parameters, "turn");
            var turnId = AppServerJsonValueReader.GetString(turn, "id");

            if (!string.IsNullOrWhiteSpace(threadId))
            {
                _context.RememberLoadedThreadId(threadId);
                if (!string.IsNullOrWhiteSpace(turnId))
                {
                    _context.RememberActiveTurnId(threadId, turnId);
                }
            }

            _context.RaiseEvent(AppServerNotificationFactory.CreateTurnStarted(threadId, turnId));
        }

        private void HandleTurnPlanUpdated(IDictionary<string, object> parameters)
        {
            _context.RaiseEvent(new AppServerEventArgs(
                "turn_plan_updated",
                L("AppServerClient.Turn.PlanUpdated", "Plan updated."),
                threadId: AppServerJsonValueReader.GetString(parameters, "threadId"),
                turnId: AppServerJsonValueReader.GetString(parameters, "turnId"),
                explanation: AppServerJsonValueReader.GetString(parameters, "explanation"),
                planSteps: AppServerTurnPayloadMapper.BuildTurnPlanSteps(parameters)));
        }

        private void HandleTurnDiffUpdated(IDictionary<string, object> parameters)
        {
            var diffText = AppServerTurnPayloadMapper.ExtractDiffText(parameters);
            var changedFiles = AppServerTurnPayloadMapper.ExtractChangedFiles(parameters, diffText);
            if (string.IsNullOrWhiteSpace(diffText) && changedFiles.Count == 0)
            {
                return;
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "turn_diff_updated",
                L("AppServerClient.Turn.DiffUpdated", "Change summary updated."),
                threadId: AppServerJsonValueReader.GetString(parameters, "threadId"),
                turnId: AppServerJsonValueReader.GetString(parameters, "turnId"),
                diffText: diffText,
                changedFiles: changedFiles));
        }

        private void HandleItemStarted(string threadId, string turnId, IDictionary<string, object> parameters)
        {
            var item = AppServerJsonValueReader.GetDictionary(parameters, "item");
            var itemType = AppServerJsonValueReader.GetString(item, "type");
            var itemId = AppServerJsonValueReader.GetString(item, "id");
            if (string.IsNullOrWhiteSpace(itemType))
            {
                return;
            }

            var canonicalActivity = AppServerCanonicalActivityMapper.Map(
                item,
                false,
                threadId,
                turnId,
                _context.MapServerPathToClient);
            if (canonicalActivity != null)
            {
                _context.RaiseEvent(canonicalActivity);
                return;
            }

            switch (itemType)
            {
                case "mcpToolCall":
                    if (string.IsNullOrWhiteSpace(itemId))
                    {
                        return;
                    }

                    var serverName = AppServerJsonValueReader.GetString(item, "server");
                    var toolName = AppServerJsonValueReader.GetString(item, "tool");
                    var status = AppServerJsonValueReader.GetString(item, "status");

                    lock (_syncRoot)
                    {
                        _mcpToolCalls[itemId] = new PendingMcpToolCallItem
                        {
                            ItemId = itemId,
                            ServerName = serverName,
                            ToolName = toolName,
                            Status = status
                        };
                    }

                    _context.RaiseEvent(new AppServerEventArgs(
                        "mcp_tool_progress",
                        AppServerProtocolTextComposer.BuildMcpToolStatusText(null, false),
                        threadId: threadId,
                        turnId: turnId,
                        itemId: itemId,
                        mcpServerName: serverName,
                        mcpToolName: toolName,
                        itemStatus: status));
                    break;
                case "reasoning":
                    if (!string.IsNullOrWhiteSpace(itemId))
                    {
                        lock (_syncRoot)
                        {
                            _reasoningItems[itemId] = new PendingReasoningItem
                            {
                                ItemId = itemId
                            };
                        }
                    }

                    _context.RaiseEvent(new AppServerEventArgs(
                        "reasoning_started",
                        L("AppServerClient.Reasoning.Started", "Analyzing"),
                        threadId: threadId,
                        turnId: turnId,
                        itemId: itemId));
                    break;
                case "fileChange":
                    if (!string.IsNullOrWhiteSpace(itemId))
                    {
                        lock (_syncRoot)
                        {
                            _fileChangeOutputBuffers[itemId] = new StringBuilder();
                        }
                    }

                    _context.RaiseEvent(new AppServerEventArgs(
                        "file_change_progress",
                        L("AppServerClient.FileChange.Summarizing", "Summarizing file changes"),
                        threadId: threadId,
                        turnId: turnId,
                        itemId: itemId));
                    break;
                case "webSearch":
                    _context.RaiseEvent(new AppServerEventArgs(
                        "network_activity_started",
                        BuildWebSearchStatusText(item, true),
                        threadId: threadId,
                        turnId: turnId,
                        itemId: itemId,
                        details: ExtractWebSearchDetails(item)));
                    break;
            }
        }

        private void HandleAgentMessageOutputDelta(string threadId, string turnId, IDictionary<string, object> parameters)
        {
            var itemId = AppServerJsonValueReader.GetString(parameters, "itemId");
            var delta = AppServerJsonValueReader.GetString(parameters, "delta");
            if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrEmpty(delta))
            {
                return;
            }

            lock (_syncRoot)
            {
                StringBuilder builder;
                if (!_agentMessageBuffers.TryGetValue(itemId, out builder))
                {
                    builder = new StringBuilder();
                    _agentMessageBuffers[itemId] = builder;
                }

                builder.Append(delta);
            }
        }

        private void HandlePlanOutputDelta(string threadId, string turnId, IDictionary<string, object> parameters)
        {
            var itemId = AppServerJsonValueReader.GetString(parameters, "itemId");
            var deltaText = AppServerTurnPayloadMapper.ExtractPlanDeltaText(parameters);
            if (string.IsNullOrWhiteSpace(itemId) && string.IsNullOrWhiteSpace(deltaText))
            {
                return;
            }

            var aggregatedText = deltaText;
            if (!string.IsNullOrWhiteSpace(itemId) && !string.IsNullOrEmpty(deltaText))
            {
                lock (_syncRoot)
                {
                    StringBuilder builder;
                    if (!_planItemBuffers.TryGetValue(itemId, out builder))
                    {
                        builder = new StringBuilder();
                        _planItemBuffers[itemId] = builder;
                    }

                    builder.Append(deltaText);
                    aggregatedText = builder.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(aggregatedText))
            {
                return;
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "plan_delta",
                L("AppServerClient.Turn.PlanUpdating", "Plan is being updated."),
                threadId: threadId,
                turnId: turnId,
                itemId: itemId,
                planDeltaText: aggregatedText));
        }

        private void HandleCommandOutputDelta(string threadId, string turnId, IDictionary<string, object> parameters)
        {
            var delta = AppServerJsonValueReader.GetString(parameters, "delta");
            var itemId = AppServerJsonValueReader.GetString(parameters, "itemId");
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                _context.RaiseEvent(new AppServerEventArgs(
                    "command_output",
                    delta,
                    threadId,
                    turnId,
                    itemId));
                return;
            }

            string aggregatedOutput = null;
            lock (_syncRoot)
            {
                StringBuilder builder;
                if (!_commandOutputBuffers.TryGetValue(itemId, out builder))
                {
                    builder = new StringBuilder();
                    _commandOutputBuffers[itemId] = builder;
                }

                builder.Append(delta);
                _commandOutputDeltaItemIds.Add(itemId);

                if (delta.IndexOf('\n') >= 0 || delta.IndexOf('\r') >= 0 || builder.Length >= 2048)
                {
                    aggregatedOutput = builder.ToString();
                    builder.Length = 0;
                }
            }

            if (!string.IsNullOrEmpty(aggregatedOutput))
            {
                _context.RaiseEvent(new AppServerEventArgs(
                    "command_output",
                    aggregatedOutput,
                    threadId,
                    turnId,
                    itemId));
            }
        }

        private void HandleFileChangeOutputDelta(string threadId, string turnId, IDictionary<string, object> parameters)
        {
            var delta = AppServerJsonValueReader.GetString(parameters, "delta");
            var itemId = AppServerJsonValueReader.GetString(parameters, "itemId");
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            string preview;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                preview = BuildFileChangeProgressPreview(delta);
            }
            else
            {
                lock (_syncRoot)
                {
                    StringBuilder builder;
                    if (!_fileChangeOutputBuffers.TryGetValue(itemId, out builder))
                    {
                        builder = new StringBuilder();
                        _fileChangeOutputBuffers[itemId] = builder;
                    }

                    builder.Append(delta);
                    preview = BuildFileChangeProgressPreview(builder.ToString());
                }
            }

            if (string.IsNullOrWhiteSpace(preview))
            {
                return;
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "file_change_progress",
                preview,
                threadId: threadId,
                turnId: turnId,
                itemId: itemId));
        }

        private void HandleFileChangePatchUpdated(IDictionary<string, object> parameters)
        {
            var args = AppServerFileChangePatchMapper.BuildEvent(parameters);
            if (args != null)
            {
                _context.RaiseEvent(args);
            }
        }

        private void HandleStandaloneCommandOutputDelta(IDictionary<string, object> parameters)
        {
            var processId = AppServerJsonValueReader.GetString(parameters, "processId");
            var stream = AppServerJsonValueReader.GetString(parameters, "stream");
            var decodedText = AppServerJsonValueReader.GetString(parameters, "delta");
            if (string.IsNullOrEmpty(decodedText))
            {
                decodedText = DecodeCommandExecDelta(AppServerJsonValueReader.GetString(parameters, "deltaBase64"));
            }

            if (string.IsNullOrEmpty(decodedText))
            {
                return;
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "command_exec_output",
                decodedText,
                processId: processId,
                outputStream: stream));
        }

        private void HandleCommandTerminalInteraction(IDictionary<string, object> parameters)
        {
            var item = AppServerJsonValueReader.GetDictionary(parameters, "item");
            var content = AppServerJsonValueReader.GetFirstNonEmptyString(
                parameters,
                "message",
                "text",
                "prompt",
                "reason",
                "status");
            if (string.IsNullOrWhiteSpace(content))
            {
                content = AppServerJsonValueReader.GetFirstNonEmptyString(item, "message", "text", "prompt", "reason", "status");
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                content = L("AppServerClient.Command.TerminalInteraction", "Command requires input.");
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "command_terminal_interaction",
                content,
                threadId: AppServerJsonValueReader.GetString(parameters, "threadId"),
                turnId: AppServerJsonValueReader.GetString(parameters, "turnId"),
                itemId: AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "itemId") ?? AppServerJsonValueReader.GetString(item, "id"),
                commandText: AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "commandText", "command") ?? AppServerJsonValueReader.GetFirstNonEmptyString(item, "commandText", "command"),
                reason: AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "reason") ?? AppServerJsonValueReader.GetString(item, "reason"),
                details: AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "details") ?? AppServerJsonValueReader.GetString(item, "details"),
                processId: AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "processId") ?? AppServerJsonValueReader.GetString(item, "processId")));
        }

        private bool HandleContextCompactionItemCompleted(IDictionary<string, object> parameters)
        {
            var item = AppServerJsonValueReader.GetDictionary(parameters, "item");
            if (!string.Equals(AppServerJsonValueReader.GetString(item, "type"), "contextCompaction", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "context_compaction_completed",
                "Session context compressed.",
                AppServerJsonValueReader.GetString(parameters, "threadId"),
                AppServerJsonValueReader.GetString(parameters, "turnId"),
                AppServerJsonValueReader.GetString(item, "id")));
            return true;
        }

        private void HandleMcpToolCallProgress(string threadId, string turnId, IDictionary<string, object> parameters)
        {
            var itemId = AppServerJsonValueReader.GetString(parameters, "itemId");
            var message = AppServerJsonValueReader.GetString(parameters, "message");
            PendingMcpToolCallItem pendingItem = null;

            if (!string.IsNullOrWhiteSpace(itemId))
            {
                lock (_syncRoot)
                {
                    _mcpToolCalls.TryGetValue(itemId, out pendingItem);
                }
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "mcp_tool_progress",
                AppServerProtocolTextComposer.BuildMcpToolStatusText(message, false),
                threadId: threadId,
                turnId: turnId,
                itemId: itemId,
                mcpServerName: pendingItem != null ? pendingItem.ServerName : null,
                mcpToolName: pendingItem != null ? pendingItem.ToolName : null,
                itemStatus: pendingItem != null ? pendingItem.Status : null));
        }

        private void HandleReasoningSummaryTextDelta(string threadId, string turnId, IDictionary<string, object> parameters)
        {
            var itemId = AppServerJsonValueReader.GetString(parameters, "itemId");
            var delta = AppServerJsonValueReader.GetString(parameters, "delta");
            var summaryIndex = AppServerJsonValueReader.GetNullableInt(parameters, "summaryIndex") ?? 0;
            if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrEmpty(delta))
            {
                return;
            }

            string aggregatedSummary;
            lock (_syncRoot)
            {
                var pendingItem = GetOrCreatePendingReasoningItem(itemId);
                EnsureIndexedTextBuffer(pendingItem.SummaryParts, summaryIndex).Append(delta);
                aggregatedSummary = BuildReasoningSummaryText(pendingItem);
            }

            if (string.IsNullOrWhiteSpace(aggregatedSummary))
            {
                return;
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "reasoning_summary_delta",
                aggregatedSummary,
                threadId: threadId,
                turnId: turnId,
                itemId: itemId));
        }

        private void HandleReasoningSummaryPartAdded(IDictionary<string, object> parameters)
        {
            var itemId = AppServerJsonValueReader.GetString(parameters, "itemId");
            var summaryIndex = AppServerJsonValueReader.GetNullableInt(parameters, "summaryIndex") ?? 0;
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            lock (_syncRoot)
            {
                var pendingItem = GetOrCreatePendingReasoningItem(itemId);
                EnsureIndexedTextBuffer(pendingItem.SummaryParts, summaryIndex);
            }
        }

        private void HandleReasoningTextDelta(string threadId, string turnId, IDictionary<string, object> parameters)
        {
            var itemId = AppServerJsonValueReader.GetString(parameters, "itemId");
            var delta = AppServerJsonValueReader.GetString(parameters, "delta");
            var contentIndex = AppServerJsonValueReader.GetNullableInt(parameters, "contentIndex") ?? 0;
            if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrEmpty(delta))
            {
                return;
            }

            string preview;
            lock (_syncRoot)
            {
                var pendingItem = GetOrCreatePendingReasoningItem(itemId);
                EnsureIndexedTextBuffer(pendingItem.ContentParts, contentIndex).Append(delta);
                preview = BuildReasoningTextPreview(BuildReasoningContentText(pendingItem));
            }

            if (string.IsNullOrWhiteSpace(preview))
            {
                return;
            }

            _context.RaiseEvent(new AppServerEventArgs(
                "reasoning_text_delta",
                preview,
                threadId: threadId,
                turnId: turnId,
                itemId: itemId));
        }

        private void HandleItemCompleted(string threadId, string turnId, IDictionary<string, object> parameters)
        {
            var item = AppServerJsonValueReader.GetDictionary(parameters, "item");
            var itemType = AppServerJsonValueReader.GetString(item, "type");
            threadId = string.IsNullOrWhiteSpace(threadId) ? AppServerJsonValueReader.GetString(parameters, "threadId") : threadId;
            turnId = string.IsNullOrWhiteSpace(turnId) ? AppServerJsonValueReader.GetString(parameters, "turnId") : turnId;
            var itemId = AppServerJsonValueReader.GetString(item, "id");
            var canonicalActivity = AppServerCanonicalActivityMapper.Map(
                item,
                true,
                threadId,
                turnId,
                _context.MapServerPathToClient);
            if (canonicalActivity != null)
            {
                _context.RaiseEvent(canonicalActivity);
                return;
            }

            string bufferedPlanText = null;
            string bufferedCommandOutput = null;
            string bufferedFileChangeOutput = null;
            var sawCommandOutputDelta = false;
            PendingMcpToolCallItem pendingMcpToolCall = null;
            PendingReasoningItem pendingReasoningItem = null;

            lock (_syncRoot)
            {
                if (!string.IsNullOrWhiteSpace(itemId))
                {
                    StringBuilder planBuilder;
                    if (_planItemBuffers.TryGetValue(itemId, out planBuilder))
                    {
                        bufferedPlanText = planBuilder.ToString();
                        _planItemBuffers.Remove(itemId);
                    }

                    StringBuilder commandOutputBuilder;
                    if (_commandOutputBuffers.TryGetValue(itemId, out commandOutputBuilder))
                    {
                        bufferedCommandOutput = commandOutputBuilder.ToString();
                        _commandOutputBuffers.Remove(itemId);
                    }

                    if (_commandOutputDeltaItemIds.Contains(itemId))
                    {
                        sawCommandOutputDelta = true;
                        _commandOutputDeltaItemIds.Remove(itemId);
                    }

                    StringBuilder fileChangeOutputBuilder;
                    if (_fileChangeOutputBuffers.TryGetValue(itemId, out fileChangeOutputBuilder))
                    {
                        bufferedFileChangeOutput = fileChangeOutputBuilder.ToString();
                        _fileChangeOutputBuffers.Remove(itemId);
                    }

                    if (_mcpToolCalls.TryGetValue(itemId, out pendingMcpToolCall))
                    {
                        _mcpToolCalls.Remove(itemId);
                    }

                    if (_reasoningItems.TryGetValue(itemId, out pendingReasoningItem))
                    {
                        _reasoningItems.Remove(itemId);
                    }
                }
            }

            switch (itemType)
            {
                case "agentMessage":
                    var finalText = AppServerJsonValueReader.GetString(item, "text");
                    if (string.IsNullOrWhiteSpace(finalText))
                    {
                        lock (_syncRoot)
                        {
                            StringBuilder builder;
                            if (_agentMessageBuffers.TryGetValue(itemId, out builder))
                            {
                                finalText = builder.ToString();
                            }
                        }
                    }

                    lock (_syncRoot)
                    {
                        _agentMessageBuffers.Remove(itemId);
                    }

                    if (string.Equals(AppServerJsonValueReader.GetString(item, "delivery"), "async", StringComparison.OrdinalIgnoreCase))
                    {
                        var questions = AppServerInteractionRequestMapper.BuildAsyncUserInputQuestions(item);
                        if (questions.Count > 0)
                        {
                            _context.RaiseEvent(new AppServerEventArgs(
                                "async_input_requested",
                                finalText,
                                threadId,
                                turnId,
                                itemId,
                                toolQuestions: questions));
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(finalText))
                    {
                        _context.RaiseEvent(new AppServerEventArgs("assistant", finalText, threadId, turnId, itemId));
                    }
                    break;
                case "plan":
                    var finalPlanText = AppServerJsonValueReader.GetString(item, "text");
                    if (string.IsNullOrWhiteSpace(finalPlanText))
                    {
                        finalPlanText = bufferedPlanText;
                    }

                    if (!string.IsNullOrWhiteSpace(finalPlanText))
                    {
                        _context.RaiseEvent(new AppServerEventArgs("plan", AppServerProtocolTextComposer.BuildPlanMessageText(finalPlanText), threadId, turnId, itemId));
                    }
                    break;
                case "commandExecution":
                    var command = AppServerJsonValueReader.GetString(item, "command");
                    var commandWorkingDirectory = _context.MapServerPathToClient(AppServerJsonValueReader.GetString(item, "cwd"));
                    var aggregatedOutput = AppServerJsonValueReader.GetString(item, "aggregatedOutput");
                    if (!string.IsNullOrWhiteSpace(bufferedCommandOutput))
                    {
                        _context.RaiseEvent(new AppServerEventArgs(
                            "command_output",
                            bufferedCommandOutput,
                            threadId,
                            turnId,
                            itemId,
                            commandText: command,
                            workingDirectory: commandWorkingDirectory));
                    }

                    if (!string.IsNullOrWhiteSpace(aggregatedOutput) && !sawCommandOutputDelta)
                    {
                        _context.RaiseEvent(new AppServerEventArgs(
                            "command_output",
                            aggregatedOutput,
                            threadId,
                            turnId,
                            itemId,
                            commandText: command,
                            workingDirectory: commandWorkingDirectory));
                    }

                    var status = AppServerJsonValueReader.GetString(item, "status");
                    var exitCode = AppServerJsonValueReader.GetNullableInt(item, "exitCode");
                    var summary = string.Format(
                        L("AppServerClient.Command.CompletedSummary.Template", "Command finished: {0}, status: {1}{2}"),
                        string.IsNullOrWhiteSpace(command) ? L("AppServerClient.Value.UnknownCommand", "(Unknown command)") : command,
                        string.IsNullOrWhiteSpace(status) ? L("AppServerClient.Value.Unknown", "(Unknown)") : status,
                        exitCode.HasValue
                            ? LF("AppServerClient.Command.ExitCodeSuffix.Template", ", exit code: {0}", exitCode.Value)
                            : string.Empty);
                    _context.RaiseEvent(new AppServerEventArgs("command_completed", summary, threadId, turnId, itemId, commandText: command, workingDirectory: commandWorkingDirectory));
                    break;
                case "reasoning":
                    var finalReasoningSummary = BuildReasoningSummaryText(item);
                    if (string.IsNullOrWhiteSpace(finalReasoningSummary) && pendingReasoningItem != null)
                    {
                        finalReasoningSummary = BuildReasoningSummaryText(pendingReasoningItem);
                    }

                    var finalReasoningText = BuildReasoningContentText(item);
                    if (string.IsNullOrWhiteSpace(finalReasoningText) && pendingReasoningItem != null)
                    {
                        finalReasoningText = BuildReasoningContentText(pendingReasoningItem);
                    }

                    var finalReasoningPreview = BuildReasoningTextPreview(finalReasoningText);
                    _context.RaiseEvent(new AppServerEventArgs(
                        "reasoning_completed",
                        !string.IsNullOrWhiteSpace(finalReasoningSummary)
                            ? finalReasoningSummary
                            : (!string.IsNullOrWhiteSpace(finalReasoningPreview)
                                ? finalReasoningPreview
                                : L("AppServerClient.Reasoning.Completed", "Analysis completed")),
                        threadId: threadId,
                        turnId: turnId,
                        itemId: itemId,
                        details: finalReasoningPreview));
                    break;
                case "fileChange":
                    List<string> changedFiles;
                    var fileChangeDetails = AppServerProtocolTextComposer.BuildFileChangeSummaryText(item, bufferedFileChangeOutput, out changedFiles);
                    _context.RaiseEvent(new AppServerEventArgs(
                        "file_change_completed",
                        L("AppServerClient.FileChange.Completed", "File changes completed"),
                        threadId: threadId,
                        turnId: turnId,
                        itemId: itemId,
                        details: fileChangeDetails,
                        changedFiles: changedFiles));
                    break;
                case "mcpToolCall":
                    var serverName = AppServerJsonValueReader.GetString(item, "server") ?? (pendingMcpToolCall != null ? pendingMcpToolCall.ServerName : null);
                    var toolName = AppServerJsonValueReader.GetString(item, "tool") ?? (pendingMcpToolCall != null ? pendingMcpToolCall.ToolName : null);
                    var mcpStatus = AppServerJsonValueReader.GetString(item, "status") ?? (pendingMcpToolCall != null ? pendingMcpToolCall.Status : null);
                    var error = AppServerJsonValueReader.GetDictionary(item, "error");
                    var errorMessage = AppServerJsonValueReader.GetFirstNonEmptyString(error, "message", "text");
                    var isCompleted = string.Equals(mcpStatus, "completed", StringComparison.OrdinalIgnoreCase);
                    var isFailed = string.Equals(mcpStatus, "failed", StringComparison.OrdinalIgnoreCase);
                    _context.RaiseEvent(new AppServerEventArgs(
                        "mcp_tool_completed",
                        !string.IsNullOrWhiteSpace(errorMessage)
                            ? errorMessage
                            : (isCompleted
                                ? L("AppServerClient.Status.Completed", "Completed")
                                : (isFailed
                                    ? L("AppServerClient.Status.Failed", "Failed")
                                    : L("AppServerClient.Status.Ended", "Ended"))),
                        threadId: threadId,
                        turnId: turnId,
                        itemId: itemId,
                        mcpServerName: serverName,
                        mcpToolName: toolName,
                        itemStatus: mcpStatus));
                    break;
                case "webSearch":
                    _context.RaiseEvent(new AppServerEventArgs(
                        "network_activity_completed",
                        BuildWebSearchStatusText(item, false),
                        threadId: threadId,
                        turnId: turnId,
                        itemId: itemId,
                        details: ExtractWebSearchDetails(item),
                        itemStatus: AppServerJsonValueReader.GetString(item, "status")));
                    break;
            }
        }

        private static string BuildWebSearchStatusText(IDictionary<string, object> item, bool started)
        {
            var details = ExtractWebSearchDetails(item);
            var prefix = started
                ? L("AppServerClient.Network.WebSearchStarted", "Web search started")
                : L("AppServerClient.Network.WebSearchCompleted", "Web search completed");
            return string.IsNullOrWhiteSpace(details)
                ? prefix
                : prefix + ": " + details;
        }

        private static string ExtractWebSearchDetails(IDictionary<string, object> item)
        {
            return AppServerJsonValueReader.GetFirstNonEmptyString(
                item,
                "query",
                "searchQuery",
                "title",
                "summary",
                "content",
                "text");
        }

        private void HandleTurnCompleted(string threadId, string turnId, IDictionary<string, object> parameters)
        {
            var turn = AppServerJsonValueReader.GetDictionary(parameters, "turn");
            turnId = AppServerJsonValueReader.GetString(turn, "id");

            _context.ForgetActiveTurnId(threadId);

            var status = AppServerJsonValueReader.GetString(turn, "status");
            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                var error = AppServerJsonValueReader.GetDictionary(turn, "error");
                var errorMessage = AppServerJsonValueReader.GetString(error, "message");
                var reason = AppServerJsonValueReader.GetString(error, "codexErrorInfo");
                HandleTurnFailed(threadId, turnId, errorMessage, reason);
                return;
            }

            if (string.Equals(status, "interrupted", StringComparison.OrdinalIgnoreCase))
            {
                _context.RaiseEvent(AppServerNotificationFactory.CreateTurnInterrupted(threadId, turnId));
                return;
            }

            _context.RaiseEvent(AppServerNotificationFactory.CreateTurnCompleted(threadId, turnId));
        }

        private void HandleTurnFailed(string threadId, string turnId, string errorMessage, string reason = null)
        {
            var eventArgs = AppServerNotificationFactory.CreateTurnFailed(threadId, turnId, errorMessage, reason);
            Trace.TraceWarning(
                "App Server turn failed. ThreadId={0}; TurnId={1}; Message={2}",
                threadId,
                turnId,
                AbbreviateForLog(eventArgs.Content, 1000));
            _context.RaiseEvent(eventArgs);
        }

        private void HandleError(IDictionary<string, object> parameters)
        {
            var error = AppServerJsonValueReader.GetDictionary(parameters, "error");
            var errorMessage = AppServerJsonValueReader.GetString(error, "message");
            var reason = AppServerJsonValueReader.GetString(error, "codexErrorInfo");
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            var turnId = AppServerJsonValueReader.GetString(parameters, "turnId");
            var willRetry = AppServerJsonValueReader.GetNullableBool(parameters, "willRetry") == true;
            var eventArgs = AppServerNotificationFactory.CreateError(threadId, turnId, errorMessage, reason, willRetry);
            Trace.TraceWarning(
                "App Server error event. ThreadId={0}; TurnId={1}; Message={2}",
                threadId,
                turnId,
                AbbreviateForLog(eventArgs.Content, 1000));
            _context.RaiseEvent(eventArgs);
        }

        private void HandleAccountUpdated(IDictionary<string, object> parameters)
        {
            var authMode = AppServerJsonValueReader.GetString(parameters, "authMode");
            var planType = AppServerJsonValueReader.GetString(parameters, "planType");
            _context.RaiseEvent(AppServerNotificationFactory.CreateAuthChanged(authMode, planType));
        }

        private void HandleAccountLoginCompleted(IDictionary<string, object> parameters)
        {
            var success = AppServerJsonValueReader.GetNullableBool(parameters, "success").GetValueOrDefault();
            _context.RaiseEvent(AppServerNotificationFactory.CreateAuthLoginCompleted(
                success,
                AppServerJsonValueReader.GetString(parameters, "error"),
                AppServerJsonValueReader.GetString(parameters, "loginId")));
        }

        private void HandleMcpServerOauthLoginCompleted(IDictionary<string, object> parameters)
        {
            var serverName = AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "name", "serverName");
            var success = AppServerJsonValueReader.GetNullableBool(parameters, "success");
            _context.RaiseEvent(AppServerNotificationFactory.CreateMcpOauthLoginCompleted(
                serverName,
                success,
                AppServerJsonValueReader.GetString(parameters, "error")));
        }

        private void HandleSkillsChanged()
        {
            _context.RaiseEvent(new AppServerEventArgs(
                "skills_changed",
                L("AppServerClient.Skills.Changed", "Detected changes in skill files.")));
        }

        private void HandleRateLimitsUpdated()
        {
            _context.RaiseEvent(AppServerNotificationFactory.CreateRateLimitsChanged());
        }

        private void HandleThreadTokenUsageUpdated(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            var turnId = AppServerJsonValueReader.GetString(parameters, "turnId");
            var tokenUsage = AppServerJsonValueReader.GetDictionary(parameters, "tokenUsage");
            var total = AppServerJsonValueReader.GetDictionary(tokenUsage, "total");
            var last = AppServerJsonValueReader.GetDictionary(tokenUsage, "last");
            var usage = new AppServerThreadTokenUsageSnapshot
            {
                Total = BuildTokenUsageBreakdown(total),
                Last = BuildTokenUsageBreakdown(last),
                ModelContextWindow = AppServerJsonValueReader.GetNullableLong(tokenUsage, "modelContextWindow")
            };

            _context.RaiseEvent(AppServerNotificationFactory.CreateTokenUsage(
                threadId,
                turnId,
                usage));
        }

        private static AppServerTokenUsageBreakdown BuildTokenUsageBreakdown(IDictionary<string, object> source)
        {
            return new AppServerTokenUsageBreakdown
            {
                TotalTokens = AppServerJsonValueReader.GetNullableLong(source, "totalTokens").GetValueOrDefault(),
                InputTokens = AppServerJsonValueReader.GetNullableLong(source, "inputTokens").GetValueOrDefault(),
                CachedInputTokens = AppServerJsonValueReader.GetNullableLong(source, "cachedInputTokens").GetValueOrDefault(),
                OutputTokens = AppServerJsonValueReader.GetNullableLong(source, "outputTokens").GetValueOrDefault(),
                ReasoningOutputTokens = AppServerJsonValueReader.GetNullableLong(source, "reasoningOutputTokens").GetValueOrDefault()
            };
        }

        private void HandleServerRequestResolved(IDictionary<string, object> parameters)
        {
            _context.ClearResolvedInteractionRequest(AppServerJsonValueReader.GetString(parameters, "requestId"));
        }

        private PendingReasoningItem GetOrCreatePendingReasoningItem(string itemId)
        {
            PendingReasoningItem pendingItem;
            if (!_reasoningItems.TryGetValue(itemId, out pendingItem))
            {
                pendingItem = new PendingReasoningItem
                {
                    ItemId = itemId
                };
                _reasoningItems[itemId] = pendingItem;
            }

            return pendingItem;
        }

        private static StringBuilder EnsureIndexedTextBuffer(SortedDictionary<int, StringBuilder> parts, int index)
        {
            if (parts == null)
            {
                return new StringBuilder();
            }

            StringBuilder builder;
            if (!parts.TryGetValue(index, out builder))
            {
                builder = new StringBuilder();
                parts[index] = builder;
            }

            return builder;
        }

        private static string BuildReasoningSummaryText(PendingReasoningItem pendingItem)
        {
            return pendingItem == null ? string.Empty : JoinIndexedTextParts(pendingItem.SummaryParts);
        }

        private static string BuildReasoningContentText(PendingReasoningItem pendingItem)
        {
            return pendingItem == null ? string.Empty : JoinIndexedTextParts(pendingItem.ContentParts);
        }

        private static string BuildReasoningSummaryText(IDictionary<string, object> item)
        {
            return JoinStringArray(item, "summary");
        }

        private static string BuildReasoningContentText(IDictionary<string, object> item)
        {
            return JoinStringArray(item, "content");
        }

        private static string JoinIndexedTextParts(SortedDictionary<int, StringBuilder> parts)
        {
            if (parts == null || parts.Count == 0)
            {
                return string.Empty;
            }

            var lines = new List<string>();
            foreach (var pair in parts)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                var text = pair.Value.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text);
                }
            }

            return lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines.ToArray());
        }

        private static string JoinStringArray(IDictionary<string, object> source, string key)
        {
            var lines = new List<string>();
            foreach (var item in AppServerJsonValueReader.GetArray(source, key))
            {
                var text = Convert.ToString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text.Trim());
                }
            }

            return lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines.ToArray());
        }

        private static string BuildReasoningTextPreview(string text)
        {
            return AppServerProtocolTextComposer.BuildRecentTextPreview(text, 3, 220);
        }

        private static string BuildFileChangeProgressPreview(string text)
        {
            return AppServerProtocolTextComposer.BuildFileChangeProgressPreview(text);
        }

        private static string DecodeCommandExecDelta(string deltaBase64)
        {
            if (string.IsNullOrWhiteSpace(deltaBase64))
            {
                return string.Empty;
            }

            try
            {
                var bytes = Convert.FromBase64String(deltaBase64);
                try
                {
                    return new UTF8Encoding(false, true).GetString(bytes);
                }
                catch
                {
                    return Encoding.Default.GetString(bytes);
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string AbbreviateForLog(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0 || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "...";
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key, fallback), args ?? Array.Empty<object>());
        }

        private sealed class PendingMcpToolCallItem
        {
            public string ItemId { get; set; }

            public string ServerName { get; set; }

            public string ToolName { get; set; }

            public string Status { get; set; }
        }

        private sealed class PendingReasoningItem
        {
            public PendingReasoningItem()
            {
                SummaryParts = new SortedDictionary<int, StringBuilder>();
                ContentParts = new SortedDictionary<int, StringBuilder>();
            }

            public string ItemId { get; set; }

            public SortedDictionary<int, StringBuilder> SummaryParts { get; private set; }

            public SortedDictionary<int, StringBuilder> ContentParts { get; private set; }
        }
    }
}
