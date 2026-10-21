using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly AsyncInputRequestQueue _asyncInputRequests = new AsyncInputRequestQueue();
        private Button _pendingAsyncInputButton;
        private AsyncInputRequestForm _asyncInputRequestForm;

        private void InitializeAsyncInputUi()
        {
            _pendingAsyncInputButton = CreateActionButton(
                string.Empty,
                UiLocalization.Text("MainForm.Accessible.PendingAsyncInput", "Pending questions"),
                6,
                delegate { ShowPendingAsyncInputWindow(true); });
            _pendingAsyncInputButton.Visible = false;
        }

        private void HandleAsyncInputRequested(AppServerEventArgs request)
        {
            if (request == null || request.ToolQuestions == null || request.ToolQuestions.Count == 0)
            {
                return;
            }

            var wasEmpty = _asyncInputRequests.Count == 0;
            var pending = _asyncInputRequests.Enqueue(request, BuildAsyncInputSourceName(request.ThreadId));
            if (pending == null)
            {
                return;
            }

            EnsureAsyncInputForm();
            if (_asyncInputRequestForm.Visible)
            {
                _asyncInputRequestForm.AddRequest(pending);
            }
            else if (wasEmpty)
            {
                _asyncInputRequestForm.RefreshRequests(pending.Key);
                ShowPendingAsyncInputWindow(ContainsFocus || Form.ActiveForm == this);
            }

            RefreshPendingAsyncInputButton();
            var notice = string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("MainForm.Status.AsyncInputRequested.Template", "Received a request for additional information. {0} pending."),
                _asyncInputRequests.Count);
            SetHighPriorityStatus(notice);
            if (AsyncInputArrivalPlanner.ShouldAnnounce(wasEmpty, ContainsFocus || Form.ActiveForm == this))
            {
                SpeakAuxiliaryTextWithScreenReader(notice);
            }
        }

        private void EnsureAsyncInputForm()
        {
            if (_asyncInputRequestForm != null && !_asyncInputRequestForm.IsDisposed)
            {
                return;
            }

            _asyncInputRequestForm = new AsyncInputRequestForm(_asyncInputRequests);
            _asyncInputRequestForm.SubmitRequested += AsyncInputRequestForm_SubmitRequested;
            _asyncInputRequestForm.IgnoreRequested += AsyncInputRequestForm_IgnoreRequested;
            _asyncInputRequestForm.FormClosed += delegate { _asyncInputRequestForm = null; };
            _asyncInputRequestForm.RefreshRequests();
        }

        private void ShowPendingAsyncInputWindow(bool activate)
        {
            if (_asyncInputRequests.Count == 0)
            {
                return;
            }

            EnsureAsyncInputForm();
            if (!_asyncInputRequestForm.Visible)
            {
                _asyncInputRequestForm.RefreshRequests();
                _asyncInputRequestForm.Show(this);
            }

            if (activate)
            {
                _asyncInputRequestForm.Activate();
                _asyncInputRequestForm.FocusQuestion();
            }
        }

        private async void AsyncInputRequestForm_SubmitRequested(object sender, AsyncInputRequestEventArgs e)
        {
            var request = _asyncInputRequests.Find(e.RequestKey);
            if (request == null)
            {
                return;
            }

            var responseText = AsyncInputRequestQueue.BuildResponseText(request);
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return;
            }

            _asyncInputRequestForm.SetBusy(
                true,
                UiLocalization.Text("AsyncInputForm.Status.Sending", "Sending answers..."));
            try
            {
                await SendAsyncInputResponseAsync(request, responseText);
                CompleteAsyncInputRequest(request.Key);
                SetStatus(UiLocalization.Text("MainForm.Status.AsyncInputSent", "Additional information sent"));
            }
            catch (Exception ex)
            {
                _asyncInputRequestForm.SetBusy(false, ex.Message);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.AsyncInputSendFailed", "Additional information was not sent; your draft was kept"));
            }
        }

        private void AsyncInputRequestForm_IgnoreRequested(object sender, AsyncInputRequestEventArgs e)
        {
            CompleteAsyncInputRequest(e.RequestKey);
            SetStatus(UiLocalization.Text("MainForm.Status.AsyncInputIgnored", "Additional information request ignored"));
        }

        private async Task SendAsyncInputResponseAsync(PendingAsyncInputRequest pending, string responseText)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.SendAsyncInput", "Send additional information")))
            {
                throw new InvalidOperationException(UiLocalization.Text("MainForm.Status.AppServerUnavailable", "App Server is unavailable"));
            }

            var source = pending.Source;
            var session = FindSessionByThreadId(source.ThreadId);
            var workspace = session == null ? null : FindWorkspaceById(session.WorkspaceId);
            if (session == null || workspace == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                throw new InvalidOperationException(UiLocalization.Text("AsyncInputForm.Status.SourceUnavailable", "The source session is unavailable."));
            }

            var inputItems = new List<AppServerUserInputItem>
            {
                new AppServerUserInputItem { Type = "text", Text = responseText }
            };
            var sendResult = new SessionSendResult();
            var outcome = await AsyncInputResponseDispatcher.DispatchAsync(
                _appServerClient,
                session.ThreadId,
                source.TurnId,
                inputItems,
                () => IsThreadCurrentlyRunning(session.ThreadId),
                async delegate
                {
                    await EnsureAvailableModelsLoadedAsync(workspace);
                    if (BlockSendForUnavailableSessionModel(session))
                    {
                        throw new InvalidOperationException(UiLocalization.Text("MainForm.Status.SessionModelUnavailable", "The selected model is unavailable. Select a model before sending."));
                    }

                    var approvalMode = GetEffectiveApprovalMode(session);
                    var modelId = GetEffectiveModelId(session);
                    var reasoningEffort = GetEffectiveReasoningEffort(session);
                    var collaborationMode = BuildCollaborationModeForSend(session, workspace, modelId, reasoningEffort);
                    sendResult = await _sessionThreadService.SendUserMessageAsync(
                        session,
                        workspace,
                        inputItems,
                        approvalMode,
                        modelId,
                        reasoningEffort,
                        collaborationMode);
                    MarkCollaborationModeSent(session, collaborationMode);
                });
            if (outcome == AsyncInputDispatchOutcome.NewerTurnRunning)
            {
                throw new InvalidOperationException(UiLocalization.Text(
                    "AsyncInputForm.Status.NewTurnBusy",
                    "A newer turn is still running. Your draft was kept; send it after that turn finishes."));
            }

            if (outcome == AsyncInputDispatchOutcome.Steered)
            {
                session.LastActiveAt = DateTime.Now;
            }

            AddSendTargetHistoryItem(session, ChatHistoryItemFactory.GetUserSenderText(), responseText);
            try
            {
                await ApplyAsyncInputSendCompletionAsync(session, workspace, responseText, sendResult);
            }
            catch (Exception ex)
            {
                TraceSendDiagnosticError("AsyncInputCompletionFailed", ex);
            }
        }

        private async Task ApplyAsyncInputSendCompletionAsync(
            ChatSession session,
            WorkspaceInfo workspace,
            string responseText,
            SessionSendResult sendResult)
        {
            var completion = SessionSendCompletionPlanner.Build(sendResult, session, workspace, responseText, responseText);
            await ApplySessionSendCompletionPlanAsync(completion, session);
            SortSessions();
            ApplySendTargetRefresh(session);
        }

        private void CompleteAsyncInputRequest(string key)
        {
            _asyncInputRequests.Remove(key);
            RefreshPendingAsyncInputButton();
            if (_asyncInputRequestForm == null || _asyncInputRequestForm.IsDisposed)
            {
                return;
            }

            _asyncInputRequestForm.RefreshRequests();
            _asyncInputRequestForm.SetBusy(false, string.Empty);
            if (_asyncInputRequests.Count == 0)
            {
                _asyncInputRequestForm.Hide();
            }
        }

        private void RefreshPendingAsyncInputButton()
        {
            if (_pendingAsyncInputButton == null)
            {
                return;
            }

            _pendingAsyncInputButton.Text = string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("MainForm.Button.PendingAsyncInput.Template", "&Questions ({0})"),
                _asyncInputRequests.Count);
            _pendingAsyncInputButton.AccessibleName = string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("MainForm.Accessible.PendingAsyncInput.Template", "Pending questions, {0}"),
                _asyncInputRequests.Count);
            _pendingAsyncInputButton.Visible = _asyncInputRequests.Count > 0;
        }

        private string BuildAsyncInputSourceName(string threadId)
        {
            var session = FindSessionByThreadId(threadId);
            return AsyncInputSourceNameBuilder.Build(threadId, session, session == null ? null : FindWorkspaceById(session.WorkspaceId));
        }

    }
}
