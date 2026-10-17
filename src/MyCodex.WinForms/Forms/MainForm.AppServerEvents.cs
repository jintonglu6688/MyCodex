using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly ApprovalRequestDialogQueue _approvalRequestDialogQueue = new ApprovalRequestDialogQueue();

        private void AppServerClientOnEventReceived(object sender, AppServerEventArgs e)
        {
            if (IsTargetStopping) return;
            if (InvokeRequired || !IsHandleCreated)
            {
                if (AppServerEventRouting.IsBufferedCommandOutputEvent(e))
                {
                    QueueBufferedCommandOutputEvent(e);
                    return;
                }

                if (AppServerEventRouting.IsBufferedTurnSummaryEvent(e))
                {
                    QueueBufferedTurnSummaryEvent(e);
                    return;
                }

                this.BeginInvokeSafe(() => AppServerClientOnEventReceived(sender, e));
                return;
            }

            if (_reviewStartEvents != null && _reviewStartEvents.IsPending &&
                _reviewStartEvents.TryBuffer(e, e != null && IsKnownThreadId(e.ThreadId)))
            {
                return;
            }

            if (_remoteAppServerEventForwarder != null)
            {
                _ = _remoteAppServerEventForwarder.ForwardAsync(e, System.Threading.CancellationToken.None);
            }

            TrackSubAgentWork(e);
            if (e != null && string.Equals(e.EventType, "thread_deleted", StringComparison.Ordinal))
                HandleConfirmedThreadDeletion(e.ThreadId);
            if (e != null && string.Equals(e.EventType, "thread_archived", StringComparison.Ordinal))
                RemoveConfirmedThreadLocally(e.ThreadId);
            ForwardAppServerEventToSubAgentWorkbenches(e);
            ForwardAppServerEventToSessionWorkbenches(e);
            ForwardWindowsSandboxSetupCompletedToRuntimeSettings(e);

            var route = AppServerEventRouting.Resolve(e);
            ForwardAppServerFileWindowEvent(e);
            switch (route)
            {
                case AppServerEventRoute.AuthenticationChanged:
                    // The transition/recovery flow refreshes these itself. A second model
                    // refresh would invalidate the in-flight read used to confirm reconnect.
                    if (!_isRuntimeWindowTransitionInProgress && !_isRecoveringAppServerConnection)
                    {
                        _ = RefreshAuthenticationStateAsync(false);
                        _ = LoadAvailableModelsAsync(true);
                    }
                    ApplyCurrentActivityTimelineEvent(e);
                    AddChatHistoryItem(GetStatusSenderText(), e.Content, ChatHistoryCategory.Account);
                    SetStatus(e.Content);
                    return;
                case AppServerEventRoute.McpStartupStatusUpdated:
                    HandleMcpStartupStatusUpdated(e);
                    return;
                case AppServerEventRoute.Ignore:
                    return;
                case AppServerEventRoute.StandaloneCommandOutput:
                    HandleStandaloneCommandOutputEvent(e);
                    return;
                case AppServerEventRoute.SystemNotice:
                    AppendLog(e.Content);
                    return;
                case AppServerEventRoute.AppServerDisconnected:
                    HandleAppServerDisconnected(e);
                    return;
                case AppServerEventRoute.ThreadTokenUsageChanged:
                    HandleThreadTokenUsageChanged(e);
                    UpdateTurnProgressSummaryState(e);
                    return;
            }

            if (AppServerEventRouting.IsTurnTerminal(route))
            {
                UpdateTurnProgressSummaryState(e);
                TurnProgressSummaryState state = null;
                if (MainFormWorkbenchStatePlanner.ShouldEnsureTurnSummaryState(e.ThreadId))
                {
                    state = EnsureThreadTurnSummaryState(e.ThreadId);
                }

                var status = MainFormWorkbenchStatePlanner.ResolveAutomationTerminalStatus(route);
                var completion = AutomationRunCompletionBuilder.Build(
                    status,
                    e.Content,
                    route == AppServerEventRoute.TurnFailed ? e.Content : null,
                    state);
                bool automationUpdated;
                if (!TryHandleAutomationTurnTerminal(e, completion, out automationUpdated))
                {
                    return;
                }

                var automationsFormAvailable = _automationsForm != null && !_automationsForm.IsDisposed;
                if (MainFormWorkbenchStatePlanner.ShouldRefreshAutomationForm(automationUpdated, automationsFormAvailable))
                {
                    _automationsForm.RefreshAutomationState();
                }
            }
            else
            {
                UpdateTurnProgressSummaryState(e);
            }

            HandleReasoningSummaryChatMessageEvent(e, route);

            if (ThreadDiscoveryRefreshPlanner.ShouldRequestThreadListRefresh(route, e != null ? e.ThreadId : null, IsKnownThreadId))
            {
                if (ShouldScheduleAutomaticThreadSyncFollowUp(route, e))
                {
                    ScheduleAutomaticThreadSyncFollowUp();
                }

                ScheduleLocalThreadSessionFileSync();
                _ = RunAutomaticThreadSyncAsync(false);
            }

            if (route == AppServerEventRoute.SummaryOnly)
            {
                return;
            }

            if (ShouldInvalidateHistoryForEvent(e))
            {
                InvalidateSessionHistoryCache(e.ThreadId);
                ScheduleRealtimeCurrentSessionHistoryReload(e);
            }

            if (route == AppServerEventRoute.TurnStarted)
            {
                MarkThreadBusy(e.ThreadId);
                MarkPausedGoalActiveForRunningTurn(e.ThreadId);
            }
            else if (AppServerEventRouting.IsTurnTerminal(route))
            {
                var wasBusy = IsThreadBusy(e.ThreadId);
                ClearThreadBusy(e.ThreadId);
                RefreshGoalAfterTurnTerminal(e.ThreadId);
                if (wasBusy && AppServerEventRouting.IsTurnCompleted(route))
                {
                    HandleThreadBecameIdleNotification(e);
                }

                if (AppServerEventRouting.IsTurnCompleted(route) ||
                    route == AppServerEventRoute.TurnInterrupted)
                {
                    ClearPendingInvalidEncryptedContentRetry(e.ThreadId);
                }

                ClearLocalDesktopRealtimeTurn(e.ThreadId);
                ScheduleQueuedFollowUpDispatch(e.ThreadId);
            }

            if (TurnInputStateRefreshPlanner.ShouldRefresh(route))
            {
                RefreshInputState();
            }

            if (route == AppServerEventRoute.TurnStarted &&
                TryHandleCompactionTurnStarted(e))
            {
                return;
            }

            switch (route)
            {
                case AppServerEventRoute.ContextCompactionCompleted:
                    HandleContextCompactionCompleted(e);
                    return;
                case AppServerEventRoute.TurnCompleted:
                    if (TryHandleCompactionTurnCompleted(e))
                    {
                        return;
                    }

                    _ = PromptForPlanExecutionIfReadyAsync(e);
                    return;
                case AppServerEventRoute.ApprovalRequested:
                    QueueApprovalRequestDialog(e);
                    return;
                case AppServerEventRoute.ApprovalResolved:
                    _approvalRequestDialogQueue.MarkResolved(e.RequestId);
                    _remoteInteractionDialogs.Complete(e.RequestId);
                    if (!ShouldDisplayThreadEvent(e))
                    {
                        return;
                    }

                    AddThreadEventHistoryItem(e, GetStatusSenderText(), e.Content, ChatHistoryCategory.Approval);
                    SetStatus(e.Content);
                    return;
                case AppServerEventRoute.ToolUserInputRequested:
                    if (ShouldDisplayThreadEvent(e))
                    {
                        AddThreadEventHistoryItem(e, GetStatusSenderText(), e.Content, ChatHistoryCategory.System);
                    }
                    SetHighPriorityStatus(e.IsMcpElicitationConfirmation
                        ? UiLocalization.Text("MainForm.Status.McpConfirmationRequested", "Received MCP confirmation request")
                        : UiLocalization.Text("MainForm.Status.ToolInputRequested", "Received tool input request"));
                    ShowToolUserInputDialog(e);
                    return;
                case AppServerEventRoute.AsyncInputRequested:
                    HandleAsyncInputRequested(e);
                    return;
                case AppServerEventRoute.McpElicitationUrlRequested:
                    if (ShouldDisplayThreadEvent(e))
                    {
                        AddThreadEventHistoryItem(e, GetStatusSenderText(), e.Content, ChatHistoryCategory.System);
                    }

                    SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.McpElicitationRequested", "Received MCP input request"));
                    ShowMcpElicitationUrlDialog(e);
                    return;
                case AppServerEventRoute.ToolUserInputResolved:
                    _remoteInteractionDialogs.Complete(e.RequestId);
                    if (!ShouldDisplayThreadEvent(e))
                    {
                        return;
                    }

                    AddThreadEventHistoryItem(e, GetStatusSenderText(), e.Content, ChatHistoryCategory.System);
                    SetStatus(e.Content);
                    return;
                case AppServerEventRoute.ProtocolWarning:
                    if (!ShouldDisplayThreadEvent(e))
                    {
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(e.ThreadId)) ApplyCurrentActivityTimelineEvent(e);
                    AddThreadEventHistoryItem(e, GetErrorSenderText(), e.Content, ChatHistoryCategory.ProtocolWarning);
                    SetAppServerStatus(e, true);
                    return;
                case AppServerEventRoute.ProtocolNotice:
                    if (!ShouldDisplayThreadEvent(e))
                    {
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(e.ThreadId)) ApplyCurrentActivityTimelineEvent(e);
                    AddThreadEventHistoryItem(e, GetStatusSenderText(), e.Content, ChatHistoryCategory.ProtocolNotice);
                    SetStatus(e.Content);
                    return;
                case AppServerEventRoute.ThreadArchivedChanged:
                    return;
                case AppServerEventRoute.ThreadGoalChanged:
                    HandleThreadGoalChanged(e);
                    return;
                case AppServerEventRoute.ThreadNameChanged:
                    ApplySyncedThreadTitles(new[]
                    {
                        new AppServerThreadSummary
                        {
                            ThreadId = e.ThreadId,
                            Title = e.Content
                        }
                    });
                    return;
                case AppServerEventRoute.Assistant:
                {
                    if (!ShouldDisplayThreadEvent(e) || !AssistantMessageUtility.HasDisplayableContent(e.Content))
                    {
                        return;
                    }

                    var chatItem = AddThreadEventHistoryItem(e, ChatHistoryItemFactory.GetCodexSenderText(), e.Content, ChatHistoryCategory.Conversation);
                    RegisterExecutablePlanCandidate(e);
                    // The pending-input window announces its companion event; do not read the same question twice.
                    if (!_asyncInputRequests.ContainsSource(e))
                    {
                        TryAutoReadIncomingMessage(chatItem, e.ThreadId);
                    }
                    SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.AssistantReplyReceived", "Reply received"));
                    return;
                }
                case AppServerEventRoute.CommandOutput:
                    if (!ShouldDisplayThreadEvent(e))
                    {
                        return;
                    }

                    AppendCommandTerminalEntry(GetCommandTerminalEntryKind(e.OutputStream), e.Content);
                    SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.CommandRunning", "Command is running"));
                    return;
                case AppServerEventRoute.CommandTerminalInteraction:
                    HandleCommandTerminalInteractionEvent(e);
                    return;
                case AppServerEventRoute.InternalToolStatus:
                    HandleInternalToolStatusEvent(e);
                    return;
            }

            if (AppServerEventRouting.IsErrorDisplayEvent(route))
            {
                if (TryHandleCompactionFailed(e))
                {
                    return;
                }

                if (TryHandleUnsupportedFlexServiceTier(e))
                {
                    return;
                }

                if (TryHandleInvalidEncryptedContentFailure(route, e))
                {
                    return;
                }

                if (route == AppServerEventRoute.TurnFailed)
                {
                    ClearPendingInvalidEncryptedContentRetry(e.ThreadId);
                }

                if (!ShouldDisplayThreadEvent(e))
                {
                    return;
                }

                AddThreadEventHistoryItem(e, GetErrorSenderText(), e.Content, ChatHistoryCategory.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.ErrorOccurred", "An error occurred"));
                return;
            }

            if (!ShouldDisplayThreadEvent(e))
            {
                return;
            }

            AddThreadEventHistoryItem(e, GetStatusSenderText(), e.Content, AppServerEventRouting.GetFallbackCategory(route));
            var statusText = route == AppServerEventRoute.CommandCompleted
                ? ActivityCommandTextFormatter.CompactCompletionText(e.Content, e.CommandText, e.WorkingDirectory)
                : e.Content;
            SetAppServerStatus(e, false, statusText);
        }

        private void ForwardAppServerEventToSubAgentWorkbenches(AppServerEventArgs e)
        {
            if (e == null || _subAgentWorkbenches.Count == 0)
            {
                return;
            }

            foreach (var form in _subAgentWorkbenches.Values.ToArray())
            {
                if (form == null || form.IsDisposed)
                {
                    continue;
                }

                form.HandleAppServerEvent(e);
            }
        }

        private void ForwardWindowsSandboxSetupCompletedToRuntimeSettings(AppServerEventArgs e)
        {
            if (e == null || e.WindowsSandboxSetupCompleted == null)
            {
                return;
            }

            var form = _appServerRuntimeSettingsForm;
            if (form == null || form.IsDisposed)
            {
                return;
            }

            form.ApplyWindowsSandboxSetupCompleted(e.WindowsSandboxSetupCompleted);
        }

        private void CloseSubAgentWorkbenchForms()
        {
            if (_subAgentWorkbenches.Count == 0)
            {
                return;
            }

            var forms = _subAgentWorkbenches.Values.ToArray();
            _subAgentWorkbenches.Clear();
            foreach (var form in forms)
            {
                if (form == null || form.IsDisposed)
                {
                    continue;
                }

                try
                {
                    form.Close();
                }
                catch
                {
                }
            }
        }

        private void CloseSubAgentWorkbenchesForMissingParents()
        {
            if (_subAgentWorkbenches.Count == 0)
            {
                return;
            }

            var activeSessionIds = new HashSet<string>(
                _sessions
                    .Where(session => session != null && !string.IsNullOrWhiteSpace(session.Id))
                    .Select(session => session.Id),
                StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _subAgentWorkbenches.ToArray())
            {
                if (activeSessionIds.Contains(pair.Key))
                {
                    continue;
                }

                _subAgentWorkbenches.Remove(pair.Key);
                var form = pair.Value;
                if (form == null || form.IsDisposed)
                {
                    continue;
                }

                try
                {
                    form.Close();
                }
                catch
                {
                }
            }
        }

        private void QueueBufferedCommandOutputEvent(AppServerEventArgs e)
        {
            if (e == null || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (!_bufferedCommandOutputState.Enqueue(e))
            {
                return;
            }

            try
            {
                if (!this.BeginInvokeSafe(FlushBufferedCommandOutputEvents))
                {
                    _bufferedCommandOutputState.Reset();
                }
            }
            catch (InvalidOperationException)
            {
                _bufferedCommandOutputState.Reset();
            }
        }

        private void FlushBufferedCommandOutputEvents()
        {
            List<AppServerEventArgs> pendingEvents;
            bool hasMore;
            if (!_bufferedCommandOutputState.TryDequeueBatch(
                MaxBufferedCommandOutputEventsPerUiPass,
                out pendingEvents,
                out hasMore))
            {
                return;
            }

            for (var i = 0; i < pendingEvents.Count; i++)
            {
                AppServerClientOnEventReceived(_appServerClient, pendingEvents[i]);
            }

            if (hasMore && !this.BeginInvokeSafe(FlushBufferedCommandOutputEvents))
            {
                _bufferedCommandOutputState.Reset();
            }
        }

        private void QueueBufferedTurnSummaryEvent(AppServerEventArgs e)
        {
            if (!_bufferedTurnSummaryEventState.Enqueue(e))
            {
                return;
            }

            if (!this.BeginInvokeSafe(FlushBufferedTurnSummaryEvents))
            {
                _bufferedTurnSummaryEventState.Reset();
            }
        }

        private void FlushBufferedTurnSummaryEvents()
        {
            List<AppServerEventArgs> pendingEvents;
            if (!_bufferedTurnSummaryEventState.TryDequeueAll(out pendingEvents))
            {
                return;
            }

            for (var i = 0; i < pendingEvents.Count; i++)
            {
                AppServerClientOnEventReceived(_appServerClient, pendingEvents[i]);
            }
        }

        private static string BuildBufferedTurnSummaryEventKey(AppServerEventArgs e)
        {
            if (e == null)
            {
                return string.Empty;
            }

            return string.Join(
                "|",
                new[]
                {
                    e.EventType ?? string.Empty,
                    e.ThreadId ?? string.Empty,
                    e.TurnId ?? string.Empty,
                    e.ItemId ?? string.Empty
                });
        }

        private void HandleThreadBecameIdleNotification(AppServerEventArgs e)
        {
            var threadId = e != null ? e.ThreadId : null;
            var turnId = e != null ? e.TurnId : null;
            if (HasEncodingSafetyWarning(threadId, turnId))
            {
                SetHighPriorityStatus(UiLocalization.Text(
                    "MainForm.EncodingSafety.Warning.CompletionSuppressed",
                    "Possible encoding corruption detected. Review changes before continuing."));
                return;
            }

            var targetText = BuildNotificationTargetText(e != null ? e.ThreadId : null);
            var isSubAgentTurn = IsSubAgentCompletionThread(e != null ? e.ThreadId : null);
            SetLowPriorityStatus(targetText + UiLocalization.Text("MainForm.Notification.WorkCompletedSuffix", "Work completed"));
            ExecuteNotificationPrompt(
                isSubAgentTurn ? GetSubAgentTurnCompletedNotificationMode() : GetTurnCompletedNotificationMode(),
                isSubAgentTurn ? SubAgentTurnCompletedSoundFileName : TurnCompletedSoundFileName,
                isSubAgentTurn ? "subagent" : "mainline",
                true);
        }

        private void HandleAppServerDisconnected(AppServerEventArgs e)
        {
            _pendingRemoteInteractions.Clear();
            _subAgentRuntimeVersion++;
            _approvalRequestDialogQueue.Clear();
            _remoteInteractionDialogs.CloseAll();
            _subAgentEventVersions.Clear();
            ClearGuardianApprovals();
            _subAgentSummaries.Clear();
            _subAgentStatuses.Clear();
            _subAgentPrimedParents.Clear();
            _isLocalSubAgentThreadSyncRunning = false;
            ClearAppServerFileWindowWatches();

            var message = e != null && !string.IsNullOrWhiteSpace(e.Content)
                ? e.Content
                : UiLocalization.Text("MainForm.Message.AppServerExited", "Codex App Server has exited.");

            ClearAllThreadBusy();
            ClearAllInFlightThreadStates();
            FinalizeAllActiveTurnSummaries(message);
            _connectionStatus = GetConnectionStatusDisconnectedText();
            RefreshHeader();
            RefreshInputState();
            AppendLog(message);
            ApplyCurrentActivityTimelineEvent(new AppServerEventArgs("app_server_disconnected", message));
            if (_isRuntimeWindowTransitionInProgress)
            {
                SetHighPriorityStatus(message);
                return;
            }

            SetHighPriorityStatus(message + UiLocalization.Text("MainForm.Status.TryingReconnectSuffix", " Trying to reconnect..."));
            _ = TryRecoverAppServerConnectionAsync(
                UiLocalization.Text("MainForm.Status.ReconnectingAppServer", "Reconnecting to App Server..."),
                UiLocalization.Text("MainForm.Status.AppServerDisconnectedRetryLater", "App Server disconnected. Please try again later."),
                true);
        }

        private void ApplyCurrentActivityTimelineEvent(AppServerEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            var threadId = e.ThreadId;
            if (string.IsNullOrWhiteSpace(threadId) && _currentSession != null)
            {
                threadId = _currentSession.ThreadId;
            }

            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            if (!EnsureThreadActivityTimelineState(threadId).ApplyEvent(e, _appConfigSnapshot))
            {
                return;
            }

            if (IsCurrentSessionThread(threadId))
            {
                RefreshTurnSummaryDisplay();
            }

            NotifyAutomationRunActivitySubscribers(threadId);
        }

        private void HandleThreadGoalChanged(AppServerEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ThreadId))
            {
                return;
            }

            var session = FindSessionByThreadId(e.ThreadId);
            if (session == null)
            {
                return;
            }

            var shouldUpdateVisibleUi = false;
            if (string.Equals(e.EventType, "thread_goal_cleared", StringComparison.OrdinalIgnoreCase))
            {
                shouldUpdateVisibleUi = SessionDisplayFormatter.HasGoal(session);
                ClearThreadGoalFromSession(session, shouldUpdateVisibleUi, true);
            }
            else
            {
                shouldUpdateVisibleUi = IsSignificantThreadGoalChange(session, e.ThreadGoal);
                ApplyThreadGoalToSession(session, e.ThreadGoal, shouldUpdateVisibleUi, shouldUpdateVisibleUi);
            }

            if (shouldUpdateVisibleUi && !string.IsNullOrWhiteSpace(e.Content))
            {
                SetStatus(e.Content);
            }
        }

        private void HandleThreadTokenUsageChanged(AppServerEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ThreadId) || e.ThreadTokenUsage == null)
            {
                return;
            }

            var session = FindSessionByThreadId(e.ThreadId);
            if (session == null)
            {
                return;
            }

            if (!ApplyThreadTokenUsageToSession(session, e.ThreadTokenUsage))
            {
                return;
            }

            PersistState();
            RefreshLiveWorkStatus();
            RefreshSessionWorkbenches();
        }

        private static bool ApplyThreadTokenUsageToSession(ChatSession session, AppServerThreadTokenUsageSnapshot usage)
        {
            if (session == null || usage == null)
            {
                return false;
            }

            var total = usage.Total ?? new AppServerTokenUsageBreakdown();
            var last = usage.Last ?? new AppServerTokenUsageBreakdown();
            var changed =
                session.ThreadTokenUsageModelContextWindow != usage.ModelContextWindow ||
                session.ThreadTokenUsageTotalTokens != total.TotalTokens ||
                session.ThreadTokenUsageInputTokens != total.InputTokens ||
                session.ThreadTokenUsageCachedInputTokens != total.CachedInputTokens ||
                session.ThreadTokenUsageOutputTokens != total.OutputTokens ||
                session.ThreadTokenUsageReasoningOutputTokens != total.ReasoningOutputTokens ||
                session.ThreadTokenUsageLastTotalTokens != last.TotalTokens ||
                session.ThreadTokenUsageLastInputTokens != last.InputTokens ||
                session.ThreadTokenUsageLastCachedInputTokens != last.CachedInputTokens ||
                session.ThreadTokenUsageLastOutputTokens != last.OutputTokens ||
                session.ThreadTokenUsageLastReasoningOutputTokens != last.ReasoningOutputTokens;

            if (!changed)
            {
                return false;
            }

            session.ThreadTokenUsageModelContextWindow = usage.ModelContextWindow;
            session.ThreadTokenUsageTotalTokens = total.TotalTokens;
            session.ThreadTokenUsageInputTokens = total.InputTokens;
            session.ThreadTokenUsageCachedInputTokens = total.CachedInputTokens;
            session.ThreadTokenUsageOutputTokens = total.OutputTokens;
            session.ThreadTokenUsageReasoningOutputTokens = total.ReasoningOutputTokens;
            session.ThreadTokenUsageLastTotalTokens = last.TotalTokens;
            session.ThreadTokenUsageLastInputTokens = last.InputTokens;
            session.ThreadTokenUsageLastCachedInputTokens = last.CachedInputTokens;
            session.ThreadTokenUsageLastOutputTokens = last.OutputTokens;
            session.ThreadTokenUsageLastReasoningOutputTokens = last.ReasoningOutputTokens;
            return true;
        }

        private void ClearAllThreadBusy()
        {
            if (_busyThreadIds.Count == 0)
            {
                return;
            }

            _busyThreadIds.Clear();
            RefreshLiveWorkStatus();
        }

        private void ClearAllInFlightThreadStates()
        {
            _threadOperationStateTracker.ClearAllInFlightStates();
        }

        private void FinalizeAllActiveTurnSummaries(string message)
        {
            foreach (var state in _threadTurnSummaryStates.Values)
            {
                if (state == null || state.IsFinalized)
                {
                    continue;
                }

                TurnProgressSummaryReducer.FinalizeInterrupted(state, message);
            }
        }

        private bool ShouldScheduleAutomaticThreadSyncFollowUp(AppServerEventRoute route, AppServerEventArgs e)
        {
            if (!ThreadDiscoveryRefreshPlanner.ShouldScheduleFollowUp(route, e != null ? e.ThreadId : null, IsKnownThreadId))
            {
                return false;
            }

            if (route != AppServerEventRoute.TurnStarted)
            {
                return true;
            }

            return e != null &&
                   !string.IsNullOrWhiteSpace(e.ThreadId) &&
                   (_currentSession == null ||
                    string.IsNullOrWhiteSpace(_currentSession.ThreadId) ||
                   !string.Equals(e.ThreadId, _currentSession.ThreadId, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsKnownThreadId(string threadId)
        {
            return FindSessionByThreadId(threadId) != null;
        }

        private void HandleStandaloneCommandOutputEvent(AppServerEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ProcessId))
            {
                return;
            }

            var commandTask = CommandTerminalSelectionState.FindByProcessId(e.ProcessId, _commandTasks);
            if (commandTask == null ||
                !string.Equals(commandTask.Status, "running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AppendCommandOutput(commandTask, e.Content, e.OutputStream, true);
        }

        private void HandleCommandTerminalInteractionEvent(AppServerEventArgs e)
        {
            if (!CommandTerminalInteractionState.TryMarkWaitingForInput(_activeCommandTask, e, DateTime.Now))
            {
                HandleUnmatchedCommandTerminalInteraction(e);
                return;
            }

            if (!string.IsNullOrWhiteSpace(e.Content))
            {
                AppendLog(e.Content);
                AppendCommandTerminalEntry(CommandTerminalEntryKind.Interaction, e.Content);
            }

            SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.CommandWaitingForInput", "Command is waiting for input"));
        }

        private void HandleUnmatchedCommandTerminalInteraction(AppServerEventArgs e)
        {
            var notice = CommandTerminalInteractionState.BuildUnmatchedNotice(
                e,
                UiLocalization.Text(
                    "MainForm.Log.CommandInteractionUnmatched.Template",
                    "Command interaction was not attached to the active terminal: {0}"));
            if (string.IsNullOrWhiteSpace(notice))
            {
                return;
            }

            AppendLog(notice);
            SetLowPriorityStatus(notice);
        }

        private void ResetTurnProgressSummaryBeforeSend(string threadId, IReadOnlyList<string> skillNames = null)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                RefreshTurnSummaryDisplay();
                return;
            }

            ClearReasoningSummaryMessageStates(threadId);
            var state = EnsureThreadTurnSummaryState(threadId);
            TurnProgressSummaryReducer.PrepareForSend(state, skillNames);

            if (IsCurrentSessionThread(threadId))
            {
                RefreshTurnSummaryDisplay();
            }
        }

        private void UpdateTurnProgressSummaryState(AppServerEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ThreadId))
            {
                return;
            }

            var state = EnsureThreadTurnSummaryState(e.ThreadId);
            var timelineChanged = EnsureThreadActivityTimelineState(e.ThreadId).ApplyEvent(e, _appConfigSnapshot);
            if (!TurnProgressSummaryReducer.ApplyEvent(state, e))
            {
                TraceTurnProgressSummaryDiagnostic(e, state, false, false);
                if (timelineChanged && IsCurrentSessionThread(e.ThreadId))
                {
                    RefreshTurnSummaryDisplay();
                }

                if (timelineChanged)
                {
                    NotifyAutomationRunActivitySubscribers(e.ThreadId);
                }

                return;
            }

            timelineChanged = SyncFinalizedTurnSummaryActivityTimelineItem(e.ThreadId, state) || timelineChanged;
            CheckTurnEncodingSafety(e.ThreadId, state.TurnId, state.DiffText, state.ChangedFiles);

            var isCurrentSessionThread = IsCurrentSessionThread(e.ThreadId);
            TraceTurnProgressSummaryDiagnostic(e, state, true, isCurrentSessionThread);

            if (isCurrentSessionThread)
            {
                RefreshTurnSummaryDisplay();
            }

            if (timelineChanged)
            {
                NotifyAutomationRunActivitySubscribers(e.ThreadId);
            }
        }

        private bool SyncFinalizedTurnSummaryActivityTimelineItem(string threadId, TurnProgressSummaryState state)
        {
            if (string.IsNullOrWhiteSpace(threadId) || state == null || !state.IsFinalized)
            {
                return false;
            }

            return EnsureThreadActivityTimelineState(threadId).UpsertTurnSummary(
                state.TurnId,
                TurnProgressSummaryReducer.BuildCurrentStepText(state));
        }

        private TurnProgressSummaryState EnsureThreadTurnSummaryState(string threadId)
        {
            TurnProgressSummaryState state;
            if (!_threadTurnSummaryStates.TryGetValue(threadId, out state))
            {
                state = new TurnProgressSummaryState();
                _threadTurnSummaryStates[threadId] = state;
            }

            return state;
        }

        private ActivityTimelineState EnsureThreadActivityTimelineState(string threadId)
        {
            ActivityTimelineState state;
            if (!_threadActivityTimelineStates.TryGetValue(threadId, out state))
            {
                state = new ActivityTimelineState();
                _threadActivityTimelineStates[threadId] = state;
            }

            return state;
        }

        private void RefreshTurnSummaryDisplay()
        {
            if (_currentSession == null || string.IsNullOrWhiteSpace(_currentSession.ThreadId))
            {
                _activitySummaryText = UiLocalization.Text("MainForm.TurnSummary.Empty", "No summary currently.");
                RefreshActivityDisplay();
                return;
            }

            TurnProgressSummaryState state;
            if (!_threadTurnSummaryStates.TryGetValue(_currentSession.ThreadId, out state))
            {
                _activitySummaryText = UiLocalization.Text("MainForm.TurnSummary.Empty", "No summary currently.");
                RefreshActivityDisplay();
                return;
            }

            _activitySummaryText = TurnProgressSummaryReducer.BuildSummaryText(state);
            TraceActivitySummaryDisplayDiagnostic(_currentSession.ThreadId, state, _activitySummaryText);
            RefreshActivityDisplay();
        }

        private void HandleReasoningSummaryChatMessageEvent(AppServerEventArgs e, AppServerEventRoute route)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ThreadId))
            {
                return;
            }

            var eventType = (e.EventType ?? string.Empty).Trim();
            if (string.Equals(eventType, "reasoning_started", StringComparison.OrdinalIgnoreCase))
            {
                ResetReasoningSummaryMessageState(e);
                return;
            }

            if (IsTurnTerminalReasoningSummaryEvent(route))
            {
                FlushReasoningSummaryMessageStates(e);
                ClearReasoningSummaryMessageStates(e.ThreadId, e.TurnId);
                return;
            }

            if (!string.Equals(eventType, "reasoning_summary_delta", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(eventType, "reasoning_completed", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!ShouldAppendReasoningSummaryToChat(e))
            {
                return;
            }

            var state = EnsureReasoningSummaryMessageState(e);
            var forceFlush = string.Equals(eventType, "reasoning_completed", StringComparison.OrdinalIgnoreCase);
            AppendReasoningSummaryChatSegments(e, state, e.Content, forceFlush);
        }

        private bool ShouldAppendReasoningSummaryToChat(AppServerEventArgs e)
        {
            return false;
        }

        private static bool IsTurnTerminalReasoningSummaryEvent(AppServerEventRoute route)
        {
            return route == AppServerEventRoute.TurnCompleted ||
                   route == AppServerEventRoute.TurnInterrupted ||
                   route == AppServerEventRoute.TurnFailed;
        }

        private ReasoningSummaryMessageState EnsureReasoningSummaryMessageState(AppServerEventArgs e)
        {
            var key = BuildReasoningSummaryMessageStateKey(e);
            ReasoningSummaryMessageState state;
            if (!_reasoningSummaryMessageStates.TryGetValue(key, out state))
            {
                state = new ReasoningSummaryMessageState();
                _reasoningSummaryMessageStates[key] = state;
            }

            return state;
        }

        private void ResetReasoningSummaryMessageState(AppServerEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ThreadId))
            {
                return;
            }

            _reasoningSummaryMessageStates[BuildReasoningSummaryMessageStateKey(e)] = new ReasoningSummaryMessageState();
        }

        private void FlushReasoningSummaryMessageStates(AppServerEventArgs e)
        {
            if (!ShouldAppendReasoningSummaryToChat(e))
            {
                return;
            }

            var keys = GetReasoningSummaryMessageStateKeys(e.ThreadId, e.TurnId);
            for (var i = 0; i < keys.Count; i++)
            {
                ReasoningSummaryMessageState state;
                if (_reasoningSummaryMessageStates.TryGetValue(keys[i], out state))
                {
                    AppendReasoningSummaryChatSegments(e, state, null, true);
                }
            }
        }

        private void AppendReasoningSummaryChatSegments(
            AppServerEventArgs e,
            ReasoningSummaryMessageState state,
            string summaryText,
            bool forceFlush)
        {
            var segments = ReasoningSummaryMessageSegmenter.Append(state, summaryText, forceFlush);
            for (var i = 0; i < segments.Count; i++)
            {
                AddThreadEventHistoryItem(e, ChatHistoryItemFactory.GetCodexSenderText(), segments[i], ChatHistoryCategory.ReasoningSummary);
            }
        }

        private void ClearReasoningSummaryMessageStates(string threadId, string turnId = null)
        {
            var keys = GetReasoningSummaryMessageStateKeys(threadId, turnId);
            for (var i = 0; i < keys.Count; i++)
            {
                _reasoningSummaryMessageStates.Remove(keys[i]);
            }
        }

        private List<string> GetReasoningSummaryMessageStateKeys(string threadId, string turnId)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return result;
            }

            var prefix = BuildReasoningSummaryMessageStatePrefix(threadId, turnId);
            foreach (var key in _reasoningSummaryMessageStates.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(key);
                }
            }

            return result;
        }

        private static string BuildReasoningSummaryMessageStateKey(AppServerEventArgs e)
        {
            var threadId = e != null ? e.ThreadId : null;
            var turnId = e != null ? e.TurnId : null;
            var itemId = e != null ? e.ItemId : null;
            return BuildReasoningSummaryMessageStatePrefix(threadId, turnId) +
                   (string.IsNullOrWhiteSpace(itemId) ? "default" : itemId.Trim());
        }

        private static string BuildReasoningSummaryMessageStatePrefix(string threadId, string turnId)
        {
            return (threadId ?? string.Empty).Trim() + "|" +
                   (turnId ?? string.Empty).Trim() + "|";
        }

        private static void TraceTurnProgressSummaryDiagnostic(
            AppServerEventArgs e,
            TurnProgressSummaryState state,
            bool applied,
            bool isCurrentSessionThread)
        {
            if (!IsAppServerEventDiagnosticsEnabled() || !ShouldTraceTurnProgressSummaryEvent(e))
            {
                return;
            }

            Trace.TraceInformation(
                "Turn summary diagnostic. EventType={0}; ThreadId={1}; TurnId={2}; Applied={3}; IsCurrentSessionThread={4}; ContentLength={5}; DetailsLength={6}; ReasoningSummaryLength={7}; ReasoningTextLength={8}; CompletedReasoningCount={9}; IsReasoningActive={10}; ContentPreview={11}; ReasoningSummaryPreview={12}",
                e.EventType ?? string.Empty,
                e.ThreadId ?? string.Empty,
                e.TurnId ?? string.Empty,
                applied,
                isCurrentSessionThread,
                GetLength(e.Content),
                GetLength(e.Details),
                state == null ? 0 : GetLength(state.ReasoningSummaryText),
                state == null ? 0 : GetLength(state.ReasoningText),
                state == null ? 0 : state.CompletedReasoningSummaries.Count,
                state != null && state.IsReasoningActive,
                AbbreviateDiagnosticText(e.Content, 160),
                state == null ? string.Empty : AbbreviateDiagnosticText(state.ReasoningSummaryText, 160));
        }

        private static void TraceActivitySummaryDisplayDiagnostic(
            string threadId,
            TurnProgressSummaryState state,
            string activitySummaryText)
        {
            if (!IsAppServerEventDiagnosticsEnabled())
            {
                return;
            }

            Trace.TraceInformation(
                "Activity summary display diagnostic. ThreadId={0}; SummaryTextLength={1}; ReasoningSummaryLength={2}; ReasoningTextLength={3}; CompletedReasoningCount={4}; IsReasoningActive={5}; ActivityPreview={6}",
                threadId ?? string.Empty,
                GetLength(activitySummaryText),
                state == null ? 0 : GetLength(state.ReasoningSummaryText),
                state == null ? 0 : GetLength(state.ReasoningText),
                state == null ? 0 : state.CompletedReasoningSummaries.Count,
                state != null && state.IsReasoningActive,
                AbbreviateDiagnosticText(activitySummaryText, 220));
        }

        private static bool ShouldTraceTurnProgressSummaryEvent(AppServerEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.EventType))
            {
                return false;
            }

            return e.EventType.StartsWith("turn_", StringComparison.OrdinalIgnoreCase) ||
                   e.EventType.IndexOf("reasoning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   e.EventType.IndexOf("assistant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(e.EventType, "error", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAppServerEventDiagnosticsEnabled()
        {
            var value = Environment.GetEnvironmentVariable(AppServerEventDiagnosticsEnvironmentVariable);
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetLength(string value)
        {
            return value == null ? 0 : value.Length;
        }

        private static string AbbreviateDiagnosticText(string value, int maxLength)
        {
            var normalized = (value ?? string.Empty)
                .Replace("\r\n", " ")
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            if (maxLength <= 0 || normalized.Length <= maxLength)
            {
                return normalized;
            }

            return normalized.Substring(0, maxLength) + "...";
        }

        private bool TryHandleCompactionTurnStarted(AppServerEventArgs e)
        {
            if (e == null || !_threadOperationStateTracker.TryMarkCompactionStarted(e.ThreadId, e.TurnId))
            {
                return false;
            }

            RefreshInputState();
            return true;
        }

        private void HandleContextCompactionCompleted(AppServerEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ThreadId))
            {
                return;
            }

            if (!TryRegisterCompactionCompletionTurn(e.TurnId))
            {
                return;
            }

            var plan = ThreadCompactionEventPlanner.BuildCompletionPlan(
                IsCurrentSessionThread(e.ThreadId) && _currentSession != null);

            if (plan.ShouldClearCompactionState)
            {
                ClearThreadCompactionState(e.ThreadId);
            }

            if (plan.ShouldInvalidateHistoryCache)
            {
                InvalidateSessionHistoryCache(e.ThreadId);
            }

            if (plan.ShouldRefreshInputState)
            {
                RefreshInputState();
            }

            if (plan.ShouldAnnounceCompletion)
            {
                SpeakAuxiliaryTextWithScreenReader(UiLocalization.Text("MainForm.Message.CompactionCompleted", "Current session context has been compacted."));
            }
        }

        private bool TryHandleCompactionTurnCompleted(AppServerEventArgs e)
        {
            if (e == null)
            {
                return false;
            }

            var hasRecentCompletionTurn = HasRecentCompactionCompletionTurn(e.TurnId);
            var isThreadPendingCompaction = IsThreadPendingCompaction(e.ThreadId);
            string activeTurnId;
            var hasActiveCompactionTurn = _threadOperationStateTracker.TryGetActiveCompactionTurnId(e.ThreadId, out activeTurnId);
            if (!ThreadCompactionEventPlanner.ShouldHandleTurnCompleted(
                    hasRecentCompletionTurn,
                    isThreadPendingCompaction,
                    hasActiveCompactionTurn ? activeTurnId : null,
                    e.TurnId))
            {
                return false;
            }

            if (hasRecentCompletionTurn)
            {
                return true;
            }

            HandleContextCompactionCompleted(e);
            return true;
        }

        private bool TryHandleCompactionFailed(AppServerEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ThreadId))
            {
                return false;
            }

            if (!ThreadCompactionEventPlanner.ShouldHandleFailure(
                    IsThreadPendingCompaction(e.ThreadId),
                    IsThreadActiveCompaction(e.ThreadId, e.TurnId)))
            {
                return false;
            }

            ClearThreadCompactionState(e.ThreadId);
            InvalidateSessionHistoryCache(e.ThreadId);
            RefreshInputState();

            if (IsCurrentSessionThread(e.ThreadId))
            {
                AddChatHistoryItem(
                    GetErrorSenderText(),
                    string.Format(UiLocalization.Text("MainForm.Message.CompactionFailed.Template", "Current session context compaction failed: {0}"), e.Content),
                    ChatHistoryCategory.Error);
            }

            return true;
        }

        private bool TryHandleUnsupportedFlexServiceTier(AppServerEventArgs e)
        {
            if (e == null || !ContainsUnsupportedFlexServiceTierError(e.Content))
            {
                return false;
            }

            if (!ShouldDisplayThreadEvent(e))
            {
                return true;
            }

            AddChatHistoryItem(
                GetErrorSenderText(),
                UiLocalization.Text(
                    "MainForm.SessionSettings.Chat.UnsupportedFlexServiceTier",
                    "The selected target does not support the Flex service tier. Update the target configuration outside MyCodex, then use Target Reload and review the effective settings."),
                ChatHistoryCategory.Error);
            SetHighPriorityStatus(
                UiLocalization.Text(
                    "MainForm.SessionSettings.Status.UnsupportedFlexServiceTier",
                    "Flex service tier is unsupported. Use Target Reload after reviewing target settings."));
            return true;
        }

        private static bool ContainsUnsupportedFlexServiceTierError(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   text.IndexOf("Unsupported service_tier: flex", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void HandleApprovalRequestedNotification(AppServerEventArgs e)
        {
            if (!TryRegisterApprovalRequestNotification(e))
            {
                return;
            }

            var summary = ApprovalRequestSummaryBuilder.Build(e);
            var targetText = BuildNotificationTargetText(e != null ? e.ThreadId : null);
            SetHighPriorityStatus(summary.SpeechSummary + " " + targetText);
            var notificationMode = GetApprovalRequestNotificationMode();
            ExecuteNotificationPrompt(
                notificationMode,
                ApprovalSoundFileName,
                string.Empty,
                false);
        }

        private NotificationPromptMode GetTurnCompletedNotificationMode()
        {
            return _appConfigSnapshot != null
                ? _appConfigSnapshot.TurnCompletedNotificationMode
                : NotificationPromptMode.Sound;
        }

        private NotificationPromptMode GetSubAgentTurnCompletedNotificationMode()
        {
            return _appConfigSnapshot != null
                ? _appConfigSnapshot.SubAgentTurnCompletedNotificationMode
                : NotificationPromptMode.Sound;
        }

        private NotificationPromptMode GetApprovalRequestNotificationMode()
        {
            return _appConfigSnapshot != null
                ? _appConfigSnapshot.ApprovalRequestNotificationMode
                : NotificationPromptMode.Sound;
        }

        private void ExecuteNotificationPrompt(NotificationPromptMode mode, string soundFileName, string throttleKey, bool throttleTurnCompleted)
        {
            if (!IsSoundNotificationEnabled(mode))
            {
                return;
            }

            if (throttleTurnCompleted && !CanPlayTurnCompletedPrompt(throttleKey))
            {
                return;
            }

            PlayNotificationSound(soundFileName);
        }

        private static bool IsSoundNotificationEnabled(NotificationPromptMode mode)
        {
            return mode == NotificationPromptMode.Sound ||
                   mode == NotificationPromptMode.SoundAndSpeech;
        }

        private bool CanPlayTurnCompletedPrompt(string notificationKey)
        {
            return _threadOperationStateTracker.CanPlayTurnCompletedPrompt(notificationKey, DateTime.Now);
        }

        private bool TryRegisterApprovalRequestNotification(AppServerEventArgs e)
        {
            return _threadOperationStateTracker.TryRegisterApprovalRequest(e != null ? e.RequestId : null);
        }

        private bool TryRegisterCompactionCompletionTurn(string turnId)
        {
            return _threadOperationStateTracker.TryRegisterCompactionCompletion(turnId);
        }

        private bool HasRecentCompactionCompletionTurn(string turnId)
        {
            return _threadOperationStateTracker.HasRecentCompactionCompletionTurn(turnId);
        }

        private string BuildNotificationTargetText(string threadId)
        {
            var session = FindSessionByThreadId(threadId);
            if (session == null)
            {
                return UiLocalization.Text("MainForm.Notification.Target.BackgroundSession", "Background session");
            }

            if (_currentSession != null &&
                string.Equals(_currentSession.Id, session.Id, StringComparison.OrdinalIgnoreCase))
            {
                return (session.IsFork || session.IsReview)
                    ? SessionDisplayFormatter.BuildBranchLabel(session)
                    : UiLocalization.Text("MainForm.Notification.Target.CurrentMainline", "Current mainline");
            }

            return SessionDisplayFormatter.BuildBranchLabel(session);
        }

        private bool IsSubAgentCompletionThread(string threadId)
        {
            return SessionListProjection.IsSubAgent(FindSessionByThreadId(threadId));
        }

        private bool ShouldDisplayThreadEvent(AppServerEventArgs e)
        {
            return e == null ||
                   string.IsNullOrWhiteSpace(e.ThreadId) ||
                   IsCurrentSessionThread(e.ThreadId);
        }

        private bool TryBeginThreadCompaction(string threadId)
        {
            return _threadOperationStateTracker.TryBeginCompaction(threadId);
        }

        private void ClearThreadCompactionState(string threadId)
        {
            _threadOperationStateTracker.ClearThreadCompactionState(threadId);
        }

        private bool IsThreadCompacting(string threadId)
        {
            return _threadOperationStateTracker.IsThreadCompacting(threadId);
        }

        private bool IsThreadPendingCompaction(string threadId)
        {
            return _threadOperationStateTracker.IsThreadPendingCompaction(threadId);
        }

        private bool IsThreadActiveCompaction(string threadId, string turnId)
        {
            return _threadOperationStateTracker.IsThreadActiveCompaction(threadId, turnId);
        }

        private bool IsThreadBusy(string threadId)
        {
            return !string.IsNullOrWhiteSpace(threadId) &&
                   _busyThreadIds.Contains(threadId);
        }

        private bool HasInterruptibleTurn(string threadId)
        {
            return !string.IsNullOrWhiteSpace(GetInterruptibleTurnId(threadId));
        }

        private bool CanInterruptThread(string threadId)
        {
            return !string.IsNullOrWhiteSpace(threadId) &&
                   (IsThreadBusy(threadId) ||
                    HasInterruptibleTurn(threadId) ||
                    _appServerClient.HasInterruptibleTurn(threadId));
        }

        private bool IsThreadCurrentlyRunning(string threadId)
        {
            return !string.IsNullOrWhiteSpace(threadId) &&
                   (IsThreadBusy(threadId) ||
                    HasInterruptibleTurn(threadId) ||
                    _appServerClient.HasInterruptibleTurn(threadId));
        }

        private string GetInterruptibleTurnId(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return string.Empty;
            }

            TurnProgressSummaryState state;
            if (!_threadTurnSummaryStates.TryGetValue(threadId, out state) ||
                state == null ||
                state.IsFinalized ||
                string.IsNullOrWhiteSpace(state.TurnId))
            {
                return string.Empty;
            }

            return state.TurnId;
        }

        private void MarkThreadBusy(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            if (_busyThreadIds.Add(threadId))
            {
                RefreshLiveWorkStatus();
            }
        }

        private void ClearThreadBusy(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            if (_busyThreadIds.Remove(threadId))
            {
                RefreshLiveWorkStatus();
            }
        }

        private bool CanReviewSession(ChatSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return false;
            }

            var workspace = FindWorkspaceById(session.WorkspaceId);
            return IsWorkspaceAvailable(workspace) &&
                   !IsThreadBusy(session.ThreadId) &&
                   !IsThreadCompacting(session.ThreadId);
        }

        private bool CanCompactSession(ChatSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return false;
            }

            var workspace = FindWorkspaceById(session.WorkspaceId);
            return IsWorkspaceAvailable(workspace) &&
                   !IsThreadCompacting(session.ThreadId);
        }

        private bool IsCurrentSessionThread(string threadId)
        {
            return _currentSession != null &&
                   !string.IsNullOrWhiteSpace(threadId) &&
                   string.Equals(_currentSession.ThreadId, threadId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldInvalidateHistoryForEvent(AppServerEventArgs e)
        {
            return SessionHistoryEventPolicy.ShouldInvalidateForEvent(e);
        }

        private void PlayNotificationSound(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            try
            {
                var player = GetNotificationSoundPlayer(fileName);
                if (player == null)
                {
                    return;
                }

                player.Play();
            }
            catch
            {
                // Ignore sound playback failures silently to avoid interrupting main flow.
            }
        }

        private void PlayMessageSentSoundIfEnabled()
        {
            if (_appConfigSnapshot == null || !_appConfigSnapshot.SendMessageSoundEnabled)
            {
                return;
            }

            PlayNotificationSound(MessageSentSoundFileName);
        }

        private SoundPlayer GetNotificationSoundPlayer(string fileName)
        {
            var soundPath = GetNotificationSoundPath(fileName);
            if (string.IsNullOrWhiteSpace(soundPath) || !File.Exists(soundPath))
            {
                return null;
            }

            if (string.Equals(fileName, ApprovalSoundFileName, StringComparison.OrdinalIgnoreCase))
            {
                if (_approvalSoundPlayer == null)
                {
                    _approvalSoundPlayer = new SoundPlayer(soundPath);
                    _approvalSoundPlayer.Load();
                }

                return _approvalSoundPlayer;
            }

            if (string.Equals(fileName, TurnCompletedSoundFileName, StringComparison.OrdinalIgnoreCase))
            {
                if (_turnCompletedSoundPlayer == null)
                {
                    _turnCompletedSoundPlayer = new SoundPlayer(soundPath);
                    _turnCompletedSoundPlayer.Load();
                }

                return _turnCompletedSoundPlayer;
            }

            if (string.Equals(fileName, SubAgentTurnCompletedSoundFileName, StringComparison.OrdinalIgnoreCase))
            {
                if (_subAgentTurnCompletedSoundPlayer == null)
                {
                    _subAgentTurnCompletedSoundPlayer = new SoundPlayer(soundPath);
                    _subAgentTurnCompletedSoundPlayer.Load();
                }

                return _subAgentTurnCompletedSoundPlayer;
            }

            if (string.Equals(fileName, MessageSentSoundFileName, StringComparison.OrdinalIgnoreCase))
            {
                if (_messageSentSoundPlayer == null)
                {
                    _messageSentSoundPlayer = new SoundPlayer(soundPath);
                    _messageSentSoundPlayer.Load();
                }

                return _messageSentSoundPlayer;
            }

            var player = new SoundPlayer(soundPath);
            player.Load();
            return player;
        }

        private static string GetNotificationSoundPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sound", fileName);
        }

        private void QueueApprovalRequestDialog(AppServerEventArgs approvalRequest)
        {
            AppServerEventArgs nextRequest;
            if (_approvalRequestDialogQueue.Enqueue(approvalRequest, out nextRequest))
            {
                _ = ShowApprovalRequestDialogQueueAsync(nextRequest);
            }
        }

        private async Task ShowApprovalRequestDialogQueueAsync(AppServerEventArgs approvalRequest)
        {
            var runtimeVersion = _subAgentRuntimeVersion;
            var nextRequest = approvalRequest;
            while (nextRequest != null)
            {
                await ShowApprovalRequestDialogAsync(nextRequest);
                if (IsDisposed || _isExitRequested || runtimeVersion != _subAgentRuntimeVersion) return;

                AppServerEventArgs queuedRequest;
                nextRequest = _approvalRequestDialogQueue.CompleteCurrent(out queuedRequest)
                    ? queuedRequest
                    : null;
            }
        }

        private async Task ShowApprovalRequestDialogAsync(AppServerEventArgs approvalRequest)
        {
            if (approvalRequest == null || string.IsNullOrWhiteSpace(approvalRequest.RequestId))
            {
                return;
            }

            try
            {
                HandleApprovalRequestedNotification(approvalRequest);
                if (!await WaitToPresentTargetInteractionAsync(approvalRequest.RequestId)) return;
                if (approvalRequest.GuardianApprovalReview != null)
                {
                    await ShowGuardianDeniedActionApprovalAsync(approvalRequest);
                    return;
                }

                TurnProgressSummaryState turnState = null;
                if (!string.IsNullOrWhiteSpace(approvalRequest.ThreadId))
                {
                    _threadTurnSummaryStates.TryGetValue(approvalRequest.ThreadId, out turnState);
                }

                var displayRequest = ApprovalRequestDisplayRequestBuilder.Build(approvalRequest, turnState);
                using (var form = new ApprovalRequestForm(displayRequest))
                {
                    var registration = _remoteInteractionDialogs.Register(approvalRequest.RequestId, delegate
                    {
                        if (!form.IsDisposed)
                        {
                            form.CloseAsExternallyResolved();
                        }
                    });
                    try
                    {
                        form.ShowDialog(this);
                    }
                    finally
                    {
                        registration.Dispose();
                    }

                    if (form.WasExternallyResolved)
                    {
                        return;
                    }

                    await _appServerClient.ResolveApprovalRequestAsync(approvalRequest.RequestId, form.SelectedDecision);
                }
            }
            catch (Exception ex)
            {
                AddChatHistoryItem(GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.ApprovalResponseFailed", "Approval response failed"));
            }
        }

    }
}
