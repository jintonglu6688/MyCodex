using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private bool CanToggleSessionPlanMode(ChatSession session)
        {
            return SessionPlanModePlanner.CanChangePlanOrGoalState(BuildPlanModeAvailabilityContext(session));
        }

        private bool CanManageSessionGoal(ChatSession session)
        {
            return SessionPlanModePlanner.CanChangePlanOrGoalState(BuildPlanModeAvailabilityContext(session));
        }

        private SessionPlanModeAvailabilityContext BuildPlanModeAvailabilityContext(ChatSession session)
        {
            var workspace = session == null ? null : FindWorkspaceById(session.WorkspaceId);
            var hasThread = session != null && !string.IsNullOrWhiteSpace(session.ThreadId);
            return new SessionPlanModeAvailabilityContext
            {
                HasSession = session != null,
                IsWorkspaceAvailable = IsWorkspaceAvailable(workspace),
                HasThread = hasThread,
                IsThreadBusy = hasThread && IsThreadBusy(session.ThreadId),
                CanInterruptThread = hasThread && CanInterruptThread(session.ThreadId),
                IsThreadCompacting = hasThread && IsThreadCompacting(session.ThreadId)
            };
        }

        private async Task SetSessionGoalAsync(ChatSession session)
        {
            if (!CanManageSessionGoal(session))
            {
                SetStatus(UiLocalization.Text("MainForm.Status.GoalUnavailable", "Goal mode is not available right now"));
                return;
            }

            using (var form = new SessionGoalForm(session.ThreadGoalObjective, session.ThreadGoalTokenBudget))
            {
                var result = form.ShowDialog(this);
                if (result != DialogResult.OK)
                {
                    return;
                }

                if (SessionDisplayFormatter.HasGoal(session))
                {
                    var replaceResult = MessageBox.Show(
                        this,
                        UiLocalization.Text("MainForm.Message.ReplaceGoalConfirm", "This session already has a goal. Replace it?"),
                        UiLocalization.Text("MainForm.DialogTitle.ReplaceGoal", "Replace Goal"),
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2);
                    if (replaceResult != DialogResult.Yes)
                    {
                        return;
                    }
                }

                if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.SetGoal", "Set goal")))
                {
                    return;
                }

                try
                {
                    SetStatus(UiLocalization.Text("MainForm.Status.SettingGoal", "Setting goal..."));
                    await EnsureSessionThreadCreatedAsync(session);
                    var workspace = FindWorkspaceById(session.WorkspaceId);
                    var goal = await _appServerClient.SetThreadGoalAsync(
                        session.ThreadId,
                        workspace.RootPath,
                        GetEffectiveApprovalMode(session),
                        GetEffectiveModelId(session),
                        form.Objective,
                        form.TokenBudget);

                    ApplyThreadGoalToSession(session, goal ?? CreateLocalThreadGoal(session, form.Objective, form.TokenBudget));
                    SetStatus(UiLocalization.Text("MainForm.Status.GoalSet", "Goal set"));
                    await StartSessionGoalExecutionAsync(session);
                }
                catch (Exception ex)
                {
                    AddChatHistoryItem(ChatHistoryItemFactory.GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                    SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.GoalSetFailed", "Failed to set goal"));
                }
            }
        }

        private async Task StartSessionGoalExecutionAsync(ChatSession session)
        {
            if (session == null ||
                _currentSession == null ||
                !string.Equals(session.Id, _currentSession.Id, StringComparison.OrdinalIgnoreCase) ||
                !SessionDisplayFormatter.HasGoal(session))
            {
                return;
            }

            if (!CanManageSessionGoal(session))
            {
                SetStatus(UiLocalization.Text("MainForm.Status.GoalExecutionUnavailable", "Goal set. Goal execution is not available right now"));
                return;
            }

            SetStatus(UiLocalization.Text("MainForm.Status.StartingGoalExecution", "Starting goal execution..."));
            await SendCurrentInputAsync(
                UiLocalization.Text("MainForm.Goal.StartPrompt", "Please start working on the current goal."),
                false);
        }

        private async Task ViewSessionGoalAsync(ChatSession session)
        {
            if (session == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(session.ThreadId) &&
                await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.ReadGoal", "Read goal")))
            {
                await RefreshSessionGoalAsync(session, true);
            }

            var text = SessionDisplayFormatter.HasGoal(session)
                ? BuildGoalDetailsText(session)
                : UiLocalization.Text("MainForm.Message.GoalNotSet", "No goal is set for this session.");

            MessageBox.Show(
                this,
                text,
                UiLocalization.Text("MainForm.DialogTitle.GoalDetails", "Session Goal"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private async Task ClearSessionGoalAsync(ChatSession session)
        {
            if (!CanManageSessionGoal(session))
            {
                SetStatus(UiLocalization.Text("MainForm.Status.GoalUnavailable", "Goal mode is not available right now"));
                return;
            }

            var confirm = MessageBox.Show(
                this,
                UiLocalization.Text("MainForm.Message.ClearGoalConfirm", "Clear the goal for this session?"),
                UiLocalization.Text("MainForm.DialogTitle.ClearGoal", "Clear Goal"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.ClearGoal", "Clear goal")))
            {
                return;
            }

            try
            {
                SetStatus(UiLocalization.Text("MainForm.Status.ClearingGoal", "Clearing goal..."));
                if (!string.IsNullOrWhiteSpace(session.ThreadId))
                {
                    var workspace = FindWorkspaceById(session.WorkspaceId);
                    await _appServerClient.ClearThreadGoalAsync(
                        session.ThreadId,
                        workspace.RootPath,
                        GetEffectiveApprovalMode(session),
                        GetEffectiveModelId(session));
                }

                ClearThreadGoalFromSession(session);
                SetStatus(UiLocalization.Text("MainForm.Status.GoalCleared", "Goal cleared"));
            }
            catch (Exception ex)
            {
                AddChatHistoryItem(ChatHistoryItemFactory.GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.GoalClearFailed", "Failed to clear goal"));
            }
        }

        private async Task EnsureSessionThreadCreatedAsync(ChatSession session)
        {
            if (session == null || !string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return;
            }

            var workspace = FindWorkspaceById(session.WorkspaceId);
            if (!IsWorkspaceAvailable(workspace))
            {
                throw new InvalidOperationException(UiLocalization.Text("MainForm.Message.WorkspaceUnavailable", "The workspace is unavailable."));
            }

            var recreation = await _sessionThreadService.RecreateThreadAsync(
                session,
                workspace,
                GetEffectiveApprovalMode(session),
                GetEffectiveModelId(session),
                GetEffectiveReasoningEffort(session),
                BuildThreadToolContext(session, workspace));
            ApplySessionThreadRecreationUiState(recreation, session);
        }

        private async Task RefreshSessionGoalAsync(ChatSession session, bool showErrors)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return;
            }

            var workspace = FindWorkspaceById(session.WorkspaceId);
            if (!IsWorkspaceAvailable(workspace))
            {
                return;
            }

            try
            {
                var goal = await _appServerClient.GetThreadGoalAsync(
                    session.ThreadId,
                    workspace.RootPath,
                    GetEffectiveApprovalMode(session),
                    GetEffectiveModelId(session));
                if (goal == null)
                {
                    ClearThreadGoalFromSession(session);
                    return;
                }

                ApplyThreadGoalToSession(session, BuildEffectiveThreadGoalForSession(session, goal));
            }
            catch (Exception ex)
            {
                if (showErrors)
                {
                    AddChatHistoryItem(ChatHistoryItemFactory.GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                    SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.GoalReadFailed", "Failed to read goal"));
                }
            }
        }

        private AppServerThreadGoal BuildEffectiveThreadGoalForSession(ChatSession session, AppServerThreadGoal goal)
        {
            if (session == null || goal == null)
            {
                return goal;
            }

            var effectiveStatus = ThreadGoalRuntimeStatePlanner.ResolveEffectiveStatus(
                goal.Status,
                IsThreadCurrentlyRunning(session.ThreadId));
            if (string.Equals(effectiveStatus, goal.Status, StringComparison.Ordinal))
            {
                return goal;
            }

            return new AppServerThreadGoal
            {
                ThreadId = goal.ThreadId,
                Objective = goal.Objective,
                Status = effectiveStatus,
                TokenBudget = goal.TokenBudget,
                TokensUsed = goal.TokensUsed,
                TimeUsedSeconds = goal.TimeUsedSeconds,
                CreatedAt = goal.CreatedAt,
                UpdatedAt = goal.UpdatedAt
            };
        }

        private void MarkPausedGoalActiveForRunningTurn(string threadId)
        {
            var session = FindSessionByThreadId(threadId);
            if (session == null ||
                !SessionDisplayFormatter.HasGoal(session) ||
                !string.Equals(session.ThreadGoalStatus, "paused", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            session.ThreadGoalStatus = "active";
            PersistState();
            RefreshSessionList();
            RefreshHeader();
            RefreshDisplayedSessionTitle(session);
        }

        private void RefreshGoalAfterTurnTerminal(string threadId)
        {
            var session = FindSessionByThreadId(threadId);
            if (session == null || !SessionDisplayFormatter.HasGoal(session))
            {
                return;
            }

            _ = RefreshSessionGoalAsync(session, false);
        }

        private void ApplyThreadGoalToSession(ChatSession session, AppServerThreadGoal goal)
        {
            ApplyThreadGoalToSession(session, goal, true, true);
        }

        private void ApplyThreadGoalToSession(ChatSession session, AppServerThreadGoal goal, bool refreshVisibleUi, bool persist)
        {
            if (session == null || goal == null)
            {
                return;
            }

            session.ThreadGoalObjective = goal.Objective;
            session.ThreadGoalStatus = goal.Status;
            session.ThreadGoalTokenBudget = goal.TokenBudget;
            session.ThreadGoalTokensUsed = goal.TokensUsed;
            session.ThreadGoalTimeUsedSeconds = goal.TimeUsedSeconds;
            session.ThreadGoalCreatedAt = goal.CreatedAt;
            session.ThreadGoalUpdatedAt = goal.UpdatedAt;
            if (persist)
            {
                PersistState();
            }

            if (refreshVisibleUi)
            {
                RefreshSessionList();
                RefreshHeader();
                RefreshDisplayedSessionTitle(session);
            }
        }

        private void ClearThreadGoalFromSession(ChatSession session)
        {
            ClearThreadGoalFromSession(session, true, true);
        }

        private void ClearThreadGoalFromSession(ChatSession session, bool refreshVisibleUi, bool persist)
        {
            if (session == null)
            {
                return;
            }

            session.ThreadGoalObjective = null;
            session.ThreadGoalStatus = null;
            session.ThreadGoalTokenBudget = null;
            session.ThreadGoalTokensUsed = null;
            session.ThreadGoalTimeUsedSeconds = null;
            session.ThreadGoalCreatedAt = null;
            session.ThreadGoalUpdatedAt = null;
            if (persist)
            {
                PersistState();
            }

            if (refreshVisibleUi)
            {
                RefreshSessionList();
                RefreshHeader();
                RefreshDisplayedSessionTitle(session);
            }
        }

        private static bool IsSignificantThreadGoalChange(ChatSession session, AppServerThreadGoal goal)
        {
            if (session == null || goal == null)
            {
                return false;
            }

            return !string.Equals(session.ThreadGoalObjective, goal.Objective, StringComparison.Ordinal) ||
                   !string.Equals(session.ThreadGoalStatus, goal.Status, StringComparison.Ordinal) ||
                   session.ThreadGoalTokenBudget != goal.TokenBudget;
        }

        private static AppServerThreadGoal CreateLocalThreadGoal(ChatSession session, string objective, long? tokenBudget)
        {
            return new AppServerThreadGoal
            {
                ThreadId = session != null ? session.ThreadId : null,
                Objective = objective,
                Status = "active",
                TokenBudget = tokenBudget
            };
        }

        private static string BuildGoalDetailsText(ChatSession session)
        {
            if (!SessionDisplayFormatter.HasGoal(session))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.AppendLine(string.Format(
                UiLocalization.Text("MainForm.Goal.Details.Objective.Template", "Goal: {0}"),
                session.ThreadGoalObjective.Trim()));
            if (!string.IsNullOrWhiteSpace(session.ThreadGoalStatus))
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("MainForm.Goal.Details.Status.Template", "Status: {0}"),
                    SessionDisplayFormatter.MapGoalStatusText(session.ThreadGoalStatus)));
            }

            if (session.ThreadGoalTokensUsed.HasValue)
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("MainForm.Goal.Details.TokensUsed.Template", "Tokens used: {0}"),
                    FormatTokenCountInTenThousands(session.ThreadGoalTokensUsed.Value)));
            }

            if (session.ThreadGoalTokenBudget.HasValue)
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("MainForm.Goal.Details.TokenBudget.Template", "Token budget: {0}"),
                    FormatTokenCountInTenThousands(session.ThreadGoalTokenBudget.Value)));
            }

            if (session.ThreadGoalTimeUsedSeconds.HasValue)
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("MainForm.Goal.Details.TimeUsed.Template", "Time used: {0}"),
                    FormatDuration(session.ThreadGoalTimeUsedSeconds.Value)));
            }

            return builder.ToString().TrimEnd();
        }

        private static string FormatTokenCountInTenThousands(long tokenCount)
        {
            var value = tokenCount / 10000M;
            return string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("MainForm.Goal.TokenCount.TenThousands.Template", "{0:0.##} ten-thousand"),
                value);
        }

        private static string FormatDuration(long seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }

            var duration = TimeSpan.FromSeconds(seconds);
            if (duration.TotalDays >= 1)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("MainForm.Goal.Duration.WithDays.Template", "{0} day(s) {1:00}:{2:00}:{3:00}"),
                    (int)duration.TotalDays,
                    duration.Hours,
                    duration.Minutes,
                    duration.Seconds);
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("MainForm.Goal.Duration.HoursMinutesSeconds.Template", "{0:00}:{1:00}:{2:00}"),
                duration.Hours,
                duration.Minutes,
                duration.Seconds);
        }

        private void SetSessionPlanMode(ChatSession session, bool enabled, bool addHistoryItem)
        {
            if (session == null)
            {
                return;
            }

            var changed = session.IsPlanModeEnabled != enabled;
            session.IsPlanModeEnabled = enabled;

            if (enabled || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                _sessionsPendingDefaultCollaborationMode.Remove(session.Id);
            }
            else
            {
                _sessionsPendingDefaultCollaborationMode.Add(session.Id);
                if (!string.IsNullOrWhiteSpace(session.ThreadId))
                {
                    _pendingExecutablePlans.Remove(session.ThreadId);
                }
            }

            PersistState();
            RefreshSessionList();
            RefreshHeader();
            RefreshInputState();

            if (changed && addHistoryItem && IsCurrentSession(session))
            {
                AddChatHistoryItem(
                    ChatHistoryItemFactory.GetSystemSenderText(),
                    enabled
                        ? UiLocalization.Text("MainForm.Message.PlanModeEnabled", "Plan mode is enabled for this session. Send messages normally, or use /plan followed by a prompt.")
                        : UiLocalization.Text("MainForm.Message.PlanModeDisabled", "Plan mode is disabled for this session."));
            }

            SetStatus(enabled
                ? UiLocalization.Text("MainForm.Status.PlanModeEnabled", "Plan mode enabled")
                : UiLocalization.Text("MainForm.Status.PlanModeDisabled", "Plan mode disabled"));
        }

        private bool IsCurrentSession(ChatSession session)
        {
            return session != null &&
                   _currentSession != null &&
                   string.Equals(session.Id, _currentSession.Id, StringComparison.OrdinalIgnoreCase);
        }

        private void MarkCollaborationModeSent(ChatSession session, AppServerCollaborationMode collaborationMode)
        {
            if (session == null ||
                collaborationMode == null ||
                !string.Equals(collaborationMode.Mode, "default", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _sessionsPendingDefaultCollaborationMode.Remove(session.Id);
        }

        private void RegisterExecutablePlanCandidate(AppServerEventArgs e)
        {
            if (e == null ||
                !IsPlanEvent(e) ||
                string.IsNullOrWhiteSpace(e.ThreadId) ||
                string.IsNullOrWhiteSpace(e.Content))
            {
                return;
            }

            _pendingExecutablePlans[e.ThreadId] = new PendingExecutablePlan(e.ThreadId, e.TurnId, e.Content);
        }

        private async Task PromptForPlanExecutionIfReadyAsync(AppServerEventArgs turnCompletedEvent)
        {
            if (turnCompletedEvent == null ||
                string.IsNullOrWhiteSpace(turnCompletedEvent.ThreadId) ||
                _currentSession == null ||
                !_currentSession.IsPlanModeEnabled ||
                !IsCurrentSessionThread(turnCompletedEvent.ThreadId))
            {
                return;
            }

            PendingExecutablePlan plan;
            if (!_pendingExecutablePlans.TryGetValue(turnCompletedEvent.ThreadId, out plan))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(plan.TurnId) &&
                !string.IsNullOrWhiteSpace(turnCompletedEvent.TurnId) &&
                !string.Equals(plan.TurnId, turnCompletedEvent.TurnId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _pendingExecutablePlans.Remove(turnCompletedEvent.ThreadId);

            try
            {
                var requestId = "plan-exec-" + Guid.NewGuid().ToString("N");
                using (var form = new PlanExecutionPromptForm(plan.Content))
                using (RegisterRemotePlanExecutionPrompt(requestId, plan, form))
                {
                    await ForwardRemotePlanExecutionRequestedAsync(requestId, plan).ConfigureAwait(true);

                    var result = form.ShowDialog(this);
                    var shouldExecutePlan = result == DialogResult.OK && form.ShouldExecutePlan;
                    await ForwardRemotePlanExecutionCompletedAsync(
                        requestId,
                        plan,
                        shouldExecutePlan ? "execute" : "continue").ConfigureAwait(true);

                    if (!shouldExecutePlan)
                    {
                        SetStatus(UiLocalization.Text("MainForm.Status.ContinuePlanning", "Plan mode remains enabled"));
                        return;
                    }
                }

                await ExecuteAcceptedPlanAsync(turnCompletedEvent.ThreadId);
            }
            catch (Exception ex)
            {
                AddChatHistoryItem(ChatHistoryItemFactory.GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.PlanExecutionStartFailed", "Failed to start plan execution"));
            }
        }

        private IDisposable RegisterRemotePlanExecutionPrompt(string requestId, PendingExecutablePlan plan, PlanExecutionPromptForm form)
        {
            if (string.IsNullOrWhiteSpace(requestId) || plan == null || form == null)
            {
                return null;
            }

            var pending = new PendingRemotePlanExecutionPrompt(requestId, plan.ThreadId, plan.TurnId, plan.Content);
            _pendingRemotePlanExecutionPrompts[requestId] = pending;
            return _remoteInteractionDialogs.Register(requestId, delegate
            {
                if (!form.IsDisposed)
                {
                    form.CloseAsExternallyResolved(string.Equals(pending.Action, "execute", StringComparison.OrdinalIgnoreCase));
                }
            });
        }

        private async Task ForwardRemotePlanExecutionRequestedAsync(string requestId, PendingExecutablePlan plan)
        {
            if (_remoteAppServerEventForwarder == null || plan == null)
            {
                return;
            }

            try
            {
                await _remoteAppServerEventForwarder.ForwardAsync(
                    new AppServerEventArgs(
                        "plan_execution_requested",
                        plan.Content,
                        threadId: plan.ThreadId,
                        turnId: plan.TurnId,
                        requestId: requestId),
                    System.Threading.CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                // Local plan confirmation remains available if the remote channel cannot publish.
            }
        }

        private async Task ForwardRemotePlanExecutionCompletedAsync(string requestId, PendingExecutablePlan plan, string action)
        {
            _pendingRemotePlanExecutionPrompts.Remove(requestId);
            if (_remoteAppServerEventForwarder == null || plan == null)
            {
                return;
            }

            try
            {
                await _remoteAppServerEventForwarder.ForwardAsync(
                    new AppServerEventArgs(
                        "plan_execution_completed",
                        string.Empty,
                        threadId: plan.ThreadId,
                        turnId: plan.TurnId,
                        requestId: requestId,
                        details: action),
                    System.Threading.CancellationToken.None).ConfigureAwait(true);
            }
            catch
            {
                // The decision has already been applied locally.
            }
        }

        private Task<CodingRemotePlanExecutionResponseResult> RespondRemotePlanExecutionAsync(
            CodingRemotePlanExecutionResponseRequest request,
            System.Threading.CancellationToken cancellationToken)
        {
            if (InvokeRequired)
            {
                var completion = new TaskCompletionSource<CodingRemotePlanExecutionResponseResult>();
                BeginInvoke(new Action(async delegate
                {
                    try
                    {
                        completion.SetResult(await RespondRemotePlanExecutionAsync(request, cancellationToken).ConfigureAwait(true));
                    }
                    catch (Exception ex)
                    {
                        completion.SetException(ex);
                    }
                }));
                return completion.Task;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Plan execution request ID is required.");
            }

            PendingRemotePlanExecutionPrompt pending;
            if (!_pendingRemotePlanExecutionPrompts.TryGetValue(request.RequestId, out pending))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.RequestNotFound, "Plan execution request was not found.");
            }

            if (!string.IsNullOrWhiteSpace(request.ThreadId) &&
                !string.Equals(request.ThreadId, pending.ThreadId, StringComparison.OrdinalIgnoreCase))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.RequestNotFound, "Plan execution request was not found.");
            }

            if (!string.Equals(request.Action, "execute", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.Action, "continue", StringComparison.OrdinalIgnoreCase))
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Plan execution action is invalid.");
            }

            pending.Action = request.Action;
            _remoteInteractionDialogs.Complete(request.RequestId);
            return Task.FromResult(new CodingRemotePlanExecutionResponseResult
            {
                RequestId = request.RequestId,
                ThreadId = pending.ThreadId,
                Action = pending.Action,
                Completed = true,
                StatusText = string.Empty
            });
        }

        private async Task ExecuteAcceptedPlanAsync(string threadId)
        {
            if (_currentSession == null ||
                string.IsNullOrWhiteSpace(threadId) ||
                !IsCurrentSessionThread(threadId))
            {
                return;
            }

            if (!CanToggleSessionPlanMode(_currentSession))
            {
                SetStatus(UiLocalization.Text("MainForm.Status.PlanExecutionUnavailable", "Plan execution is not available right now"));
                return;
            }

            SetSessionPlanMode(_currentSession, false, true);
            await SendCurrentInputAsync(
                UiLocalization.Text("MainForm.PlanMode.ExecutePrompt", "Please execute the plan above."),
                false);
        }

        private static bool IsPlanEvent(AppServerEventArgs e)
        {
            return e != null && string.Equals(e.EventType, "plan", StringComparison.OrdinalIgnoreCase);
        }
    }
}
