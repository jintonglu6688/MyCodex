using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Office;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ModeHistoryViewState
    {
        private readonly string _runtimeProfileId;
        private readonly string _workspaceId;
        private readonly string _sessionId;
        private readonly ChatHistorySelectionSnapshot _selectionSnapshot;
        private readonly ChatHistorySelectionSnapshot _topItemSnapshot;

        internal ModeHistoryViewState(
            string runtimeProfileId,
            string workspaceId,
            string sessionId,
            ChatHistoryItem[] historyItems,
            ChatHistorySelectionSnapshot selectionSnapshot,
            ChatHistorySelectionSnapshot topItemSnapshot)
        {
            _runtimeProfileId = runtimeProfileId ?? string.Empty;
            _workspaceId = workspaceId ?? string.Empty;
            _sessionId = sessionId ?? string.Empty;
            HistoryItems = historyItems ?? Array.Empty<ChatHistoryItem>();
            _selectionSnapshot = selectionSnapshot;
            _topItemSnapshot = topItemSnapshot;
        }

        internal ChatHistoryItem[] HistoryItems { get; }

        internal bool Matches(string runtimeProfileId, string workspaceId, string sessionId)
        {
            return string.Equals(_runtimeProfileId, runtimeProfileId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(_workspaceId, workspaceId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(_sessionId, sessionId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        internal int ResolveSelectedIndex(ChatHistoryItem[] visibleItems)
        {
            return _selectionSnapshot != null
                ? _selectionSnapshot.ResolveSelectedIndex(visibleItems)
                : -1;
        }

        internal int ResolveTopIndex(ChatHistoryItem[] visibleItems)
        {
            return _topItemSnapshot != null
                ? _topItemSnapshot.ResolveSelectedIndex(visibleItems)
                : -1;
        }
    }

    public partial class MainForm
    {
        private ModeHistoryViewState _codingModeHistoryViewState;
        private ModeHistoryViewState _officeModeHistoryViewState;

        private void InitializeModeTabsAfterStateLoad()
        {
            if (_modeTabControl == null)
            {
                return;
            }

            if (_appStateSnapshot == null && _appConfigSnapshot != null)
            {
                _currentInteractionMode = _activeModeSettings.DefaultStartupMode;
            }

            _modeTabControl.SelectedIndexChanged -= ModeTabControl_SelectedIndexChanged;
            _modeTabControl.SelectedTab = GetModeTabPage(_currentInteractionMode);
            MoveSharedWorkbenchToSelectedModeTab();
            ApplyModeVisibility();
            _modeTabControl.SelectedIndexChanged += ModeTabControl_SelectedIndexChanged;
        }

        private bool SelectInteractionModeTab(AppInteractionMode nextMode)
        {
            if (!Enabled || _modeTabControl == null)
            {
                return false;
            }

            var targetTab = GetModeTabPage(nextMode);
            if (!ReferenceEquals(_modeTabControl.SelectedTab, targetTab))
            {
                _modeTabControl.SelectedTab = targetTab;
            }

            return _currentInteractionMode == nextMode;
        }

        private TabPage GetModeTabPage(AppInteractionMode mode)
        {
            return mode == AppInteractionMode.Office ? _officeTabPage : _codingTabPage;
        }

        private void ModeTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!Enabled)
            {
                return;
            }

            ActivateSelectedModeTab();
        }

        private void ActivateSelectedModeTab()
        {
            var nextMode = ReferenceEquals(_modeTabControl.SelectedTab, _officeTabPage)
                ? AppInteractionMode.Office
                : AppInteractionMode.Coding;
            if (_currentInteractionMode == nextMode)
            {
                MoveSharedWorkbenchToSelectedModeTab();
                return;
            }

            CaptureCurrentModeHistoryViewState();
            SaveCurrentComposeState();
            CaptureCurrentModeSelection();
            _currentInteractionMode = nextMode;
            MoveSharedWorkbenchToSelectedModeTab();
            RestoreSelectionForCurrentMode();
            ApplyModeVisibility();

            if (!RestoreCurrentModeHistoryViewState())
            {
                if (_currentSession == null)
                {
                    ClearChatHistoryItems();
                    if (_currentWorkspace != null)
                    {
                        AddChatHistoryItem(
                            ChatHistoryItemFactory.GetSystemSenderText(),
                            GetNoSessionMessageForCurrentWorkspace());
                    }
                }
                else
                {
                    MarkPendingHistoryReload(_currentSession);
                    ReplaceChatHistoryItems(BuildSessionHeaderItems(_currentSession, false));
                }
            }

            ApplyComposeState(_currentSession);
            RefreshModeProjectionFromCache();
            SetLowPriorityStatus(nextMode == AppInteractionMode.Office
                ? UiLocalization.Text("MainForm.Status.OfficeModeActive", "Office Mode active")
                : UiLocalization.Text("MainForm.Status.CodingModeActive", "Coding Mode active"));
            _modeTabControl.Focus();
        }

        private void CaptureCurrentModeHistoryViewState()
        {
            var topIndex = _chatHistoryListBox.Items.Count > 0 ? _chatHistoryListBox.TopIndex : -1;
            var topItem = topIndex >= 0 && topIndex < _chatHistoryListBox.Items.Count
                ? _chatHistoryListBox.Items[topIndex] as ChatHistoryItem
                : null;
            var state = new ModeHistoryViewState(
                GetConfiguredRuntimeProfileId(),
                _currentWorkspace != null ? _currentWorkspace.Id : null,
                _currentSession != null ? _currentSession.Id : null,
                _allChatHistoryItems.ToArray(),
                ChatHistorySelectionSnapshot.Capture(
                    GetPrimaryChatHistoryItem(),
                    _chatHistoryListBox.FocusedIndex),
                ChatHistorySelectionSnapshot.Capture(topItem, topIndex));

            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                _officeModeHistoryViewState = state;
            }
            else
            {
                _codingModeHistoryViewState = state;
            }
        }

        private bool RestoreCurrentModeHistoryViewState()
        {
            var state = GetCurrentModeHistoryViewState();
            if (!MatchesCurrentModeHistoryViewState(state))
            {
                return false;
            }

            ReplaceChatHistoryItems(state.HistoryItems, false);
            var visibleItems = _chatHistoryListBox.Items.OfType<ChatHistoryItem>().ToArray();
            var selectedIndex = state.ResolveSelectedIndex(visibleItems);
            if (selectedIndex >= 0)
            {
                _chatHistoryListBox.SelectSingleIndex(selectedIndex);
            }

            RestoreChatHistoryTopIndex(state.ResolveTopIndex(visibleItems));
            return true;
        }

        private ModeHistoryViewState GetCurrentModeHistoryViewState()
        {
            return _currentInteractionMode == AppInteractionMode.Office
                ? _officeModeHistoryViewState
                : _codingModeHistoryViewState;
        }

        private bool MatchesCurrentModeHistoryViewState(ModeHistoryViewState state)
        {
            return state != null &&
                (_currentSession == null || state.HistoryItems.Length > 0) &&
                state.Matches(
                GetConfiguredRuntimeProfileId(),
                _currentWorkspace != null ? _currentWorkspace.Id : null,
                _currentSession != null ? _currentSession.Id : null);
        }

        private void ClearModeHistoryViewStates()
        {
            _codingModeHistoryViewState = null;
            _officeModeHistoryViewState = null;
        }

        private void RefreshModeProjectionFromCache()
        {
            RefreshWorkspaceView();
            RefreshSessionList();
            RefreshHeader();
            RefreshInputStateFromCache();
        }

        private void MoveSharedWorkbenchToSelectedModeTab()
        {
            var targetTab = GetModeTabPage(_currentInteractionMode);
            if (!targetTab.Controls.Contains(_mainSplitContainer))
            {
                targetTab.Controls.Add(_mainSplitContainer);
            }
        }

        private void CaptureCurrentModeSelection()
        {
            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                var officeWorkspace = _currentWorkspace != null && _currentWorkspace.Kind == WorkspaceKind.Office
                    ? _currentWorkspace
                    : FindWorkspaceById(_currentOfficeWorkspaceId);
                _currentOfficeWorkspaceId = officeWorkspace != null && officeWorkspace.Kind == WorkspaceKind.Office
                    ? officeWorkspace.Id
                    : null;
                if (_currentSession != null)
                {
                    _currentOfficeSessionId = _currentSession.Mode == AppInteractionMode.Office
                        ? _currentSession.Id
                        : _currentOfficeSessionId;
                }
                else if (_currentWorkspace != null && _currentWorkspace.Kind == WorkspaceKind.Office)
                {
                    _currentOfficeSessionId = null;
                }

                return;
            }

            if (_currentWorkspace != null && _currentWorkspace.Kind == WorkspaceKind.Coding)
            {
                _currentCodingWorkspaceId = _currentWorkspace.Id;
            }

            if (_currentSession != null)
            {
                _currentCodingSessionId = _currentSession.Mode == AppInteractionMode.Coding
                    ? _currentSession.Id
                    : _currentCodingSessionId;
            }
            else if (_currentWorkspace != null && _currentWorkspace.Kind == WorkspaceKind.Coding)
            {
                _currentCodingSessionId = null;
            }
        }

        private void SaveCurrentModeUnboundComposeState()
        {
            var draft = CaptureCurrentComposeDraftState();
            _composeDraftStore.Save(GetCurrentModeDraftScopeKey(), UnboundComposeDraftSessionId,
                draft.DraftText,
                draft.Attachments);
        }

        private ComposeDraftState GetCurrentModeUnboundComposeState()
        {
            return _composeDraftStore.Get(GetCurrentModeDraftScopeKey(), UnboundComposeDraftSessionId);
        }

        private void ClearCurrentModeUnboundComposeState()
        {
            _composeDraftStore.Remove(GetCurrentModeDraftScopeKey(), UnboundComposeDraftSessionId,
                false,
                null);
        }

        private void RestoreSelectionForCurrentMode()
        {
            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                var selection = SessionSelectionResolver.RestoreOfficeSelection(
                    _recentWorkspaces,
                    _sessions,
                    _currentOfficeWorkspaceId,
                    _currentOfficeSessionId);
                _currentWorkspace = selection.Workspace;
                _currentSession = selection.Session;
                CaptureCurrentModeSelection();
                return;
            }

            _currentWorkspace = FindWorkspaceById(_currentCodingWorkspaceId);
            if (_currentWorkspace == null || !IsWorkspaceInCurrentMode(_currentWorkspace))
            {
                _currentWorkspace = GetFirstVisibleWorkspaceFromCache();
            }

            _currentSession = FindSessionById(_currentCodingSessionId);
            if (_currentSession != null && _currentSession.Mode != AppInteractionMode.Coding)
            {
                _currentSession = null;
            }

            RestoreCurrentSession();
            CaptureCurrentModeSelection();
        }

        private WorkspaceInfo EnsureOfficeWorkspace()
        {
            return EnsureDefaultOfficeWorkspace(true);
        }

        private WorkspaceInfo EnsureDefaultOfficeWorkspace(bool makeCurrent)
        {
            if (string.IsNullOrWhiteSpace(_resolvedOfficeRootPath))
            {
                return FindWorkspaceById(_currentOfficeWorkspaceId);
            }

            var target = GetActiveOfficeRuntimeTarget();
            var workspace = _officeWorkspaceService.CreateOfficeWorkspace(
                target.RuntimeScopeId,
                target.HostPlatform,
                _resolvedOfficeRootPath);
            workspace.Name = UiLocalization.Text("MainForm.Title.Mode.Office", "Office Mode");
            var existing = FindWorkspaceById(workspace.Id);
            if (existing != null)
            {
                existing.Kind = WorkspaceKind.Office;
                existing.Name = workspace.Name;
                existing.RootPath = workspace.RootPath;
                if (makeCurrent)
                {
                    _currentOfficeWorkspaceId = existing.Id;
                }
                return existing;
            }

            UpsertRecentWorkspace(workspace);
            if (makeCurrent)
            {
                _currentOfficeWorkspaceId = workspace.Id;
            }
            return workspace;
        }

        private AppConfigSnapshot GetConfiguredRuntimeConfigSnapshot()
        {
            return _appConfigSnapshot ?? LoadAppConfigForAction();
        }

        private string GetConfiguredRuntimeProfileId()
        {
            return _runtimeProfileService.GetCurrentRuntimeProfileId(_appConfigSnapshot);
        }

        private bool IsWorkspaceInCurrentMode(WorkspaceInfo workspace)
        {
            if (workspace == null)
            {
                return false;
            }

            return _currentInteractionMode == AppInteractionMode.Office
                ? workspace.Kind == WorkspaceKind.Office
                : workspace.Kind == WorkspaceKind.Coding;
        }

        private void ApplyModeVisibility()
        {
            var isOfficeMode = _currentInteractionMode == AppInteractionMode.Office;
            if (_appConfigSnapshot == null)
            {
                _appConfigSnapshot = new AppConfigSnapshot();
            }

            _gitMenuItem.Visible = !isOfficeMode;
            _commandMenuButton.Visible = !isOfficeMode;
            _diffWorkbenchButton.Visible = !isOfficeMode;
            _openWorkspaceButton.Visible = !isOfficeMode;
            _workspaceNewMenuItem.Visible = !isOfficeMode;
            _workspaceProjectInstructionsMenuItem.Visible = !isOfficeMode;
            _workspaceCreateAutomationMenuItem.Visible = !isOfficeMode;
            _workspaceImportSyncMenuItem.Visible = !isOfficeMode;
            _workspaceRestoreArchivedMenuItem.Visible = !isOfficeMode;
            _workspaceShowUnavailableMenuItem.Visible = !isOfficeMode;
            _workspaceRemoveMenuItem.Visible = !isOfficeMode;
            _workspaceArchiveMenuItem.Visible = !isOfficeMode;
            _sessionSubAgentsMenuItem.Visible = !isOfficeMode;
            _sessionAdvancedSeparatorMenuItem.Visible = !isOfficeMode;
            _sessionArchiveMenuItem.Visible = !isOfficeMode;
            _forkMenuItem.Visible = !isOfficeMode;
            _reviewChatHistoryMenuItem.Visible = !isOfficeMode;
            if (_contentSplitContainer != null)
            {
                _contentSplitContainer.Panel2Collapsed = isOfficeMode && !_appConfigSnapshot.OfficeShowActivityPane;
            }

            if (_workspaceList != null)
            {
                _workspaceList.Visible = !isOfficeMode;
                _workspaceList.TabStop = !isOfficeMode;
                _workspaceList.AccessibleName = GetWorkspaceListAccessibleName();
                ApplyWorkspaceListColumnText();
            }

            if (_workspaceSectionTitleLabel != null)
            {
                _workspaceSectionTitleLabel.Visible = !isOfficeMode;
                _workspaceSectionTitleLabel.Text = GetWorkspaceSectionTitleText();
            }

            RefreshWindowTitle();
        }

        private string GetCurrentModeWindowTitleText()
        {
            return _currentInteractionMode == AppInteractionMode.Office
                ? UiLocalization.Text("MainForm.Title.Mode.Office", "Office Mode")
                : UiLocalization.Text("MainForm.Title.Mode.Coding", "Coding Mode");
        }

        private async Task RunStartupModeOnboardingIfNeeded()
        {
            if (!await WaitForTargetExitDecisionAsync()) return;
            if (_isPersistenceRecoveryPending)
            {
                return;
            }

            if (_appStateSnapshot != null && _appStateSnapshot.HasCompletedStartupModeOnboarding)
            {
                return;
            }

            if (_appConfigSnapshot == null)
            {
                _appConfigSnapshot = new AppConfigSnapshot();
            }

            if (_appStateSnapshot == null)
            {
                _appStateSnapshot = new AppStateSnapshot();
            }

            using (var form = new StartupModeOnboardingForm(_activeModeSettings.DefaultStartupMode))
            {
                if (form.ShowDialog(this) != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }

                try
                {
                    await SaveModeSettingsForCurrentRuntimeAsync(new ModeSettingsSnapshot
                    {
                        DefaultStartupMode = form.SelectedMode,
                        OfficeRootPath = _activeModeSettings.OfficeRootPath
                    });
                }
                catch (Exception ex)
                {
                    AppendLog(ex.Message);
                    MessageBox.Show(
                        this,
                        string.Format(
                            UiLocalization.Text(
                                "OptionsForm.ModeSettings.SaveFailed.Template",
                                "The mode settings for the current runtime could not be saved: {0}"),
                            ex.Message),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if (!await WaitForTargetExitDecisionAsync()) return;
                _appStateSnapshot.HasCompletedStartupModeOnboarding = true;
                SelectInteractionModeTab(form.SelectedMode);
                PersistState();
                ScheduleOfficeCliOnboardingPromptIfNeeded();
            }
        }

        private void ScheduleOfficeCliOnboardingPromptIfNeeded()
        {
            if (_currentInteractionMode != AppInteractionMode.Office ||
                !IsWindowsOfficeCliIntegrationRelevant() ||
                _isOfficeCliOnboardingPromptActive ||
                IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(new Action(async () => await PromptForOfficeCliIntegrationIfNeededAsync().ConfigureAwait(true)));
        }

        private async Task PromptForOfficeCliIntegrationIfNeededAsync()
        {
            if (!await WaitForTargetExitDecisionAsync()) return;
            if (_currentInteractionMode != AppInteractionMode.Office ||
                !IsWindowsOfficeCliIntegrationRelevant() ||
                _isOfficeCliOnboardingPromptActive)
            {
                return;
            }

            if (_appStateSnapshot == null)
            {
                _appStateSnapshot = new AppStateSnapshot();
            }

            if (_appStateSnapshot.HasCompletedOfficeCliOnboarding)
            {
                return;
            }

            var installationService = new OfficeCliInstallationService();
            var paths = OfficeCliWinFormsPathResolver.BuildPaths();
            var status = installationService.GetStatus(paths);
            if (status != null && status.IsReady)
            {
                _appStateSnapshot.HasCompletedOfficeCliOnboarding = true;
                PersistState();
                return;
            }

            _isOfficeCliOnboardingPromptActive = true;
            try
            {
                _appStateSnapshot.HasCompletedOfficeCliOnboarding = true;
                PersistState();

                var promptResult = MessageBox.Show(
                    this,
                    UiLocalization.Text(
                        "MainForm.OfficeCliOnboarding.Message",
                        "Office Mode can use OfficeCLI and its Codex skill to create and inspect Word, Excel, and PowerPoint files. Install it now? You can also install or remove it later in Options."),
                    UiLocalization.Text("MainForm.OfficeCliOnboarding.Title", "OfficeCLI"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);
                if (promptResult != DialogResult.Yes)
                {
                    return;
                }

                SetStatus(UiLocalization.Text("MainForm.OfficeCliOnboarding.Status.Installing", "Installing OfficeCLI..."));
                var installResult = await installationService.InstallLatestAsync(
                    paths,
                    default(System.Threading.CancellationToken));
                MessageBox.Show(
                    this,
                    installResult.Message,
                    UiLocalization.Text("MainForm.OfficeCliOnboarding.Title", "OfficeCLI"),
                    MessageBoxButtons.OK,
                    installResult.Success ? MessageBoxIcon.Information : MessageBoxIcon.Error);

                if (installResult.Success)
                {
                    await RestartAppServerAfterOfficeCliEnvironmentChangeAsync();
                }
                else
                {
                    SetStatus(UiLocalization.Text("MainForm.OfficeCliOnboarding.Status.InstallFailed", "OfficeCLI installation failed"));
                }
            }
            catch (Exception ex)
            {
                AddChatHistoryItem(GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                SetStatus(UiLocalization.Text("MainForm.OfficeCliOnboarding.Status.InstallFailed", "OfficeCLI installation failed"));
                MessageBox.Show(
                    this,
                    ex.Message,
                    UiLocalization.Text("MainForm.OfficeCliOnboarding.Title", "OfficeCLI"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _isOfficeCliOnboardingPromptActive = false;
            }
        }

        private bool IsWindowsOfficeCliIntegrationRelevant()
        {
            return GetActiveOfficeRuntimeTarget().HostPlatform == AppServerHostPlatform.Windows;
        }
    }
}
