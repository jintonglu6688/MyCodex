using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private MenuItem _authenticationCenterMenuItem;
        private bool _authenticationReconnectPending;
        private bool _isAuthenticationCenterOpen;
        private CancellationTokenSource _authenticationCaptureCancellation;

        private async Task EnsureLocalAuthenticationBackendBeforeConnectAsync()
        {
            if (!IsCurrentExecutionTargetWindows() || _isExitRequested) return;
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_targetShutdownCancellation.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    await AuthenticationCenterClient.EnsureLocalBackendAsync(timeout.Token);
                }
            }
            catch (Exception error)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                // An absent optional authentication package must not disable ordinary App Server use.
                SetLowPriorityStatus(AuthenticationCenterUiText.Error(error));
            }
        }

        private void StopLocalAuthenticationBackendOnExit()
        {
            _authenticationCaptureCancellation?.Cancel();
            // Runtime switching can leave a Windows lease even when the current target is WSL/SSH.
            if (!AuthenticationCenterClient.IsLocalBackendCreated) return;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                var token = timeout.Token;
                var stop = Task.Run(async () =>
                {
                    try { await AuthenticationCenterClient.ShutdownLocalBackendAsync(token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { Trace.TraceWarning("Authentication backend shutdown timed out."); }
                    catch (AuthenticationCenterException error) { Trace.TraceWarning("Authentication backend shutdown failed: {0}", error.Code); }
                    catch (Exception) { Trace.TraceWarning("Authentication backend shutdown failed."); }
                });
                if (!stop.Wait(TimeSpan.FromSeconds(9)))
                    Trace.TraceWarning("Authentication backend shutdown exceeded the exit budget.");
            }
        }

        private void InitializeAuthenticationCenterMenu()
        {
            _authenticationCenterMenuItem = new MenuItem(UiLocalization.Text("MainMenu.AuthenticationCenter", "&Authentication Center…"));
            _authenticationCenterMenuItem.Click += OnAuthenticationCenterMenuItemClick;
            _accountMenuItem.MenuItems.Add(_authenticationCenterMenuItem);
            FormClosed += (sender, args) => _authenticationCaptureCancellation?.Cancel();
        }

        private void OnAuthenticationCenterMenuItemClick(object sender, EventArgs e)
        {
            if (_isAuthenticationCenterOpen || _isRuntimeWindowTransitionInProgress || _isRecoveringAppServerConnection ||
                string.Equals(_connectionStatus, GetConnectionStatusReconnectingText(), StringComparison.Ordinal))
            {
                SetStatus(UiLocalization.Text("AuthenticationCenter.Error.Busy", "Work or a configuration operation is active. Finish it before applying a provider; no work was stopped."));
                return;
            }
            var client = CreateCurrentAuthenticationCenterClient();
            var backend = client as AuthenticationCenterClient;
            var coordinator = new AuthenticationApplyCoordinator(client, AcquireAuthenticationTransition, ReconnectAfterAuthenticationAsync,
                () => _appServerClient.GetAuthenticationStateAsync(false),
                () => _appServerClient.ReadConfigAsync(_currentWorkspace?.RootPath, false));
            var stackCoordinator = new AuthenticationStackCoordinator(AcquireAuthenticationTransition, ReconnectAfterAuthenticationAsync);
            _isAuthenticationCenterOpen = true;
            try
            {
                using (var form = new AuthenticationCenterForm(client, _screenReaderService,
                    (provider, token) => TrackAuthenticationMutationAsync(() => coordinator.ApplyAsync(provider, token)),
                    RetryAuthenticationConnectionAsync, GetAuthenticationCenterIdentity, _authenticationReconnectPending,
                    (provider, token) => DeleteAuthenticationProviderAsync(client, provider, token),
                    (edit, token) => TrackAuthenticationMutationAsync(() => coordinator.SaveAsync(edit, token)),
                    (accountId, token) => TrackAuthenticationMutationAsync(() => coordinator.DeleteAccountAsync(accountId, token)),
                    backend != null && backend.CanUpdateBackend
                        ? (Func<CancellationToken, Task>)(token => UpdateAuthenticationBackendAsync(backend, token)) : null,
                    ModelsDevCatalogSource.Shared,
                    (unified, token) => TrackAuthenticationMutationAsync(() =>
                        coordinator.SetSessionHistoryAsync(unified, token)),
                    (change, token) => TrackAuthenticationMutationAsync(() => stackCoordinator.RunAsync(change, token))))
                {
                    form.ShowDialog(this);
                }
            }
            finally
            {
                _isAuthenticationCenterOpen = false;
            }
        }

        private async Task UpdateAuthenticationBackendAsync(AuthenticationCenterClient client, CancellationToken token)
        {
            using (AcquireAuthenticationTransition())
                await client.UpdateBackendAsync(token);
        }

        private IAuthenticationCenterClient CreateCurrentAuthenticationCenterClient()
        {
            var runtimeSource = _appConfigSnapshot?.CodexRuntimeSource;
            if (string.Equals(runtimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase))
                return AuthenticationCenterClient.CreateWsl(GetCurrentWslConfig());
            if (CodexRuntimeSources.IsRemoteUnixSsh(runtimeSource))
                return AuthenticationCenterClient.CreateRemote(GetCurrentRemoteMacConfig());
            return AuthenticationCenterClient.CreateLocal(runtimeSource,
                token => _appServerClient.ReadConfigAsync(null, true, token), _appConfigService.GetSharedDataDirectory());
        }

        private async Task CaptureGlobalAuthenticationAfterConnectAsync()
        {
            ResetSessionHistoryResumeRepair();
            _authenticationCaptureCancellation?.Cancel();
            using (var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                _authenticationCaptureCancellation = cancellation;
                var profile = _runtimeProfileService.GetCurrentRuntimeProfileId(_appConfigSnapshot);
                try
                {
                    await AuthenticationCaptureOnConnect.RunAsync(CreateCurrentAuthenticationCenterClient(),
                        () => !IsTargetStopping && !IsTargetExitPreparing && IsConnectedStatus() &&
                            ReferenceEquals(_authenticationCaptureCancellation, cancellation) &&
                            string.Equals(profile, _runtimeProfileService.GetCurrentRuntimeProfileId(_appConfigSnapshot), StringComparison.Ordinal),
                        SetLowPriorityStatus, cancellation.Token);
                }
                catch (Exception error)
                {
                    // Client construction cannot affect the App Server connection.
                    if (!IsDisposed && !Disposing && ReferenceEquals(_authenticationCaptureCancellation, cancellation))
                        SetLowPriorityStatus(AuthenticationCenterUiText.Error(error));
                }
                finally
                {
                    if (ReferenceEquals(_authenticationCaptureCancellation, cancellation))
                        _authenticationCaptureCancellation = null;
                }
            }
        }

        private async Task<T> TrackAuthenticationMutationAsync<T>(Func<Task<T>> action)
        {
            try { return await action(); }
            catch (AuthenticationCenterException error) when (error.Code == "operation_unknown")
            {
                _authenticationReconnectPending = true;
                _connectionStatus = GetConnectionStatusDisconnectedText();
                RefreshHeader();
                RefreshInputState();
                throw;
            }
        }

        private async Task<AuthenticationDeleteResult> DeleteAuthenticationProviderAsync(
            IAuthenticationCenterClient client, AuthenticationProvider provider, CancellationToken cancellationToken)
        {
            using (AcquireAuthenticationTransition())
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool accountRemoved;
                try
                {
                    accountRemoved = await client.DeleteAsync(provider, cancellationToken);
                }
                catch (AuthenticationCenterException error) when (error.Code == "account_delete_recovery_required")
                {
                    _authenticationReconnectPending = true;
                    _connectionStatus = GetConnectionStatusDisconnectedText();
                    RefreshHeader();
                    RefreshInputState();
                    SetHighPriorityStatus(AuthenticationCenterUiText.Error(error));
                    throw;
                }
                var reconnected = !accountRemoved || await ReconnectAfterAuthenticationAsync();
                if (accountRemoved)
                {
                    var message = reconnected
                        ? UiLocalization.Text("AuthenticationCenter.Status.AccountSignedOut", "Configuration deleted. The account was signed out and its saved credentials were removed.")
                        : UiLocalization.Text("AuthenticationCenter.Status.AccountReconnectFailed", "Configuration and account deleted, but MyCodex could not reconnect. Retry the connection.");
                    if (reconnected) SetStatus(message);
                    else SetHighPriorityStatus(message);
                }
                return new AuthenticationDeleteResult { AccountRemoved = accountRemoved, Reconnected = reconnected };
            }
        }

        private IDisposable AcquireAuthenticationTransition()
        {
            var lease = RuntimeOperationGate.Shared.TryEnterTransition();
            if (lease == null)
            {
                throw new AuthenticationCenterException("runtime_busy");
            }
            var marked = false;
            try
            {
                var actualClient = _appServerClient as AppServerClient;
                if (_isRuntimeWindowTransitionInProgress || _isRecoveringAppServerConnection ||
                    string.Equals(_connectionStatus, GetConnectionStatusReconnectingText(), StringComparison.Ordinal) ||
                    _isAuthenticationBootstrapOpen || _busyThreadIds.Count != 0 || _isCommandRunning ||
                    _threadOperationStateTracker.HasActiveCompaction ||
                    (actualClient != null && actualClient.HasAuthenticationTransitionWork) ||
                    _sessionCommandTerminals.Values.Any(item => item != null && item.HasActiveCommand) ||
                    _sessionWorkbenches.Values.Any(item => item != null && item.IsBusy) ||
                    _subAgentWorkbenches.Values.Any(item => item != null && item.IsBusy) ||
                    _gitWorkbenchForms.Values.Any(item => item != null && item.IsBusy) ||
                    _runtimeFileBrowserForms.Any(item => item != null && item.IsBusyForRuntimeTransition) ||
                    _appServerFileWindowManager.GetBusyFormForRuntimeTransition() != null)
                {
                    throw new AuthenticationCenterException("runtime_busy");
                }
                var automations = _automationStore?.Load();
                if (automations?.Runs != null && automations.Runs.Any(run => run != null && run.Status == AutomationRunStatus.Running))
                {
                    throw new AuthenticationCenterException("runtime_busy");
                }
                _isRuntimeWindowTransitionInProgress = true;
                marked = true;
                RefreshInputState();
                return new AuthenticationTransitionLease(this, lease);
            }
            catch
            {
                if (marked)
                {
                    _isRuntimeWindowTransitionInProgress = false;
                }
                lease.Dispose();
                throw new AuthenticationCenterException("runtime_busy");
            }
        }

        private async Task<bool> RetryAuthenticationConnectionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (AcquireAuthenticationTransition())
            {
                return await ReconnectAfterAuthenticationAsync();
            }
        }

        private async Task<bool> ReconnectAfterAuthenticationAsync()
        {
            _authenticationReconnectPending = true;
            _connectionStatus = GetConnectionStatusReconnectingText();
            RefreshHeader();
            var stage = "restart";
            try
            {
                if (!await EnsurePendingBundledRuntimeUpdateAsync()) return false;
                // Restart the existing target without loading another profile,
                // replacing session collections, or interrupting any work.
                ResetSessionHistoryResumeRepair();
                await _appServerClient.RestartAsync();
                stage = "account/read";
                _authenticationState = await _appServerClient.GetAuthenticationStateAsync(false);
                stage = "model-configuration";
                _modelConfigurationService?.Invalidate();
                var models = await RefreshModelConfigurationAsync(_currentWorkspace?.RootPath, false, false);
                if (models == null)
                {
                    throw new AuthenticationCenterException("reconnect_failed");
                }
                stage = "restore-file-watches";
                await RestoreAppServerFileWindowWatchesAsync();
                stage = "restore-thread-watches";
                await RefreshLocalThreadSessionWatchersAsync();
                stage = "complete";
                _connectionStatus = GetConnectionStatusConnectedText();
                _ = CaptureGlobalAuthenticationAfterConnectAsync();
                _authenticationReconnectPending = false;
                RefreshAuthenticationUi();
                SetStatus(UiLocalization.Text("AuthenticationCenter.Status.Applied", "Global configuration applied and MyCodex reconnected. Existing threads may require a compatible model or a new conversation."));
                Trace.TraceInformation("[AuthenticationReconnect] Completed.");
                return true;
            }
            catch (Exception error)
            {
                Trace.TraceWarning("[AuthenticationReconnect] Failed. Stage={0}; ExceptionType={1}", stage, error.GetType().FullName);
                _connectionStatus = GetConnectionStatusDisconnectedText();
                SetHighPriorityStatus(UiLocalization.Text("AuthenticationCenter.Status.ReconnectFailed", "Global configuration is applied, but MyCodex could not reconnect. Retry the connection; the global change has not been rolled back."));
                return false;
            }
            finally
            {
                RefreshHeader();
                RefreshInputState();
            }
        }

        private string GetAuthenticationCenterIdentity()
        {
            if (_authenticationReconnectPending || _authenticationState == null)
            {
                return UiLocalization.Text("AuthenticationCenter.Identity.Unknown", "Unknown");
            }
            if (string.Equals(_authenticationState.AccountType, "chatgpt", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_authenticationState.AuthMode, "chatgpt", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(_authenticationState.Email)
                    ? UiLocalization.Text("AuthenticationCenter.Identity.ChatGpt", "ChatGPT account")
                    : _authenticationState.Email;
            }
            return UiLocalization.Text("AuthenticationCenter.Identity.None", "No ChatGPT account reported");
        }

        private sealed class AuthenticationTransitionLease : IDisposable
        {
            private MainForm _owner;
            private readonly IDisposable _lease;

            internal AuthenticationTransitionLease(MainForm owner, IDisposable lease)
            {
                _owner = owner;
                _lease = lease;
            }

            public void Dispose()
            {
                var owner = _owner;
                if (owner == null)
                {
                    return;
                }
                _owner = null;
                owner._isRuntimeWindowTransitionInProgress = false;
                _lease.Dispose();
                if (!owner.IsDisposed)
                {
                    owner.RefreshHeader();
                    owner.RefreshInputState();
                }
            }
        }
    }
}
