using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async Task ActivateSessionAsync(ChatSession session, bool includeSystemHeader)
        {
            var plan = SessionActivationWorkflowPlanner.BuildActivation(session, includeSystemHeader);
            if (!plan.ShouldActivate)
            {
                return;
            }

            SaveCurrentComposeState();
            _currentSession = plan.Session;
            UpdateCurrentModeSessionSelection(session);
            plan.ApplyActivityTimestamp(DateTime.Now);
            ApplyComposeState(_currentSession);
            SortSessions();
            PersistState();
            RefreshHeader();
            RefreshSessionList();
            RefreshInputState();
            await LoadActivatedSessionHistoryAsync(_currentSession, plan.IncludeSystemHeader);
        }

        private async Task LoadSessionHistoryAsync(ChatSession session, bool includeSystemHeader)
        {
            var request = _sessionHistoryLoadWorkflow.BuildRequest(session, includeSystemHeader);
            ApplySessionHistoryLoadResult(request.ImmediateResult);
            if (!request.RequiresRemoteRead)
            {
                return;
            }

            var result = await _sessionHistoryLoadWorkflow.CompleteRemoteLoadAsync(
                request,
                IsCurrentSessionHistoryLoadTarget,
                TryConvertMissingThreadSessionToDraft,
                async () => { await WaitForTargetExitDecisionAsync(); });
            ApplySessionHistoryLoadResult(result);
        }

        private async Task LoadActivatedSessionHistoryAsync(ChatSession session, bool includeSystemHeader)
        {
            var request = _sessionHistoryLoadWorkflow.BuildRequest(session, includeSystemHeader);
            ApplySessionHistoryLoadResult(request.ImmediateResult);

            var goalRefreshTask = RefreshSessionGoalAsync(session, false);
            if (!request.RequiresRemoteRead)
            {
                await goalRefreshTask;
                if (IsCurrentSessionHistoryLoadTarget(session, session != null ? session.ThreadId : null))
                {
                    ApplySessionHistoryLoadResult(_sessionHistoryLoadWorkflow.BuildRequest(session, includeSystemHeader).ImmediateResult);
                }

                return;
            }

            var result = await _sessionHistoryLoadWorkflow.CompleteRemoteLoadAsync(
                request,
                IsCurrentSessionHistoryLoadTarget,
                TryConvertMissingThreadSessionToDraft,
                async () =>
                {
                    await goalRefreshTask;
                    await WaitForTargetExitDecisionAsync();
                });
            ApplySessionHistoryLoadResult(result);
        }

        private void ApplySessionHistoryLoadResult(SessionHistoryLoadResult result)
        {
            if (IsTargetStopping || result == null || result.Kind == SessionHistoryLoadResultKind.Stale)
            {
                return;
            }

            if (result.ShouldReplaceHistory)
            {
                var preserveLocalConversationItems = IsDisplayingCurrentSessionHistory();
                if (!(result.Kind == SessionHistoryLoadResultKind.LoadingRemoteHistory &&
                      preserveLocalConversationItems))
                {
                    var historyItems = result.Kind == SessionHistoryLoadResultKind.LoadedRemoteHistory &&
                        preserveLocalConversationItems
                        ? SessionHistoryOptimisticMerge.PreserveLocalConversationItems(
                            _allChatHistoryItems.ToArray(),
                            result.HistoryItems)
                        : result.HistoryItems;
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    ReplaceChatHistoryItems(historyItems);
                    if (result.Kind == SessionHistoryLoadResultKind.LoadedRemoteHistory ||
                        result.Kind == SessionHistoryLoadResultKind.CachedHistory)
                    {
                        SessionHistoryLoadWorkflow.TraceLoad("display", _currentSession != null ? _currentSession.ThreadId : null,
                            "Kind={0}; Items={1}; VisibleItems={2}; ElapsedMs={3}",
                            result.Kind, historyItems != null ? historyItems.Length : 0, _chatHistoryListBox.Items.Count, watch.ElapsedMilliseconds);
                    }
                }
            }

            if (!result.HasStatus)
            {
                return;
            }

            if (result.StatusPriority == SessionHistoryLoadStatusPriority.High)
            {
                SetHighPriorityStatus(result.StatusText);
                return;
            }

            SetStatus(result.StatusText);
        }

        private bool IsCurrentSessionHistoryLoadTarget(ChatSession session, string threadId)
        {
            return !IsTargetStopping && _currentSession != null &&
                   session != null &&
                   string.Equals(_currentSession.Id, session.Id, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(_currentSession.ThreadId, threadId, StringComparison.OrdinalIgnoreCase);
        }

        private void ChatHistoryListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (HasPendingHistoryReload(_currentSession))
                {
                    _ = ReloadCurrentSessionHistoryAsync();
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    return;
                }

                OpenCurrentChatMessageViewer();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Apps && GetPrimaryChatHistoryItem() != null)
            {
                ShowPopupMenuForListBoxSelection(_chatHistoryContextMenu, _chatHistoryListBox);
                e.Handled = true;
            }
        }

        private void ChatHistoryListBox_MouseDown(object sender, MouseEventArgs e)
        {
            SelectListItemOnRightClick(_chatHistoryListBox, e);
        }

        private void ChatHistoryListBox_AnchorNavigationRequested(object sender, ChatHistoryAnchorNavigationEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (!TryMoveChatHistorySelectionByTurn(e.Direction))
            {
                SetStatus(e.Direction < 0
                    ? UiLocalization.Text("MainForm.Status.AlreadyEarliestTurn", "Already at the earliest turn")
                    : UiLocalization.Text("MainForm.Status.AlreadyLatestTurn", "Already at the latest turn"));
            }

            e.Handled = true;
        }

        private void ChatHistoryListBox_Enter(object sender, EventArgs e)
        {
            if (_chatHistoryListBox.Items.Count > 0 && GetPrimaryChatHistoryItem() == null)
            {
                _chatHistoryListBox.SelectSingleIndex(0);
            }
        }

        private void ChatHistoryListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Message-list selection and Alt+Arrow manual reading keep independent cursors.
        }

        private void FocusChatHistoryList()
        {
            if (_chatHistoryListBox.Items.Count > 0 && GetPrimaryChatHistoryItem() == null)
            {
                _chatHistoryListBox.SelectSingleIndex(0);
            }

            ActiveControl = _chatHistoryListBox;
            _chatHistoryListBox.Focus();
        }

        private void FocusInputBox()
        {
            if (!_inputBox.Enabled)
            {
                SetStatus(UiLocalization.Text("MainForm.Status.InputUnavailable", "Input is currently unavailable"));
                return;
            }

            ActiveControl = _inputBox;
            _inputBox.Focus();
        }

        private void FocusLogTextBox()
        {
            ActiveControl = _activityBrowseControl;
            _activityBrowseControl.Focus();
        }

        private void ChatHistoryContextMenu_Popup(object sender, EventArgs e)
        {
            UpdateChatHistorySpeechMenuState();
            UpdateChatHistoryPlanModeMenuState();
            UpdateChatHistoryGoalMenuState();
            var basicState = ChatHistoryContextMenuPlanner.BuildBasicState(
                GetPrimaryChatHistoryItem() != null,
                _chatHistoryListBox.Items.Count);
            _copySelectedMenuItem.Enabled = basicState.CopySelectedEnabled;
            _findChatMessageContextMenuItem.Enabled = basicState.FindEnabled;
            UpdateChatHistoryForkMenuState();
            UpdateChatHistoryCompactionMenuState();
            UpdateChatHistoryReviewMenuState();
        }

        private void UpdateChatHistoryPlanModeMenuState()
        {
            var state = ChatHistoryContextMenuPlanner.BuildPlanModeState(
                CanToggleSessionPlanMode(_currentSession),
                _currentSession != null && _currentSession.IsPlanModeEnabled);
            _planModeMenuItem.Enabled = state.Enabled;
            _planModeMenuItem.Checked = state.Checked;
        }

        private void UpdateChatHistoryGoalMenuState()
        {
            var state = ChatHistoryContextMenuPlanner.BuildGoalState(
                CanManageSessionGoal(_currentSession),
                _currentSession != null && !string.IsNullOrWhiteSpace(_currentSession.ThreadId));
            _goalSetMenuItem.Enabled = state.SetEnabled;
            _goalViewMenuItem.Enabled = state.ViewEnabled;
            _goalClearMenuItem.Enabled = state.ClearEnabled;
            _goalMenuItem.Enabled = state.MenuEnabled;
        }

        private void UpdateChatHistoryForkMenuState()
        {
            _forkFullMenuItem.Enabled = _currentSession != null && !string.IsNullOrWhiteSpace(_currentSession.ThreadId);

            var selectedItem = GetPrimaryChatHistoryItem();
            int trimTurnsAfterAnchor;
            if (TryGetForkTrimTurnsFromHistoryItem(selectedItem, out trimTurnsAfterAnchor))
            {
                _forkFromHereMenuItem.Enabled = true;
                _forkFromHereMenuItem.Text = trimTurnsAfterAnchor <= 0
                    ? UiLocalization.Text("MainForm.Menu.ForkFromHere.SingleTurn", "Fork from this turn (keep through this turn)...")
                    : string.Format(UiLocalization.Text("MainForm.Menu.ForkFromHere.MultiTurn.Template", "Fork from this turn (trim following {0} turn(s))..."), trimTurnsAfterAnchor);
            }
            else
            {
                _forkFromHereMenuItem.Enabled = false;
                _forkFromHereMenuItem.Text = UiLocalization.Text("MainForm.Menu.ForkFromHere.Default", "Fork from this turn...");
            }

            _forkMenuItem.Enabled = _forkFullMenuItem.Enabled || _forkFromHereMenuItem.Enabled;
        }

        private void UpdateChatHistoryCompactionMenuState()
        {
            _compactChatHistoryMenuItem.Enabled = CanCompactSession(_currentSession);
        }

        private void UpdateChatHistoryReviewMenuState()
        {
            _reviewChatHistoryMenuItem.Enabled = CanReviewSession(_currentSession);
        }

        private void CopySelectedChatItemToClipboard()
        {
            _chatHistoryListBox.CopySelectionToClipboard();
        }

        private void ChatHistoryListBox_SelectionSummaryRequested(object sender, ChatHistorySelectionSummaryEventArgs e)
        {
            if (e != null && !string.IsNullOrWhiteSpace(e.SummaryText))
            {
                SetStatus(e.SummaryText);
            }
        }

        private ChatHistoryItem GetPrimaryChatHistoryItem()
        {
            return _chatHistoryListBox.PrimaryChatHistoryItem;
        }

        private void ScheduleRealtimeCurrentSessionHistoryReload(AppServerEventArgs e)
        {
            var currentThreadId = _currentSession != null ? _currentSession.ThreadId : null;
            if (!SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(e, currentThreadId, IsLocalDesktopRealtimeTurn(e.ThreadId)))
            {
                return;
            }

            var threadId = e.ThreadId;
            int version;
            _realtimeSessionHistoryReloadVersions.TryGetValue(threadId, out version);
            version++;
            _realtimeSessionHistoryReloadVersions[threadId] = version;
            _ = ReloadCurrentSessionHistoryAfterRealtimeEventAsync(threadId, version);
        }

        private async Task ReloadCurrentSessionHistoryAfterRealtimeEventAsync(string threadId, int version)
        {
            await Task.Delay(RealtimeSessionHistoryReloadDelayMilliseconds).ConfigureAwait(true);
            if (IsDisposed || !IsHandleCreated || !IsCurrentSessionThread(threadId))
            {
                return;
            }

            int currentVersion;
            if (!_realtimeSessionHistoryReloadVersions.TryGetValue(threadId, out currentVersion) ||
                currentVersion != version)
            {
                return;
            }

            await ReloadCurrentSessionHistorySilentlyAsync(threadId).ConfigureAwait(true);

            if (_realtimeSessionHistoryReloadVersions.TryGetValue(threadId, out currentVersion) &&
                currentVersion == version)
            {
                _realtimeSessionHistoryReloadVersions.Remove(threadId);
            }
        }

        private async Task ReloadCurrentSessionHistorySilentlyAsync(string threadId)
        {
            if (!IsCurrentSessionThread(threadId))
            {
                return;
            }

            var session = _currentSession;
            var request = _sessionHistoryLoadWorkflow.BuildRequest(session, true);
            if (!request.RequiresRemoteRead)
            {
                return;
            }

            var result = await _sessionHistoryLoadWorkflow.CompleteRemoteLoadAsync(
                request,
                IsCurrentSessionHistoryLoadTarget,
                TryConvertMissingThreadSessionToDraft).ConfigureAwait(true);
            if (result == null ||
                result.Kind != SessionHistoryLoadResultKind.LoadedRemoteHistory ||
                !result.ShouldReplaceHistory ||
                !IsCurrentSessionHistoryLoadTarget(session, threadId))
            {
                return;
            }

            var mergedHistoryItems = SessionHistoryOptimisticMerge.PreserveLocalConversationItems(
                _allChatHistoryItems.ToArray(),
                result.HistoryItems);
            ReplaceChatHistoryItems(
                mergedHistoryItems,
                ShouldPreserveChatHistorySelectionForSilentReload());
        }

        private async Task ReloadCurrentSessionHistoryAsync()
        {
            if (_currentSession == null)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.ReloadSessionHistory", "Reload session history")))
            {
                return;
            }

            await LoadSessionHistoryAsync(_currentSession, true);
        }

        private void ReplaceChatHistoryItems(ChatHistoryItem[] items)
        {
            ReplaceChatHistoryItems(items, true);
        }

        private void ReplaceChatHistoryItems(ChatHistoryItem[] items, bool preserveSelection)
        {
            var selectionSnapshot = ChatHistorySelectionSnapshot.Capture(
                GetPrimaryChatHistoryItem(),
                _chatHistoryListBox.FocusedIndex,
                preserveSelection);
            _allChatHistoryItems.Clear();
            _visibleVoiceTranscriptIds.Clear();
            if (items != null && items.Length > 0)
            {
                foreach (var item in items)
                {
                    _allChatHistoryItems.Add(item);
                    if (item?.IsVoiceTranscript == true && item.SourceItemId != null) _visibleVoiceTranscriptIds.Add(item.SourceItemId);
                }
            }

            ApplyChatHistoryFilters(selectionSnapshot);
        }

        private void ApplyChatHistoryFilters(bool preserveSelection)
        {
            ApplyChatHistoryFilters(
                preserveSelection
                    ? ChatHistorySelectionSnapshot.Capture(
                        GetPrimaryChatHistoryItem(),
                        _chatHistoryListBox.FocusedIndex)
                    : null);
        }

        private void ApplyChatHistoryFilters(ChatHistorySelectionSnapshot selectionSnapshot)
        {
            var visibleItems = new System.Collections.Generic.List<ChatHistoryItem>();
            var shouldPreserveViewport = selectionSnapshot != null;
            var topIndex = _chatHistoryListBox.Items.Count > 0 ? _chatHistoryListBox.TopIndex : -1;

            foreach (var item in _allChatHistoryItems)
            {
                if (ShouldDisplayChatHistoryItem(item)) visibleItems.Add(item);
            }
            var replacementItems = visibleItems.ToArray();
            var selectedIndex = selectionSnapshot != null
                ? selectionSnapshot.ResolveSelectedIndex(replacementItems) : -1;
            var visibleCount = Math.Max(1, _chatHistoryListBox.ClientSize.Height / Math.Max(1, _chatHistoryListBox.ItemHeight));
            if (!ChatHistoryVisibleItemsUpdater.HasChanges(_chatHistoryListBox.Items, replacementItems) &&
                (replacementItems.Length == 0 ||
                 selectedIndex >= 0 && selectedIndex == _chatHistoryListBox.FocusedIndex ||
                 selectedIndex < 0 && replacementItems.Length <= topIndex + visibleCount)) return;

            _chatHistoryListBox.BeginUpdate();
            try
            {
                ChatHistoryVisibleItemsUpdater.Apply(_chatHistoryListBox.Items, replacementItems);

                if (_chatHistoryListBox.Items.Count <= 0)
                {
                    return;
                }

                if (selectedIndex >= 0)
                {
                    _chatHistoryListBox.SelectSingleIndex(selectedIndex);

                    if (shouldPreserveViewport)
                    {
                        RestoreChatHistoryTopIndex(topIndex);
                    }
                    else
                    {
                        EnsureChatHistoryItemVisible(selectedIndex);
                    }

                    return;
                }

                _chatHistoryListBox.TopIndex = _chatHistoryListBox.Items.Count - 1;
            }
            finally
            {
                _chatHistoryListBox.EndUpdate();
            }
        }

        private bool ShouldDisplayChatHistoryItem(ChatHistoryItem item)
        {
            if (item == null)
            {
                return false;
            }

            switch (item.Category)
            {
                case ChatHistoryCategory.ReasoningSummary:
                case ChatHistoryCategory.Processing:
                case ChatHistoryCategory.Command:
                case ChatHistoryCategory.FileChange:
                case ChatHistoryCategory.Approval:
                case ChatHistoryCategory.Account:
                case ChatHistoryCategory.ProtocolNotice:
                case ChatHistoryCategory.ProtocolWarning:
                    return false;
                default:
                    return true;
            }
        }
    }
}
