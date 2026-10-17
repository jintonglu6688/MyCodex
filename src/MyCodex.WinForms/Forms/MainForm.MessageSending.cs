using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async void OnSendButtonClick(object sender, EventArgs e)
        {
            await SendCurrentInputAsync();
        }

        private async Task SendCurrentInputAsync()
        {
            await SendCurrentInputAsync(null, true);
        }

        private async Task<ChatWorkbenchSendResult> SendSubAgentInputAsync(ChatSession session, WorkspaceInfo workspace, ChatWorkbenchSendRequest request)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId) || request?.InputItems == null || request.InputItems.Count == 0)
                return new ChatWorkbenchSendResult(false, false, UiLocalization.Text("SubAgentWorkbenchForm.Status.NoActiveSubAgent", "No active subagent"));
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("SubAgentWorkbenchForm.Action.Send", "Send subagent message")))
                return new ChatWorkbenchSendResult(false, false, UiLocalization.Text("SubAgentWorkbenchForm.Status.AppServerUnavailable", "App Server is unavailable"));
            var client = _appServerClient as MyCodex.Application.Interfaces.IAppServerSubAgentClient;
            if (client == null) throw new NotSupportedException();
            var runtimeVersion = _subAgentRuntimeVersion;
            await client.SendSubAgentMessageAsync(session.ThreadId, request.InputItems, System.Threading.CancellationToken.None);
            if (IsDisposed || runtimeVersion != _subAgentRuntimeVersion) throw new OperationCanceledException();
            session.LastActiveAt = DateTime.Now;
            PersistState();
            return new ChatWorkbenchSendResult(true, true, UiLocalization.Text("SubAgentWorkbenchForm.Status.MessageSent", "Message sent"));
        }
        private async Task SendCurrentInputAsync(string overrideText, bool includePendingAttachments)
        {
            if (IsTargetExitPreparing || _isExitRequested) return;
            var sendWatch = Stopwatch.StartNew();
            EnsureCurrentSessionMatchesInteractionMode();
            var hasOverrideText = overrideText != null;
            var rawText = hasOverrideText ? overrideText : _inputBox.ComposeText;
            TraceSendDiagnostic("Begin", string.Format(
                CultureInfo.InvariantCulture,
                "hasOverride={0}; includeAttachments={1}; rawLength={2}; {3}",
                hasOverrideText,
                includePendingAttachments,
                rawText.Length,
                BuildSendDiagnosticContext()));
            var planCommand = ParsePlanSlashCommand(rawText);
            var pendingDraftState = _currentSession == null && !hasOverrideText ? CaptureCurrentComposeDraftState() : null;
            var sessionBootstrap = SendInputPreflight.EvaluateSessionBootstrap(_currentSession != null, _currentWorkspace != null);
            if (sessionBootstrap.IsBlocked)
            {
                TraceSendDiagnostic("Blocked", "session bootstrap blocked; status=" + sessionBootstrap.StatusText);
                SetStatus(sessionBootstrap.StatusText);
                return;
            }

            if (sessionBootstrap.RequiresNewSession)
            {
                TraceSendDiagnostic("Bootstrap", "creating new session before send");
                await CreateNewSessionAsync();
                if (_currentSession == null)
                {
                    TraceSendDiagnostic("Blocked", "new session was not created");
                    return;
                }

                RestoreComposeDraftState(pendingDraftState);
                ClearCurrentModeUnboundComposeState();
                TraceSendDiagnostic("Bootstrap", "new session created; " + BuildSendDiagnosticContext());
            }

            if (planCommand.IsPlanCommand && string.IsNullOrWhiteSpace(planCommand.PromptText))
            {
                TraceSendDiagnostic("Plan", "plan command toggled without dispatch text");
                SetSessionPlanMode(_currentSession, true, true);
                SetInputBoxText(string.Empty, 0, 0);
                return;
            }

            var sendContext = CaptureSendOperationContext();
            var targetSession = _currentSession;
            var targetWorkspace = _currentWorkspace;
            TraceSendDiagnostic("Context", "captured; " + BuildSendDiagnosticContext());

            var dispatchText = planCommand.IsPlanCommand ? planCommand.PromptText : rawText;
            var attachmentsSnapshot = includePendingAttachments
                ? _pendingAttachments.ToArray()
                : new PendingAttachmentItem[0];
            var previewText = BuildOutgoingMessagePreview(dispatchText, attachmentsSnapshot);

            List<PendingAttachmentItem> unavailableAttachments;
            try
            {
                unavailableAttachments = includePendingAttachments
                    ? await EnsurePendingAttachmentsReadyForSendAsync(attachmentsSnapshot)
                    : new List<PendingAttachmentItem>();
            }
            catch (Exception ex)
            {
                TraceSendDiagnosticError("AttachmentCheck", ex);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.CheckRemoteAttachmentFailed", "Failed to check remote attachment"));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (AbortSendIfContextChanged(sendContext, "attachment check"))
            {
                return;
            }

            var attachmentValidation = SendInputPreflight.EvaluateAttachmentAvailability(unavailableAttachments);
            if (!attachmentValidation.CanContinue)
            {
                TraceSendDiagnostic("Blocked", "attachment validation failed; unavailable=" + unavailableAttachments.Count);
                MessageBox.Show(
                    this,
                    attachmentValidation.WarningMessage,
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SetStatus(attachmentValidation.StatusText);
                return;
            }

            var inputItems = await Task.Run(() => BuildOutgoingInputItems(dispatchText, attachmentsSnapshot)).ConfigureAwait(true);
            if (AbortSendIfContextChanged(sendContext, "input build"))
            {
                return;
            }

            var workspaceAvailable = IsWorkspaceAvailable(targetWorkspace);
            TraceSendDiagnostic("Preflight", string.Format(
                CultureInfo.InvariantCulture,
                "inputItems={0}; workspaceAvailable={1}; {2}",
                inputItems.Count,
                workspaceAvailable,
                BuildSendDiagnosticContext()));
            var dispatchValidation = SendInputPreflight.EvaluateReadyToDispatch(workspaceAvailable, inputItems.Count);
            if (dispatchValidation.Outcome == SendInputValidationOutcome.EmptyInput)
            {
                TraceSendDiagnostic("Blocked", "empty input");
                return;
            }

            if (dispatchValidation.Outcome == SendInputValidationOutcome.WorkspaceUnavailable)
            {
                TraceSendDiagnostic("Blocked", "workspace unavailable; status=" + dispatchValidation.StatusText + "; " + BuildSendDiagnosticContext());
                SetStatus(dispatchValidation.StatusText);
                return;
            }

            if (ShouldApplySendTargetUi(targetSession))
                SetStatus(UiLocalization.Text("MainForm.Status.SendingMessage", "Sending message..."));
            var configStarted = sendWatch.ElapsedMilliseconds;
            var preparedSettings = await PrepareSendSettingsAsync(targetSession, targetWorkspace);
            if (preparedSettings == null) return;
            TraceSendDiagnostic("Timing", string.Format(CultureInfo.InvariantCulture,
                "phase=configuration; elapsedMs={0}; totalMs={1}", sendWatch.ElapsedMilliseconds - configStarted, sendWatch.ElapsedMilliseconds));

            if (AbortSendIfContextChanged(sendContext, "App Server readiness"))
            {
                return;
            }

            var approvalMode = GetEffectiveApprovalMode(targetSession);
            var modelId = preparedSettings.ModelId;
            var reasoningEffort = preparedSettings.ReasoningEffort;
            TraceSendDiagnostic("DispatchConfig", string.Format(
                CultureInfo.InvariantCulture,
                "approval={0}; model={1}; effort={2}; planCommand={3}; {4}",
                approvalMode,
                FormatDiagnosticValue(modelId),
                FormatDiagnosticValue(reasoningEffort),
                planCommand.IsPlanCommand,
                BuildSendDiagnosticContext()));
            if (planCommand.IsPlanCommand)
            {
                SetSessionPlanMode(targetSession, true, true);
            }

            AddSendTargetHistoryItem(targetSession, ChatHistoryItemFactory.GetUserSenderText(), previewText);
            if (!hasOverrideText)
            {
                TraceSendDiagnostic("Dispatch", "clearing input before preparing remote thread; thread=" + FormatDiagnosticValue(targetSession.ThreadId));
                ClearSendTargetInputBeforeDispatch(targetSession, rawText, attachmentsSnapshot);
            }
            if (ShouldApplySendTargetUi(targetSession))
            {
                SetStatus(UiLocalization.Text("MainForm.Status.SendingMessage", "Sending message..."));
            }

            var threadStarted = sendWatch.ElapsedMilliseconds;
            var originalThreadIdBeforeRecreate = targetSession.ThreadId;
            if (string.IsNullOrWhiteSpace(targetSession.ThreadId))
            {
                TraceSendDiagnostic("ThreadRecreate", "current session has no thread; creating one before send");
                SessionThreadRecreationResult recreation;
                try
                {
                    var toolContext = BuildThreadToolContext(targetSession, targetWorkspace);
                    recreation = await _sessionThreadService.RecreateThreadAsync(
                        targetSession,
                        targetWorkspace,
                        approvalMode,
                        modelId,
                        reasoningEffort,
                        toolContext, true, preparedSettings);
                }
                catch (Exception ex)
                {
                    TraceSendDiagnosticError("ThreadRecreate", ex);
                    AddSendTargetHistoryItem(targetSession, ChatHistoryItemFactory.GetErrorSenderText(), ex.Message);
                    if (ShouldApplySendTargetUi(targetSession))
                    {
                        SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.SendFailed", "Send failed"));
                    }
                    return;
                }

                TraceSendDiagnostic("ThreadRecreate", "created thread; " + BuildSendDiagnosticContext());
                if (AbortSendIfContextChanged(sendContext, "thread recreation"))
                {
                    return;
                }

                ApplySessionThreadRecreationUiState(recreation, targetSession);
                AddSendTargetHistoryItem(
                    targetSession,
                    ChatHistoryItemFactory.GetSystemSenderText(),
                    UiLocalization.Text("MainForm.Message.RecreatedMissingThreadBeforeSend", "Current session had no remote thread. A new one was created automatically."));
            }
            TraceSendDiagnostic("Timing", string.Format(CultureInfo.InvariantCulture,
                "phase=thread; elapsedMs={0}; totalMs={1}", sendWatch.ElapsedMilliseconds - threadStarted, sendWatch.ElapsedMilliseconds));
            var threadWasRecreatedDuringSend = !string.Equals(originalThreadIdBeforeRecreate, targetSession.ThreadId, StringComparison.OrdinalIgnoreCase);

            try
            {
                var turnModelId = modelId;
                var turnReasoningEffort = reasoningEffort;
                var collaborationMode = BuildCollaborationModeForSend(targetSession, targetWorkspace, modelId, reasoningEffort);
                var toolContext = BuildThreadToolContext(targetSession, targetWorkspace);
                var dispatchDecision = MainFormSendDispatchPlanner.Choose(new MainFormSendDispatchContext
                {
                    ThreadId = targetSession.ThreadId,
                    IsThreadRunning = IsThreadCurrentlyRunning(targetSession.ThreadId),
                    ActiveTurnFollowUpMode = GetConfiguredActiveTurnFollowUpMode(),
                    InputItemCount = inputItems.Count,
                    IsPlanCommand = planCommand.IsPlanCommand,
                    HasSpecialCollaborationPayload = collaborationMode != null,
                    ThreadWasRecreatedDuringSend = threadWasRecreatedDuringSend
                });
                if (dispatchDecision.Mode == MainFormSendDispatchMode.Queue)
                {
                    EnqueueFollowUpMessage(
                        targetSession,
                        targetWorkspace,
                        inputItems,
                        approvalMode,
                        turnModelId,
                        turnReasoningEffort,
                        collaborationMode,
                        toolContext,
                        dispatchText,
                        previewText,
                        attachmentsSnapshot);
                    PlayMessageSentSoundIfEnabled();
                    var queueCompletionPlan = SessionSendCompletionPlanner.Build(
                        new SessionSendResult(),
                        targetSession,
                        targetWorkspace,
                        dispatchText,
                        previewText);
                    await ApplySessionSendCompletionPlanAsync(queueCompletionPlan, targetSession, false);
                    SortSessions();
                    ClearSendTargetAttachmentsAfterSuccess(targetSession, attachmentsSnapshot, includePendingAttachments);
                    ApplySendTargetRefresh(targetSession);
                    if (ShouldApplySendTargetUi(targetSession))
                    {
                        SetStatus(UiLocalization.Text("MainForm.Status.MessageQueued", "Message queued"));
                    }

                    TraceSendDiagnostic("Queued", "message queued for next turn; thread=" + FormatDiagnosticValue(targetSession.ThreadId));
                    return;
                }

                InvalidateSessionHistoryCache(targetSession.ThreadId);
                ResetTurnProgressSummaryBeforeSend(targetSession.ThreadId, CollectTurnSkillNames(attachmentsSnapshot));
                if (dispatchDecision.Mode == MainFormSendDispatchMode.TurnSteer)
                {
                    TraceSendDiagnostic("Dispatch", "sending turn/steer; thread=" + FormatDiagnosticValue(targetSession.ThreadId));
                    MarkLocalDesktopRealtimeTurn(targetSession.ThreadId);
                    var steerResult = await _appServerClient.TrySteerTurnAsync(
                        targetSession.ThreadId,
                        inputItems);
                    if (steerResult != null && steerResult.Accepted)
                    {
                        targetSession.LastActiveAt = DateTime.Now;
                        PlayMessageSentSoundIfEnabled();
                        var steerCompletionPlan = SessionSendCompletionPlanner.Build(new SessionSendResult(), targetSession, targetWorkspace, dispatchText, previewText);
                        await ApplySessionSendCompletionPlanAsync(steerCompletionPlan, targetSession);
                        SortSessions();
                        ClearSendTargetAttachmentsAfterSuccess(targetSession, attachmentsSnapshot, includePendingAttachments);
                        ApplySendTargetRefresh(targetSession);

                        TraceSendDiagnostic("Complete", "steer completed; turn=" + FormatDiagnosticValue(steerResult.TurnId) + "; " + BuildSendDiagnosticContext());
                        return;
                    }

                    ClearLocalDesktopRealtimeTurn(targetSession.ThreadId);
                    TraceSendDiagnostic(
                        "DispatchFallback",
                        "turn/steer fallback; reason=" + FormatDiagnosticValue(steerResult != null ? steerResult.FallbackReason : null));
                }

                TraceSendDiagnostic("Dispatch", "sending turn/start; thread=" + FormatDiagnosticValue(targetSession.ThreadId) + "; plannerReason=" + dispatchDecision.Reason);
                MarkLocalDesktopRealtimeTurn(targetSession.ThreadId);
                RegisterPendingInvalidEncryptedContentRetry(
                    targetSession,
                    targetWorkspace,
                    inputItems,
                    approvalMode,
                    turnModelId,
                    turnReasoningEffort,
                    collaborationMode,
                    toolContext,
                    dispatchText,
                    previewText);
                var dispatchStarted = sendWatch.ElapsedMilliseconds;
                var sendResult = await _sessionThreadService.SendUserMessageAsync(
                    targetSession,
                    targetWorkspace,
                    inputItems,
                    approvalMode,
                    turnModelId,
                    turnReasoningEffort,
                    collaborationMode,
                    toolContext, preparedSettings, deferMetadata: true);
                TraceSendDiagnostic("Acknowledged", string.Format(CultureInfo.InvariantCulture,
                    "phase=dispatch; elapsedMs={0}; totalMs={1}", sendWatch.ElapsedMilliseconds - dispatchStarted, sendWatch.ElapsedMilliseconds));
                PlayMessageSentSoundIfEnabled();
                MarkCollaborationModeSent(targetSession, collaborationMode);
                var completionPlan = SessionSendCompletionPlanner.Build(sendResult, targetSession, targetWorkspace, dispatchText, previewText);
                await ApplySessionSendCompletionPlanAsync(completionPlan, targetSession);
                SortSessions();
                ClearSendTargetAttachmentsAfterSuccess(targetSession, attachmentsSnapshot, includePendingAttachments);
                ApplySendTargetRefresh(targetSession);
                TraceSendDiagnostic("Complete", "send completed; " + BuildSendDiagnosticContext());
            }
            catch (Exception ex)
            {
                ClearPendingInvalidEncryptedContentRetry(targetSession != null ? targetSession.ThreadId : null);
                ClearLocalDesktopRealtimeTurn(targetSession != null ? targetSession.ThreadId : null);
                TraceSendDiagnosticError("SendFailed", ex);
                UpdateTurnProgressSummaryState(new AppServerEventArgs(
                    "turn_failed",
                    ex.Message,
                    threadId: targetSession != null ? targetSession.ThreadId : null));
                AddSendTargetHistoryItem(targetSession, ChatHistoryItemFactory.GetErrorSenderText(), ex.Message);
                if (ShouldApplySendTargetUi(targetSession))
                {
                    SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.SendFailed", "Send failed"));
                }
            }
        }

        private bool ShouldApplySendTargetUi(ChatSession targetSession)
        {
            return MainFormSendDispatchPlanner.ShouldApplyVisibleSessionUi(
                targetSession != null ? targetSession.Id : null,
                _currentSession != null ? _currentSession.Id : null);
        }

        private void AddSendTargetHistoryItem(ChatSession targetSession, string sender, string message)
        {
            if (ShouldApplySendTargetUi(targetSession))
            {
                AddChatHistoryItem(sender, message);
            }
        }

        private void ClearSendTargetInputBeforeDispatch(
            ChatSession targetSession,
            string capturedInputText,
            IReadOnlyList<PendingAttachmentItem> attachments)
        {
            if (targetSession == null || string.IsNullOrWhiteSpace(targetSession.Id))
            {
                return;
            }

            if (ShouldApplySendTargetUi(targetSession))
            {
                if (string.Equals(_inputBox.ComposeText, capturedInputText ?? string.Empty, StringComparison.Ordinal))
                {
                    SetInputBoxText(string.Empty, 0, 0);
                }
                return;
            }

            var draft = _composeDraftStore.Get(GetComposeDraftScopeKey(targetSession), targetSession.Id);
            var draftText = draft != null ? draft.DraftText : capturedInputText;
            var draftAttachments = draft != null ? draft.Attachments : attachments;
            _composeDraftStore.Save(
                GetComposeDraftScopeKey(targetSession),
                targetSession.Id,
                string.Equals(draftText ?? string.Empty, capturedInputText ?? string.Empty, StringComparison.Ordinal)
                    ? string.Empty
                    : draftText,
                draftAttachments);
        }

        private void ClearSendTargetAttachmentsAfterSuccess(
            ChatSession targetSession,
            IReadOnlyList<PendingAttachmentItem> sentAttachments,
            bool includePendingAttachments)
        {
            if (!includePendingAttachments || targetSession == null)
            {
                return;
            }

            var isVisibleTarget = ShouldApplySendTargetUi(targetSession);
            var draft = isVisibleTarget
                ? CaptureCurrentComposeDraftState()
                : _composeDraftStore.Get(GetComposeDraftScopeKey(targetSession), targetSession.Id);
            var retainedAttachments = (draft != null ? draft.Attachments : new PendingAttachmentItem[0])
                .Where(item => !ContainsSentAttachment(sentAttachments, item))
                .ToArray();
            var draftText = draft != null ? draft.DraftText ?? string.Empty : string.Empty;
            if (isVisibleTarget)
            {
                ReplacePendingAttachments(retainedAttachments, false);
                SynchronizePendingAttachmentsWithInputText(false);
            }

            if (string.IsNullOrEmpty(draftText) && retainedAttachments.Length == 0)
            {
                _composeDraftStore.Remove(
                    GetComposeDraftScopeKey(targetSession),
                    targetSession.Id,
                    false,
                    DeleteTemporaryAttachmentFile);
                return;
            }

            _composeDraftStore.Save(
                GetComposeDraftScopeKey(targetSession),
                targetSession.Id,
                draftText,
                retainedAttachments);
        }

        private static bool ContainsSentAttachment(
            IReadOnlyList<PendingAttachmentItem> sentAttachments,
            PendingAttachmentItem candidate)
        {
            if (candidate == null || sentAttachments == null)
            {
                return false;
            }

            for (var i = 0; i < sentAttachments.Count; i++)
            {
                var sent = sentAttachments[i];
                if (sent != null &&
                    sent.Kind == candidate.Kind &&
                    string.Equals(sent.LocalPath, candidate.LocalPath, StringComparison.Ordinal) &&
                    string.Equals(sent.MarkerText, candidate.MarkerText, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplySendTargetRefresh(ChatSession targetSession)
        {
            if (ShouldApplySendTargetUi(targetSession))
            {
                ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderPersist);
                return;
            }

            PersistState();
        }

        private SendOperationContext CaptureSendOperationContext()
        {
            return new SendOperationContext(
                _appConfigSnapshot != null ? _appConfigSnapshot.CodexRuntimeSource : null,
                GetCurrentRuntimeProfileIdForDiagnostics(),
                _currentWorkspace != null ? _currentWorkspace.Id : null,
                _currentSession != null ? _currentSession.Id : null);
        }

        private bool AbortSendIfContextChanged(SendOperationContext sendContext, string stage)
        {
            if (IsTargetExitPreparing || _isExitRequested) return true;
            if (sendContext == null ||
                sendContext.MatchesRuntime(
                    _appConfigSnapshot != null ? _appConfigSnapshot.CodexRuntimeSource : null,
                    GetCurrentRuntimeProfileIdForDiagnostics()))
            {
                return false;
            }

            TraceSendDiagnostic("Blocked", "send context changed after " + stage + "; capturedRuntime=" +
                FormatDiagnosticValue(sendContext.RuntimeSource) + "; current=" + BuildSendDiagnosticContext());
            SetStatus(UiLocalization.Text(
                "MainForm.Status.SendCancelledContextChanged",
                "Send cancelled because the active runtime or authentication source changed."));
            return true;
        }
    }
}
