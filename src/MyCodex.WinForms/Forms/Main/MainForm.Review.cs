using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly ReviewStartEventBuffer _reviewStartEvents = new ReviewStartEventBuffer();

        private async Task<SessionDefaultsResolution> ReadReviewSettingsAsync(ChatSession session, WorkspaceInfo workspace)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.StartReview", "Start review")))
            {
                throw new OperationCanceledException();
            }
            var settings = await ReadSessionModelSettingsAsync(session, workspace);
            var observed = _modelConfigurationService.GetObserved(BuildModelConfigurationScope(), workspace.RootPath);
            if (observed == null) throw new OperationCanceledException();
            var reviewModel = observed.WorkspaceConfig.ReviewModelId;
            if (!string.IsNullOrWhiteSpace(reviewModel)) settings.ModelId = reviewModel.Trim();
            if (!SessionDefaultsService.IsReasoningEffortAvailable(settings.ModelId, settings.ReasoningEffort, observed.Models))
            {
                throw new InvalidOperationException(UiLocalization.Text("MainForm.ModelConfig.InvalidEffort",
                    "The selected reasoning effort is not supported by this model. Update the session or official model settings."));
            }
            return settings;
        }

        private async Task StartInlineReviewAsync(ChatSession session, WorkspaceInfo workspace, AppServerReviewTarget target = null)
        {
            if (session == null || workspace == null || !CanReviewSession(session)) return;
            MarkThreadBusy(session.ThreadId);
            var started = false;
            try
            {
                SetStatus(UiLocalization.Text("MainForm.Status.StartingReview", "Starting review..."));
                var settings = await ReadReviewSettingsAsync(session, workspace);
                ResetTurnProgressSummaryBeforeSend(session.ThreadId);
                var result = await _sessionThreadOperationWorkflow.StartInlineReviewAsync(session, workspace,
                    GetEffectiveApprovalMode(session), settings.ModelId, settings.ReasoningEffort, settings.ModelId, target);
                started = result != null;
                session.LastActiveAt = DateTime.Now;
                SortSessions();
                ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderPersist);
                if (IsCurrentSessionThread(session.ThreadId))
                {
                    AddChatHistoryItem(ChatHistoryItemFactory.GetStatusSenderText(),
                        BuildReviewTargetMessage(target, false), ChatHistoryCategory.System);
                    AddReviewConfigurationMessage(result);
                }
                SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.ReviewStarted", "Review started"));
            }
            finally
            {
                if (!started) ClearThreadBusy(session.ThreadId);
                RefreshInputState();
            }
        }

        private async Task StartDetachedReviewAsync(ChatSession sourceSession, WorkspaceInfo workspace, AppServerReviewTarget target = null)
        {
            if (sourceSession == null || workspace == null || !CanReviewSession(sourceSession) || _reviewStartEvents.IsPending) return;
            MarkThreadBusy(sourceSession.ThreadId);
            _reviewStartEvents.Begin();
            try
            {
                SetStatus(UiLocalization.Text("MainForm.Status.StartingReview", "Starting review..."));
                var settings = await ReadReviewSettingsAsync(sourceSession, workspace);
                var result = await _sessionThreadOperationWorkflow.StartDetachedReviewAsync(sourceSession, workspace,
                    GetEffectiveApprovalMode(sourceSession), settings.ModelId, settings.ReasoningEffort, settings.ModelId, target);
                var resolution = await _sessionThreadOperationWorkflow.ResolveReviewSessionAsync(sourceSession, workspace,
                    result, FindSessionByThreadId, sourceSession.ApprovalMode, sourceSession.ModelId, sourceSession.ReasoningEffort);
                if (resolution == null || resolution.Session == null) return;
                if (resolution.IsNewSession) _sessions.Insert(0, resolution.Session);
                ReplayReviewStartEvents();
                _currentWorkspace = workspace;
                RefreshWorkspaceView();
                await ActivateSessionAsync(resolution.Session, true);
                resolution.Session.LastActiveAt = DateTime.Now;
                SortSessions();
                ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderPersist);
                AddChatHistoryItem(ChatHistoryItemFactory.GetStatusSenderText(),
                    BuildReviewTargetMessage(target, true), ChatHistoryCategory.System);
                AddReviewConfigurationMessage(result);
            }
            catch (AppServerReviewStartException ex)
            {
                var recovery = await _sessionThreadOperationWorkflow.ResolveReviewSessionAsync(sourceSession, workspace,
                    ex.CreatedReview, FindSessionByThreadId, sourceSession.ApprovalMode, sourceSession.ModelId, sourceSession.ReasoningEffort);
                if (recovery.Session != null)
                {
                    if (recovery.IsNewSession) _sessions.Insert(0, recovery.Session);
                    ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderPersist);
                }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
            finally
            {
                ReplayReviewStartEvents();
                ClearThreadBusy(sourceSession.ThreadId);
                RefreshInputState();
            }
        }

        internal static string BuildReviewTargetMessage(AppServerReviewTarget target, bool detached)
        {
            target = target ?? AppServerReviewTarget.CreateUncommittedChanges();
            string summary;
            switch (target.Kind)
            {
                case AppServerReviewTargetKind.BaseBranch:
                    summary = UiLocalization.Text("ReviewStartForm.Target.BaseBranch", "Base branch") + ": " + target.Branch;
                    break;
                case AppServerReviewTargetKind.Commit:
                    summary = UiLocalization.Text("ReviewStartForm.Target.Commit", "Commit") + ": " + target.Sha;
                    break;
                case AppServerReviewTargetKind.Custom:
                    summary = UiLocalization.Text("ReviewStartForm.Target.Custom", "Custom instructions") + ": " + target.Instructions;
                    break;
                default:
                    summary = UiLocalization.Text("ReviewStartForm.Value.TargetCurrentWorkspace", "Uncommitted changes");
                    break;
            }
            return string.Format(detached
                ? UiLocalization.Text("MainForm.Review.DetachedTarget.Template", "Review started in new session: {0}")
                : UiLocalization.Text("MainForm.Review.InlineTarget.Template", "Review started: {0}"), summary);
        }

        private void AddReviewConfigurationMessage(AppServerReviewStartResult result)
        {
            if (result == null) return;
            var configured = BuildModelSummaryText(result.ConfiguredModelId, result.ConfiguredReasoningEffort,
                UiLocalization.Text("MainForm.Review.UnknownModel", "Unknown"));
            AddChatHistoryItem(ChatHistoryItemFactory.GetStatusSenderText(),
                string.Format(UiLocalization.Text("MainForm.Review.Configuration.Template",
                    "Review configuration: {0}; directory: {1}. The server does not report the actual executing model."),
                    configured, result.WorkingDirectory), ChatHistoryCategory.System);
        }

        private void ReplayReviewStartEvents()
        {
            foreach (var item in _reviewStartEvents.Complete()) AppServerClientOnEventReceived(_appServerClient, item);
        }
    }

    internal sealed class ReviewStartEventBuffer
    {
        private readonly List<AppServerEventArgs> _events = new List<AppServerEventArgs>();
        internal bool IsPending { get; private set; }
        internal void Begin() { IsPending = true; }
        internal bool TryBuffer(AppServerEventArgs item, bool knownThread)
        {
            if (item != null && item.EventType == "app_server_disconnected")
            {
                Complete();
                return false;
            }
            if (!IsPending || knownThread || item == null || string.IsNullOrWhiteSpace(item.ThreadId)) return false;
            _events.Add(item);
            return true;
        }
        internal AppServerEventArgs[] Complete()
        {
            IsPending = false;
            var result = _events.ToArray();
            _events.Clear();
            return result;
        }
    }
}
