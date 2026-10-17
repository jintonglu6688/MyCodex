using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
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
        private async void OnSyncThreadsMenuItemClick(object sender, EventArgs e)
        {
            var selectedWorkspace = GetSelectedWorkspaceListItem();
            await ShowThreadImportDialogAsync(false, selectedWorkspace != null ? selectedWorkspace.Workspace : _currentWorkspace);
        }

        private async void OnWorkspaceRestoreArchivedMenuItemClick(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedWorkspaceListItem();
            if (selectedItem == null)
            {
                return;
            }

            await ShowArchivedThreadRestoreDialogAsync(selectedItem.Workspace);
        }

        private async void AutomaticThreadSyncTimer_Tick(object sender, EventArgs e)
        {
            await RunAutomaticThreadSyncAsync();
        }

        private async void AutomaticThreadSyncFollowUpTimer_Tick(object sender, EventArgs e)
        {
            _automaticThreadSyncFollowUpTimer.Stop();
            await RunLocalCurrentSubAgentThreadSyncAsync();
            await RunAutomaticThreadSyncAsync(false);
            if (IsDisposed || Disposing)
            {
                return;
            }

            ScheduleAutomaticThreadSyncFollowUpTimer();
        }

        private void ScheduleAutomaticThreadSyncFollowUp()
        {
            _automaticThreadSyncFollowUpTracker.RequestFollowUp();
            ScheduleAutomaticThreadSyncFollowUpTimer();
        }

        private void ScheduleAutomaticThreadSyncFollowUpTimer()
        {
            int delayMilliseconds;
            if (!_automaticThreadSyncFollowUpTracker.TryConsumeNextDelay(out delayMilliseconds))
            {
                _automaticThreadSyncFollowUpTimer.Stop();
                return;
            }

            _automaticThreadSyncFollowUpTimer.Stop();
            _automaticThreadSyncFollowUpTimer.Interval = delayMilliseconds;
            _automaticThreadSyncFollowUpTimer.Start();
        }

        private void ScheduleLocalThreadSessionFileSync()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(ScheduleLocalThreadSessionFileSync));
                }
                catch
                {
                    // The window may be closing while a filesystem notification is in flight.
                }

                return;
            }

            _localThreadSessionWatchDebounceTimer.Stop();
            _localThreadSessionWatchDebounceTimer.Start();
        }

        private async Task RefreshLocalThreadSessionWatchersAsync()
        {
            var localThreadCatalogClient = _appServerClient as IAppServerLocalThreadCatalogClient;
            if (localThreadCatalogClient == null)
            {
                return;
            }

            try
            {
                var codexHomePaths = await localThreadCatalogClient.ListLocalCodexHomePathsForThreadScanAsync();
                if (!await WaitForTargetExitDecisionAsync()) return;
                var watchRoots = LocalThreadSessionWatchPlanner.BuildWatchRoots(codexHomePaths, Directory.Exists);
                ConfigureLocalThreadSessionWatchers(watchRoots);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to refresh local thread session watchers: {0}", ex.Message);
            }
        }

        private void ConfigureLocalThreadSessionWatchers(IEnumerable<string> watchRoots)
        {
            DisposeLocalThreadSessionWatchers();
            if (watchRoots == null)
            {
                return;
            }

            foreach (var watchRoot in watchRoots)
            {
                try
                {
                    var watcher = new FileSystemWatcher(watchRoot, "*.jsonl")
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName |
                                       NotifyFilters.LastWrite |
                                       NotifyFilters.CreationTime |
                                       NotifyFilters.Size
                    };
                    watcher.Created += LocalThreadSessionFileChanged;
                    watcher.Changed += LocalThreadSessionFileChanged;
                    watcher.Renamed += LocalThreadSessionFileRenamed;
                    watcher.Error += LocalThreadSessionWatcherError;
                    watcher.EnableRaisingEvents = true;
                    _localThreadSessionWatchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Failed to watch local Codex session root {0}: {1}", watchRoot, ex.Message);
                }
            }
        }

        private void DisposeLocalThreadSessionWatchers()
        {
            foreach (var watcher in _localThreadSessionWatchers.ToArray())
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Created -= LocalThreadSessionFileChanged;
                    watcher.Changed -= LocalThreadSessionFileChanged;
                    watcher.Renamed -= LocalThreadSessionFileRenamed;
                    watcher.Error -= LocalThreadSessionWatcherError;
                    watcher.Dispose();
                }
                catch
                {
                    // Disposing a watcher can race with native change notifications during shutdown.
                }
            }

            _localThreadSessionWatchers.Clear();
        }

        private void LocalThreadSessionFileChanged(object sender, FileSystemEventArgs e)
        {
            if (!IsLocalThreadSessionFile(e.FullPath))
            {
                return;
            }

            ScheduleLocalThreadSessionFileSync();
        }

        private void LocalThreadSessionFileRenamed(object sender, RenamedEventArgs e)
        {
            if (!IsLocalThreadSessionFile(e.FullPath))
            {
                return;
            }

            ScheduleLocalThreadSessionFileSync();
        }

        private void LocalThreadSessionWatcherError(object sender, ErrorEventArgs e)
        {
            ScheduleLocalThreadSessionFileSync();
        }

        private static bool IsLocalThreadSessionFile(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase);
        }

        private async void LocalThreadSessionWatchDebounceTimer_Tick(object sender, EventArgs e)
        {
            _localThreadSessionWatchDebounceTimer.Stop();
            await RunLocalCurrentSubAgentThreadSyncAsync();
            await RunAutomaticThreadSyncAsync(false, true);
        }

        private async void LocalSubAgentThreadSyncTimer_Tick(object sender, EventArgs e)
        {
            await RunLocalCurrentSubAgentThreadSyncAsync();
        }

        private async Task RunInitialThreadImportFlowAsync()
        {
            if (_hasCompletedThreadImportPrompt)
            {
                return;
            }

            _hasCompletedThreadImportPrompt = true;
            PersistState();

            var result = MessageBox.Show(
                this,
                UiLocalization.Text(
                    "MainForm.Confirm.ImportExistingHistorySessions",
                    "Sync existing local history sessions now? After syncing, MyCodex can continue sessions already present in CLI and the official app."),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.InitialHistorySyncSkipped", "Skipped initial history session sync"));
                return;
            }

            await ShowThreadImportDialogAsync(true);
        }

        private async Task ShowThreadAssociationDialogAsync(WorkspaceInfo targetWorkspace)
        {
            if (targetWorkspace == null || string.IsNullOrWhiteSpace(targetWorkspace.RootPath))
            {
                return;
            }

            List<AppServerThreadSummary> selectedThreads;
            using (var form = new ThreadAssociationForm(
                _appServerClient,
                _sessions,
                targetWorkspace,
                _projectInstructionsHelperThreadIds))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                selectedThreads = form.GetSelectedThreads();
            }

            if (selectedThreads == null || selectedThreads.Count == 0)
            {
                SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.NoSessionsImported", "No sessions were imported"));
                return;
            }

            ThreadAssociationMergeResult mergeResult;
            try
            {
                mergeResult = await ThreadAssociationSessionMerger.ImportThreadsAsync(
                    selectedThreads,
                    _sessions,
                    targetWorkspace,
                    _sessionService,
                    _appServerClient,
                    _sessionThreadService);
            }
            catch (Exception ex)
            {
                SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.ImportSessionsFailed", "Failed to import selected sessions."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!mergeResult.HasAssociatedSessions)
            {
                SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.NoSessionsImported", "No sessions were imported"));
                return;
            }

            _currentWorkspace = targetWorkspace;
            _currentSession = mergeResult.FirstAssociatedSession;
            SortSessions();
            ApplyComposeState(_currentSession);
            ApplyMainFormRefreshPreset(MainFormRefreshPreset.WorkspaceSessionHeaderInputPersist);
            SetLowPriorityStatus(string.Format(
                UiLocalization.Text("MainForm.Status.SessionsImported.Template", "Imported {0} session(s) into current workspace"),
                mergeResult.AssociatedThreadCount));
            await LoadSessionHistoryAsync(_currentSession, true);
        }

        private async Task ShowThreadImportDialogAsync(bool isInitialImport)
        {
            await ShowThreadImportDialogAsync(isInitialImport, _currentWorkspace);
        }

        private async Task ShowThreadImportDialogAsync(bool isInitialImport, WorkspaceInfo preferredWorkspace)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.SyncHistorySessions", "Sync history sessions")))
            {
                return;
            }

            List<AppServerThreadSummary> selectedThreads;
            using (var form = new ThreadImportForm(
                _appServerClient,
                GetImportedThreadIds(),
                preferredWorkspace != null ? preferredWorkspace.RootPath : null,
                IsWorkspacePathAvailable,
                ShouldShowThreadForCurrentInteractionMode))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                selectedThreads = form.GetSelectedThreads();
            }

            if (selectedThreads == null || selectedThreads.Count == 0)
            {
                var noSelectionPlan = ThreadImportWorkflowPlanner.BuildManualNoSelection(isInitialImport);
                SetLowPriorityStatus(noSelectionPlan.StatusText);
                return;
            }

            selectedThreads = FilterThreadsForCurrentInteractionMode(selectedThreads);
            if (selectedThreads.Count == 0)
            {
                var noSelectionPlan = ThreadImportWorkflowPlanner.BuildManualNoSelection(isInitialImport);
                SetLowPriorityStatus(noSelectionPlan.StatusText);
                return;
            }

            await PreloadThreadWorkspaceGitInfoAsync(selectedThreads);
            ImportThreads(selectedThreads);
            await RefreshAfterImportedThreadsAsync(true);

            var completionPlan = ThreadImportWorkflowPlanner.BuildManualSuccess(selectedThreads.Count, DateTime.Now);
            UpdateThreadSyncSummary(completionPlan.SummaryText);
            SetLowPriorityStatus(completionPlan.StatusText);
        }

        private async Task RefreshAfterImportedThreadsAsync(bool loadCurrentSessionHistory)
        {
            ApplyMainFormRefreshPreset(MainFormRefreshPreset.WorkspaceSessionHeaderInputPersist);

            if (_currentSession == null)
            {
                RestoreCurrentSession();
            }

            if (loadCurrentSessionHistory && _currentSession != null)
            {
                await LoadSessionHistoryAsync(_currentSession, true);
            }
        }

        private async Task ShowArchivedThreadRestoreDialogAsync(WorkspaceInfo workspace)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.ReadArchivedSessions", "Read archived sessions")))
            {
                return;
            }

            List<AppServerThreadSummary> selectedThreads;
            using (var form = new ArchivedThreadRestoreForm(
                _appServerClient,
                GetImportedThreadIds(),
                workspace != null ? workspace.RootPath : null,
                IsWorkspacePathAvailable))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                selectedThreads = form.GetSelectedThreads();
            }

            if (selectedThreads == null || selectedThreads.Count == 0)
            {
                SetStatus(UiLocalization.Text("MainForm.Status.NoArchivedSessionsSelectedForRestore", "No archived sessions selected for restore"));
                return;
            }

            await RestoreArchivedThreadsAsync(selectedThreads);
        }

        private async Task RestoreArchivedThreadsAsync(List<AppServerThreadSummary> selectedThreads)
        {
            if (selectedThreads == null || selectedThreads.Count == 0)
            {
                return;
            }

            SetStatus(string.Format(UiLocalization.Text("MainForm.Status.RestoringArchivedSessions.Template", "Restoring {0} archived session(s)..."), selectedThreads.Count));

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.RestoreArchivedSessions", "Restore archived sessions")))
            {
                return;
            }

            var restoreResult = await _archivedThreadRestoreWorkflow.RestoreAsync(selectedThreads, BuildThreadDisplayTitle);
            var completionPlan = restoreResult.CompletionPlan;
            if (!restoreResult.HasRestorableThreads)
            {
                MessageBox.Show(this, completionPlan.EmptyRestoreMessage, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                SetHighPriorityStatus(completionPlan.StatusText);
                return;
            }

            var selectedWorkspaceId = _currentWorkspace != null ? _currentWorkspace.Id : null;
            var selectedSessionId = _currentSession != null ? _currentSession.Id : null;

            await PreloadThreadWorkspaceGitInfoAsync(restoreResult.RestorableThreads);
            ImportThreads(restoreResult.RestorableThreads, true);
            RefreshWorkspaceView();

            if (restoreResult.RestorableThreads.Count == 1)
            {
                var restoredSession = FindSessionByThreadId(restoreResult.RestorableThreads[0].ThreadId);
                var restoredWorkspace = restoredSession != null ? FindWorkspaceById(restoredSession.WorkspaceId) : null;
                if (restoredWorkspace != null)
                {
                    _currentWorkspace = restoredWorkspace;
                }

                if (restoredSession != null)
                {
                    await ActivateSessionAsync(restoredSession, true);
                }
                else
                {
                    ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderInputPersist);
                }
            }
            else
            {
                RestoreSelection(selectedWorkspaceId, selectedSessionId);
                await RefreshAfterImportedThreadsAsync(false);
            }

            UpdateThreadSyncSummary(completionPlan.SummaryText);
            var statusText = completionPlan.StatusText;
            AddChatHistoryItem(ChatHistoryItemFactory.GetStatusSenderText(), statusText, ChatHistoryCategory.System);
            SetLowPriorityStatus(statusText);

            if (completionPlan.HasFailureDialogMessage)
            {
                MessageBox.Show(
                    this,
                    completionPlan.FailureDialogMessage,
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private bool BelongsToKnownWorkspace(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return false;
            }

            string normalizedPath;
            try
            {
                normalizedPath = _workspaceService.NormalizeWorkspacePath(workspacePath);
            }
            catch
            {
                return false;
            }

            return ThreadImportFilterPlanner.BelongsToKnownWorkspace(
                workspacePath,
                BuildWorkspaceId(normalizedPath),
                _recentWorkspaces,
                _currentInteractionMode,
                ResolveOfficeRootPathForHistorySync(),
                IsSameOrChildPath);
        }

        private List<AppServerThreadSummary> FilterThreadsForCurrentInteractionMode(List<AppServerThreadSummary> threads)
        {
            return ThreadImportWorkflowPlanner.FilterThreadsForInteractionMode(
                threads,
                _currentInteractionMode,
                ResolveOfficeRootPathForHistorySync());
        }

        private bool ShouldShowThreadForCurrentInteractionMode(AppServerThreadSummary thread)
        {
            return FilterThreadsForCurrentInteractionMode(new List<AppServerThreadSummary> { thread }).Count == 1;
        }

        private bool ShouldBootstrapRuntimeEndpointHistorySync()
        {
            return IsConnectedStatus() &&
                !HasImportedSessions();
        }

        private static string BuildWorkspaceId(string normalizedPath)
        {
            return (normalizedPath ?? string.Empty)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLower(CultureInfo.InvariantCulture);
        }

        private void RestoreSelection(string workspaceId, string sessionId)
        {
            var selection = SessionSelectionResolver.RestoreSelection(
                _recentWorkspaces,
                _sessions,
                _currentWorkspace,
                _currentSession,
                workspaceId,
                sessionId);
            _currentWorkspace = selection.Workspace;
            _currentSession = selection.Session;
            UpdateCurrentModeSessionSelection(_currentSession);
        }

        private void ImportThreads(List<AppServerThreadSummary> threads, bool allowMissingWorkspaces = false)
        {
            if (threads == null)
            {
                return;
            }

            threads = FilterImportedProjectInstructionHelperThreads(threads);
            if (threads.Count == 0)
            {
                return;
            }

            var mergeResult = ThreadImportSessionMerger.ImportThreads(
                threads,
                _sessions,
                _workspaceService,
                _sessionService,
                IsWorkspacePathAvailable,
                UpsertRecentWorkspace,
                allowMissingWorkspaces,
                CreateWorkspaceForHistoryImport,
                _currentInteractionMode,
                ResolveOfficeRootPathForHistorySync(),
                CreateAutomationThreadTitleFallbackResolver());
            EnsureActiveOfficeSessionScopes();

            SortSessions();
            if (_currentWorkspace == null && mergeResult.PreferredWorkspace != null)
            {
                _currentWorkspace = mergeResult.PreferredWorkspace;
            }

            if (_currentWorkspace != null && GetWorkspaceSessionCount(_currentWorkspace.Id) > 0)
            {
                RestoreCurrentSession();
            }
            else if (mergeResult.HasPreferredSelection)
            {
                _currentWorkspace = mergeResult.PreferredWorkspace;
                _currentSession = mergeResult.PreferredSession;
                UpdateCurrentModeSessionSelection(_currentSession);
            }
            ApplyComposeState(_currentSession);
        }

        private Func<AppServerThreadSummary, string> CreateAutomationThreadTitleFallbackResolver()
        {
            var automationThreadTitles = BuildAutomationThreadTitleMap();
            if (automationThreadTitles.Count == 0)
            {
                return null;
            }

            return thread =>
            {
                if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId))
                {
                    return null;
                }

                string title;
                return automationThreadTitles.TryGetValue(thread.ThreadId, out title) ? title : null;
            };
        }

        private Dictionary<string, string> BuildAutomationThreadTitleMap()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_automationStore == null)
            {
                return result;
            }

            AutomationStateSnapshot snapshot;
            if (!TryLoadAutomationSnapshot(out snapshot))
            {
                return result;
            }

            if (snapshot == null || snapshot.Definitions == null || snapshot.Runs == null)
            {
                return result;
            }

            var definitionById = snapshot.Definitions
                .Where(item => item != null &&
                               item.Kind != AutomationKind.Thread &&
                               !string.IsNullOrWhiteSpace(item.Id) &&
                               !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var run in snapshot.Runs
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ThreadId))
                .OrderByDescending(item => item.StartedAt ?? item.ScheduledFor ?? DateTime.MinValue))
            {
                if (result.ContainsKey(run.ThreadId))
                {
                    continue;
                }

                AutomationDefinitionSnapshot definition;
                if (!string.IsNullOrWhiteSpace(run.AutomationId) &&
                    definitionById.TryGetValue(run.AutomationId, out definition))
                {
                    result[run.ThreadId] = definition.Name.Trim();
                }
            }

            return result;
        }

        private WorkspaceInfo CreateWorkspaceForHistoryImport(string path, bool requireExisting)
        {
            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                var officeRootPath = ResolveOfficeRootPathForHistorySync();
                var defaultWorkspace = EnsureDefaultOfficeWorkspace(false);
                var target = GetActiveOfficeRuntimeTarget();
                var workspace = _officeWorkspaceService.ResolveOfficeWorkspaceForImport(
                    target.RuntimeScopeId,
                    target.HostPlatform,
                    officeRootPath,
                    path,
                    _recentWorkspaces);
                return defaultWorkspace != null &&
                    string.Equals(workspace.Id, defaultWorkspace.Id, StringComparison.OrdinalIgnoreCase)
                    ? defaultWorkspace
                    : workspace;
            }

            return CreateWorkspaceFromPath(path, requireExisting);
        }

        private List<AppServerThreadSummary> FilterImportedProjectInstructionHelperThreads(List<AppServerThreadSummary> threads)
        {
            return ThreadImportFilterPlanner.FilterProjectInstructionHelperThreads(
                threads,
                _projectInstructionsHelperThreadIds);
        }

        private void RemoveProjectInstructionsHelperSessionLocally(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            var session = FindSessionByThreadId(threadId);
            if (session == null)
            {
                return;
            }

            RemoveSessionLocally(session);
            RefreshSessionList();
            RefreshHeader();
            RefreshInputState();
            PersistState();
        }

        private async Task PreloadThreadWorkspaceGitInfoAsync(IEnumerable<AppServerThreadSummary> threads)
        {
            if (threads == null || !IsUnixRemoteRuntimeEnabled())
            {
                return;
            }

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var thread in threads)
            {
                if (thread == null || string.IsNullOrWhiteSpace(thread.WorkingDirectory))
                {
                    continue;
                }

                string normalizedPath;
                try
                {
                    normalizedPath = _workspaceService.NormalizeWorkspacePath(thread.WorkingDirectory);
                }
                catch
                {
                    continue;
                }

                if (WorkspacePathUtilities.LooksLikeUnixAbsolutePath(normalizedPath))
                {
                    paths.Add(normalizedPath);
                }
            }

            foreach (var path in paths)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                await RefreshUnixWorkspaceGitInfoCacheAsync(path);
                if (!await WaitForTargetExitDecisionAsync()) return;
            }
        }

        private async Task RefreshKnownUnixWorkspaceGitInfoAsync()
        {
            if (!IsUnixRemoteRuntimeEnabled())
            {
                return;
            }

            var paths = new List<string>();
            foreach (var workspace in _recentWorkspaces)
            {
                if (workspace == null || string.IsNullOrWhiteSpace(workspace.RootPath))
                {
                    continue;
                }

                string normalizedPath;
                try
                {
                    normalizedPath = _workspaceService.NormalizeWorkspacePath(workspace.RootPath);
                }
                catch
                {
                    continue;
                }

                if (WorkspacePathUtilities.LooksLikeUnixAbsolutePath(normalizedPath) &&
                    !paths.Contains(normalizedPath, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(normalizedPath);
                }
            }

            foreach (var path in paths)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                await RefreshUnixWorkspaceGitInfoCacheAsync(path);
                if (!await WaitForTargetExitDecisionAsync()) return;
            }

            if (paths.Count > 0)
            {
                ApplyMainFormRefreshPreset(MainFormRefreshPreset.WorkspaceSessionHeaderInputPersist);
            }
        }

        private async Task RefreshUnixWorkspaceGitInfoCacheAsync(string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath) ||
                !IsUnixRuntimePath(normalizedPath))
            {
                return;
            }

            try
            {
                var exists = IsRemoteMacRuntimeEnabled()
                    ? await _remoteMacFileSystemBridge.DirectoryExistsAsync(GetCurrentRemoteMacConfig(), normalizedPath)
                    : await _wslFileSystemBridge.DirectoryExistsAsync(GetCurrentWslConfig(), normalizedPath);
                if (!await WaitForTargetExitDecisionAsync()) return;
                SetWorkspaceAvailabilityCache(normalizedPath, exists);
                if (!exists)
                {
                    return;
                }

                var gitInfo = IsRemoteMacRuntimeEnabled()
                    ? await _remoteMacFileSystemBridge.ReadGitInfoAsync(GetCurrentRemoteMacConfig(), normalizedPath)
                    : await _wslFileSystemBridge.ReadGitInfoAsync(GetCurrentWslConfig(), normalizedPath);
                if (!await WaitForTargetExitDecisionAsync()) return;
                SetWorkspaceGitInfoCache(normalizedPath, gitInfo);
                UpdateWorkspaceGitBranchFromSnapshot(normalizedPath, gitInfo);
            }
            catch (Exception ex)
            {
                AppendLog(UiLocalization.Text("MainForm.Log.WorkspaceGitRefreshFailedPrefix", "Workspace Git refresh failed: ") + ex.Message);
            }
        }

        private void UpdateWorkspaceGitBranchFromSnapshot(string workspacePath, WorkspaceGitInfoSnapshot gitInfo)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return;
            }

            var workspace = FindWorkspaceById(BuildWorkspaceId(workspacePath));
            if (workspace == null)
            {
                return;
            }

            var refreshedWorkspace = _workspaceService.CreateFromPath(
                workspacePath,
                false,
                _ => true,
                _ => gitInfo ?? new WorkspaceGitInfoSnapshot());
            if (string.Equals(workspace.GitBranch, refreshedWorkspace.GitBranch, StringComparison.Ordinal))
            {
                return;
            }

            workspace.GitBranch = refreshedWorkspace.GitBranch;
            if (_currentWorkspace != null &&
                string.Equals(_currentWorkspace.Id, workspace.Id, StringComparison.OrdinalIgnoreCase))
            {
                _currentWorkspace.GitBranch = refreshedWorkspace.GitBranch;
            }
        }

        private async Task RunAutomaticThreadSyncAsync(bool reportNoNewThreads = true, bool forceFullThreadScan = false)
        {
            while (true)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                var shouldBootstrapRuntimeEndpointState = ShouldBootstrapRuntimeEndpointHistorySync();
                var canRun = ThreadImportWorkflowPlanner.ShouldRunAutomaticSync(
                    false,
                    HasImportedSessions(),
                    IsConnectedStatus(),
                    shouldBootstrapRuntimeEndpointState);
                if (!_automaticThreadSyncRunCoordinator.TryBegin(canRun, reportNoNewThreads, forceFullThreadScan))
                {
                    UpdateAutomaticThreadSyncState();
                    return;
                }

                AutomaticThreadSyncResult syncResult = null;
                var shouldRunPendingSync = false;
                try
                {
                    var knownThreadIds = new HashSet<string>(GetImportedThreadIds(), StringComparer.OrdinalIgnoreCase);
                    syncResult = await _automaticThreadSyncWorkflow.RunAsync(
                        knownThreadIds,
                        !forceFullThreadScan && !shouldBootstrapRuntimeEndpointState,
                        IsWorkspacePathAvailable,
                        BelongsToKnownWorkspace,
                        IsAppServerConnectivityFailure,
                        IsConnectedStatus(),
                        DateTime.Now,
                        shouldBootstrapRuntimeEndpointState,
                        _projectInstructionsHelperThreadIds,
                        _currentInteractionMode,
                        ResolveOfficeRootPathForHistorySync());

                    if (!await WaitForTargetExitDecisionAsync()) return;
                    if (syncResult.HasFailure)
                    {
                        UpdateThreadSyncSummary(syncResult.FailurePlan.SummaryText);
                        AppendLog(syncResult.FailurePlan.LogText);
                        if (syncResult.FailurePlan.HasStatusText)
                        {
                            SetHighPriorityStatus(syncResult.FailurePlan.StatusText);
                        }
                    }
                    else
                    {
                        ApplySyncedThreadTitles(syncResult.ThreadSummaries);
                        if (syncResult.HasNewThreads)
                        {
                            var selectedWorkspaceId = _currentWorkspace != null ? _currentWorkspace.Id : null;
                            var selectedSessionId = _currentSession != null ? _currentSession.Id : null;

                            await PreloadThreadWorkspaceGitInfoAsync(syncResult.NewThreads);
                            if (!await WaitForTargetExitDecisionAsync()) return;
                            ImportThreads(syncResult.NewThreads);
                            RestoreSelection(selectedWorkspaceId, selectedSessionId);
                            await RefreshAfterImportedThreadsAsync(false);
                            if (!await WaitForTargetExitDecisionAsync()) return;
                            if (syncResult.CompletionPlan != null)
                            {
                                UpdateThreadSyncSummary(syncResult.CompletionPlan.SummaryText);
                                SetLowPriorityStatus(syncResult.CompletionPlan.StatusText);
                            }
                        }
                    }
                }
                finally
                {
                    try
                    {
                        if (!IsTargetStopping && !IsTargetExitPreparing && syncResult != null &&
                            reportNoNewThreads &&
                            syncResult.CompletedCheck &&
                            syncResult.ShouldReportNoNewThreads &&
                            !syncResult.HasNewThreads)
                        {
                            var noNewPlan = ThreadImportWorkflowPlanner.BuildAutomaticNoNew(DateTime.Now);
                            UpdateThreadSyncSummary(noNewPlan.SummaryText);
                        }
                    }
                    finally
                    {
                        shouldRunPendingSync = _automaticThreadSyncRunCoordinator.CompleteAndTryConsumePending(
                            out reportNoNewThreads,
                            out forceFullThreadScan);
                        UpdateAutomaticThreadSyncState();
                    }
                }

                if (!shouldRunPendingSync)
                {
                    return;
                }
            }
        }

        private void ApplySyncedThreadTitles(IEnumerable<AppServerThreadSummary> threads)
        {
            var updatedSessions = ThreadImportSessionMerger.ApplyThreadTitles(threads, _sessions);
            if (updatedSessions.Count == 0)
            {
                return;
            }

            RefreshSessionList();
            RefreshHeader();
            if (_currentSession != null && updatedSessions.Contains(_currentSession))
            {
                RefreshDisplayedSessionTitle(_currentSession);
            }

            PersistState();
        }
    }
}
