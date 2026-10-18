using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private bool TryHandleInvalidEncryptedContentFailure(AppServerEventRoute route, AppServerEventArgs e)
        {
            var decision = _invalidEncryptedContentFailureState.Evaluate(route, e);
            if (!decision.ShouldSuppressDisplay)
            {
                return false;
            }

            var session = FindSessionByThreadId(e != null ? e.ThreadId : null);
            if (session == null)
            {
                return false;
            }

            if (decision.ShouldCompleteRepairAndRetry)
            {
                _ = CompleteInvalidEncryptedContentRepairAndRetryAsync(e, session);
            }

            return true;
        }

        private async Task<ThreadHistoryRepairResult> RepairInvalidEncryptedContentHistoryAsync(ChatSession session)
        {
            try
            {
                var repairService = await ResolveThreadHistoryRepairServiceAsync(session).ConfigureAwait(true);
                return await Task.Run(async () =>
                    await repairService.TryRepairInvalidEncryptedContentAsync(session).ConfigureAwait(false)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to repair invalid encrypted content. Message={0}", ex.Message);
                return ThreadHistoryRepairResult.NotRepaired("repair failed");
            }
        }

        private async Task<IThreadHistoryRepairService> ResolveThreadHistoryRepairServiceAsync(ChatSession session)
        {
            var localThreadCatalogClient = _appServerClient as IAppServerLocalThreadCatalogClient;
            if (localThreadCatalogClient == null)
            {
                return _threadHistoryRepairService;
            }

            var codexHomePaths = await localThreadCatalogClient.ListLocalCodexHomePathsForThreadScanAsync()
                .ConfigureAwait(true);
            return CreateThreadHistoryRepairService(new CodexThreadHistoryRepairService(BuildThreadHistoryRepairRoots(codexHomePaths)));
        }

        private static IReadOnlyList<string> BuildThreadHistoryRepairRoots(IEnumerable<string> codexHomePaths)
        {
            var roots = new List<string>();
            if (codexHomePaths == null)
            {
                return roots;
            }

            foreach (var codexHomePath in codexHomePaths)
            {
                if (string.IsNullOrWhiteSpace(codexHomePath))
                {
                    continue;
                }

                roots.Add(Path.Combine(codexHomePath, "sessions"));
                roots.Add(Path.Combine(codexHomePath, "archived_sessions"));
            }

            return roots;
        }

        private async Task CompleteInvalidEncryptedContentRepairAndRetryAsync(AppServerEventArgs e, ChatSession session)
        {
            try
            {
                var repairResult = await InvalidEncryptedContentRepairWorkflow.RepairWithAppServerRestartFallbackAsync(
                    () => ResetThreadAfterInvalidEncryptedContentRepairAsync(session),
                    () => RepairInvalidEncryptedContentHistoryAsync(session),
                    RestartAppServerAfterInvalidEncryptedContentRepairFailureAsync).ConfigureAwait(true);

                if (!repairResult.Repaired)
                {
                    ClearPendingInvalidEncryptedContentRetry(e != null ? e.ThreadId : null);
                    if (ShouldDisplayThreadEvent(e))
                    {
                        AddThreadEventHistoryItem(e, GetErrorSenderText(), e != null ? e.Content : string.Empty, ChatHistoryCategory.Error);
                        SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.ErrorOccurred", "An error occurred"));
                    }

                    return;
                }

                InvalidateSessionHistoryCache(session.ThreadId);
                ScheduleRealtimeCurrentSessionHistoryReload(new AppServerEventArgs(
                    "thread_history_repaired",
                    UiLocalization.Text(
                        "MainForm.Message.RepairedInvalidEncryptedContent",
                        "The current session history was repaired automatically. Send the message again if it was not resent."),
                    session.ThreadId));

                var retryContext = ConsumePendingInvalidEncryptedContentRetry(session.ThreadId);
                if (retryContext == null)
                {
                    if (ShouldDisplayThreadEvent(e))
                    {
                        AddChatHistoryItem(
                            ChatHistoryItemFactory.GetSystemSenderText(),
                            UiLocalization.Text(
                                "MainForm.Message.RepairedInvalidEncryptedContent",
                                "The current session history was repaired automatically. Send the message again if it was not resent."));
                        SetStatus(UiLocalization.Text(
                            "MainForm.Status.RepairedInvalidEncryptedContent",
                            "Session history was repaired"));
                    }

                    return;
                }

                await RetryInvalidEncryptedContentSendAsync(retryContext, repairResult).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ClearPendingInvalidEncryptedContentRetry(e != null ? e.ThreadId : null);
                Trace.TraceWarning("Invalid encrypted content repair workflow failed. Message={0}", ex.Message);
                AddChatHistoryItem(ChatHistoryItemFactory.GetErrorSenderText(), ex.Message);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.SendFailed", "Send failed"));
            }
        }

        private async Task RestartAppServerAfterInvalidEncryptedContentRepairFailureAsync()
        {
            try
            {
                if (!await EnsurePendingBundledRuntimeUpdateAsync()) return;
                Trace.TraceWarning("Restarting App Server after invalid encrypted content repair did not update the session file.");
                await _appServerClient.RestartAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to restart App Server after invalid encrypted content repair failure. Message={0}", ex.Message);
            }
        }

        private async Task ResetThreadAfterInvalidEncryptedContentRepairAsync(ChatSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return;
            }

            try
            {
                await _appServerClient.UnsubscribeThreadAsync(session.ThreadId).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to unsubscribe repaired thread. ThreadId={0}; Message={1}", session.ThreadId, ex.Message);
            }
        }

        private async Task RetryInvalidEncryptedContentSendAsync(
            PendingInvalidEncryptedContentRetryContext retryContext,
            ThreadHistoryRepairResult repairResult)
        {
            if (retryContext == null ||
                retryContext.Session == null ||
                string.IsNullOrWhiteSpace(retryContext.Session.ThreadId) ||
                retryContext.InputItems == null ||
                retryContext.InputItems.Count == 0)
            {
                return;
            }

            if (retryContext.RetryAttempted)
            {
                return;
            }

            retryContext.RetryAttempted = true;
            try
            {
                TraceSendDiagnostic("InvalidEncryptedContentRepair", "retrying repaired turn; thread=" + FormatDiagnosticValue(retryContext.Session.ThreadId));
                MarkLocalDesktopRealtimeTurn(retryContext.Session.ThreadId);
                var sendResult = await _sessionThreadService.SendUserMessageAsync(
                    retryContext.Session,
                    retryContext.Workspace,
                    retryContext.InputItems,
                    retryContext.ApprovalMode,
                    retryContext.ModelId,
                    retryContext.ReasoningEffort,
                    retryContext.CollaborationMode,
                    retryContext.ToolContext).ConfigureAwait(true);
                if (!sendResult.RepairedInvalidEncryptedContent)
                {
                    sendResult.InvalidEncryptedContentRepairResult = repairResult;
                }

                MarkCollaborationModeSent(retryContext.Session, retryContext.CollaborationMode);
                var completionPlan = SessionSendCompletionPlanner.Build(
                    sendResult,
                    retryContext.Session,
                    retryContext.Workspace,
                    retryContext.DispatchText,
                    retryContext.PreviewText);
                await ApplySessionSendCompletionPlanAsync(completionPlan, retryContext.Session).ConfigureAwait(true);
                SortSessions();
                ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderPersist);
                TraceSendDiagnostic("InvalidEncryptedContentRepair", "retry dispatched; thread=" + FormatDiagnosticValue(retryContext.Session.ThreadId));
            }
            catch (Exception ex)
            {
                ClearLocalDesktopRealtimeTurn(retryContext.Session.ThreadId);
                TraceSendDiagnosticError("InvalidEncryptedContentRetryFailed", ex);
                AddChatHistoryItem(ChatHistoryItemFactory.GetErrorSenderText(), ex.Message);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.SendFailed", "Send failed"));
            }
        }

        private void RegisterPendingInvalidEncryptedContentRetry(
            ChatSession session,
            WorkspaceInfo workspace,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort,
            AppServerCollaborationMode collaborationMode,
            AppServerThreadToolContext toolContext,
            string dispatchText,
            string previewText)
        {
            if (session == null ||
                string.IsNullOrWhiteSpace(session.ThreadId) ||
                inputItems == null ||
                inputItems.Count == 0)
            {
                return;
            }

            _pendingInvalidEncryptedContentRetries[session.ThreadId] = new PendingInvalidEncryptedContentRetryContext
            {
                Session = session,
                Workspace = workspace,
                InputItems = CloneInputItems(inputItems),
                ApprovalMode = approvalMode,
                ModelId = modelId,
                ReasoningEffort = reasoningEffort,
                CollaborationMode = collaborationMode,
                ToolContext = toolContext,
                DispatchText = dispatchText,
                PreviewText = previewText
            };
        }

        private PendingInvalidEncryptedContentRetryContext ConsumePendingInvalidEncryptedContentRetry(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return null;
            }

            PendingInvalidEncryptedContentRetryContext retryContext;
            if (!_pendingInvalidEncryptedContentRetries.TryGetValue(threadId, out retryContext))
            {
                return null;
            }

            _pendingInvalidEncryptedContentRetries.Remove(threadId);
            return retryContext;
        }

        private void ClearPendingInvalidEncryptedContentRetry(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _pendingInvalidEncryptedContentRetries.Remove(threadId);
        }

        private static List<AppServerUserInputItem> CloneInputItems(IReadOnlyCollection<AppServerUserInputItem> inputItems)
        {
            var result = new List<AppServerUserInputItem>();
            if (inputItems == null)
            {
                return result;
            }

            foreach (var item in inputItems)
            {
                if (item == null)
                {
                    continue;
                }

                result.Add(new AppServerUserInputItem
                {
                    Type = item.Type,
                    Name = item.Name,
                    Text = item.Text,
                    Path = item.Path,
                    Url = item.Url
                });
            }

            return result;
        }

        private sealed class PendingInvalidEncryptedContentRetryContext
        {
            public ChatSession Session { get; set; }

            public WorkspaceInfo Workspace { get; set; }

            public List<AppServerUserInputItem> InputItems { get; set; }

            public ApprovalMode? ApprovalMode { get; set; }

            public string ModelId { get; set; }

            public string ReasoningEffort { get; set; }

            public AppServerCollaborationMode CollaborationMode { get; set; }

            public AppServerThreadToolContext ToolContext { get; set; }

            public string DispatchText { get; set; }

            public string PreviewText { get; set; }

            public bool RetryAttempted { get; set; }
        }
    }
}
