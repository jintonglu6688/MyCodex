using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly RuntimeTargetIdentityService _runtimeTargetIdentityService =
            new RuntimeTargetIdentityService();

        private async Task OpenAppServerRuntimeSettingsAsync()
        {
            if (_targetCatalog != null)
            {
                ShowTargetManager();
                return;
            }
            if (_appConfigSnapshot == null)
            {
                LoadAppConfig();
            }

            var previousSource = _appConfigSnapshot.CodexRuntimeSource;
            var previousCustomExecutablePath = _appConfigSnapshot.CodexCustomExecutablePath;
            var previousWindowsShellPreference = _appConfigSnapshot.WindowsShellPreference;
            var previousWslConfig = CloneWslAppServerConfig(_appConfigSnapshot.WslAppServer);
            var previousRemoteMacConfig = CloneRemoteMacSshConfig(_appConfigSnapshot.RemoteMacSsh);
            var previousRemoteProfiles = CloneRemoteUnixProfiles(_appConfigSnapshot.RemoteUnixSshProfiles);
            var previousSelectedRemoteProfileId = _appConfigSnapshot.SelectedRemoteUnixSshProfileId;
            var previousRuntimeProfileId = _runtimeProfileService.GetCurrentRuntimeProfileId(_appConfigSnapshot);

            using (var form = new WslAppServerSettingsForm(
                _appConfigSnapshot.CodexRuntimeSource,
                _appConfigSnapshot.CodexCustomExecutablePath,
                _appConfigSnapshot.WindowsShellPreference,
                _appConfigSnapshot.WslAppServer,
                _appConfigSnapshot.RemoteMacSsh,
                _appConfigSnapshot.RemoteUnixSshProfiles,
                _appConfigSnapshot.SelectedRemoteUnixSshProfileId,
                _codexRuntimeManager,
                CreateCodexRuntimeUpdateService(),
                _appServerClient,
                text => SpeakAuxiliaryTextWithScreenReader(text)))
            {
                _appServerRuntimeSettingsForm = form;
                try
                {
                    if (form.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    var nextRuntimeSnapshot = new AppConfigSnapshot
                    {
                        CodexRuntimeSource = form.SelectedRuntimeSource,
                        CodexCustomExecutablePath = form.CustomExecutablePath,
                        WindowsShellPreference = form.SelectedWindowsShellPreference,
                        WslAppServer = form.WslConfig,
                        RemoteMacSsh = form.RemoteMacConfig,
                        RemoteUnixSshProfiles = form.RemoteUnixProfiles,
                        SelectedRemoteUnixSshProfileId = form.SelectedRemoteUnixProfileId
                    };
                    var previousRuntimeTargetIdentity = _runtimeTargetIdentityService.ResolveIdentity(_appConfigSnapshot);
                    var nextRuntimeTargetIdentity = _runtimeTargetIdentityService.ResolveIdentity(nextRuntimeSnapshot);
                    var runtimeTargetChanged = !string.Equals(
                        previousRuntimeTargetIdentity,
                        nextRuntimeTargetIdentity,
                        StringComparison.Ordinal);
                    var requiresRestart = (form.BundledRuntimeUpdated &&
                        CodexRuntimeSources.IsBundledFamily(form.SelectedRuntimeSource)) ||
                        RequiresAppServerRuntimeRestart(
                            previousSource,
                            previousCustomExecutablePath,
                            previousWindowsShellPreference,
                            previousWslConfig,
                            previousRemoteMacConfig,
                            previousRemoteProfiles,
                            previousSelectedRemoteProfileId,
                            form.SelectedRuntimeSource,
                            form.CustomExecutablePath,
                            form.SelectedWindowsShellPreference,
                            form.WslConfig,
                            form.RemoteMacConfig,
                            form.RemoteUnixProfiles,
                            form.SelectedRemoteUnixProfileId);

                    if (runtimeTargetChanged &&
                        (!PreflightRuntimeWindowsForTransition() || !ConfirmRuntimeTargetChange()))
                    {
                        return;
                    }

                    var wasConnectedForRestartPrompt = requiresRestart &&
                        !runtimeTargetChanged &&
                        IsConnectedStatus();
                    var shouldShowRemoteSwitchWait = ShouldShowRemoteRuntimeSwitchWait(previousSource, form.SelectedRuntimeSource);
                    var restartNow = DialogResult.No;
                    if (wasConnectedForRestartPrompt)
                    {
                        restartNow = MessageBox.Show(
                            this,
                            UiLocalization.Text(
                                "MainForm.SessionSettings.Message.RestartAppServerNow",
                                "Save App Server runtime settings and restart Codex App Server now? In-progress responses or commands will be interrupted."),
                            AppConstants.AppName,
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button1);
                    }

                    if (!runtimeTargetChanged &&
                        restartNow == DialogResult.Yes &&
                        !PreflightRuntimeWindowsForTransition())
                    {
                        return;
                    }

                    var selectedMode = _currentInteractionMode;
                    SaveCurrentComposeState();
                    CaptureCurrentModeSelection();
                    PersistState();
                    _appConfigSnapshot.CodexRuntimeSource = form.SelectedRuntimeSource;
                    _appConfigSnapshot.CodexCustomExecutablePath = form.CustomExecutablePath;
                    _appConfigSnapshot.WindowsShellPreference = form.SelectedWindowsShellPreference;
                    _appConfigSnapshot.WslAppServer = form.WslConfig;
                    _appConfigSnapshot.RemoteMacSsh = form.RemoteMacConfig;
                    _appConfigSnapshot.RemoteUnixSshProfiles = form.RemoteUnixProfiles;
                    _appConfigSnapshot.SelectedRemoteUnixSshProfileId = form.SelectedRemoteUnixProfileId;
                    if (!SaveAppConfig())
                    {
                        _appConfigSnapshot.CodexRuntimeSource = previousSource;
                        _appConfigSnapshot.CodexCustomExecutablePath = previousCustomExecutablePath;
                        _appConfigSnapshot.WindowsShellPreference = previousWindowsShellPreference;
                        _appConfigSnapshot.WslAppServer = previousWslConfig;
                        _appConfigSnapshot.RemoteMacSsh = previousRemoteMacConfig;
                        _appConfigSnapshot.RemoteUnixSshProfiles = previousRemoteProfiles;
                        _appConfigSnapshot.SelectedRemoteUnixSshProfileId = previousSelectedRemoteProfileId;
                        return;
                    }

                    _globalServerConfigSnapshot = null;

                    if (!await EnsureBundledRuntimeUpdatedAsync()) return;

                    if (runtimeTargetChanged)
                    {
                        Func<Task> switchTarget = () => SwitchRuntimeTargetAfterSaveAsync(
                            selectedMode,
                            form.UpdatedBundledRuntimeVersion,
                            form.UpdatedBundledRuntimeTargetTriple);
                        if (shouldShowRemoteSwitchWait)
                        {
                            await RunRemoteRuntimeSwitchOperationAsync(switchTarget);
                        }
                        else
                        {
                            await switchTarget();
                        }

                        return;
                    }

                    if (!requiresRestart)
                    {
                        if (shouldShowRemoteSwitchWait)
                        {
                            await RunRemoteRuntimeSwitchOperationAsync(() => ReloadStateAfterRuntimeSettingsSaveAsync(previousRuntimeProfileId));
                        }
                        else
                        {
                            await ReloadStateAfterRuntimeSettingsSaveAsync(previousRuntimeProfileId);
                        }

                        SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.AppServerRuntimeSaved", "App Server runtime settings saved"));
                        return;
                    }

                    if (!wasConnectedForRestartPrompt)
                    {
                        if (shouldShowRemoteSwitchWait)
                        {
                            await RunRemoteRuntimeSwitchOperationAsync(() => ReloadStateAfterRuntimeSettingsSaveAsync(previousRuntimeProfileId));
                        }
                        else
                        {
                            await ReloadStateAfterRuntimeSettingsSaveAsync(previousRuntimeProfileId);
                        }

                        SetStatus(UiLocalization.Text(
                            "MainForm.SessionSettings.Status.AppServerRuntimeSavedNextConnect",
                            "App Server runtime settings saved. The next connection will use the new runtime."));
                        return;
                    }

                    if (restartNow != DialogResult.Yes)
                    {
                        if (shouldShowRemoteSwitchWait)
                        {
                            await RunRemoteRuntimeSwitchOperationAsync(() => ReloadStateAfterRuntimeSettingsSaveAsync(previousRuntimeProfileId));
                        }
                        else
                        {
                            await ReloadStateAfterRuntimeSettingsSaveAsync(previousRuntimeProfileId);
                        }

                        SetStatus(UiLocalization.Text(
                            "MainForm.SessionSettings.Status.AppServerRuntimeSavedRestartLater",
                            "App Server runtime settings saved. Restart MyCodex or reconnect App Server to apply them."));
                        return;
                    }

                    if (shouldShowRemoteSwitchWait)
                    {
                        await RunRemoteRuntimeSwitchOperationAsync(() => RestartCurrentRuntimeAfterSaveAsync(
                            selectedMode,
                            form.UpdatedBundledRuntimeVersion,
                            form.UpdatedBundledRuntimeTargetTriple));
                    }
                    else
                    {
                        await RestartCurrentRuntimeAfterSaveAsync(
                            selectedMode,
                            form.UpdatedBundledRuntimeVersion,
                            form.UpdatedBundledRuntimeTargetTriple);
                    }
                }
                finally
                {
                    _appServerRuntimeSettingsForm = null;
                }
            }
        }

        private bool ConfirmRuntimeTargetChange()
        {
            return MessageBox.Show(
                this,
                UiLocalization.Text(
                    "MainForm.SessionSettings.Message.ConfirmRuntimeTargetChange",
                    "Switch runtime target now? All active Coding and Office responses, commands, and automations on the current runtime will stop."),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private async Task SwitchRuntimeTargetAfterSaveAsync(
            MyCodex.Domain.Enums.AppInteractionMode selectedMode,
            string updatedBundledRuntimeVersion,
            string updatedBundledRuntimeTargetTriple)
        {
            var reason = UiLocalization.Text(
                "MainForm.SessionSettings.Status.RuntimeTargetChangedActivityStopped",
                "Runtime target changed. Active Coding and Office work was stopped.");
            await StopAndRestartRuntimeAfterSaveAsync(
                selectedMode,
                reason,
                true,
                updatedBundledRuntimeVersion,
                updatedBundledRuntimeTargetTriple);
        }

        private async Task RestartCurrentRuntimeAfterSaveAsync(
            MyCodex.Domain.Enums.AppInteractionMode selectedMode,
            string updatedBundledRuntimeVersion,
            string updatedBundledRuntimeTargetTriple)
        {
            var reason = UiLocalization.Text(
                "MainForm.SessionSettings.Status.RuntimeRestartedActivityStopped",
                "Runtime restarted. Active Coding and Office work was stopped.");
            await StopAndRestartRuntimeAfterSaveAsync(
                selectedMode,
                reason,
                false,
                updatedBundledRuntimeVersion,
                updatedBundledRuntimeTargetTriple);
        }

        private async Task StopAndRestartRuntimeAfterSaveAsync(
            MyCodex.Domain.Enums.AppInteractionMode selectedMode,
            string reason,
            bool useTargetDefaultMode,
            string updatedBundledRuntimeVersion,
            string updatedBundledRuntimeTargetTriple)
        {
            _isRuntimeWindowTransitionInProgress = true;
            try
            {
                await StopCurrentRuntimeActivityAsync(reason);
                await RestartAppServerForRuntimeSettingsAsync(
                    selectedMode,
                    useTargetDefaultMode,
                    updatedBundledRuntimeVersion,
                    updatedBundledRuntimeTargetTriple);
            }
            finally
            {
                _isRuntimeWindowTransitionInProgress = false;
            }
        }

        private bool ShouldShowRemoteRuntimeSwitchWait(string previousSource, string nextSource)
        {
            return !CodexRuntimeSources.IsRemoteUnixSsh(CodexRuntimeSources.CanonicalizeForExecution(previousSource)) &&
                CodexRuntimeSources.IsRemoteUnixSsh(CodexRuntimeSources.CanonicalizeForExecution(nextSource));
        }

        private async Task RunRemoteRuntimeSwitchOperationAsync(Func<Task> operation)
        {
            using (var form = new RuntimeSwitchWaitForm(
                UiLocalization.Text("MainForm.SessionSettings.RemoteSwitchWait.Title", "Connecting Remote Runtime"),
                UiLocalization.Text(
                    "MainForm.SessionSettings.RemoteSwitchWait.Message",
                    "Connecting to the remote runtime and loading its MyCodex configuration. Please wait..."),
                operation))
            {
                form.ShowDialog(this);
                if (form.OperationException != null)
                {
                    throw form.OperationException;
                }
            }

            await Task.CompletedTask;
        }

        private async Task ReloadStateAfterRuntimeSettingsSaveAsync(string previousRuntimeProfileId)
        {
            var nextRuntimeProfileId = _runtimeProfileService.GetCurrentRuntimeProfileId(_appConfigSnapshot);
            if (!_runtimeProfileService.IsSameRuntimeProfile(previousRuntimeProfileId, nextRuntimeProfileId))
            {
                await ReloadLocalStateForCurrentRuntimeProfileAsync(false);
                return;
            }

            RefreshWindowTitle();
        }

        private async Task RestartAppServerForRuntimeSettingsAsync(
            MyCodex.Domain.Enums.AppInteractionMode selectedMode,
            bool useTargetDefaultMode,
            string updatedBundledRuntimeVersion = null,
            string updatedBundledRuntimeTargetTriple = null)
        {
            _authenticationCaptureCancellation?.Cancel();
            _connectionStatus = GetConnectionStatusReconnectingText();
            RefreshHeader();
            RefreshInputState();
            SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.RestartingAppServer", "Restarting App Server..."));

            var nextMode = selectedMode;
            if (useTargetDefaultMode && await LoadModeSettingsForCurrentRuntimeAsync())
            {
                nextMode = _activeModeSettings.DefaultStartupMode;
            }

            _currentInteractionMode = nextMode;

            try
            {
                if (!await EnsurePendingBundledRuntimeUpdateAsync()) return;
                await EnsureLocalAuthenticationBackendBeforeConnectAsync();
                if (_isExitRequested) return;
                await _appServerClient.RestartAsync();
                CleanupConnectedBundledRuntimeUpdate();
                _connectionStatus = GetConnectionStatusConnectedText();
                _ = CaptureGlobalAuthenticationAfterConnectAsync();
                await ReloadLocalStateForCurrentRuntimeProfileAsync(true);
                await RefreshActiveOfficeRuntimeRootAsync();
                await EnsureMyCodexOfficialMarketplaceRegisteredAsync();
                await RefreshAuthenticationStateAsync(false, false);
                await LoadAvailableModelsAsync(false, false);
                await RefreshOfficialConfigStateAfterConnectAsync();
                await RunAutomaticThreadSyncAsync();
                SelectInteractionModeTab(nextMode);
                _automationRuntimeHost?.Start();
                SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.AppServerRestarted", "App Server restarted"));
            }
            catch (Exception ex)
            {
                _connectionStatus = GetConnectionStatusDisconnectedText();
                try
                {
                    await ReloadLocalStateForCurrentRuntimeProfileAsync(true);
                }
                catch (Exception reloadException)
                {
                    AppendLog(reloadException.Message);
                }

                SelectInteractionModeTab(nextMode);
                RefreshHeader();
                RefreshInputState();
                AppendLog(string.Format(
                    UiLocalization.Text("MainForm.SessionSettings.Log.RestartAppServerFailed.Template", "Failed to restart App Server: {0}"),
                    ex.Message));
                SetHighPriorityStatus(UiLocalization.Text(
                    "MainForm.SessionSettings.Status.AppServerRestartFailed",
                    "App Server restart failed. Check the runtime settings and try again."));
            }
        }

        private void TryCleanupObsoleteBundledRuntimeVersions(string updatedVersion, string targetTriple)
        {
            if (_appConfigSnapshot == null ||
                !string.Equals(_appConfigSnapshot.CodexRuntimeSource, CodexRuntimeSources.Bundled, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var runtimeStatus = _codexRuntimeManager.GetStatus(_appConfigSnapshot);
                if (runtimeStatus == null ||
                    !runtimeStatus.CanLaunch ||
                    !string.Equals(runtimeStatus.EffectiveSource, CodexRuntimeSources.Bundled, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var service = CreateCodexRuntimeUpdateService();
                if (string.IsNullOrWhiteSpace(updatedVersion) || string.IsNullOrWhiteSpace(targetTriple))
                {
                    var installed = service.GetInstalledInfo();
                    updatedVersion = installed.InstalledVersion;
                    targetTriple = installed.TargetTriple;
                }
                if (string.IsNullOrWhiteSpace(updatedVersion) ||
                    !string.Equals(runtimeStatus.Version, updatedVersion, StringComparison.OrdinalIgnoreCase)) return;

                var removedVersions = service.RemoveObsoleteInstalledVersions(updatedVersion, targetTriple);
                if (removedVersions != null && removedVersions.Count > 0)
                {
                    AppendLog(string.Format(
                        UiLocalization.Text(
                            "MainForm.SessionSettings.Log.BundledRuntimeCleanupRemoved.Template",
                            "Removed older bundled Codex runtimes: {0}"),
                        string.Join(", ", removedVersions)));
                }
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(
                    UiLocalization.Text(
                        "MainForm.SessionSettings.Log.BundledRuntimeCleanupFailed.Template",
                        "Failed to clean older bundled Codex runtimes: {0}"),
                    ex.Message));
            }
        }

        private static bool RequiresAppServerRuntimeRestart(
            string previousSource,
            string previousCustomExecutablePath,
            string previousWindowsShellPreference,
            WslAppServerConfigSnapshot previousWslConfig,
            RemoteMacSshConfigSnapshot previousRemoteMacConfig,
            IList<RemoteUnixSshProfileConfigSnapshot> previousRemoteProfiles,
            string previousSelectedRemoteProfileId,
            string nextSource,
            string nextCustomExecutablePath,
            string nextWindowsShellPreference,
            WslAppServerConfigSnapshot nextWslConfig,
            RemoteMacSshConfigSnapshot nextRemoteMacConfig,
            IList<RemoteUnixSshProfileConfigSnapshot> nextRemoteProfiles,
            string nextSelectedRemoteProfileId)
        {
            var normalizedPreviousSource = CodexRuntimeSources.CanonicalizeForExecution(previousSource);
            var normalizedNextSource = CodexRuntimeSources.CanonicalizeForExecution(nextSource);

            if (!string.Equals(normalizedPreviousSource, normalizedNextSource, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(normalizedNextSource, CodexRuntimeSources.Custom, StringComparison.OrdinalIgnoreCase))
            {
                return !string.Equals(
                    NormalizeOptionalPath(previousCustomExecutablePath),
                    NormalizeOptionalPath(nextCustomExecutablePath),
                    StringComparison.OrdinalIgnoreCase) ||
                    !AreWindowsShellPreferencesEqual(previousWindowsShellPreference, nextWindowsShellPreference);
            }

            if (CodexRuntimeSources.IsBundledFamily(normalizedNextSource) ||
                string.Equals(normalizedNextSource, CodexRuntimeSources.System, StringComparison.OrdinalIgnoreCase))
            {
                return !AreWindowsShellPreferencesEqual(previousWindowsShellPreference, nextWindowsShellPreference);
            }

            if (string.Equals(normalizedNextSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase))
            {
                return !AreWslAppServerConfigsEqual(previousWslConfig, nextWslConfig);
            }

            if (CodexRuntimeSources.IsRemoteUnixSsh(normalizedNextSource))
            {
                return !AreRemoteMacSshConfigsEqual(
                    GetSelectedRemoteUnixConfig(previousRemoteProfiles, previousSelectedRemoteProfileId, previousRemoteMacConfig),
                    GetSelectedRemoteUnixConfig(nextRemoteProfiles, nextSelectedRemoteProfileId, nextRemoteMacConfig));
            }

            return false;
        }

        private static bool AreWindowsShellPreferencesEqual(string left, string right)
        {
            return string.Equals(
                WindowsShellPreferences.Normalize(left),
                WindowsShellPreferences.Normalize(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool AreWslAppServerConfigsEqual(WslAppServerConfigSnapshot left, WslAppServerConfigSnapshot right)
        {
            left = left ?? new WslAppServerConfigSnapshot();
            right = right ?? new WslAppServerConfigSnapshot();

            return string.Equals(left.DistributionName, right.DistributionName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.CodexCommand, right.CodexCommand, StringComparison.Ordinal) &&
                string.Equals(left.ShellCommand, right.ShellCommand, StringComparison.Ordinal);
        }

        private static WslAppServerConfigSnapshot CloneWslAppServerConfig(WslAppServerConfigSnapshot config)
        {
            config = config ?? new WslAppServerConfigSnapshot();
            return new WslAppServerConfigSnapshot
            {
                DistributionName = config.DistributionName,
                CodexCommand = config.CodexCommand,
                ShellCommand = config.ShellCommand
            };
        }

        private static bool AreRemoteMacSshConfigsEqual(RemoteMacSshConfigSnapshot left, RemoteMacSshConfigSnapshot right)
        {
            left = left ?? new RemoteMacSshConfigSnapshot();
            right = right ?? new RemoteMacSshConfigSnapshot();

            return string.Equals(left.HostName, right.HostName, StringComparison.OrdinalIgnoreCase) &&
                left.Port == right.Port &&
                string.Equals(left.UserName, right.UserName, StringComparison.Ordinal) &&
                left.AuthenticationMode == right.AuthenticationMode &&
                string.Equals(left.PrivateKeyPath, right.PrivateKeyPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.ProtectedPassword, right.ProtectedPassword, StringComparison.Ordinal) &&
                string.Equals(left.HostKeyFingerprint, right.HostKeyFingerprint, StringComparison.Ordinal) &&
                string.Equals(left.PlinkExecutablePath, right.PlinkExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(left.ShellCommand, right.ShellCommand, StringComparison.Ordinal) &&
                string.Equals(left.CodexCommand, right.CodexCommand, StringComparison.Ordinal);
        }

        private static RemoteMacSshConfigSnapshot CloneRemoteMacSshConfig(RemoteMacSshConfigSnapshot config)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            return new RemoteMacSshConfigSnapshot
            {
                HostName = config.HostName,
                Port = config.Port,
                UserName = config.UserName,
                AuthenticationMode = config.AuthenticationMode,
                PrivateKeyPath = config.PrivateKeyPath,
                ProtectedPassword = config.ProtectedPassword,
                HostKeyFingerprint = config.HostKeyFingerprint,
                PlinkExecutablePath = config.PlinkExecutablePath,
                ShellCommand = config.ShellCommand,
                CodexCommand = config.CodexCommand
            };
        }

        private static List<RemoteUnixSshProfileConfigSnapshot> CloneRemoteUnixProfiles(IList<RemoteUnixSshProfileConfigSnapshot> profiles)
        {
            var result = new List<RemoteUnixSshProfileConfigSnapshot>();
            if (profiles == null)
            {
                return result;
            }

            for (var i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                if (profile == null)
                {
                    continue;
                }

                result.Add(new RemoteUnixSshProfileConfigSnapshot
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    Ssh = CloneRemoteMacSshConfig(profile.Ssh)
                });
            }

            return result;
        }

        private static RemoteMacSshConfigSnapshot GetSelectedRemoteUnixConfig(
            IList<RemoteUnixSshProfileConfigSnapshot> profiles,
            string selectedProfileId,
            RemoteMacSshConfigSnapshot fallback)
        {
            if (profiles != null)
            {
                for (var i = 0; i < profiles.Count; i++)
                {
                    var profile = profiles[i];
                    if (profile != null &&
                        !string.IsNullOrWhiteSpace(selectedProfileId) &&
                        string.Equals(profile.Id, selectedProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        return profile.Ssh;
                    }
                }

                for (var i = 0; i < profiles.Count; i++)
                {
                    if (profiles[i] != null)
                    {
                        return profiles[i].Ssh;
                    }
                }
            }

            return fallback;
        }

        private static string NormalizeOptionalPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        }
    }
}
