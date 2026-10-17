using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private Task<CodingRemoteMessageSendResult> TryDispatchRemoteActiveTurnMessageAsync(
            CodingRemoteActiveTurnDispatchContext context,
            CancellationToken cancellationToken)
        {
            if (InvokeRequired)
            {
                var completion = new TaskCompletionSource<CodingRemoteMessageSendResult>();
                BeginInvoke(new Action(async delegate
                {
                    try
                    {
                        completion.SetResult(await TryDispatchRemoteActiveTurnMessageAsync(context, cancellationToken).ConfigureAwait(true));
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(ex);
                    }
                }));
                return completion.Task;
            }

            return TryDispatchRemoteActiveTurnMessageOnUiThreadAsync(context, cancellationToken);
        }

        private async Task<CodingRemoteMessageSendResult> TryDispatchRemoteActiveTurnMessageOnUiThreadAsync(
            CodingRemoteActiveTurnDispatchContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context == null || string.IsNullOrWhiteSpace(context.SessionId))
            {
                return null;
            }

            var session = FindSessionById(context.SessionId);
            var workspace = session == null ? null : FindWorkspaceById(session.WorkspaceId);
            if (session == null || workspace == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return null;
            }

            var inputItems = context.InputItems;
            var dispatchDecision = MainFormSendDispatchPlanner.Choose(new MainFormSendDispatchContext
            {
                ThreadId = session.ThreadId,
                IsThreadRunning = IsThreadCurrentlyRunning(session.ThreadId),
                ActiveTurnFollowUpMode = GetConfiguredActiveTurnFollowUpMode(),
                InputItemCount = inputItems == null ? 0 : inputItems.Count,
                HasSpecialCollaborationPayload = context.CollaborationMode != null
            });
            if (dispatchDecision.Mode == MainFormSendDispatchMode.TurnStart)
            {
                return null;
            }

            TraceSendDiagnostic(
                "RemoteDispatch",
                "dispatching mobile follow-up; mode=" + dispatchDecision.Mode + "; thread=" + FormatDiagnosticValue(session.ThreadId));
            if (dispatchDecision.Mode == MainFormSendDispatchMode.Queue)
            {
                AddSendTargetHistoryItem(session, ChatHistoryItemFactory.GetUserSenderText(), context.PreviewText);
                EnqueueFollowUpMessage(
                    session,
                    workspace,
                    inputItems,
                    context.ApprovalMode,
                    context.ModelId,
                    context.ReasoningEffort,
                    context.CollaborationMode,
                    context.ToolContext,
                    context.DispatchText,
                    context.PreviewText,
                    new PendingAttachmentItem[0]);
                var completionPlan = SessionSendCompletionPlanner.Build(
                    new SessionSendResult(),
                    session,
                    workspace,
                    context.DispatchText,
                    context.PreviewText);
                await ApplySessionSendCompletionPlanAsync(completionPlan, session).ConfigureAwait(true);
                SortSessions();
                ApplySendTargetRefresh(session);
                if (ShouldApplySendTargetUi(session))
                {
                    SetStatus(UiLocalization.Text("MainForm.Status.MessageQueued", "Message queued"));
                }

                return CreateRemoteFollowUpResult(context, "queue");
            }

            MarkLocalDesktopRealtimeTurn(session.ThreadId);
            var steerResult = await _appServerClient.TrySteerTurnAsync(session.ThreadId, inputItems).ConfigureAwait(true);
            if (steerResult == null || !steerResult.Accepted)
            {
                ClearLocalDesktopRealtimeTurn(session.ThreadId);
                TraceSendDiagnostic(
                    "RemoteDispatchFallback",
                    "mobile turn/steer fallback; reason=" + FormatDiagnosticValue(steerResult != null ? steerResult.FallbackReason : null));
                return null;
            }

            AddSendTargetHistoryItem(session, ChatHistoryItemFactory.GetUserSenderText(), context.PreviewText);
            session.LastActiveAt = DateTime.Now;
            var steerCompletionPlan = SessionSendCompletionPlanner.Build(
                new SessionSendResult(),
                session,
                workspace,
                context.DispatchText,
                context.PreviewText);
            await ApplySessionSendCompletionPlanAsync(steerCompletionPlan, session).ConfigureAwait(true);
            SortSessions();
            ApplySendTargetRefresh(session);
            return CreateRemoteFollowUpResult(context, "steer");
        }

        private static CodingRemoteMessageSendResult CreateRemoteFollowUpResult(
            CodingRemoteActiveTurnDispatchContext context,
            string dispatchMode)
        {
            return new CodingRemoteMessageSendResult
            {
                SessionId = context.SessionId,
                OperationId = context.OperationId,
                Accepted = true,
                DispatchMode = dispatchMode
            };
        }
    }
}
