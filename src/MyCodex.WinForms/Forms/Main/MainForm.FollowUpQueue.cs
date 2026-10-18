using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly ThreadFollowUpQueue<QueuedFollowUpMessage> _followUpMessageQueue =
            new ThreadFollowUpQueue<QueuedFollowUpMessage>();

        private string GetConfiguredActiveTurnFollowUpMode()
        {
            return ActiveTurnFollowUpMode.Normalize(
                _appConfigSnapshot != null ? _appConfigSnapshot.ActiveTurnFollowUpMode : null);
        }

        private void EnqueueFollowUpMessage(
            ChatSession session,
            WorkspaceInfo workspace,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort,
            AppServerCollaborationMode collaborationMode,
            AppServerThreadToolContext toolContext,
            string dispatchText,
            string previewText,
            IReadOnlyList<PendingAttachmentItem> attachments)
        {
            var queuedMessage = new QueuedFollowUpMessage(
                session,
                workspace,
                inputItems,
                approvalMode,
                modelId,
                reasoningEffort,
                collaborationMode,
                toolContext,
                dispatchText,
                previewText,
                attachments);
            _followUpMessageQueue.Enqueue(session.ThreadId, queuedMessage);
        }

        private void ScheduleQueuedFollowUpDispatch(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId) || IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(new Action(() => { _ = DispatchNextQueuedFollowUpAsync(threadId); }));
        }

        private async Task DispatchNextQueuedFollowUpAsync(string threadId)
        {
            QueuedFollowUpMessage queuedMessage;
            if (!_followUpMessageQueue.TryBeginDispatch(threadId, out queuedMessage))
            {
                return;
            }

            var turnStarted = false;
            var currentThreadId = threadId;
            try
            {
                if (queuedMessage.Session == null ||
                    queuedMessage.Workspace == null ||
                    !string.Equals(queuedMessage.Session.ThreadId, threadId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(UiLocalization.Text(
                        "MainForm.Error.QueuedMessageSessionUnavailable",
                        "The queued message could not be sent because its session is no longer available."));
                }

                InvalidateSessionHistoryCache(threadId);
                ResetTurnProgressSummaryBeforeSend(threadId, CollectTurnSkillNames(queuedMessage.Attachments));
                MarkLocalDesktopRealtimeTurn(threadId);
                RegisterPendingInvalidEncryptedContentRetry(
                    queuedMessage.Session,
                    queuedMessage.Workspace,
                    queuedMessage.InputItems,
                    queuedMessage.ApprovalMode,
                    queuedMessage.ModelId,
                    queuedMessage.ReasoningEffort,
                    queuedMessage.CollaborationMode,
                    queuedMessage.ToolContext,
                    queuedMessage.DispatchText,
                    queuedMessage.PreviewText);
                TraceSendDiagnostic("QueuedDispatch", "sending queued turn/start; thread=" + FormatDiagnosticValue(threadId));
                var sendResult = await _sessionThreadService.SendUserMessageAsync(
                    queuedMessage.Session,
                    queuedMessage.Workspace,
                    queuedMessage.InputItems,
                    queuedMessage.ApprovalMode,
                    queuedMessage.ModelId,
                    queuedMessage.ReasoningEffort,
                    queuedMessage.CollaborationMode,
                    queuedMessage.ToolContext);
                turnStarted = true;
                currentThreadId = queuedMessage.Session.ThreadId;
                if (!string.Equals(threadId, currentThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    ClearLocalDesktopRealtimeTurn(threadId);
                    MarkLocalDesktopRealtimeTurn(currentThreadId);
                }

                MarkCollaborationModeSent(queuedMessage.Session, queuedMessage.CollaborationMode);
                _followUpMessageQueue.MovePending(threadId, currentThreadId);
                var completionPlan = SessionSendCompletionPlanner.Build(
                    sendResult,
                    queuedMessage.Session,
                    queuedMessage.Workspace,
                    queuedMessage.DispatchText,
                    queuedMessage.PreviewText);
                await ApplySessionSendCompletionPlanAsync(completionPlan, queuedMessage.Session);
                SortSessions();
                ApplySendTargetRefresh(queuedMessage.Session);
                TraceSendDiagnostic("QueuedDispatchComplete", "queued turn started; thread=" + FormatDiagnosticValue(currentThreadId));
            }
            catch (Exception ex)
            {
                HandleQueuedFollowUpSendFailure(queuedMessage, currentThreadId, ex);
            }
            finally
            {
                _followUpMessageQueue.CompleteDispatch(threadId);
            }

            ScheduleNextQueuedFollowUpIfReady(threadId, currentThreadId, turnStarted);
        }

        private void HandleQueuedFollowUpSendFailure(
            QueuedFollowUpMessage queuedMessage,
            string threadId,
            Exception exception)
        {
            ClearPendingInvalidEncryptedContentRetry(threadId);
            ClearLocalDesktopRealtimeTurn(threadId);
            TraceSendDiagnosticError("QueuedSendFailed", exception);
            UpdateTurnProgressSummaryState(new AppServerEventArgs(
                "turn_failed",
                exception.Message,
                threadId: threadId));
            AddSendTargetHistoryItem(queuedMessage.Session, ChatHistoryItemFactory.GetErrorSenderText(), exception.Message);
            if (ShouldApplySendTargetUi(queuedMessage.Session))
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.SendFailed", "Send failed"));
            }
        }

        private void ScheduleNextQueuedFollowUpIfReady(
            string originalThreadId,
            string currentThreadId,
            bool turnStarted)
        {
            if (!turnStarted)
            {
                ScheduleQueuedFollowUpDispatch(originalThreadId);
                return;
            }

            if (_followUpMessageQueue.HasPending(currentThreadId) && !IsThreadCurrentlyRunning(currentThreadId))
            {
                ScheduleQueuedFollowUpDispatch(currentThreadId);
            }
        }

        private sealed class QueuedFollowUpMessage
        {
            internal QueuedFollowUpMessage(
                ChatSession session,
                WorkspaceInfo workspace,
                IReadOnlyCollection<AppServerUserInputItem> inputItems,
                ApprovalMode? approvalMode,
                string modelId,
                string reasoningEffort,
                AppServerCollaborationMode collaborationMode,
                AppServerThreadToolContext toolContext,
                string dispatchText,
                string previewText,
                IReadOnlyList<PendingAttachmentItem> attachments)
            {
                Session = session;
                Workspace = workspace;
                InputItems = inputItems;
                ApprovalMode = approvalMode;
                ModelId = modelId;
                ReasoningEffort = reasoningEffort;
                CollaborationMode = collaborationMode;
                ToolContext = toolContext;
                DispatchText = dispatchText ?? string.Empty;
                PreviewText = previewText ?? string.Empty;
                Attachments = attachments ?? new PendingAttachmentItem[0];
            }

            internal ChatSession Session { get; }

            internal WorkspaceInfo Workspace { get; }

            internal IReadOnlyCollection<AppServerUserInputItem> InputItems { get; }

            internal ApprovalMode? ApprovalMode { get; }

            internal string ModelId { get; }

            internal string ReasoningEffort { get; }

            internal AppServerCollaborationMode CollaborationMode { get; }

            internal AppServerThreadToolContext ToolContext { get; }

            internal string DispatchText { get; }

            internal string PreviewText { get; }

            internal IReadOnlyList<PendingAttachmentItem> Attachments { get; }
        }
    }
}
