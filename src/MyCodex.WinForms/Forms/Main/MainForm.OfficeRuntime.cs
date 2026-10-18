using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly OfficeRuntimeScopeService _officeRuntimeScopeService;
        private readonly OfficeRuntimeStateService _officeRuntimeStateService;
        private readonly IOfficeRuntimeFileSystem _officeRuntimeFileSystem;
        private OfficeRuntimeTarget _activeOfficeRuntimeTarget;
        private ModeSettingsSnapshot _activeModeSettings = new ModeSettingsSnapshot();
        private string _modeSettingsLoadError;
        private string _resolvedOfficeRootPath;
        private string _officeRuntimeRootError;

        private OfficeRuntimeTarget GetActiveOfficeRuntimeTarget()
        {
            var target = _officeRuntimeScopeService.ResolveTarget(_appConfigSnapshot);
            if (_activeOfficeRuntimeTarget == null ||
                !string.Equals(
                    _activeOfficeRuntimeTarget.RuntimeScopeId,
                    target.RuntimeScopeId,
                    StringComparison.OrdinalIgnoreCase))
            {
                _activeOfficeRuntimeTarget = target;
                _activeModeSettings = new ModeSettingsSnapshot();
                _modeSettingsLoadError = null;
                _resolvedOfficeRootPath = null;
                _officeRuntimeRootError = null;
            }

            return _activeOfficeRuntimeTarget;
        }

        private OfficeDirectoryOptionsContext CreateOfficeDirectoryOptionsContext(
            OfficeRuntimeTarget target,
            bool modeSettingsAvailable)
        {
            return new OfficeDirectoryOptionsContext
            {
                RuntimeDisplayName = GetCurrentRuntimeDisplayName(),
                HostPlatform = target.HostPlatform,
                ConfiguredRootPath = _activeModeSettings.OfficeRootPath,
                EffectiveRootPath = _resolvedOfficeRootPath,
                ModeSettingsAvailable = modeSettingsAvailable,
                ModeSettingsError = _modeSettingsLoadError,
                BrowseDirectory = target.UsesUnixPaths
                    ? new Func<IWin32Window, string, string>(BrowseActiveOfficeRuntimeDirectory)
                    : null
            };
        }

        private string BrowseActiveOfficeRuntimeDirectory(IWin32Window owner, string initialPath)
        {
            string selectedPath = null;
            using (var browser = new RuntimeFileBrowserForm(
                BuildRuntimeFileWindowTitle(
                    UiLocalization.Text(
                        "OptionsForm.OfficeRootPath.BrowseDescription",
                        "Select office directory")),
                initialPath,
                RuntimeFileBrowserSelectionMode.Directory,
                path => _officeRuntimeFileSystem.ListDirectoryAsync(_appConfigSnapshot, path)))
            {
                browser.PathSelected += (sender, e) => selectedPath = e.Path;
                browser.ShowDialog(owner);
            }

            return selectedPath;
        }

        private string GetCurrentModeDraftScopeKey()
        {
            return _currentInteractionMode == AppInteractionMode.Office
                ? GetActiveOfficeRuntimeTarget().RuntimeScopeId
                : GetConfiguredRuntimeProfileId();
        }

        private async Task RefreshActiveOfficeRuntimeRootAsync()
        {
            var target = GetActiveOfficeRuntimeTarget();
            var configuredRootPath = _activeModeSettings.OfficeRootPath;
            try
            {
                var resolvedOfficeRootPath = await ResolveAndEnsureOfficeRootAsync(
                    target,
                    configuredRootPath);
                if (!await WaitForTargetExitDecisionAsync()) return;
                _resolvedOfficeRootPath = resolvedOfficeRootPath;
                _officeRuntimeRootError = null;
                SetWorkspaceAvailabilityCache(_resolvedOfficeRootPath, true);
                EnsureCachedDefaultOfficeWorkspace();
            }
            catch (Exception ex)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                _resolvedOfficeRootPath = null;
                _officeRuntimeRootError = UiLocalization.Text(
                    "MainForm.Status.WorkspacePathUnavailable",
                    "Current workspace path is unavailable");
                AppendLog(ex.Message);
            }

            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                RestoreOfficeSelectionAfterStateLoad();
                RefreshWorkspaceView();
                RefreshSessionList();
                RefreshHeader();
                RefreshInputState();
                if (!string.IsNullOrWhiteSpace(_officeRuntimeRootError))
                {
                    SetHighPriorityStatus(_officeRuntimeRootError);
                }
            }
        }

        private async Task<string> ResolveAndEnsureOfficeRootAsync(
            OfficeRuntimeTarget target,
            string configuredRootPath)
        {
            string unixHomePath = null;
            if (target.UsesUnixPaths && RequiresOfficeHomeResolution(configuredRootPath))
            {
                unixHomePath = await _officeRuntimeFileSystem.ResolveHomeDirectoryAsync(_appConfigSnapshot);
            }

            if (!await WaitForTargetExitDecisionAsync()) return null;
            var resolvedRootPath = _officeWorkspaceService.ResolveOfficeRootPath(
                target.HostPlatform,
                configuredRootPath,
                unixHomePath);
            var ensureResult = await _officeRuntimeFileSystem.EnsureDirectoryAsync(
                _appConfigSnapshot,
                resolvedRootPath);
            if (!await WaitForTargetExitDecisionAsync()) return null;
            if (ensureResult == null ||
                !ensureResult.Success ||
                !await _officeRuntimeFileSystem.DirectoryExistsAsync(_appConfigSnapshot, resolvedRootPath))
            {
                throw new InvalidOperationException(
                    ensureResult == null || string.IsNullOrWhiteSpace(ensureResult.Message)
                        ? UiLocalization.Text(
                            "MainForm.Status.WorkspacePathUnavailable",
                            "Current workspace path is unavailable")
                        : ensureResult.Message);
            }

            return resolvedRootPath;
        }

        private async Task<bool> LoadModeSettingsForCurrentRuntimeAsync()
        {
            var previousTargetScopeId = _activeOfficeRuntimeTarget == null
                ? null
                : _activeOfficeRuntimeTarget.RuntimeScopeId;
            var previousModeSettings = _activeModeSettings;
            GetActiveOfficeRuntimeTarget();
            var targetChanged = !string.Equals(
                previousTargetScopeId,
                _activeOfficeRuntimeTarget.RuntimeScopeId,
                StringComparison.OrdinalIgnoreCase);
            try
            {
                var modeSettings = await _modeSettingsStore.LoadForCurrentRuntimeAsync(
                    _appConfigSnapshot).ConfigureAwait(true) ?? new ModeSettingsSnapshot();
                if (!await WaitForTargetExitDecisionAsync()) return false;
                _activeModeSettings = modeSettings;
                _modeSettingsLoadError = null;
                return true;
            }
            catch (Exception ex)
            {
                if (!await WaitForTargetExitDecisionAsync()) return false;
                _activeModeSettings = targetChanged
                    ? new ModeSettingsSnapshot()
                    : previousModeSettings ?? new ModeSettingsSnapshot();
                _modeSettingsLoadError = UiLocalization.Text(
                    "OptionsForm.ModeSettings.Unavailable",
                    "Mode settings are unavailable for the current runtime.");
                AppendLog(ex.Message);
                SetHighPriorityStatus(_modeSettingsLoadError);
                return false;
            }
        }

        private async Task SaveModeSettingsForCurrentRuntimeAsync(ModeSettingsSnapshot snapshot)
        {
            await _modeSettingsStore.SaveForCurrentRuntimeAsync(
                _appConfigSnapshot,
                snapshot).ConfigureAwait(true);
            _activeModeSettings = snapshot ?? new ModeSettingsSnapshot();
            _modeSettingsLoadError = null;
        }

        private static bool RequiresOfficeHomeResolution(string configuredRootPath)
        {
            if (string.IsNullOrWhiteSpace(configuredRootPath))
            {
                return true;
            }

            var value = configuredRootPath.Trim().Trim('"');
            return string.Equals(value, "~", StringComparison.Ordinal) ||
                   value.StartsWith("~/", StringComparison.Ordinal) ||
                   string.Equals(value, "$HOME", StringComparison.Ordinal) ||
                   value.StartsWith("$HOME/", StringComparison.Ordinal);
        }

        private void EnsureCachedDefaultOfficeWorkspace()
        {
            if (string.IsNullOrWhiteSpace(_resolvedOfficeRootPath))
            {
                return;
            }

            var target = GetActiveOfficeRuntimeTarget();
            var workspace = _officeWorkspaceService.CreateOfficeWorkspace(
                target.RuntimeScopeId,
                target.HostPlatform,
                _resolvedOfficeRootPath);
            workspace.Name = UiLocalization.Text("MainForm.Title.Mode.Office", "Office Mode");
            var previousWorkspaceIds = new List<string>();
            foreach (var existing in _recentWorkspaces)
            {
                if (existing != null &&
                    existing.Kind == WorkspaceKind.Office &&
                    !string.IsNullOrWhiteSpace(existing.Id))
                {
                    previousWorkspaceIds.Add(existing.Id);
                }
            }

            foreach (var previousWorkspaceId in previousWorkspaceIds)
            {
                UpdateOfficeSessionWorkspaceId(previousWorkspaceId, workspace.Id);
            }

            _recentWorkspaces.RemoveAll(item => item != null && item.Kind == WorkspaceKind.Office);
            _recentWorkspaces.Add(workspace);
            _currentOfficeWorkspaceId = workspace.Id;
            if (_currentWorkspace != null && _currentWorkspace.Kind == WorkspaceKind.Office)
            {
                _currentWorkspace = workspace;
            }
        }

        private void AddActiveRuntimeWorkspaces(
            IEnumerable<WorkspaceInfo> codingWorkspaces,
            IEnumerable<WorkspaceInfo> officeWorkspaces)
        {
            if (codingWorkspaces != null)
            {
                foreach (var workspace in codingWorkspaces)
                {
                    if (workspace == null ||
                        workspace.Kind == WorkspaceKind.Office ||
                        string.IsNullOrWhiteSpace(workspace.RootPath) ||
                        !IsWorkspacePathCompatibleWithCurrentRuntime(workspace.RootPath))
                    {
                        continue;
                    }

                    try
                    {
                        var restored = CreateWorkspaceFromPath(workspace.RootPath, false);
                        restored.LastOpenedAt = workspace.LastOpenedAt;
                        AddWorkspaceIfMissing(restored);
                    }
                    catch
                    {
                    }
                }
            }

            if (officeWorkspaces == null)
            {
                return;
            }

            foreach (var workspace in CloneOfficeWorkspaces(officeWorkspaces))
            {
                AddWorkspaceIfMissing(workspace);
            }
        }

        private void AddWorkspaceIfMissing(WorkspaceInfo workspace)
        {
            if (workspace == null || FindWorkspaceById(workspace.Id) != null)
            {
                return;
            }

            _recentWorkspaces.Add(workspace);
        }

        private List<OfficeRuntimeStateSnapshot> BuildOfficeRuntimeStates(string activeOfficeRuntimeScopeId)
        {
            var result = new List<OfficeRuntimeStateSnapshot>();
            if (_appStateSnapshot != null && _appStateSnapshot.OfficeRuntimeStates != null)
            {
                foreach (var state in _appStateSnapshot.OfficeRuntimeStates)
                {
                    if (state == null ||
                        string.IsNullOrWhiteSpace(state.RuntimeScopeId) ||
                        string.Equals(
                            state.RuntimeScopeId,
                            activeOfficeRuntimeScopeId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    result.Add(CloneOfficeRuntimeState(state));
                }
            }

            result.Add(new OfficeRuntimeStateSnapshot
            {
                RuntimeScopeId = activeOfficeRuntimeScopeId,
                RecentWorkspaces = CloneOfficeWorkspaces(_recentWorkspaces),
                CurrentWorkspaceId = _currentOfficeWorkspaceId,
                CurrentSessionId = _currentOfficeSessionId
            });
            return result;
        }

        private static OfficeRuntimeStateSnapshot CloneOfficeRuntimeState(OfficeRuntimeStateSnapshot state)
        {
            return new OfficeRuntimeStateSnapshot
            {
                RuntimeScopeId = state.RuntimeScopeId,
                RecentWorkspaces = CloneOfficeWorkspaces(state.RecentWorkspaces),
                CurrentWorkspaceId = state.CurrentWorkspaceId,
                CurrentSessionId = state.CurrentSessionId
            };
        }

        private static List<WorkspaceInfo> CloneCodingWorkspaces(IEnumerable<WorkspaceInfo> workspaces)
        {
            return CloneWorkspacesByKind(workspaces, WorkspaceKind.Coding);
        }

        private static List<WorkspaceInfo> CloneOfficeWorkspaces(IEnumerable<WorkspaceInfo> workspaces)
        {
            return CloneWorkspacesByKind(workspaces, WorkspaceKind.Office);
        }

        private static List<WorkspaceInfo> CloneWorkspacesByKind(
            IEnumerable<WorkspaceInfo> workspaces,
            WorkspaceKind kind)
        {
            var result = new List<WorkspaceInfo>();
            if (workspaces == null)
            {
                return result;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace == null || workspace.Kind != kind)
                {
                    continue;
                }

                result.Add(new WorkspaceInfo
                {
                    Id = workspace.Id,
                    Name = workspace.Name,
                    RootPath = workspace.RootPath,
                    GitBranch = workspace.GitBranch,
                    Kind = workspace.Kind,
                    LastOpenedAt = workspace.LastOpenedAt
                });
            }

            return result;
        }

        private void EnsureActiveOfficeSessionScope(ChatSession session)
        {
            if (session != null && session.Mode == AppInteractionMode.Office)
            {
                session.OfficeRuntimeScopeId = GetActiveOfficeRuntimeTarget().RuntimeScopeId;
            }
        }

        private void EnsureActiveOfficeSessionScopes()
        {
            foreach (var session in _sessions)
            {
                EnsureActiveOfficeSessionScope(session);
            }
        }
    }
}
