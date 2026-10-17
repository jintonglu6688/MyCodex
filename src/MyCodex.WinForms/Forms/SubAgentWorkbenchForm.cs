using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed partial class SubAgentWorkbenchForm : Form
    {
        private readonly ChatSession _parentSession;
        private readonly SubAgentWorkbenchOperations _operations;
        private readonly List<ChatSession> _subAgents;
        private readonly ListView _subAgentListView;
        private readonly ContextMenu _subAgentContextMenu;
        private readonly MenuItem _openSubAgentMenuItem;
        private readonly MenuItem _deleteSubAgentMenuItem;
        private readonly MenuItem _deleteAllSubAgentsMenuItem;
        private readonly ChatWorkbenchControl _chatWorkbench;
        private readonly ActivityVirtualBrowseControl _activityBrowseControl;
        private readonly Label _activityCurrentStatusLabel;
        private readonly Dictionary<string, TurnProgressSummaryState> _turnSummaryStates;
        private readonly Dictionary<string, ActivityTimelineState> _activityTimelineStates;
        private string _activitySummaryText;
        private bool _isLoadingHistory;
        private readonly Dictionary<string, string> _runningTurns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pendingSendThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ComposeDraftState> _agentDrafts = new Dictionary<string, ComposeDraftState>(StringComparer.OrdinalIgnoreCase);
        private bool _isSending => ActiveSession != null && !string.IsNullOrWhiteSpace(ActiveSession.ThreadId) &&
            (_runningTurns.ContainsKey(ActiveSession.ThreadId) || _pendingSendThreadIds.Contains(ActiveSession.ThreadId));
        private bool _isDeleting;

        public SubAgentWorkbenchForm(
            ChatSession parentSession,
            IReadOnlyList<ChatSession> subAgents,
            SubAgentWorkbenchOperations operations)
        {
            if (operations == null)
            {
                throw new ArgumentNullException(nameof(operations));
            }

            _parentSession = parentSession;
            _operations = operations;
            _subAgents = new List<ChatSession>();

            Text = BuildWindowTitle(parentSession);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(980, 640);
            Size = new Size(1100, 760);
            ShowInTaskbar = true;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _subAgentListView = CreateSubAgentListView();
            _openSubAgentMenuItem = new MenuItem(UiLocalization.Text("SubAgentWorkbenchForm.Context.Open", "Open(&O)"));
            _deleteSubAgentMenuItem = new MenuItem(UiLocalization.Text("SubAgentWorkbenchForm.Context.Delete", "Delete(&D)"));
            _deleteAllSubAgentsMenuItem = new MenuItem(UiLocalization.Text("SubAgentWorkbenchForm.Context.DeleteAll", "Delete All(&A)"));
            _subAgentContextMenu = new ContextMenu(new[] { _openSubAgentMenuItem, new MenuItem("-"), _deleteSubAgentMenuItem, _deleteAllSubAgentsMenuItem });
            _subAgentListView.ContextMenu = _subAgentContextMenu;
            _chatWorkbench = new ChatWorkbenchControl(CreateWorkbenchCallbacks());
            _activityBrowseControl = CreateActivityBrowseControl();
            _activityCurrentStatusLabel = CreateActivityCurrentStatusLabel();
            _turnSummaryStates = new Dictionary<string, TurnProgressSummaryState>(StringComparer.OrdinalIgnoreCase);
            _activityTimelineStates = new Dictionary<string, ActivityTimelineState>(StringComparer.OrdinalIgnoreCase);
            _activitySummaryText = string.Empty;

            _subAgentListView.DoubleClick += async (sender, e) => await ActivateSelectedSubAgentAsync();
            _subAgentListView.KeyDown += SubAgentListView_KeyDown;
            _subAgentListView.MouseDown += SubAgentListView_MouseDown;
            _subAgentListView.SelectedIndexChanged += delegate { RefreshActionState(); };
            _subAgentContextMenu.Popup += delegate { RefreshActionState(); };
            _openSubAgentMenuItem.Click += async (sender, e) => await ActivateSelectedSubAgentFromListAsync();
            _deleteSubAgentMenuItem.Click += async (sender, e) => await DeleteSelectedSubAgentAsync();
            _deleteAllSubAgentsMenuItem.Click += async (sender, e) => await DeleteAllSubAgentsAsync();

            Controls.Add(BuildLayout());
            RefreshSubAgents(subAgents);
            if (ActiveSession == null && _subAgents.Count > 0)
            {
                ShowNoActiveSubAgentMessage();
                SetStatus(UiLocalization.Text(
                    "SubAgentWorkbenchForm.Status.SelectSubAgentToOpen",
                    "Select a subagent and press Enter to open it."));
            }

            RefreshActionState();

            KeyDown += SubAgentWorkbenchForm_KeyDown;
            ConfigureObservation();
        }

        internal ChatSession ActiveSession { get; private set; }

        internal bool IsBusy
        {
            get { return _isLoadingHistory || _pendingSendThreadIds.Count > 0 || _isSending || _isDeleting ||
                (ActiveSession != null && _operations.IsWorking != null && _operations.IsWorking(ActiveSession)); }
        }

        internal bool HasPendingTargetOperation => _pendingSendThreadIds.Count > 0 || _isDeleting;

        private ChatWorkbenchHostCallbacks CreateWorkbenchCallbacks()
        {
            var callbacks = _operations.WorkbenchCallbacks ?? new ChatWorkbenchHostCallbacks();
            return new ChatWorkbenchHostCallbacks
            {
                SelectAttachmentsAsync = callbacks.SelectAttachmentsAsync,
                SelectImageAttachmentsAsync = callbacks.SelectImageAttachmentsAsync,
                SelectFileAttachmentsAsync = callbacks.SelectFileAttachmentsAsync,
                SelectFolderAttachmentsAsync = callbacks.SelectFolderAttachmentsAsync,
                SelectSkillAttachmentsAsync = callbacks.SelectSkillAttachmentsAsync,
                SelectPluginAttachmentsAsync = callbacks.SelectPluginAttachmentsAsync,
                CanPasteAttachments = callbacks.CanPasteAttachments,
                PasteAttachmentsAsync = callbacks.PasteAttachmentsAsync,
                ValidateAttachmentsAsync = callbacks.ValidateAttachmentsAsync,
                ReadImageBytes = callbacks.ReadImageBytes,
                OpenMessageDetail = callbacks.OpenMessageDetail,
                AnnounceStatus = callbacks.AnnounceStatus,
                DeleteTemporaryAttachment = callbacks.DeleteTemporaryAttachment,
                SendAsync = SendCurrentInputAsync,
                StopAsync = StopActiveSubAgentAsync,
                CanSend = () =>
                    ActiveSession != null &&
                    !_pendingSendThreadIds.Contains(ActiveSession.ThreadId ?? string.Empty) &&
                    !_isLoadingHistory &&
                    !_isDeleting &&
                    (callbacks.CanSend == null || callbacks.CanSend()),
                CanStop = () => !_isDeleting && CanStopActiveSession()
            };
        }

        internal void RefreshSubAgents(IReadOnlyList<ChatSession> subAgents)
        {
            var activeSessionId = ActiveSession != null ? ActiveSession.Id : null;
            var selectedSession = GetSelectedListSession();
            var selectedSessionId = selectedSession != null ? selectedSession.Id : null;
            var listHadFocus = _subAgentListView.Focused || _subAgentListView.ContainsFocus;
            var nextSubAgents = (subAgents ?? Array.Empty<ChatSession>())
                .Where(session => session != null)
                .ToList();
            var activeSessionRemoved = ActiveSession != null &&
                !nextSubAgents.Any(session => string.Equals(session.Id, ActiveSession.Id, StringComparison.OrdinalIgnoreCase));
            if (!activeSessionRemoved && IsSameSubAgentIdentityList(_subAgents, nextSubAgents))
            {
                _subAgents.Clear();
                _subAgents.AddRange(nextSubAgents);
                UpdateSubAgentListItems(nextSubAgents);
                RestoreSubAgentListFocus(listHadFocus);
                RefreshActionState();
                return;
            }

            _subAgents.Clear();
            _subAgents.AddRange(nextSubAgents);

            if (activeSessionRemoved)
            {
                ActiveSession = null;
                activeSessionId = null;
            }

            BindSubAgentList(activeSessionId ?? selectedSessionId);
            if (activeSessionRemoved && _subAgents.Count > 0)
            {
                ShowNoActiveSubAgentMessage();
            }

            RefreshActivityDisplay();
            RefreshActionState();
            RestoreSubAgentListFocus(listHadFocus);
        }

        internal async Task ActivateSelectedSubAgentAsync()
        {
            var session = GetSelectedListSession();
            if (session == null)
            {
                return;
            }

            if (!ReferenceEquals(ActiveSession, session))
            {
                if (ActiveSession != null) _agentDrafts[ActiveSession.Id] = _chatWorkbench.CaptureDraftState();
                _chatWorkbench.RestoreDraftState(_agentDrafts.TryGetValue(session.Id, out var draft) ? draft : null);
            }
            ActiveSession = session;
            await LoadActiveSessionHistoryAsync(true);
            RefreshActivityDisplay();
            RefreshActionState();
        }

        internal async Task DeleteSelectedSubAgentAsync()
        {
            var session = GetSelectedListSession();
            if (session == null)
            {
                return;
            }

            await DeleteSubAgentsAsync(new[] { session }, false);
        }

        internal async Task DeleteAllSubAgentsAsync()
        {
            if (_subAgents.Count == 0)
            {
                return;
            }

            await DeleteSubAgentsAsync(_subAgents.ToArray(), true);
        }

        internal void HandleAppServerEvent(AppServerEventArgs e)
        {
            if (e != null && e.EventType == "app_server_disconnected")
            {
                _historyLoadVersion++;
                _isLoadingHistory = false;
                _runningTurns.Clear();
                RefreshActionState();
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.RefreshFailed", "Refresh failed; status may be outdated. Press F5 to retry."));
                return;
            }
            if (e?.SubAgentActivity != null && !e.IsHistorical &&
                (string.Equals(e.ThreadId, _parentSession?.ThreadId, StringComparison.OrdinalIgnoreCase) ||
                 _subAgents.Any(session => string.Equals(session.ThreadId, e.ThreadId, StringComparison.OrdinalIgnoreCase))))
            {
                var activity = e.SubAgentActivity;
                var id = activity.AgentThreadId;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    _operations.HistoryLoadWorkflow?.Invalidate(id);
                    if (activity.Kind == "started" || activity.Kind == "interacted") _runningTurns[id] = null;
                    else if (activity.Kind == "completed" || activity.Kind == "interrupted") _runningTurns.Remove(id);
                    if (string.Equals(ActiveSession?.ThreadId, id, StringComparison.OrdinalIgnoreCase))
                    {
                        _historyRevision++;
                        _ = LoadActiveSessionHistoryAsync(false);
                    }
                    else if (!_subAgents.Any(session => string.Equals(session.ThreadId, id, StringComparison.OrdinalIgnoreCase)))
                        _ = RefreshObservedSubAgentsAsync();
                    RefreshActionState();
                }
            }
            if (e == null || string.IsNullOrWhiteSpace(e.ThreadId))
            {
                return;
            }

            if (!_subAgents.Any(session => string.Equals(session.ThreadId, e.ThreadId, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            ApplySubAgentActivityEvent(e);
            if (SessionHistoryEventPolicy.ShouldInvalidateForEvent(e))
            {
                _operations.HistoryLoadWorkflow?.Invalidate(e.ThreadId);
                if (ActiveSession != null && string.Equals(ActiveSession.ThreadId, e.ThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    _historyRevision++;
                }
            }
            var route = AppServerEventRouting.Resolve(e);
            if (route == AppServerEventRoute.TurnStarted)
            {
                _runningTurns[e.ThreadId] = e.TurnId;
            }
            else if (AppServerEventRouting.IsTurnTerminal(route))
            {
                string runningTurn;
                if (!_runningTurns.TryGetValue(e.ThreadId, out runningTurn) ||
                    string.IsNullOrWhiteSpace(e.TurnId) || string.Equals(runningTurn, e.TurnId, StringComparison.Ordinal))
                {
                    _runningTurns.Remove(e.ThreadId);
                }
            }
            RefreshActionState();
            if (ActiveSession == null ||
                !string.Equals(ActiveSession.ThreadId, e.ThreadId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (route == AppServerEventRoute.TurnStarted)
            {
                SetStatus(e.Content);
                RefreshActionState();
                return;
            }

            if (AppServerEventRouting.IsTurnTerminal(route))
            {
                SetStatus(e.Content);
                RefreshActionState();
                _ = LoadActiveSessionHistoryAsync(false);
                if (!AppServerEventRouting.IsErrorDisplayEvent(route))
                {
                    return;
                }
            }

            if (route == AppServerEventRoute.Assistant && AssistantMessageUtility.HasDisplayableContent(e.Content))
            {
                AddHistoryItem("codex", e.Content, ChatHistoryCategory.Conversation);
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.ReplyReceived", "Reply received"));
                return;
            }

            if (AppServerEventRouting.IsErrorDisplayEvent(route))
            {
                AddHistoryItem(UiLocalization.Text("MainForm.Chat.Sender.Error", "Error"), e.Content, ChatHistoryCategory.Error);
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.ErrorOccurred", "An error occurred"));
            }
        }

        private void ShowNoActiveSubAgentMessage()
        {
            ReplaceHistoryItems(new[]
            {
                CreateHistoryItem(
                    UiLocalization.Text("MainForm.Chat.Sender.System", "System"),
                    UiLocalization.Text("SubAgentWorkbenchForm.Message.NoActiveSubAgent", "No subagent is active."),
                    ChatHistoryCategory.System)
            });
        }

        private bool IsActiveHistoryLoadTarget(ChatSession session, string threadId)
        {
            return ActiveSession != null &&
                   session != null &&
                   string.Equals(ActiveSession.Id, session.Id, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(ActiveSession.ThreadId, threadId, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyHistoryLoadResult(SessionHistoryLoadResult result)
        {
            if (result == null || result.Kind == SessionHistoryLoadResultKind.Stale)
            {
                return;
            }

            if (result.ShouldReplaceHistory)
            {
                ReplaceHistoryItems(result.HistoryItems);
            }

            if (result.HasStatus)
            {
                SetStatus(result.Kind == SessionHistoryLoadResultKind.MissingThread
                    ? UiLocalization.Text("SubAgentWorkbenchForm.ThreadUnavailable", "The agent thread is unavailable. Its identity is retained; no replacement will be created.")
                    : result.StatusText);
            }
        }

        private void ApplySubAgentActivityEvent(AppServerEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ThreadId))
            {
                return;
            }

            var summaryState = EnsureTurnSummaryState(e.ThreadId);
            var timelineChanged = EnsureActivityTimelineState(e.ThreadId).ApplyEvent(e);
            var summaryChanged = TurnProgressSummaryReducer.ApplyEvent(summaryState, e);
            if (!summaryChanged && !timelineChanged)
            {
                return;
            }

            if (ActiveSession != null &&
                string.Equals(ActiveSession.ThreadId, e.ThreadId, StringComparison.OrdinalIgnoreCase))
            {
                RefreshActivityDisplay();
            }
        }

        private TurnProgressSummaryState EnsureTurnSummaryState(string threadId)
        {
            TurnProgressSummaryState state;
            if (!_turnSummaryStates.TryGetValue(threadId, out state))
            {
                state = new TurnProgressSummaryState();
                _turnSummaryStates[threadId] = state;
            }

            return state;
        }

        private ActivityTimelineState EnsureActivityTimelineState(string threadId)
        {
            ActivityTimelineState state;
            if (!_activityTimelineStates.TryGetValue(threadId, out state))
            {
                state = new ActivityTimelineState();
                _activityTimelineStates[threadId] = state;
            }

            return state;
        }

        private void RefreshActivityDisplay()
        {
            if (_activityBrowseControl == null)
            {
                return;
            }

            _activitySummaryText = BuildActiveSubAgentActivitySummaryText();
            var currentStatus = ActivityDisplayState.BuildCurrentStatus(
                _activitySummaryText,
                UiLocalization.Text("MainForm.Activity.Current.Empty", "No active activity currently."));
            var items = BuildActiveSubAgentActivityTimelineItems(
                ActivityDisplayState.BuildStableItems(
                    _activitySummaryText,
                    UiLocalization.Text("MainForm.Activity.List.Empty", "No activity records currently.")));

            if (_activityCurrentStatusLabel.Text != currentStatus) _activityCurrentStatusLabel.Text = currentStatus;
            if (!_activityBrowseControl.GetReadableItems().SequenceEqual(items)) _activityBrowseControl.SetItems(items);
        }

        private string BuildActiveSubAgentActivitySummaryText()
        {
            if (ActiveSession == null || string.IsNullOrWhiteSpace(ActiveSession.ThreadId))
            {
                return string.Empty;
            }

            TurnProgressSummaryState state;
            if (!_turnSummaryStates.TryGetValue(ActiveSession.ThreadId, out state))
            {
                return string.Empty;
            }

            return TurnProgressSummaryReducer.BuildSummaryText(state);
        }

        private string[] BuildActiveSubAgentActivityTimelineItems(string[] fallbackItems)
        {
            if (ActiveSession == null || string.IsNullOrWhiteSpace(ActiveSession.ThreadId))
            {
                return fallbackItems ?? new string[0];
            }

            ActivityTimelineState state;
            if (!_activityTimelineStates.TryGetValue(ActiveSession.ThreadId, out state))
            {
                return fallbackItems ?? new string[0];
            }

            return state.BuildDisplayItems(
                UiLocalization.Text("MainForm.Activity.List.Empty", "No activity records currently."));
        }

        private async Task<ChatWorkbenchSendResult> SendCurrentInputAsync(ChatWorkbenchSendRequest request)
        {
            if (ActiveSession == null || _operations.SendAsync == null)
            {
                return new ChatWorkbenchSendResult(
                    false,
                    false,
                    UiLocalization.Text("SubAgentWorkbenchForm.Status.NoActiveSubAgent", "No active subagent"));
            }

            var sendingSession = ActiveSession;
            var sendingThreadId = sendingSession.ThreadId;
            var sentDraft = _chatWorkbench.CaptureDraftState();
            _pendingSendThreadIds.Add(sendingThreadId);
            if (ActiveSession != null && !string.IsNullOrWhiteSpace(ActiveSession.ThreadId))
            {
                TurnProgressSummaryReducer.ResetForPreparing(EnsureTurnSummaryState(ActiveSession.ThreadId));
                EnsureActivityTimelineState(ActiveSession.ThreadId).Clear();
                RefreshActivityDisplay();
            }

            RefreshActionState();
            AddHistoryItem(
                UiLocalization.Text("MainForm.Chat.Sender.User", "Me"),
                request != null ? request.PreviewText : string.Empty,
                ChatHistoryCategory.Conversation);
            try
            {
                var result = await _operations.SendAsync(sendingSession, request);
                if (result?.IsSuccess == true && result.ClearInput)
                {
                    if (_agentDrafts.TryGetValue(sendingSession.Id, out var draft) && draft.DraftText == sentDraft.DraftText &&
                        SameAttachments(draft.Attachments, sentDraft.Attachments))
                        _agentDrafts.Remove(sendingSession.Id);
                }
                if (!ReferenceEquals(ActiveSession, sendingSession))
                    return new ChatWorkbenchSendResult(result?.IsSuccess == true, false, CanStopActiveSession(), string.Empty);

                if (ReferenceEquals(ActiveSession, sendingSession) && result != null && !string.IsNullOrWhiteSpace(result.StatusText))
                {
                    SetStatus(result.StatusText);
                }

                if (_operations.PersistState != null)
                {
                    _operations.PersistState();
                }

                return result == null ? new ChatWorkbenchSendResult(false, false, string.Empty) :
                    new ChatWorkbenchSendResult(result.IsSuccess, result.ClearInput && _chatWorkbench.InputText == sentDraft.DraftText &&
                        SameAttachments(_chatWorkbench.PendingAttachments, sentDraft.Attachments),
                        _runningTurns.ContainsKey(sendingThreadId), result.StatusText);
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(ActiveSession, sendingSession))
                {
                    AddHistoryItem(UiLocalization.Text("MainForm.Chat.Sender.Error", "Error"), ex.Message, ChatHistoryCategory.Error);
                    SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.SendFailed", "Send failed"));
                }
                return new ChatWorkbenchSendResult(
                    false,
                    false,
                    ReferenceEquals(ActiveSession, sendingSession) ? UiLocalization.Text("SubAgentWorkbenchForm.Status.SendFailed", "Send failed") : string.Empty);
            }
            finally
            {
                _pendingSendThreadIds.Remove(sendingThreadId);
                RefreshActionState();
            }
        }

        private static bool SameAttachments(IReadOnlyList<PendingAttachmentItem> first, IReadOnlyList<PendingAttachmentItem> second)
        {
            return first.Count == second.Count && first.Zip(second, (a, b) =>
                a.LocalPath == b.LocalPath && a.Kind == b.Kind && a.InputName == b.InputName &&
                a.MarkerText == b.MarkerText && a.IsDirectory == b.IsDirectory && a.IsRemotePath == b.IsRemotePath &&
                (a.InMemoryContent ?? Array.Empty<byte>()).SequenceEqual(b.InMemoryContent ?? Array.Empty<byte>())).All(equal => equal);
        }

        private async Task StopActiveSubAgentAsync()
        {
            if (ActiveSession == null || _operations.StopAsync == null)
            {
                return;
            }

            var stoppingSession = ActiveSession;
            try
            {
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.RequestingStop", "Requesting stop..."));
                await _operations.StopAsync(stoppingSession);
                if (ReferenceEquals(ActiveSession, stoppingSession))
                    SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.StopRequested", "Stop requested"));
            }
            catch (Exception ex)
            {
                if (!ReferenceEquals(ActiveSession, stoppingSession)) return;
                AddHistoryItem(UiLocalization.Text("MainForm.Chat.Sender.Error", "Error"), ex.Message, ChatHistoryCategory.Error);
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.StopFailed", "Stop failed"));
            }
            finally
            {
                RefreshActionState();
            }
        }

        private async Task DeleteSubAgentsAsync(IReadOnlyList<ChatSession> sessions, bool isDeleteAll)
        {
            if (_isDeleting || _operations.DeleteAsync == null)
            {
                return;
            }

            var targets = NormalizeDeleteTargets(sessions);
            if (targets.Count == 0 || (_operations.RefreshAsync == null && !ConfirmDeleteSubAgents(targets, isDeleteAll)))
            {
                return;
            }

            _isDeleting = true;
            RefreshActionState();
            SetStatus(isDeleteAll
                ? string.Format(UiLocalization.Text("SubAgentWorkbenchForm.Status.DeletingAll.Template", "Deleting {0} subagents..."), targets.Count)
                : UiLocalization.Text("SubAgentWorkbenchForm.Status.Deleting", "Deleting subagent..."));
            try
            {
                await _operations.DeleteAsync(targets);
                if (_operations.RefreshAsync != null)
                {
                    await _operations.RefreshAsync();
                    return;
                }
                var targetKeys = new HashSet<string>(targets.Select(BuildSessionIdentityKey), StringComparer.OrdinalIgnoreCase);
                RefreshSubAgents(_subAgents.Where(session => !targetKeys.Contains(BuildSessionIdentityKey(session))).ToArray());
                SetStatus(isDeleteAll
                    ? string.Format(UiLocalization.Text("SubAgentWorkbenchForm.Status.DeletedAll.Template", "Deleted {0} subagents."), targets.Count)
                    : UiLocalization.Text("SubAgentWorkbenchForm.Status.Deleted", "Subagent deleted"));
            }
            catch (Exception ex)
            {
                AddHistoryItem(UiLocalization.Text("MainForm.Chat.Sender.Error", "Error"), ex.Message, ChatHistoryCategory.Error);
                SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Status.DeleteFailed", "Delete failed"));
            }
            finally
            {
                _isDeleting = false;
                RefreshActionState();
            }
        }

        private bool ConfirmDeleteSubAgents(IReadOnlyList<ChatSession> sessions, bool isDeleteAll)
        {
            if (_operations.ConfirmDelete != null)
            {
                return _operations.ConfirmDelete(sessions, isDeleteAll);
            }

            var message = isDeleteAll
                ? string.Format(
                    UiLocalization.Text(
                        "SubAgentWorkbenchForm.Confirm.DeleteAll.Template",
                        "Permanently delete all {0} subagents?{1}{1}This cannot be undone."),
                    sessions.Count,
                    Environment.NewLine)
                : string.Format(
                    UiLocalization.Text(
                        "SubAgentWorkbenchForm.Confirm.Delete.Template",
                        "Permanently delete subagent \"{0}\"?{1}{1}This cannot be undone."),
                    BuildDeleteDisplayTitle(sessions[0]),
                    Environment.NewLine);
            return MessageBox.Show(
                this,
                message,
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void BindSubAgentList(string preferredSessionId)
        {
            _subAgentListView.BeginUpdate();
            try
            {
                _subAgentListView.Items.Clear();
                foreach (var session in _subAgents)
                {
                    _subAgentListView.Items.Add(CreateSubAgentListItem(session));
                }

                if (_subAgentListView.Items.Count == 0)
                {
                    ReplaceHistoryItems(new[]
                    {
                        CreateHistoryItem(
                            UiLocalization.Text("MainForm.Chat.Sender.System", "System"),
                            UiLocalization.Text("SubAgentWorkbenchForm.Message.NoSubAgents", "No subagents are available for this session."),
                            ChatHistoryCategory.System)
                    });
                    return;
                }

                var selectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(preferredSessionId))
                {
                    for (var i = 0; i < _subAgentListView.Items.Count; i++)
                    {
                        var session = _subAgentListView.Items[i].Tag as ChatSession;
                        if (session != null && string.Equals(session.Id, preferredSessionId, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }

                SelectSubAgentItemAt(selectedIndex);
            }
            finally
            {
                _subAgentListView.EndUpdate();
            }
        }

        private ListViewItem CreateSubAgentListItem(ChatSession session)
        {
            var name = !string.IsNullOrWhiteSpace(session.SubAgentNickname)
                ? session.SubAgentNickname
                : UiLocalization.Text("SubAgentWorkbenchForm.Value.UnknownName", "(Unknown)");
            var role = !string.IsNullOrWhiteSpace(session.SubAgentRole)
                ? session.SubAgentRole
                : UiLocalization.Text("SubAgentWorkbenchForm.Value.UnknownRole", "(Unknown)");
            var title = SessionDisplayFormatter.BuildDisplayTitle(session);
            var summary = _operations.GetSummary?.Invoke(session.ThreadId);
            var status = SubAgentDisplayPlanner.BuildStatus(summary?.ThreadStatus, _operations.GetAgentStatus?.Invoke(session.ThreadId));
            var work = _operations.GetRecentWork?.Invoke(session.ThreadId) ?? session.Preview ?? string.Empty;
            return new ListViewItem(new[] { name, role, title, status, SubAgentDisplayPlanner.Abbreviate(work), session.LastActiveAt.ToString("g"), session.ParentThreadId ?? string.Empty })
            {
                Tag = session,
                ToolTipText = string.Format("{0}, {1}, {2}, {3}\r\n{4}", name, role, title, status, work)
            };
        }

        private void SelectSubAgentItemAt(int index)
        {
            if (index < 0 || index >= _subAgentListView.Items.Count)
            {
                return;
            }

            _subAgentListView.SelectedItems.Clear();
            _subAgentListView.Items[index].Selected = true;
            _subAgentListView.Items[index].Focused = true;
        }

        private static bool IsSameSubAgentIdentityList(IReadOnlyList<ChatSession> current, IReadOnlyList<ChatSession> next)
        {
            if (current == null || next == null || current.Count != next.Count)
            {
                return false;
            }

            for (var i = 0; i < current.Count; i++)
            {
                if (!IsSameSubAgentIdentity(current[i], next[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSameSubAgentIdentity(ChatSession left, ChatSession right)
        {
            return left != null &&
                   right != null &&
                   string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(left.ThreadId, right.ThreadId, StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateSubAgentListItems(IReadOnlyList<ChatSession> sessions)
        {
            if (sessions == null || sessions.Count != _subAgentListView.Items.Count)
            {
                return;
            }

            for (var i = 0; i < sessions.Count; i++)
            {
                var item = _subAgentListView.Items[i];
                var refreshed = CreateSubAgentListItem(sessions[i]);
                if (item.Text != refreshed.Text) item.Text = refreshed.Text;
                while (item.SubItems.Count < refreshed.SubItems.Count)
                {
                    item.SubItems.Add(string.Empty);
                }

                for (var subItemIndex = 1; subItemIndex < refreshed.SubItems.Count; subItemIndex++)
                {
                    if (item.SubItems[subItemIndex].Text != refreshed.SubItems[subItemIndex].Text)
                        item.SubItems[subItemIndex].Text = refreshed.SubItems[subItemIndex].Text;
                }

                if (item.ToolTipText != refreshed.ToolTipText) item.ToolTipText = refreshed.ToolTipText;
                item.Tag = sessions[i];
            }
        }

        private void RestoreSubAgentListFocus(bool shouldRestore)
        {
            if (!shouldRestore || _subAgentListView.Focused || _subAgentListView.IsDisposed || !_subAgentListView.CanFocus)
            {
                return;
            }

            _subAgentListView.Focus();
        }

        private ChatSession GetSelectedListSession()
        {
            return _subAgentListView.SelectedItems.Count == 0
                ? null
                : _subAgentListView.SelectedItems[0].Tag as ChatSession;
        }

        private static List<ChatSession> NormalizeDeleteTargets(IEnumerable<ChatSession> sessions)
        {
            if (sessions == null)
            {
                return new List<ChatSession>();
            }

            return sessions
                .Where(session => session != null)
                .GroupBy(BuildSessionIdentityKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static string BuildSessionIdentityKey(ChatSession session)
        {
            if (session == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(session.Id))
            {
                return "id:" + session.Id;
            }

            return "thread:" + (session.ThreadId ?? string.Empty);
        }

        private static string BuildDeleteDisplayTitle(ChatSession session)
        {
            return session == null
                ? UiLocalization.Text("MainForm.Value.UnnamedSession", "Unnamed session")
                : SessionDisplayFormatter.BuildDisplayTitle(session);
        }

        private void ReplaceHistoryItems(ChatHistoryItem[] items)
        {
            _chatWorkbench.ReplaceHistoryItems(items);
        }

        private void AddHistoryItem(string sender, string message, ChatHistoryCategory category)
        {
            var item = CreateHistoryItem(sender, message, category);
            _chatWorkbench.AddHistoryItem(item);
        }

        private static ChatHistoryItem CreateHistoryItem(string sender, string message, ChatHistoryCategory category)
        {
            var prefix = sender + UiLocalization.Text("MainForm.Chat.Prefix.Separator", ":");
            var normalized = message ?? string.Empty;
            var shouldSanitize = ChatMessageTextSanitizer.ShouldSanitizeSender(sender);
            var displayMessage = shouldSanitize
                ? ChatMessageTextSanitizer.SanitizeForDisplay(normalized)
                : normalized.Replace("\r", " ").Replace("\n", " ");
            var accessibleMessage = shouldSanitize
                ? ChatMessageTextSanitizer.SanitizeForReadableText(normalized)
                : normalized;
            var displayText = string.Format("{0} {1}", prefix, displayMessage).Trim();
            var fullText = string.Format("{0} {1}", prefix, normalized).Trim();
            var accessibleText = string.Format("{0} {1}", prefix, accessibleMessage).Trim();
            return new ChatHistoryItem
            {
                Sender = sender,
                Category = category,
                DisplayText = displayText,
                FullText = fullText,
                AccessibleText = shouldSanitize ? accessibleText : fullText,
                MarkdownText = normalized
            };
        }

        private void RefreshActionState()
        {
            var selectedSession = GetSelectedListSession();
            var canDelete = !_isDeleting && _operations.DeleteAsync != null;
            _openSubAgentMenuItem.Enabled = selectedSession != null && !_isLoadingHistory && !_isDeleting;
            _deleteSubAgentMenuItem.Enabled = canDelete && selectedSession != null;
            _deleteAllSubAgentsMenuItem.Enabled = canDelete && _subAgents.Count > 0;
            _chatWorkbench.SetSendingState(_isSending);
            _chatWorkbench.RefreshActionState();
        }

        private bool CanStopActiveSession()
        {
            if (_isSending)
            {
                return true;
            }

            return _operations.CanStop != null && _operations.CanStop(ActiveSession);
        }

        private void SetStatus(string text)
        {
            _chatWorkbench.SetStatus(text);
        }

        private Control BuildLayout()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Size = ClientSize,
                FixedPanel = FixedPanel.Panel1,
                Panel1MinSize = 300,
                Panel2MinSize = 480,
                SplitterDistance = 360,
                TabStop = false
            };
            Shown += (sender, args) =>
            {
                split.Panel1MinSize = UiTheme.Scale(300);
                split.Panel2MinSize = UiTheme.Scale(480);
            };
            split.Panel1.TabStop = false;
            split.Panel2.TabStop = false;
            split.Panel1.Controls.Add(BuildLeftPanel());
            split.Panel2.Controls.Add(BuildRightPanel());
            return split;
        }

        private Control BuildLeftPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.PanelBackColor,
                TabStop = false
            };

            var title = CreateSectionTitle(UiLocalization.Text("SubAgentWorkbenchForm.Section.Subagents", "Subagents"));
            _subAgentListView.Dock = DockStyle.Fill;
            panel.Controls.Add(_subAgentListView);
            panel.Controls.Add(title);
            return panel;
        }

        private Control BuildRightPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Size = new Size(Math.Max(480, ClientSize.Width - 364), Math.Max(400, ClientSize.Height - 28)),
                Orientation = Orientation.Horizontal,
                FixedPanel = FixedPanel.Panel2,
                Panel1MinSize = 230,
                Panel2MinSize = 160,
                SplitterDistance = Math.Max(230, ClientSize.Height - 218),
                TabStop = false
            };
            Shown += (sender, args) =>
            {
                split.Panel1MinSize = UiTheme.Scale(230);
                split.Panel2MinSize = UiTheme.Scale(160);
            };
            split.Panel1.TabStop = false;
            split.Panel2.TabStop = false;
            _chatWorkbench.Dock = DockStyle.Fill;
            split.Panel1.Controls.Add(_chatWorkbench);
            split.Panel2.Controls.Add(BuildActivityPanel());
            panel.Controls.Add(split);
            return panel;
        }

        private Control BuildActivityPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.PanelBackColor,
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var title = CreateSectionTitle(BuildActivitySectionTitleText());
            title.AccessibleName = UiLocalization.Text("MainForm.Accessible.Activity", "Activity");
            title.Margin = new Padding(0);
            _activityCurrentStatusLabel.Margin = new Padding(0);
            _activityBrowseControl.Dock = DockStyle.Fill;
            _activityBrowseControl.Margin = new Padding(0);

            panel.Controls.Add(title, 0, 0);
            panel.Controls.Add(_activityCurrentStatusLabel, 0, 1);
            panel.Controls.Add(_activityBrowseControl, 0, 2);
            return panel;
        }

        private static ListView CreateSubAgentListView()
        {
            var listView = new ListView
            {
                AccessibleName = UiLocalization.Text("SubAgentWorkbenchForm.Accessible.Subagents", "Subagents"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                MultiSelect = false,
                ShowItemToolTips = true,
                TabIndex = 0,
                UseCompatibleStateImageBehavior = false,
                View = View.Details
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(UiLocalization.Text("SubAgentWorkbenchForm.Column.Name", "Name"), 90, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("SubAgentWorkbenchForm.Column.Role", "Role"), 80, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("SubAgentWorkbenchForm.Column.Session", "Session"), 180, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("SubAgentWorkbenchForm.Column.Status", "Status"), 180, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("SubAgentWorkbenchForm.Column.Work", "Recent work"), 220, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("SubAgentWorkbenchForm.Column.Updated", "Updated"), 130, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("SubAgentWorkbenchForm.Column.Parent", "Parent thread"), 180, HorizontalAlignment.Left);
            listView.Columns[3].DisplayIndex = 1;
            return listView;
        }

        private static ActivityVirtualBrowseControl CreateActivityBrowseControl()
        {
            return new ActivityVirtualBrowseControl
            {
                AccessibleName = UiLocalization.Text("MainForm.Accessible.Activity", "Activity"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                TabIndex = 1
            };
        }

        private static Label CreateActivityCurrentStatusLabel()
        {
            return new Label
            {
                AccessibleName = UiLocalization.Text("MainForm.Accessible.CurrentActivity", "Current activity"),
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                Padding = new Padding(4, 0, 0, 0),
                TabStop = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static string BuildActivitySectionTitleText()
        {
            return UiLocalization.Text("MainForm.Label.Activity", "Activity") + "(&L)";
        }


        private static Label CreateSectionTitle(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = UiTheme.SectionTitleHeight,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
        }

        private static string BuildWindowTitle(ChatSession parentSession)
        {
            var title = parentSession == null
                ? UiLocalization.Text("MainForm.Value.UnnamedSession", "Unnamed session")
                : SessionDisplayFormatter.BuildNormalizedTitle(parentSession);
            return string.Format(
                UiLocalization.Text("SubAgentWorkbenchForm.Title.Template", "Subagents - {0}"),
                title);
        }

        private async void SubAgentListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == (Keys.Control | Keys.Delete))
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                await DeleteAllSubAgentsAsync();
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                await DeleteSelectedSubAgentAsync();
                return;
            }

            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
            await ActivateSelectedSubAgentFromListAsync();
        }

        private async Task ActivateSelectedSubAgentFromListAsync()
        {
            var activationTask = ActivateSelectedSubAgentAsync();
            _chatWorkbench.FocusMessages();
            await activationTask;
            _chatWorkbench.FocusMessages();
        }

        private void SubAgentListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _subAgentListView.HitTest(e.Location);
            if (hit.Item == null)
            {
                return;
            }

            _subAgentListView.SelectedItems.Clear();
            hit.Item.Selected = true;
            hit.Item.Focused = true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                _ = RefreshObservedSubAgentsAsync();
                return true;
            }
            if (_activityBrowseControl.ContainsFocus &&
                keyData == (Keys.Shift | Keys.Tab))
            {
                return _chatWorkbench.FocusLastInteractiveControl() || base.ProcessCmdKey(ref msg, keyData);
            }

            if (_subAgentListView.ContainsFocus)
            {
                if (keyData == Keys.Tab)
                {
                    return _chatWorkbench.FocusMessages() || base.ProcessCmdKey(ref msg, keyData);
                }

                if (keyData == (Keys.Shift | Keys.Tab))
                {
                    return FocusActivity() || base.ProcessCmdKey(ref msg, keyData);
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool FocusActivity()
        {
            return !_activityBrowseControl.IsDisposed &&
                   _activityBrowseControl.CanFocus &&
                   _activityBrowseControl.Focus();
        }

        private void SubAgentWorkbenchForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape)
            {
                return;
            }

            Close();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }
    }
}
