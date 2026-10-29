using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async void OnTargetReloadMenuItemClick(object sender, EventArgs e)
        {
            if (!IsCurrentExecutionTargetWindows())
            {
                return;
            }

            var confirmation = MessageBox.Show(
                this,
                UiLocalization.Text(
                    "MainForm.TargetReload.Confirm",
                    "Reload the selected target? In-progress responses or commands will be interrupted."),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            await RestartAppServerForRuntimeSettingsAsync(_currentInteractionMode, false);
        }

        private async Task InitializeAppAsync()
        {
            if (!await WaitForTargetExitDecisionAsync()) return;
            SetStatus(GetConnectionStatusConnectingText());
            if (!await EnsureAppServerConnectedAsync())
            {
                if (!IsTargetStopping) RefreshHeader();
                return;
            }

            _connectionStatus = GetConnectionStatusConnectedText();
            _ = CaptureGlobalAuthenticationAfterConnectAsync();
            await EnsureMyCodexOfficialMarketplaceRegisteredAsync();
            await RestoreBrowserControlAsync();
            if (!await WaitForTargetExitDecisionAsync()) return;
            await RefreshAuthenticationStateAsync(false);
            if (!await WaitForTargetExitDecisionAsync()) return;
            await LoadAvailableModelsAsync(false);
            if (!await WaitForTargetExitDecisionAsync()) return;
            await RefreshOfficialConfigStateAfterConnectAsync();
            if (!await WaitForTargetExitDecisionAsync()) return;
            await RefreshLocalThreadSessionWatchersAsync();
            if (!await WaitForTargetExitDecisionAsync()) return;
            await RunInitialThreadImportFlowAsync();
            if (!await WaitForTargetExitDecisionAsync()) return;
            await RunAutomaticThreadSyncAsync();
            if (!await WaitForTargetExitDecisionAsync()) return;
            RefreshHeader();

            if (_startupCommandLineOptions != null && _startupCommandLineOptions.HasWorkspacePath)
            {
                await LoadCommandLineWorkspaceAsync(_startupCommandLineOptions.WorkspacePath);
                if (!await WaitForTargetExitDecisionAsync()) return;
            }
            else
            {
                if (_currentInteractionMode == AppInteractionMode.Office)
                {
                    RestoreOfficeSelectionAfterStateLoad();
                    RefreshWorkspaceView();
                    RefreshSessionList();
                    RefreshHeader();
                    RefreshInputState();
                }
                else
                {
                    var startupWorkspacePath = GetStartupWorkspacePath();
                    if (!string.IsNullOrWhiteSpace(startupWorkspacePath))
                    {
                        await LoadWorkspaceAsync(startupWorkspacePath, false, true);
                        if (!await WaitForTargetExitDecisionAsync()) return;
                    }
                }
            }

            if (_currentSession == null)
            {
                if (_currentWorkspace == null)
                {
                    ClearChatHistoryItems();
                    AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), UiLocalization.Text("MainForm.Message.OpenWorkspaceBeforeSession", "Please open a workspace first, then start a new session."));
                    SetStatus(UiLocalization.Text("MainForm.Status.OpenWorkspaceFirst", "Please open a workspace first"));
                    return;
                }

                ClearChatHistoryItems();
                AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), GetNoSessionMessageForCurrentWorkspace());
                SetStatus(UiLocalization.Text("MainForm.Status.NoSessionsInWorkspace", "No sessions in current workspace"));
                return;
            }

            await LoadSessionHistoryAsync(_currentSession, true);
            if (!await WaitForTargetExitDecisionAsync()) return;
            SetStatus(UiLocalization.Text("MainForm.Status.RestoredRecentState", "Recent state restored"));
        }

        private async Task<bool> EnsureAppServerConnectedAsync()
        {
            if (!await WaitForTargetExitDecisionAsync()) return false;
            try
            {
                if (!await EnsurePendingBundledRuntimeUpdateAsync()) return false;
                await EnsureLocalAuthenticationBackendBeforeConnectAsync();
                if (!await WaitForTargetExitDecisionAsync()) return false;
                await _appServerClient.ConnectAsync();
                if (!await WaitForTargetExitDecisionAsync()) return false;
                CleanupConnectedBundledRuntimeUpdate();
                await RefreshActiveOfficeRuntimeRootAsync();
                return await WaitForTargetExitDecisionAsync();
            }
            catch (CodexRuntimeUnavailableException ex)
            {
                if (!await WaitForTargetExitDecisionAsync()) return false;
                var message = ex.Status != null && !string.IsNullOrWhiteSpace(ex.Status.Message)
                    ? ex.Status.Message
                    : ex.Message;

                _connectionStatus = GetConnectionStatusDisconnectedText();
                RefreshHeader();
                RefreshInputState();
                AppendLog(message);
                SetHighPriorityStatus(message);
                return false;
            }
            catch (Exception ex)
            {
                if (!await WaitForTargetExitDecisionAsync()) return false;
                _connectionStatus = GetConnectionStatusDisconnectedText();
                _validatedRuntimeEndpointScopeKey = null;
                RefreshHeader();
                RefreshInputState();
                AppendLog(string.Format(
                    UiLocalization.Text("MainForm.Log.ReconnectAppServerFailed.Template", "Failed to reconnect App Server: {0}"),
                    ex.Message));
                SetHighPriorityStatus(ex.Message);
                return false;
            }
        }

        private async Task EnsureRuntimeEndpointReadyAsync()
        {
            if (!RequiresRuntimeEndpointStorage())
            {
                _validatedRuntimeEndpointScopeKey = null;
                return;
            }

            var scopeKey = _runtimeProfileService.GetCurrentRuntimeProfileId(_appConfigSnapshot);
            if (string.Equals(_validatedRuntimeEndpointScopeKey, scopeKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var script =
                "set -eu\n" +
                "root=\"${HOME}/.codex/mycodex\"\n" +
                "mkdir -p \"$root\"\n" +
                "chmod 700 \"$root\" 2>/dev/null || true\n" +
                "printf 'MYCODEX_RUNTIME_CONFIG_READY=%s\\n' \"$root\"";
            var result = await _appServerClient.ExecuteCommandAsync(
                script,
                "/",
                ApprovalMode.FullAccess,
                "mycodex-runtime-config-init-" + Guid.NewGuid().ToString("N"),
                30000);
            if (result == null || result.ExitCode != 0)
            {
                var message = result != null && !string.IsNullOrWhiteSpace(result.StdErr)
                    ? result.StdErr.Trim()
                    : UiLocalization.Text("MainForm.RuntimeConfig.Error.InitFailed", "Runtime MyCodex configuration directory could not be initialized.");
                throw new InvalidOperationException(message);
            }

            _validatedRuntimeEndpointScopeKey = scopeKey;
        }

        private bool RequiresRuntimeEndpointStorage()
        {
            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                return false;
            }

            if (_appConfigSnapshot == null)
            {
                return false;
            }

            if (CodexRuntimeSources.IsRemoteUnixSsh(_appConfigSnapshot.CodexRuntimeSource))
            {
                return true;
            }

            return string.Equals(_appConfigSnapshot.CodexRuntimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<bool> TryRecoverAppServerConnectionAsync(string initialStatus, string failureStatus, bool announceSuccess)
        {
            if (_isRecoveringAppServerConnection)
            {
                SetStatus(UiLocalization.Text("MainForm.Status.ReconnectingAppServer", "Reconnecting to App Server..."));
                return false;
            }

            _isRecoveringAppServerConnection = true;
            _connectionStatus = GetConnectionStatusReconnectingText();
            RefreshHeader();
            RefreshInputState();
            SetStatus(initialStatus);

            try
            {
                if (!await EnsureAppServerConnectedAsync())
                {
                    _connectionStatus = GetConnectionStatusDisconnectedText();
                    RefreshHeader();
                    RefreshInputState();
                    SetHighPriorityStatus(failureStatus);
                    return false;
                }

                _connectionStatus = GetConnectionStatusConnectedText();
                _ = CaptureGlobalAuthenticationAfterConnectAsync();
                await RestoreAppServerFileWindowWatchesAsync();
                if (!await WaitForTargetExitDecisionAsync()) return false;
                await RefreshAuthenticationStateAsync(false);
                if (!await WaitForTargetExitDecisionAsync()) return false;
                await LoadAvailableModelsAsync(false);
                if (!await WaitForTargetExitDecisionAsync()) return false;
                await RefreshOfficialConfigStateAfterConnectAsync();
                if (!await WaitForTargetExitDecisionAsync()) return false;
                await RefreshLocalThreadSessionWatchersAsync();
                if (!await WaitForTargetExitDecisionAsync()) return false;
                await RunAutomaticThreadSyncAsync();
                if (!await WaitForTargetExitDecisionAsync()) return false;
                RefreshHeader();
                RefreshInputState();

                if (announceSuccess && _currentSession != null)
                {
                    AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), UiLocalization.Text("MainForm.Message.ReconnectedToAppServer", "Reconnected to Codex App Server."));
                }

                SetStatus(UiLocalization.Text("MainForm.Status.ReconnectedToAppServer", "Reconnected to App Server"));
                return true;
            }
            catch (Exception ex)
            {
                if (!await WaitForTargetExitDecisionAsync()) return false;
                _connectionStatus = GetConnectionStatusDisconnectedText();
                _validatedRuntimeEndpointScopeKey = null;
                RefreshHeader();
                RefreshInputState();
                AppendLog(string.Format(UiLocalization.Text("MainForm.Log.ReconnectAppServerFailed.Template", "Failed to reconnect App Server: {0}"), ex.Message));
                SetHighPriorityStatus(failureStatus);
                return false;
            }
            finally
            {
                _isRecoveringAppServerConnection = false;
            }
        }

        private async Task<bool> EnsureAppServerAvailableForActionAsync(string actionName)
        {
            if (IsTargetExitPreparing || _isExitRequested) return false;
            if (!await EnsurePendingBundledRuntimeUpdateAsync()) return false;
            TraceSendDiagnostic("EnsureAppServer", "begin; action=" + actionName + "; " + BuildSendDiagnosticContext());
            if (IsConnectedStatus())
            {
                try
                {
                    TraceSendDiagnostic("EnsureAppServer", "ConnectAsync begin; " + BuildSendDiagnosticContext());
                    await _appServerClient.ConnectAsync();
                    TraceSendDiagnostic("EnsureAppServer", "ConnectAsync complete; " + BuildSendDiagnosticContext());
                }
                catch (Exception ex)
                {
                    if (!await WaitForTargetExitDecisionAsync()) return false;
                    TraceSendDiagnosticError("EnsureAppServer", ex);
                    _connectionStatus = GetConnectionStatusDisconnectedText();
                    _validatedRuntimeEndpointScopeKey = null;
                    RefreshHeader();
                    RefreshInputState();
                    AppendLog(string.Format(
                        UiLocalization.Text("MainForm.Log.ReconnectAppServerFailed.Template", "Failed to reconnect App Server: {0}"),
                        ex.Message));
                    SetHighPriorityStatus(string.Format(
                        UiLocalization.Text("MainForm.Status.ActionFailedDisconnected.Template", "{0} failed because App Server is not connected"),
                        actionName));
                    return false;
                }

                return await WaitForTargetExitDecisionAsync();
            }

            var recovered = await TryRecoverAppServerConnectionAsync(
                GetConnectionStatusConnectingText(),
                string.Format(
                    UiLocalization.Text("MainForm.Status.ActionFailedDisconnected.Template", "{0} failed because App Server is not connected"),
                    actionName),
                false);
            return recovered && !IsTargetExitPreparing && !_isExitRequested;
        }

        private bool IsCurrentExecutionTargetWindows()
        {
            if (_appConfigSnapshot == null)
            {
                return true;
            }

            return !string.Equals(_appConfigSnapshot.CodexRuntimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase) &&
                !CodexRuntimeSources.IsRemoteUnixSsh(_appConfigSnapshot.CodexRuntimeSource);
        }

        private async Task RefreshAuthenticationStateAsync(bool refreshToken, bool ensureConnection = true)
        {
            if (!await WaitForTargetExitDecisionAsync()) return;
            if (_isRefreshingAuthenticationState)
            {
                return;
            }

            if (ensureConnection && !await EnsureAppServerAvailableForActionAsync(
                    refreshToken
                        ? UiLocalization.Text("MainForm.Action.RefreshLoginState", "Refresh login state")
                        : UiLocalization.Text("MainForm.Action.ReadLoginState", "Read login state")))
            {
                RefreshAuthenticationUi();
                return;
            }

            _isRefreshingAuthenticationState = true;
            try
            {
                var authenticationState = await _appServerClient.GetAuthenticationStateAsync(refreshToken);
                if (!await WaitForTargetExitDecisionAsync()) return;
                _authenticationState = authenticationState;
                ShowAuthenticationBootstrapIfRequired();
            }
            catch (Exception ex)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                if (!IsAppServerConnectivityFailure(ex))
                {
                    _authenticationState = new AppServerAuthenticationState();
                }

                AddChatHistoryItem(ChatHistoryItemFactory.GetErrorSenderText(), ex.Message);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.ReadLoginStateFailed", "Failed to read login state"));
            }
            finally
            {
                _isRefreshingAuthenticationState = false;
                if (!IsTargetStopping && !IsTargetExitPreparing) RefreshAuthenticationUi();
            }
        }

        private static bool IsAppServerConnectivityFailure(Exception ex)
        {
            if (ex == null)
            {
                return false;
            }

            if (ex is TimeoutException)
            {
                return true;
            }

            var message = ex.Message ?? string.Empty;
            return message.IndexOf("App Server", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   (message.IndexOf("disconnected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("not connected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("\u672A\u8FDE\u63A5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("\u65AD\u5F00", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("\u8D85\u65F6", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("\u8FDE\u63A5", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void RefreshAuthenticationUi()
        {
            RefreshWindowTitle();
            _userCenterMenuItem.Enabled = IsChatGptAccountAuthenticationActive();
            _fileSyncThreadsMenuItem.Enabled = IsConnectedStatus();
            _workspaceSyncThreadsMenuItem.Enabled = IsConnectedStatus();
            UpdateAutomaticThreadSyncState();
            UpdateRateLimitAutoResetState();
        }

        private void UpdateAutomaticThreadSyncState()
        {
            if (IsTargetStopping) return;
            _automaticThreadSyncTimer.Enabled =
                !IsTargetExitPreparing &&
                !_automaticThreadSyncRunCoordinator.IsRunning &&
                IsConnectedStatus() &&
                (HasImportedSessions() || ShouldBootstrapRuntimeEndpointHistorySync());
        }
    }
}
