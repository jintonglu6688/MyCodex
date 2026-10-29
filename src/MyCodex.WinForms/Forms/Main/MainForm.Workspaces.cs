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
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Dialogs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async void WorkspaceList_DoubleClick(object sender, EventArgs e)
        {
            await OpenSelectedWorkspaceFromListAsync();
        }

        private void WorkspaceList_MouseDown(object sender, MouseEventArgs e)
        {
            SelectListViewItemOnRightClick(_workspaceList, e);
        }

        private WorkspaceListItem GetSelectedWorkspaceListItem()
        {
            return _workspaceList.SelectedItems.Count == 0
                ? null
                : _workspaceList.SelectedItems[0].Tag as WorkspaceListItem;
        }

        private async Task ExecuteWorkspaceDeletionAsync(WorkspaceInfo workspace, LocalSessionRemovalMode removalMode)
        {
            if (workspace == null)
            {
                return;
            }

            var sessions = SessionCollectionQueries.GetSessionsForWorkspace(_sessions, workspace.Id);
            if (!ConfirmWorkspaceDeletion(workspace, sessions.Count, ref removalMode))
            {
                return;
            }

            try
            {
                var flowPlan = LocalSessionRemovalFlowOrchestrator.BuildWorkspacePlan(removalMode, sessions.Count);
                if (removalMode != LocalSessionRemovalMode.PermanentDelete)
                    await InterruptSessionsBeforeRemovalAsync(sessions);
                SetStatus(LocalSessionRemovalTextPlanner.BuildWorkspaceProgressText(removalMode, workspace.Name));
                var processedCount = await ExecuteLocalSessionRemovalOperationAsync(flowPlan, sessions, null);
                if (removalMode == LocalSessionRemovalMode.PermanentDelete && processedCount == 0) return;
                processedCount = LocalSessionRemovalFlowOrchestrator.ResolveWorkspaceProcessedCount(flowPlan, processedCount);
                if (removalMode != LocalSessionRemovalMode.Archive ||
                    !SessionCollectionQueries.GetSessionsForWorkspace(_sessions, workspace.Id).Any())
                    RemoveWorkspaceLocally(workspace);
                FinalizeDeletionUiState();
                SetStatus(LocalSessionRemovalTextPlanner.BuildWorkspaceCompletionStatus(removalMode, workspace.Name, processedCount));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.WorkspaceDeletionFailed", "Failed to delete workspace"));
            }
        }

        private bool ConfirmWorkspaceDeletion(WorkspaceInfo workspace, int sessionCount, ref LocalSessionRemovalMode removalMode)
        {
            if (removalMode == LocalSessionRemovalMode.PermanentDelete) return true;

            if (removalMode != LocalSessionRemovalMode.LocalOnly)
            {
                return MessageBox.Show(
                    this,
                    LocalSessionRemovalTextPlanner.BuildWorkspaceConfirmation(workspace, sessionCount, removalMode),
                    AppConstants.AppName,
                    MessageBoxButtons.YesNo,
                    LocalSessionRemovalTextPlanner.GetConfirmationIcon(removalMode)) == DialogResult.Yes;
            }

            var confirmationText = LocalSessionRemovalTextPlanner.BuildWorkspaceConfirmation(workspace, sessionCount, LocalSessionRemovalMode.LocalOnly);
            using (var form = new LocalSessionDeleteConfirmationForm(
                confirmationText,
                LocalSessionRemovalEligibility.CanPermanentlyDeleteWorkspace(_sessions, workspace.Id)))
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

        private void RemoveWorkspaceLocally(WorkspaceInfo workspace)
        {
            var plan = LocalSessionRemovalPlanner.BuildWorkspaceRemoval(
                workspace,
                _recentWorkspaces,
                _sessions,
                _inactiveRuntimeSessions,
                _currentWorkspace,
                _currentSession);
            ApplyLocalRemovalPlan(plan);
        }

        private void WorkspaceContextMenu_Popup(object sender, EventArgs e)
        {
            var isOfficeMode = _currentInteractionMode == AppInteractionMode.Office;
            var selectedItem = GetSelectedWorkspaceListItem();
            var hasSelection = selectedItem != null;
            var hasWorkspacePath = hasSelection &&
                selectedItem.Workspace != null &&
                !string.IsNullOrWhiteSpace(selectedItem.Workspace.RootPath);
            var workspaceAvailable = hasWorkspacePath && IsWorkspaceAvailable(selectedItem.Workspace);
            _workspaceNewMenuItem.Enabled = !isOfficeMode;
            _workspaceOpenWslMenuItem.Visible = !isOfficeMode && IsWslRuntimeEnabled();
            _workspaceOpenWslMenuItem.Enabled = _workspaceOpenWslMenuItem.Visible;
            _workspaceAppServerFilesMenuItem.Enabled = workspaceAvailable;
            _workspaceProjectInstructionsMenuItem.Enabled = !isOfficeMode && hasSelection;
            _workspaceCreateAutomationMenuItem.Visible = !isOfficeMode;
            _workspaceCreateAutomationMenuItem.Enabled = !isOfficeMode && workspaceAvailable;
            _workspaceSyncThreadsMenuItem.Enabled = !isOfficeMode && IsConnectedStatus();
            _workspaceImportOtherSessionsMenuItem.Enabled = !isOfficeMode &&
                hasWorkspacePath;
            _workspaceExternalAgentConfigImportMenuItem.Enabled = !isOfficeMode && IsConnectedStatus();
            _workspaceImportSyncMenuItem.Enabled =
                !isOfficeMode &&
                (_workspaceSyncThreadsMenuItem.Enabled ||
                 _workspaceImportOtherSessionsMenuItem.Enabled ||
                 _workspaceExternalAgentConfigImportMenuItem.Enabled);
            _workspaceRestoreArchivedMenuItem.Enabled = !isOfficeMode && hasSelection;
            _workspaceShowUnavailableMenuItem.Enabled = !isOfficeMode;
            _workspaceShowUnavailableMenuItem.Checked = ShouldShowUnavailableWorkspaces();
            _workspaceRemoveMenuItem.Enabled = !isOfficeMode && hasSelection;
            _workspaceArchiveMenuItem.Enabled = !isOfficeMode &&
                hasSelection &&
                LocalSessionRemovalEligibility.HasThreadSessions(_sessions, selectedItem.Workspace.Id);
        }

        private void CreateAutomationFromSelectedWorkspace()
        {
            var selectedItem = GetSelectedWorkspaceListItem();
            if (selectedItem == null)
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.OpenWorkspaceFirst", "Please open a workspace first"));
                return;
            }

            CreateAutomationFromWorkspace(selectedItem.Workspace);
        }

        private void CreateAutomationFromWorkspace(WorkspaceInfo workspace)
        {
            if (workspace == null || string.IsNullOrWhiteSpace(workspace.RootPath))
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.OpenWorkspaceFirst", "Please open a workspace first"));
                return;
            }

            var context = AutomationCreationContextBuilder.ForProjectWorkspace(
                workspace,
                _runtimeProfileService.GetCurrentRuntimeProfileId(_appConfigSnapshot),
                GetConfiguredDefaultModelId(),
                GetConfiguredDefaultReasoningEffort(),
                GetConfiguredDefaultApprovalMode());
            ShowAutomationsForm(context);
        }

        private async void OnWorkspaceRemoveMenuItemClick(object sender, EventArgs e)
        {
            await DeleteSelectedWorkspaceAsync();
        }

        private void OnWorkspaceShowUnavailableMenuItemClick(object sender, EventArgs e)
        {
            _appConfigSnapshot.ShowUnavailableWorkspaces = !_appConfigSnapshot.ShowUnavailableWorkspaces;
            SaveAppConfig();
            ApplyWorkspaceVisibilityFilter();
        }

        private async void OnWorkspaceAssociateOtherSessionsMenuItemClick(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedWorkspaceListItem();
            if (selectedItem == null)
            {
                return;
            }

            await ShowThreadAssociationDialogAsync(selectedItem.Workspace);
        }

        private async void OnImportOtherSessionsMenuItemClick(object sender, EventArgs e)
        {
            await ShowThreadAssociationDialogAsync(_currentWorkspace);
        }

        private async void OnWorkspaceArchiveMenuItemClick(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedWorkspaceListItem();
            if (selectedItem == null)
            {
                return;
            }

            await ExecuteWorkspaceDeletionAsync(selectedItem.Workspace, LocalSessionRemovalMode.Archive);
        }

        private async void OnWorkspaceProjectInstructionsMenuItemClick(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedWorkspaceListItem();
            await ShowProjectInstructionsDialogAsync(selectedItem != null ? selectedItem.Workspace : null);
        }

        private async void OnWorkspaceAppServerFilesMenuItemClick(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedWorkspaceListItem();
            if (selectedItem == null || selectedItem.Workspace == null)
            {
                return;
            }

            await ShowAppServerFilesForWorkspaceAsync(selectedItem.Workspace.RootPath);
        }

        private void OnOpenWorkspaceFileMenuItemClick(object sender, EventArgs e)
        {
            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                OpenOfficeWorkspaceDirectory();
                return;
            }

            OnOpenWorkspaceButtonClick(sender, e);
        }

        private void OpenOfficeWorkspaceDirectory()
        {
            if (string.IsNullOrWhiteSpace(_resolvedOfficeRootPath) ||
                !string.IsNullOrWhiteSpace(_officeRuntimeRootError))
            {
                return;
            }

            var target = GetActiveOfficeRuntimeTarget();
            if (target.UsesUnixPaths)
            {
                ShowRuntimeFileBrowser(
                    BuildRuntimeFileWindowTitle(UiLocalization.Text(
                        "RuntimeFileBrowserForm.Title.OfficeWorkspace",
                        "Office Workspace")),
                    _resolvedOfficeRootPath,
                    RuntimeFileBrowserSelectionMode.Directory,
                    null);
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(_resolvedOfficeRootPath);
                SetStatus(UiLocalization.Text("MainForm.OfficeWorkspace.Status.OpenedDirectory", "Opened Office directory"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.OfficeWorkspace.Status.OpenDirectoryFailed", "Failed to open Office directory"));
            }
        }

        private Task ShowProjectInstructionsDialogAsync(WorkspaceInfo workspace)
        {
            if (workspace == null || string.IsNullOrWhiteSpace(workspace.RootPath))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.ProjectInstructions.Message.OpenWorkspaceFirst", "Please open a workspace first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SetStatus(UiLocalization.Text("MainForm.ProjectInstructions.Status.OpenWorkspaceFirst", "Open a workspace first"));
                return Task.CompletedTask;
            }

            ProjectInstructionsStatus status;
            try
            {
                status = IsUnixRuntimePath(workspace.RootPath)
                    ? _projectInstructionsService.CreateUnsupportedStatus(
                        workspace.RootPath,
                        UiLocalization.Text("ProjectInstructionsForm.Status.RemoteWriteUnsupported", "The current runtime can generate project instructions, but reading or writing AGENTS.md from this dialog is not supported yet. Review generated content and handle the file manually."))
                    : _projectInstructionsService.ReadLocalStatus(workspace.RootPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.ProjectInstructions.Status.OpenFailed", "Failed to open project instructions"));
                return Task.CompletedTask;
            }

            using (var form = new ProjectInstructionsForm(
                status,
                IsConnectedStatus(),
                UiLocalization.Text("ProjectInstructionsForm.Template.Basic", BuildProjectInstructionsBasicTemplateFallback()),
                (improve, existingContent, cancellationToken) => GenerateProjectInstructionsContentAsync(workspace, improve, existingContent, cancellationToken),
                content =>
                {
                    _projectInstructionsService.SaveLocal(status.TargetPath, content);
                    return Task.CompletedTask;
                }))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    SetStatus(status.Exists
                        ? UiLocalization.Text("MainForm.ProjectInstructions.Status.Saved", "Project instructions saved")
                        : UiLocalization.Text("MainForm.ProjectInstructions.Status.Created", "Project instructions created"));
                    FocusWorkspaceList();
                }
            }

            return Task.CompletedTask;
        }

        private void FocusWorkspaceList()
        {
            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                FocusSessionList();
                return;
            }

            SelectFirstWorkspaceListItemIfNeeded();

            ActiveControl = _workspaceList;
            _workspaceList.Focus();
        }

        private bool HasWorkspace(string workspaceId)
        {
            foreach (var workspace in _recentWorkspaces)
            {
                if (string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshWorkspaceView()
        {
            _workspaceList.BeginUpdate();
            _workspaceList.Items.Clear();
            ApplyWorkspaceListColumnText();

            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                _workspaceList.EndUpdate();
                return;
            }

            if (_recentWorkspaces.Count == 0)
            {
                _workspaceList.Items.Add(CreateWorkspacePlaceholderItem(UiLocalization.Text("MainForm.WorkspaceList.Placeholder.NotOpened", "No workspace opened yet")));
                _workspaceList.EndUpdate();
                return;
            }

            var visibleWorkspaces = ModeWorkspaceProjection.Build(
                    _recentWorkspaces,
                    _currentInteractionMode,
                    ShouldShowUnavailableWorkspaces(),
                    IsWorkspaceCompatibleWithCurrentRuntime,
                    IsWorkspaceAvailableFromCache)
                .ToList();
            var selectedIndex = -1;
            for (var i = 0; i < visibleWorkspaces.Count; i++)
            {
                var isCurrentWorkspace = _currentWorkspace != null &&
                    string.Equals(_currentWorkspace.Id, visibleWorkspaces[i].Id, StringComparison.OrdinalIgnoreCase);
                var item = new WorkspaceListItem(
                    visibleWorkspaces[i],
                    IsWorkspaceAvailableFromCache(visibleWorkspaces[i]),
                    GetCurrentCommandHostPlatform(),
                    GetCurrentRemoteUnixProfileDisplayName(),
                    isCurrentWorkspace,
                    GetWorkingSessionCount(visibleWorkspaces[i].Id));
                _workspaceList.Items.Add(item.CreateListViewItem(IsUnixRemoteRuntimeEnabled()));

                if (isCurrentWorkspace)
                {
                    selectedIndex = i;
                }
            }

            if (_workspaceList.Items.Count == 0)
            {
                _workspaceList.Items.Add(CreateWorkspacePlaceholderItem(UiLocalization.Text("MainForm.WorkspaceList.Placeholder.NoVisibleWorkspaces", "No visible workspaces")));
                _workspaceList.EndUpdate();
                return;
            }

            if (selectedIndex >= 0)
            {
                SelectWorkspaceListItemAt(selectedIndex);
            }
            else if (_workspaceList.Items.Count > 0)
            {
                SelectWorkspaceListItemAt(0);
            }

            _workspaceList.EndUpdate();
        }

        private static ListViewItem CreateWorkspacePlaceholderItem(string text)
        {
            return new ListViewItem(new[] { text ?? string.Empty, string.Empty, string.Empty, string.Empty });
        }

        private void SelectFirstWorkspaceListItemIfNeeded()
        {
            if (_workspaceList.Items.Count > 0 && _workspaceList.SelectedItems.Count == 0)
            {
                SelectWorkspaceListItemAt(0);
            }
        }

        private void SelectWorkspaceListItemAt(int index)
        {
            if (index < 0 || index >= _workspaceList.Items.Count)
            {
                return;
            }

            _workspaceList.SelectedItems.Clear();
            _workspaceList.Items[index].Selected = true;
            _workspaceList.Items[index].Focused = true;
        }

        private bool IsCurrentWorkspaceAvailable()
        {
            return IsWorkspaceAvailable(_currentWorkspace);
        }

        private bool IsCurrentWorkspaceAvailableFromCache()
        {
            return IsWorkspaceAvailableFromCache(_currentWorkspace);
        }

        private bool ShouldShowUnavailableWorkspaces()
        {
            return _appConfigSnapshot == null || _appConfigSnapshot.ShowUnavailableWorkspaces;
        }

        private bool ShouldDisplayWorkspace(WorkspaceInfo workspace)
        {
            if (workspace == null)
            {
                return false;
            }

            if (!IsWorkspaceInCurrentMode(workspace))
            {
                return false;
            }

            if (!IsWorkspaceCompatibleWithCurrentRuntime(workspace))
            {
                return false;
            }

            return ShouldShowUnavailableWorkspaces() || IsWorkspaceAvailableFromCache(workspace);
        }

        private void UpdateOfficeSessionWorkspaceId(string previousWorkspaceId, string currentWorkspaceId)
        {
            if (string.IsNullOrWhiteSpace(previousWorkspaceId) ||
                string.IsNullOrWhiteSpace(currentWorkspaceId) ||
                string.Equals(previousWorkspaceId, currentWorkspaceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            UpdateOfficeSessionWorkspaceId(_sessions, previousWorkspaceId, currentWorkspaceId);
            UpdateOfficeSessionWorkspaceId(_inactiveRuntimeSessions, previousWorkspaceId, currentWorkspaceId);
            if (string.Equals(_currentOfficeWorkspaceId, previousWorkspaceId, StringComparison.OrdinalIgnoreCase))
            {
                _currentOfficeWorkspaceId = currentWorkspaceId;
            }
        }

        private static void UpdateOfficeSessionWorkspaceId(IEnumerable<ChatSession> sessions, string previousWorkspaceId, string currentWorkspaceId)
        {
            if (sessions == null)
            {
                return;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    session.Mode == AppInteractionMode.Office &&
                    string.Equals(session.WorkspaceId, previousWorkspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    session.WorkspaceId = currentWorkspaceId;
                }
            }
        }

        private bool IsWorkspaceCompatibleWithCurrentRuntime(WorkspaceInfo workspace)
        {
            if (workspace == null || string.IsNullOrWhiteSpace(workspace.RootPath))
            {
                return false;
            }

            if (_currentInteractionMode == AppInteractionMode.Office && workspace.Kind == WorkspaceKind.Office)
            {
                return IsOfficeWorkspaceCompatibleWithActiveRuntime(workspace);
            }

            return IsWorkspacePathCompatibleWithCurrentRuntime(workspace.RootPath);
        }

        private bool IsOfficeWorkspaceCompatibleWithActiveRuntime(WorkspaceInfo workspace)
        {
            var target = GetActiveOfficeRuntimeTarget();
            var hasExpectedPathKind = target.UsesUnixPaths
                ? WorkspacePathUtilities.LooksLikeUnixAbsolutePath(workspace.RootPath)
                : WorkspacePathUtilities.LooksLikeWindowsDriveAbsolutePath(workspace.RootPath) ||
                  WorkspacePathUtilities.LooksLikeWindowsUncAbsolutePath(workspace.RootPath);
            return hasExpectedPathKind &&
                !string.IsNullOrWhiteSpace(workspace.Id) &&
                string.Equals(
                    workspace.Id,
                    "office:" + target.RuntimeScopeId,
                    StringComparison.OrdinalIgnoreCase);
        }

        private bool IsWorkspacePathCompatibleWithCurrentRuntime(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return false;
            }

            var isUnixPath = WorkspacePathUtilities.LooksLikeUnixAbsolutePath(workspacePath);
            if (IsRemoteMacRuntimeEnabled())
            {
                return isUnixPath;
            }

            if (IsWslRuntimeEnabled())
            {
                return isUnixPath || WorkspacePathUtilities.LooksLikeWindowsDriveAbsolutePath(workspacePath);
            }

            return !isUnixPath;
        }

        private WorkspaceInfo GetFirstVisibleWorkspace()
        {
            foreach (var workspace in _recentWorkspaces)
            {
                if (ShouldDisplayWorkspace(workspace))
                {
                    return workspace;
                }
            }

            return null;
        }

        private WorkspaceInfo GetFirstVisibleWorkspaceFromCache()
        {
            foreach (var workspace in _recentWorkspaces)
            {
                if (ShouldDisplayWorkspace(workspace))
                {
                    return workspace;
                }
            }

            return null;
        }

        private void EnsureCurrentWorkspaceMatchesVisibilityFilter()
        {
            if (_currentWorkspace != null && ShouldDisplayWorkspace(_currentWorkspace))
            {
                return;
            }

            _currentWorkspace = GetFirstVisibleWorkspace();
            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                ResetOfficeSessionForNewTask();
                return;
            }

            RestoreCurrentSession();
        }

        private void ApplyWorkspaceVisibilityFilter()
        {
            var previousWorkspaceId = _currentWorkspace != null ? _currentWorkspace.Id : null;
            if (_currentWorkspace != null && !ShouldDisplayWorkspace(_currentWorkspace))
            {
                SaveCurrentComposeState();
            }

            EnsureCurrentWorkspaceMatchesVisibilityFilter();
            var workspaceChanged = !string.Equals(previousWorkspaceId, _currentWorkspace != null ? _currentWorkspace.Id : null, StringComparison.OrdinalIgnoreCase);

            if (workspaceChanged)
            {
                if (_currentWorkspace == null)
                {
                    ClearChatHistoryItems();
                }
                else if (_currentSession == null)
                {
                    ClearChatHistoryItems();
                    AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), GetNoSessionMessageForCurrentWorkspace());
                }
                else
                {
                    MarkPendingHistoryReload(_currentSession);
                    ReplaceChatHistoryItems(new[]
                    {
                        ChatHistoryItemFactory.Create(
                            ChatHistoryItemFactory.GetSystemSenderText(),
                            string.Format(UiLocalization.Text("MainForm.Message.SwitchedToSession.Template", "Switched to session: {0}"), _currentSession.Title)),
                        ChatHistoryItemFactory.Create(ChatHistoryItemFactory.GetSystemSenderText(), UiLocalization.Text("MainForm.Message.PressEnterToReloadFullHistory", "Press Enter to reopen full history."))
                    });
                }

                ApplyComposeState(_currentSession);
            }

            ApplyMainFormRefreshPreset(MainFormRefreshPreset.WorkspaceSessionHeaderInputPersist);
        }

        private bool IsWorkspaceAvailable(WorkspaceInfo workspace)
        {
            if (workspace == null)
            {
                return false;
            }

            var target = GetActiveOfficeRuntimeTarget();
            return !OfficeWorkspaceAvailabilityPlanner.IsUnavailable(
                       workspace,
                       target.RuntimeScopeId,
                       _officeRuntimeRootError) &&
                   IsWorkspacePathAvailable(workspace.RootPath);
        }

        private bool IsWorkspaceAvailableFromCache(WorkspaceInfo workspace)
        {
            bool? cachedAvailability = null;
            if (workspace != null && !string.IsNullOrWhiteSpace(workspace.RootPath))
            {
                try
                {
                    var normalizedPath = _workspaceService.NormalizeWorkspacePath(workspace.RootPath);
                    Tuple<bool, DateTime> cached;
                    if (_workspaceAvailabilityCache.TryGetValue(
                        BuildWorkspaceAvailabilityCacheKey(normalizedPath),
                        out cached))
                    {
                        cachedAvailability = cached.Item1;
                    }
                }
                catch
                {
                    cachedAvailability = false;
                }
            }

            var target = GetActiveOfficeRuntimeTarget();
            return OfficeWorkspaceAvailabilityPlanner.ResolveForProjection(
                workspace,
                target.RuntimeScopeId,
                _officeRuntimeRootError,
                cachedAvailability);
        }

        private bool IsWorkspacePathAvailable(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return false;
            }

            try
            {
                var normalizedPath = _workspaceService.NormalizeWorkspacePath(workspacePath);
                var cacheKey = BuildWorkspaceAvailabilityCacheKey(normalizedPath);
                Tuple<bool, DateTime> cached;
                if (_workspaceAvailabilityCache.TryGetValue(cacheKey, out cached) &&
                    DateTime.UtcNow.Subtract(cached.Item2).TotalMilliseconds < WorkspaceAvailabilityCacheMilliseconds)
                {
                    return cached.Item1;
                }

                if (IsUnixRuntimePath(normalizedPath))
                {
                    // UI refresh paths must not synchronously spawn WSL/SSH. Opening a
                    // Unix workspace performs an async existence check and seeds this cache.
                    return true;
                }

                var isAvailable = Directory.Exists(normalizedPath);
                SetWorkspaceAvailabilityCache(normalizedPath, isAvailable);
                return isAvailable;
            }
            catch
            {
                return false;
            }
        }

        private WorkspaceInfo CreateWorkspaceFromPath(string workspacePath, bool requireExisting)
        {
            return _workspaceService.CreateFromPath(
                workspacePath,
                requireExisting,
                IsWorkspacePathAvailable,
                ReadWorkspaceGitInfo);
        }

        private async Task<WorkspaceInfo> CreateWorkspaceFromPathAsync(string workspacePath, bool requireExisting)
        {
            var normalizedPath = _workspaceService.NormalizeWorkspacePath(workspacePath);
            if (!IsUnixRuntimePath(normalizedPath))
            {
                return CreateWorkspaceFromPath(normalizedPath, requireExisting);
            }

            var exists = IsRemoteMacRuntimeEnabled()
                ? await RunRemoteMacFileSystemOperationWithHostKeyRetryAsync(config => _remoteMacFileSystemBridge.DirectoryExistsAsync(config, normalizedPath))
                : await _wslFileSystemBridge.DirectoryExistsAsync(GetCurrentWslConfig(), normalizedPath);
            if (!await WaitForTargetExitDecisionAsync()) return null;
            SetWorkspaceAvailabilityCache(normalizedPath, exists);
            WorkspaceGitInfoSnapshot gitInfo = null;
            if (exists)
            {
                gitInfo = IsRemoteMacRuntimeEnabled()
                    ? await RunRemoteMacFileSystemOperationWithHostKeyRetryAsync(config => _remoteMacFileSystemBridge.ReadGitInfoAsync(config, normalizedPath))
                    : await _wslFileSystemBridge.ReadGitInfoAsync(GetCurrentWslConfig(), normalizedPath);
                if (!await WaitForTargetExitDecisionAsync()) return null;
                SetWorkspaceGitInfoCache(normalizedPath, gitInfo);
            }

            return _workspaceService.CreateFromPath(
                normalizedPath,
                requireExisting,
                _ => exists,
                _ => gitInfo ?? new WorkspaceGitInfoSnapshot());
        }

        private async Task RefreshWorkspaceGitMetadataAsync(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return;
            }

            WorkspaceInfo refreshedWorkspace;
            try
            {
                refreshedWorkspace = await CreateWorkspaceFromPathAsync(workspacePath, true).ConfigureAwait(true);
            }
            catch
            {
                return;
            }

            if (!WorkspaceGitMetadataRefreshPlanner.Apply(_recentWorkspaces, _currentWorkspace, refreshedWorkspace))
            {
                return;
            }

            RefreshWorkspaceView();
            RefreshHeader();
            PersistState();
        }

        private WorkspaceGitInfoSnapshot ReadWorkspaceGitInfo(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return new WorkspaceGitInfoSnapshot();
            }

            var normalizedPath = _workspaceService.NormalizeWorkspacePath(workspacePath);
            if (!IsUnixRuntimePath(normalizedPath))
            {
                return _workspaceService.ReadGitInfo(normalizedPath);
            }

            WorkspaceGitInfoSnapshot cached;
            return _workspaceGitInfoCache.TryGetValue(BuildWorkspaceGitInfoCacheKey(normalizedPath), out cached)
                ? cached
                : new WorkspaceGitInfoSnapshot();
        }

        private bool IsWslRuntimePath(string path)
        {
            return IsWslRuntimeEnabled() && WorkspacePathUtilities.LooksLikeUnixAbsolutePath(path);
        }

        private bool IsUnixRuntimePath(string path)
        {
            return IsUnixRemoteRuntimeEnabled() && WorkspacePathUtilities.LooksLikeUnixAbsolutePath(path);
        }

        private bool IsWslRuntimeEnabled()
        {
            return _appConfigSnapshot != null &&
                   string.Equals(_appConfigSnapshot.CodexRuntimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsRemoteMacRuntimeEnabled()
        {
            return _appConfigSnapshot != null &&
                   CodexRuntimeSources.IsRemoteUnixSsh(_appConfigSnapshot.CodexRuntimeSource);
        }

        private bool IsUnixRemoteRuntimeEnabled()
        {
            return IsWslRuntimeEnabled() || IsRemoteMacRuntimeEnabled();
        }

        private WslAppServerConfigSnapshot GetCurrentWslConfig()
        {
            return _appConfigSnapshot != null && _appConfigSnapshot.WslAppServer != null
                ? _appConfigSnapshot.WslAppServer
                : new WslAppServerConfigSnapshot();
        }

        private RemoteMacSshConfigSnapshot GetCurrentRemoteMacConfig()
        {
            return _appConfigSnapshot != null
                ? _appConfigSnapshot.GetSelectedRemoteUnixSshConfig()
                : new RemoteMacSshConfigSnapshot();
        }

        private async Task<T> RunRemoteMacFileSystemOperationWithHostKeyRetryAsync<T>(Func<RemoteMacSshConfigSnapshot, Task<T>> operation)
        {
            var config = GetCurrentRemoteMacConfig();
            try
            {
                return await operation(config);
            }
            catch (Exception ex)
            {
                if (!TryConfirmAndApplyCurrentRemoteHostKey(config, ex))
                {
                    throw;
                }
            }

            return await operation(GetCurrentRemoteMacConfig());
        }

        private bool TryConfirmAndApplyCurrentRemoteHostKey(RemoteMacSshConfigSnapshot config, Exception ex)
        {
            string hostKey;
            if (!RemoteSshHostKeyTrustPlanner.ShouldPromptForTrust(config, ex != null ? ex.Message : null, out hostKey))
            {
                return false;
            }

            var confirm = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(
                        "MainForm.Message.RemoteHostKeyTrustConfirm.Template",
                        "The remote host key is not trusted yet. Reported host key:\r\n\r\n{0}\r\n\r\nTrust this host key for the selected Remote Unix profile and retry? Only continue if it matches the remote machine you intend to use."),
                    hostKey),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return false;
            }

            config.HostKeyFingerprint = hostKey;
            var selectedProfile = _appConfigSnapshot != null ? _appConfigSnapshot.GetSelectedRemoteUnixSshProfile() : null;
            if (selectedProfile != null)
            {
                selectedProfile.Ssh.HostKeyFingerprint = hostKey;
            }
            else if (_appConfigSnapshot != null)
            {
                _appConfigSnapshot.RemoteMacSsh.HostKeyFingerprint = hostKey;
            }

            if (!SaveAppConfig())
            {
                return false;
            }

            ClearWorkspaceAvailabilityCache();
            SetHighPriorityStatus(UiLocalization.Text(
                "MainForm.Status.RemoteHostKeyTrustedRetrying",
                "Host key trusted. Retrying remote workspace operation..."));
            return true;
        }

        private string GetCurrentRemoteUnixProfileDisplayName()
        {
            if (_appConfigSnapshot == null || !IsRemoteMacRuntimeEnabled())
            {
                return null;
            }

            var profile = _appConfigSnapshot.GetSelectedRemoteUnixSshProfile();
            if (profile == null)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(profile.Name) ? null : profile.Name.Trim();
        }

        private string GetDefaultUnixWorkspacePath()
        {
            if (_currentWorkspace != null && IsUnixRuntimePath(_currentWorkspace.RootPath))
            {
                return _currentWorkspace.RootPath;
            }

            if (IsRemoteMacRuntimeEnabled())
            {
                return "/";
            }

            return "/home";
        }

        private void ClearWorkspaceAvailabilityCache()
        {
            if (_workspaceAvailabilityCache != null)
            {
                _workspaceAvailabilityCache.Clear();
            }

            if (_workspaceGitInfoCache != null)
            {
                _workspaceGitInfoCache.Clear();
            }
        }

        private string BuildWorkspaceAvailabilityCacheKey(string path)
        {
            return WorkspaceRuntimeCacheKeyBuilder.Build(
                GetActiveOfficeRuntimeTarget().RuntimeScopeId,
                path);
        }

        private string BuildWorkspaceGitInfoCacheKey(string path)
        {
            return WorkspaceRuntimeCacheKeyBuilder.Build(
                GetActiveOfficeRuntimeTarget().RuntimeScopeId,
                path);
        }

        private void SetWorkspaceAvailabilityCache(string path, bool isAvailable)
        {
            if (_workspaceAvailabilityCache == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            _workspaceAvailabilityCache[BuildWorkspaceAvailabilityCacheKey(path)] = Tuple.Create(isAvailable, DateTime.UtcNow);
        }

        private void SetWorkspaceGitInfoCache(string path, WorkspaceGitInfoSnapshot gitInfo)
        {
            if (_workspaceGitInfoCache == null || string.IsNullOrWhiteSpace(path) || gitInfo == null)
            {
                return;
            }

            _workspaceGitInfoCache[BuildWorkspaceGitInfoCacheKey(path)] = gitInfo;
        }

        private async Task LoadCommandLineWorkspaceAsync(string workspacePath)
        {
            await LoadWorkspaceAsync(
                workspacePath,
                false,
                false,
                createSessionIfNoRestoredSession: true);
        }

        private async Task LoadWorkspaceAsync(
            string workspacePath,
            bool createFreshSession,
            bool allowMissingWorkspace = false,
            bool createSessionIfNoRestoredSession = false)
        {
            try
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                SetStatus(UiLocalization.Text("MainForm.Status.LoadingWorkspace", "Loading workspace..."));
                SaveCurrentComposeState();
                ClearWorkspaceAvailabilityCache();
                var workspace = await CreateWorkspaceFromPathAsync(workspacePath, !allowMissingWorkspace);
                if (!await WaitForTargetExitDecisionAsync()) return;
                _currentWorkspace = workspace;
                UpsertRecentWorkspace(_currentWorkspace);
                await RefreshModelConfigurationAsync(_currentWorkspace.RootPath, false);
                if (!await WaitForTargetExitDecisionAsync()) return;
                RefreshWorkspaceView();
                ClearChatHistoryItems();
                if (!IsCurrentWorkspaceAvailable())
                {
                    AddChatHistoryItem(
                        ChatHistoryItemFactory.GetSystemSenderText(),
                        UiLocalization.Text(
                            "MainForm.Message.WorkspacePathUnavailableViewOnly",
                            "Current workspace path is unavailable. You can continue browsing imported sessions, but local commands and new requests are unavailable."));
                }
                AddChatHistoryItem(
                    ChatHistoryItemFactory.GetSystemSenderText(),
                    BuildWorkspaceOpenedMessage(_currentWorkspace));

                if (createFreshSession)
                {
                    await CreateNewSessionAsync();
                }
                else
                {
                    RestoreCurrentSession();
                    ApplyComposeState(_currentSession);
                    ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderInputPersist);

                    if (_currentSession != null)
                    {
                        await LoadSessionHistoryAsync(_currentSession, true);
                        if (!await WaitForTargetExitDecisionAsync()) return;
                        SetStatus(UiLocalization.Text("MainForm.Status.RestoredRecentState", "Restored recent state"));
                    }
                    else if (createSessionIfNoRestoredSession && IsCurrentWorkspaceAvailable())
                    {
                        await CreateNewSessionAsync();
                    }
                    else
                    {
                        ClearChatHistoryItems();
                        AddChatHistoryItem(
                            ChatHistoryItemFactory.GetSystemSenderText(),
                            UiLocalization.Text(
                                "MainForm.Message.NoSessionsInWorkspaceActionHint",
                                "There are no sessions in the current workspace. You can select \"New Session\", or start typing to create one automatically."));
                        SetStatus(UiLocalization.Text("MainForm.Status.NoSessionsInWorkspace", "No sessions in current workspace"));
                    }
                }
            }
            catch (Exception ex)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                MessageBox.Show(
                    this,
                    ex.Message,
                    UiLocalization.Text("MainForm.DialogTitle.OpenWorkspaceFailed", "Open workspace failed"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.OpenWorkspaceFailed", "Open workspace failed"));
            }
        }

        private static string BuildWorkspaceOpenedMessage(WorkspaceInfo workspace)
        {
            if (workspace == null)
            {
                return string.Empty;
            }

            return workspace.Kind == WorkspaceKind.Office
                ? string.Format(
                    UiLocalization.Text("MainForm.Message.OfficeDirectoryOpened.Template", "Office directory: {0}"),
                    workspace.RootPath)
                : string.Format(
                    UiLocalization.Text("MainForm.Message.WorkspaceOpened.Template", "Opened workspace: {0}"),
                    workspace.RootPath);
        }

        private async void OnOpenWorkspaceButtonClick(object sender, EventArgs e)
        {
            if (IsRemoteMacRuntimeEnabled())
            {
                await OpenUnixWorkspacePathAsync();
                return;
            }

            string selectedPath;
            if (WindowsFolderPicker.TryPickFolder(
                this,
                UiLocalization.Text("MainForm.DialogDescription.SelectWorkspaceToOpen", "Select a workspace to open"),
                _currentWorkspace != null ? _currentWorkspace.RootPath : Environment.CurrentDirectory,
                out selectedPath))
            {
                await LoadWorkspaceAsync(selectedPath, true);
            }
        }

        private async void OnOpenWslWorkspaceMenuItemClick(object sender, EventArgs e)
        {
            if (!IsWslRuntimeEnabled())
            {
                return;
            }

            await OpenUnixWorkspacePathAsync();
        }

        private async Task OpenUnixWorkspacePathAsync()
        {
            ShowRuntimeFileBrowser(
                BuildRuntimeFileWindowTitle(
                    UiLocalization.Text(
                        IsRemoteMacRuntimeEnabled() ? "RuntimeFileBrowserForm.Title.OpenRemoteWorkspace" : "RuntimeFileBrowserForm.Title.OpenWslWorkspace",
                        IsRemoteMacRuntimeEnabled() ? "Open Remote Unix Workspace" : "Open WSL Workspace")),
                GetDefaultUnixWorkspacePath(),
                RuntimeFileBrowserSelectionMode.Directory,
                async selectedPaths =>
                {
                    var selectedPath = selectedPaths == null ? null : selectedPaths.FirstOrDefault();
                    if (selectedPath != null)
                    {
                        await LoadWorkspaceAsync(selectedPath.Path, true);
                    }
                });
            await Task.CompletedTask;
        }

        private void ShowRuntimeFileBrowser(
            string title,
            string initialPath,
            RuntimeFileBrowserSelectionMode selectionMode,
            Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>, Task> onSelectedAsync,
            bool allowMultiSelect = false)
        {
            if (_isRuntimeWindowTransitionInProgress)
            {
                return;
            }

            var form = new RuntimeFileBrowserForm(
                title,
                initialPath,
                selectionMode,
                ListCurrentUnixRuntimeDirectoryAsync,
                allowMultiSelect);
            _runtimeFileBrowserForms.Add(form);
            form.FormClosed += (sender, e) =>
            {
                _runtimeFileBrowserForms.Remove(form);
            };
            form.PathSelected += async (sender, e) =>
            {
                if (onSelectedAsync == null)
                {
                    return;
                }

                try
                {
                    await onSelectedAsync(e.SelectedPaths);
                }
                catch (Exception ex)
                {
                    SetHighPriorityStatus(UiLocalization.Text("RuntimeFileBrowserForm.Status.SelectionFailed", "Failed to use selected path"));
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            form.Show(this);
            form.Activate();
        }

        private Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListCurrentUnixRuntimeDirectoryAsync(string path)
        {
            return IsRemoteMacRuntimeEnabled()
                ? RunRemoteMacFileSystemOperationWithHostKeyRetryAsync(config => _remoteMacFileSystemBridge.ListDirectoryAsync(config, path))
                : _wslFileSystemBridge.ListDirectoryAsync(GetCurrentWslConfig(), path);
        }

        private async Task OpenSelectedWorkspaceFromListAsync()
        {
            var selectedItem = GetSelectedWorkspaceListItem();
            if (selectedItem == null)
            {
                return;
            }

            if (_currentWorkspace != null &&
                string.Equals(_currentWorkspace.Id, selectedItem.Workspace.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await LoadWorkspaceAsync(selectedItem.Workspace.RootPath, false, true);

            if (_currentSession == null)
            {
                await CreateNewSessionAsync();
            }
        }

        private void UpsertRecentWorkspace(WorkspaceInfo workspace)
        {
            if (workspace == null)
            {
                return;
            }

            for (var i = _recentWorkspaces.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_recentWorkspaces[i].Id, workspace.Id, StringComparison.OrdinalIgnoreCase))
                {
                    _recentWorkspaces.RemoveAt(i);
                }
            }

            _recentWorkspaces.Insert(0, workspace);
        }
    }
}
