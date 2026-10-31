using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void SessionList_DoubleClick(object sender, EventArgs e)
        {
            _ = OpenSelectedSessionFromListAsync();
        }

        private void SessionList_MouseDown(object sender, MouseEventArgs e)
        {
            SelectListViewItemOnRightClick(_sessionList, e);
        }

        private SessionListItem GetSelectedSessionListItem()
        {
            return _sessionList.SelectedItems.Count == 0
                ? null
                : _sessionList.SelectedItems[0].Tag as SessionListItem;
        }

        private async Task ExecuteSessionDeletionAsync(ChatSession session, LocalSessionRemovalMode removalMode)
        {
            if (session == null)
            {
                return;
            }

            if (!ConfirmSessionDeletion(session, ref removalMode))
            {
                return;
            }

            try
            {
                var flowPlan = LocalSessionRemovalFlowOrchestrator.BuildSessionPlan(removalMode);
                if (removalMode != LocalSessionRemovalMode.PermanentDelete)
                    await InterruptSessionsBeforeRemovalAsync(new[] { session });
                SetStatus(LocalSessionRemovalTextPlanner.BuildSessionProgressText(removalMode, session.Title));
                var processedCount = await ExecuteLocalSessionRemovalOperationAsync(flowPlan, null, session);
                if (removalMode == LocalSessionRemovalMode.PermanentDelete && processedCount == 0) return;
                if (removalMode != LocalSessionRemovalMode.Archive) RemoveSessionLocally(session);
                FinalizeDeletionUiState();
                SetStatus(LocalSessionRemovalTextPlanner.BuildSessionCompletionStatus(removalMode, session.Title));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.SessionDeletionFailed", "Failed to delete session"));
            }
        }

        private bool ConfirmSessionDeletion(ChatSession session, ref LocalSessionRemovalMode removalMode)
        {
            if (removalMode == LocalSessionRemovalMode.PermanentDelete) return true;

            if (removalMode != LocalSessionRemovalMode.LocalOnly)
            {
                return MessageBox.Show(
                    this,
                    LocalSessionRemovalTextPlanner.BuildSessionConfirmation(session, removalMode),
                    AppConstants.AppName,
                    MessageBoxButtons.YesNo,
                    LocalSessionRemovalTextPlanner.GetConfirmationIcon(removalMode)) == DialogResult.Yes;
            }

            var confirmationText = LocalSessionRemovalTextPlanner.BuildSessionConfirmation(session, LocalSessionRemovalMode.LocalOnly);
            var defaultDeleteLocalHistory = session.Mode == AppInteractionMode.Office;
            using (var form = new LocalSessionDeleteConfirmationForm(
                confirmationText,
                LocalSessionRemovalEligibility.CanPermanentlyDeleteSession(session),
                defaultDeleteLocalHistory))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return false;
                }

                removalMode = form.DeleteLocalHistory
                    ? LocalSessionRemovalMode.PermanentDelete
                    : LocalSessionRemovalMode.LocalOnly;
                return true;
            }
        }

        private async Task InterruptSessionsBeforeRemovalAsync(IEnumerable<ChatSession> sessions)
        {
            var targets = SessionRemovalInterruptPlanner.BuildTargets(
                sessions,
                CanInterruptThread,
                GetInterruptibleTurnId);
            if (targets.Count == 0)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.StopSessionBeforeRemoval", "Stop session before removal")))
            {
                throw new InvalidOperationException(UiLocalization.Text(
                    "MainForm.Exception.AppServerDisconnectedCannotStopSessionBeforeRemoval",
                    "App Server is not connected. Unable to stop the active session before removal."));
            }

            SetStatus(targets.Count == 1
                ? UiLocalization.Text("MainForm.Status.StoppingSessionBeforeRemoval", "Stopping active session before removal...")
                : string.Format(
                    UiLocalization.Text("MainForm.Status.StoppingSessionsBeforeRemoval.Template", "Stopping {0} active session(s) before removal..."),
                    targets.Count));

            foreach (var target in targets)
            {
                await _appServerClient.InterruptAsync(target.ThreadId, target.TurnId);
            }

            AnnounceConversationStopped();
        }

        private async Task<int> ExecuteLocalSessionRemovalOperationAsync(
            LocalSessionRemovalFlowPlan flowPlan,
            List<ChatSession> sessions,
            ChatSession session)
        {
            if (flowPlan == null)
            {
                return 0;
            }

            switch (flowPlan.OperationKind)
            {
                case LocalSessionRemovalOperationKind.ArchiveSessions:
                    return await ArchiveSessionsAsync(sessions);
                case LocalSessionRemovalOperationKind.DeleteSessionFiles:
                    return await DeleteSessionFilesAsync(sessions);
                case LocalSessionRemovalOperationKind.ArchiveSession:
                    return await ArchiveSessionsAsync(SessionDescendantCollector.Collect(session, _sessions));
                case LocalSessionRemovalOperationKind.DeleteSessionFile:
                    return await DeleteSessionFilesAsync(SessionDescendantCollector.Collect(session, _sessions));
                default:
                    return flowPlan.DefaultProcessedCount;
            }
        }

        private async Task<int> ArchiveSessionsAsync(List<ChatSession> sessions)
        {
            var archiveTargets = SessionStorageOperationPlanner.SelectArchivableSessions(sessions);

            if (archiveTargets.Count == 0)
            {
                throw new InvalidOperationException(UiLocalization.Text("MainForm.Exception.NoArchivableSessionsInWorkspace", "No archivable sessions in current workspace."));
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.ArchiveSession", "Archive session")))
            {
                throw new InvalidOperationException(UiLocalization.Text("MainForm.Exception.AppServerDisconnectedCannotArchiveSession", "App Server is not connected. Unable to archive session."));
            }

            var version = _subAgentRuntimeVersion;
            Func<bool> isCurrent = () => !IsDisposed && !_isRuntimeWindowTransitionInProgress &&
                version == _subAgentRuntimeVersion && IsConnectedStatus();
            var result = await SessionArchiveService.ArchiveAsync(_appServerClient, archiveTargets,
                System.Threading.CancellationToken.None, isCurrent);
            if (!isCurrent()) throw new OperationCanceledException();
            foreach (var id in result.ArchivedThreadIds) RemoveConfirmedThreadLocally(id);
            if (result.Failures.Count > 0)
                throw new InvalidOperationException(string.Join(Environment.NewLine,
                    result.Failures.Select(pair => pair.Key + ": " + pair.Value)));
            return result.ArchivedThreadIds.Count;
        }

        private void RemoveSessionLocally(ChatSession session)
        {
            var plan = LocalSessionRemovalPlanner.BuildSessionRemoval(
                session,
                _recentWorkspaces,
                _sessions,
                _inactiveRuntimeSessions,
                _currentWorkspace,
                _currentSession);
            ApplyLocalRemovalPlan(plan);
        }

        private void ApplyLocalRemovalPlan(LocalSessionRemovalPlan plan)
        {
            if (plan == null || !plan.HasChanges)
            {
                return;
            }

            foreach (var session in plan.SessionsToClear)
            {
                RemoveSessionCache(session);
                RemoveComposeState(session, true);
            }

            _recentWorkspaces.Clear();
            _recentWorkspaces.AddRange(plan.RetainedWorkspaces);
            _sessions.Clear();
            _sessions.AddRange(plan.RetainedSessions);
            _inactiveRuntimeSessions.Clear();
            _inactiveRuntimeSessions.AddRange(plan.RetainedInactiveRuntimeSessions);
            _currentWorkspace = plan.CurrentWorkspace;
            _currentSession = plan.CurrentSession;
            UpdateCurrentModeSessionSelection(_currentSession);
        }

        private void RemoveSessionCache(ChatSession session)
        {
            if (session == null)
            {
                return;
            }

            ResetThreadVolatileState(session.ThreadId);
        }

        private void FinalizeDeletionUiState()
        {
            var plan = DeletionUiStatePlanner.Build(_currentSession, _currentWorkspace);
            if (plan.ShouldClearHistory)
            {
                ClearChatHistoryItems();
                if (plan.HasPrimaryMessage)
                {
                    AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), plan.PrimaryMessage);
                }
            }
            else
            {
                if (plan.ShouldMarkPendingReload)
                {
                    MarkPendingHistoryReload(_currentSession);
                }

                ReplaceChatHistoryItems(new[]
                {
                    ChatHistoryItemFactory.Create(ChatHistoryItemFactory.GetSystemSenderText(), plan.PrimaryMessage),
                    ChatHistoryItemFactory.Create(ChatHistoryItemFactory.GetSystemSenderText(), plan.SecondaryMessage)
                });
            }

            ApplyComposeState(_currentSession);
            ApplyMainFormRefreshPreset(MainFormRefreshPreset.WorkspaceSessionHeaderInputPersist);
        }

        private void SessionContextMenu_Popup(object sender, EventArgs e)
        {
            var isOfficeMode = _currentInteractionMode == AppInteractionMode.Office;
            var selectedItem = GetSelectedSessionListItem();
            var hasSelection = selectedItem != null;
            _sessionNewMenuItem.Enabled = IsCurrentWorkspaceAvailable();
            _sessionRenameMenuItem.Enabled = hasSelection;
            _sessionStatisticsMenuItem.Enabled = hasSelection;
            _sessionSubAgentsMenuItem.Visible = !isOfficeMode;
            _sessionSubAgentsMenuItem.Enabled = !isOfficeMode && hasSelection && !string.IsNullOrWhiteSpace(selectedItem.Session.ThreadId);
            _sessionCreateAutomationMenuItem.Enabled = _currentSession != null &&
                _currentWorkspace != null &&
                !string.IsNullOrWhiteSpace(_currentSession.ThreadId);
            _sessionAdvancedSeparatorMenuItem.Visible = !isOfficeMode;
            _sessionRemoveMenuItem.Enabled = hasSelection;
            _sessionArchiveMenuItem.Visible = !isOfficeMode;
            _sessionArchiveMenuItem.Enabled = !isOfficeMode && hasSelection && !string.IsNullOrWhiteSpace(selectedItem.Session.ThreadId);
            RefreshSessionWorkbenchContextMenuState(selectedItem);
        }

        private void CreateAutomationFromCurrentSession()
        {
            if (_currentSession == null || _currentWorkspace == null || string.IsNullOrWhiteSpace(_currentSession.ThreadId))
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Automation.Status.SessionUnavailable", "Current session cannot create an automation."));
                return;
            }

            var context = AutomationCreationContextBuilder.ForThread(
                _currentSession.ThreadId,
                _currentSession.Mode,
                SessionRuntimeIdentityResolver.ResolveRuntimeProfileId(
                    _currentSession,
                    GetConfiguredRuntimeProfileId()),
                _currentWorkspace.Id,
                _currentWorkspace.RootPath,
                GetEffectiveModelId(_currentSession),
                GetEffectiveReasoningEffort(_currentSession),
                _currentSession.ApprovalMode ?? GetConfiguredDefaultApprovalMode(),
                _currentSession.OfficeRuntimeScopeId);
            context.SessionId = _currentSession.Id;
            ShowAutomationsForm(context);
        }

        private async void OnSessionRemoveMenuItemClick(object sender, EventArgs e)
        {
            await DeleteSelectedSessionAsync();
        }

        private void OnSessionStatisticsMenuItemClick(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedSessionListItem();
            if (selectedItem == null || selectedItem.Session == null)
            {
                return;
            }

            RestoreAutomationSessionModelSettings(new[] { selectedItem.Session });
            using (var form = new SessionStatisticsForm(
                selectedItem.Session,
                new SessionStatisticsDisplayContext
                {
                    EffectiveModelId = GetEffectiveModelId(selectedItem.Session),
                    EffectiveReasoningEffort = GetEffectiveReasoningEffort(selectedItem.Session)
                }))
            {
                form.ShowDialog(this);
            }
        }

        private void OnSessionSubAgentsMenuItemClick(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedSessionListItem();
            if (selectedItem == null || string.IsNullOrWhiteSpace(selectedItem.Session.ThreadId))
            {
                return;
            }

            OpenSubAgentWorkbench(selectedItem.Session, GetSubAgentsForParent(selectedItem.Session));
        }

        private async void OnSessionArchiveMenuItemClick(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedSessionListItem();
            if (selectedItem == null)
            {
                return;
            }

            await ExecuteSessionDeletionAsync(selectedItem.Session, LocalSessionRemovalMode.Archive);
        }

        private void FocusSessionList()
        {
            if (_sessionList.Items.Count > 0 && _sessionList.SelectedItems.Count == 0)
            {
                SelectSessionListItemAt(0);
            }

            ActiveControl = _sessionList;
            _sessionList.Focus();
        }

        private int GetWorkspaceSessionCount(string workspaceId)
        {
            return SessionCollectionQueries.CountSessionsInWorkspace(_sessions, workspaceId, _currentInteractionMode);
        }

        private void SortSessions()
        {
            _sessions.Sort((left, right) => right.LastActiveAt.CompareTo(left.LastActiveAt));
        }

        private static ListViewItem CreateSessionPlaceholderItem(string text)
        {
            return new ListViewItem(text ?? string.Empty);
        }

        private void SelectSessionListItemAt(int index)
        {
            if (index < 0 || index >= _sessionList.Items.Count)
            {
                return;
            }

            _sessionList.SelectedItems.Clear();
            _sessionList.Items[index].Selected = true;
            _sessionList.Items[index].Focused = true;
        }

        private void RefreshSessionList()
        {
            CloseSubAgentWorkbenchesForMissingParents();
            _isRefreshingSessionList = true;
            var entries = _currentWorkspace == null
                ? new List<SessionListEntry>()
                : SessionListProjection.BuildWorkspaceEntries(_sessions, _currentWorkspace.Id, _currentInteractionMode);
            var items = entries.Select(entry => new SessionListItem(entry,
                IsCurrentSessionListEntry(entry, _currentSession),
                SessionListProjection.IsWorking(entry, IsThreadBusy),
                _appConfigSnapshot?.SessionListDisplayOrder).CreateListViewItem()).ToArray();
            if (items.Length == 0)
                items = new[] { CreateSessionPlaceholderItem(UiLocalization.Text("MainForm.SessionList.Placeholder.NoSessions", "No sessions")) };
            SessionListItem.RefreshList(_sessionList, items,
                Array.FindIndex(items, item => ShouldSelectSessionListItem(item.Tag as SessionListItem, _currentSession)));
            RefreshOpenSubAgentWorkbenchLists();
            RefreshSessionWorkbenches();
            _isRefreshingSessionList = false;
        }

        private void RefreshOpenSubAgentWorkbenchLists()
        {
            if (_subAgentWorkbenches.Count == 0)
            {
                return;
            }

            foreach (var pair in _subAgentWorkbenches.ToArray())
            {
                var parentSession = _sessions.FirstOrDefault(session =>
                    session != null &&
                    string.Equals(session.Id, pair.Key, StringComparison.OrdinalIgnoreCase));
                if (parentSession == null || pair.Value == null || pair.Value.IsDisposed)
                {
                    continue;
                }

                var subAgents = GetSubAgentsForParent(parentSession);
                pair.Value.RefreshSubAgents(subAgents);
            }
        }

        private void SelectSessionInList(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            for (var i = 0; i < _sessionList.Items.Count; i++)
            {
                var item = _sessionList.Items[i].Tag as SessionListItem;
                if (item != null && string.Equals(item.Session.Id, sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    SelectSessionListItemAt(i);
                    return;
                }
            }
        }

        private static bool ShouldSelectSessionListItem(SessionListItem item, ChatSession currentSession)
        {
            if (item == null || item.Session == null || currentSession == null)
            {
                return false;
            }

            if (string.Equals(item.Session.Id, currentSession.Id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return SessionListProjection.IsSubAgent(currentSession) &&
                   !string.IsNullOrWhiteSpace(currentSession.ParentThreadId) &&
                   string.Equals(item.Session.ThreadId, currentSession.ParentThreadId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCurrentSessionListEntry(SessionListEntry entry, ChatSession currentSession)
        {
            if (entry == null || entry.Session == null || currentSession == null)
            {
                return false;
            }

            if (string.Equals(entry.Session.Id, currentSession.Id, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return SessionListProjection.IsSubAgent(currentSession) &&
                   !string.IsNullOrWhiteSpace(currentSession.ParentThreadId) &&
                   string.Equals(entry.Session.ThreadId, currentSession.ParentThreadId, StringComparison.OrdinalIgnoreCase);
        }

        private async Task CreateNewSessionAsync(string title = null)
        {
            var sessionWorkspace = ResolveSessionCreationWorkspace();
            if (sessionWorkspace == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.OpenWorkspaceBeforeCreateSession", "Please open a workspace first."),
                    UiLocalization.Text("MainForm.DialogTitle.CannotCreateSession", "Unable to create session"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            SetStatus(UiLocalization.Text("MainForm.Status.CreatingSession", "Creating session..."));
            SaveCurrentComposeState();

            if (!IsWorkspaceAvailable(sessionWorkspace))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.WorkspacePathUnavailableCannotCreateSession", "Current workspace path is unavailable, unable to create a new session."),
                    UiLocalization.Text("MainForm.DialogTitle.CannotCreateSession", "Unable to create session"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.CreateSession", "Create session")))
            {
                return;
            }

            var sessionIndex = GetWorkspaceSessionCount(sessionWorkspace.Id) + 1;
            var requestedTitle = string.IsNullOrWhiteSpace(title)
                ? _sessionService.BuildDefaultSessionTitle(sessionWorkspace, sessionIndex)
                : title;
            var uniqueTitle = BuildUniqueSessionTitle(requestedTitle, false, false);
            var session = _sessionService.CreateSession(
                sessionWorkspace,
                sessionIndex,
                uniqueTitle);
            var defaults = ResolveSessionDefaults();
            session.LastActiveAt = DateTime.Now;
            EnsureActiveOfficeSessionScope(session);

            _sessions.Insert(0, session);
            _currentSession = session;
            UpdateCurrentModeSessionSelection(session);
            ApplyComposeState(session);

            SortSessions();
            ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderInputPersist);
            ReplaceChatHistoryItems(BuildSessionHeaderItems(session, true));
            if (defaults.UsesFirstObservedModelFallback)
            {
                var fallbackMessage = string.Format(
                    UiLocalization.Text(
                        "MainForm.Message.FirstObservedModelFallback.Template",
                        "No App Server default model was reported. This new session will use the first observed model: {0}."),
                    defaults.ModelId);
                AddChatHistoryItem(GetStatusSenderText(), fallbackMessage, ChatHistoryCategory.System);
                SpeakAuxiliaryTextWithScreenReader(fallbackMessage);
            }
            SetLowPriorityStatus(string.Format(UiLocalization.Text("MainForm.Status.SessionCreated.Template", "Session created: {0}"), SessionDisplayFormatter.BuildDisplayTitle(session)));
        }

        private async void OnNewSessionButtonClick(object sender, EventArgs e)
        {
            var sessionWorkspace = ResolveSessionCreationWorkspace();
            if (sessionWorkspace == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.OpenWorkspaceBeforeCreateSession", "Please open a workspace first."),
                    UiLocalization.Text("MainForm.DialogTitle.CannotCreateSession", "Unable to create session"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!IsWorkspaceAvailable(sessionWorkspace))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.WorkspacePathUnavailableCannotCreateSession", "Current workspace path is unavailable, unable to create a new session."),
                    UiLocalization.Text("MainForm.DialogTitle.CannotCreateSession", "Unable to create session"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var sessionIndex = GetWorkspaceSessionCount(sessionWorkspace.Id) + 1;
            var defaultTitle = BuildUniqueSessionTitle(
                _sessionService.BuildDefaultSessionTitle(sessionWorkspace, sessionIndex),
                false,
                false);
            using (var form = new SessionTitleForm(
                UiLocalization.Text("MainForm.SessionTitle.CreatePainting.Title", "Create New Session"),
                UiLocalization.Text("MainForm.SessionTitle.CreatePainting.Header", "Set title for new session"),
                UiLocalization.Text("MainForm.SessionTitle.CreatePainting.Description", "You can keep the default title and edit it later."),
                defaultTitle,
                UiLocalization.Text("MainForm.SessionTitle.CreatePainting.ConfirmButton", "Create(&C)"),
                UiLocalization.Text("MainForm.SessionTitle.CreatePainting.ConfirmAccessible", "Create"),
                UiLocalization.Text("MainForm.SessionTitle.Validation.EnterPaintingTitle", "Please enter a session title.")))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                await CreateNewSessionAsync(form.SessionTitle);
            }
        }

        private async Task OpenSelectedSessionFromListAsync()
        {
            var selectedItem = GetSelectedSessionListItem();
            if (selectedItem == null)
            {
                return;
            }

            if (_currentSession != null &&
                string.Equals(_currentSession.Id, selectedItem.Session.Id, StringComparison.OrdinalIgnoreCase))
            {
                if (HasPendingHistoryReload(_currentSession) || _allChatHistoryItems.Count == 0)
                {
                    await LoadSessionHistoryAsync(_currentSession, true);
                }

                return;
            }

            await ActivateSessionAsync(selectedItem.Session, true);
        }

        private void RestoreCurrentSession()
        {
            _currentSession = SessionSelectionResolver.ResolveCurrentSession(_currentWorkspace, _currentSession, _sessions, _currentInteractionMode);
            UpdateCurrentModeSessionSelection(_currentSession);
        }

        private void ResetOfficeSessionForNewTask()
        {
            _currentSession = null;
            _currentOfficeSessionId = null;
            UpdateCurrentModeSessionSelection(null);
        }

        private void RestoreOfficeSelectionAfterStateLoad()
        {
            var savedSession = FindSessionById(_currentOfficeSessionId);
            var selection = SessionSelectionResolver.RestoreOfficeSelection(
                _recentWorkspaces,
                _sessions,
                _currentOfficeWorkspaceId,
                savedSession != null ? savedSession.Id : null);
            _currentWorkspace = selection.Workspace;
            _currentSession = selection.Session;
            if (_currentSession != null)
            {
                EnsureActiveOfficeSessionScope(_currentSession);
                UpdateCurrentModeSessionSelection(_currentSession);
                _currentOfficeWorkspaceId = _currentWorkspace.Id;
                return;
            }

            ResetOfficeSessionForNewTask();
        }

        private void EnsureCurrentSessionMatchesInteractionMode()
        {
            if (_currentSession == null)
            {
                return;
            }

            if (SessionModeConsistencyResolver.Resolve(_currentSession, _currentInteractionMode) != null)
            {
                return;
            }

            _currentSession = null;
            RestoreCurrentSession();
        }

        private WorkspaceInfo ResolveSessionCreationWorkspace()
        {
            var workspace = SessionCreationWorkspaceResolver.Resolve(
                _currentInteractionMode,
                _currentWorkspace,
                EnsureOfficeWorkspace);
            if (workspace != null && !ReferenceEquals(_currentWorkspace, workspace))
            {
                _currentWorkspace = workspace;
            }

            return workspace;
        }

        private void UpdateCurrentModeSessionSelection(ChatSession session)
        {
            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                _currentOfficeSessionId = session != null && session.Mode == AppInteractionMode.Office
                    ? session.Id
                    : null;
                return;
            }

            _currentCodingSessionId = session != null && session.Mode == AppInteractionMode.Coding
                ? session.Id
                : null;
        }
    }
}
