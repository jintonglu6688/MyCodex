using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class WslAppServerSettingsForm
    {
        private bool HasUnsavedRuntimeSourceChange()
        {
            return HasUnsavedTargetWindowsLaunchSettings() || !string.Equals(
                _initialRuntimeSource,
                CodexRuntimeSources.CanonicalizeForExecution(GetSelectedRuntimeSource()),
                StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshWindowsSandboxState()
        {
            if (_windowsSandboxDetectButton == null || _windowsSandboxSetupButton == null)
            {
                return;
            }

            var blockedByUnsavedRuntime = HasUnsavedRuntimeSourceChange();
            var canUseSandbox = IsWindowsSandboxTargetAllowed() && _windowsSandboxClient != null &&
                                !blockedByUnsavedRuntime &&
                                !_isRuntimeOperationBusy &&
                                !_isWindowsSandboxOperationBusy;

            _windowsSandboxDetectButton.Enabled = canUseSandbox;
            _windowsSandboxSetupButton.Enabled = canUseSandbox &&
                                                (_lastWindowsSandboxStatus == AppServerWindowsSandboxReadinessStatus.NotConfigured ||
                                                 _lastWindowsSandboxStatus == AppServerWindowsSandboxReadinessStatus.UpdateRequired);

            if (blockedByUnsavedRuntime)
            {
                SetWindowsSandboxDetails(UiLocalization.Text(
                    "WslAppServerSettingsForm.WindowsSandbox.Detail.SaveRuntimeFirst",
                    "Save and restart App Server before checking Windows Sandbox."));
            }
        }

        private async Task DetectWindowsSandboxAsync()
        {
            if (!IsWindowsSandboxTargetAllowed() || _windowsSandboxClient == null || HasUnsavedRuntimeSourceChange())
            {
                RefreshWindowsSandboxState();
                return;
            }

            SetWindowsSandboxBusy(true);
            SetWindowsSandboxStatus(UiLocalization.Text(
                "WslAppServerSettingsForm.WindowsSandbox.Status.Checking",
                "Checking Windows Sandbox..."));

            try
            {
                var readiness = await _windowsSandboxClient.ReadWindowsSandboxReadinessAsync();
                ApplyWindowsSandboxReadiness(readiness);
            }
            catch (Exception ex)
            {
                _lastWindowsSandboxStatus = AppServerWindowsSandboxReadinessStatus.Unknown;
                SetWindowsSandboxStatus(UiLocalization.Text(
                    "WslAppServerSettingsForm.WindowsSandbox.Status.Failed",
                    "Windows Sandbox check failed"));
                SetWindowsSandboxDetails(ex.Message);
            }
            finally
            {
                SetWindowsSandboxBusy(false);
            }
        }

        private void ApplyWindowsSandboxReadiness(AppServerWindowsSandboxReadiness readiness)
        {
            _lastWindowsSandboxStatus = readiness == null
                ? AppServerWindowsSandboxReadinessStatus.Unknown
                : readiness.Status;
            var rawStatus = readiness == null || string.IsNullOrWhiteSpace(readiness.RawStatus)
                ? _lastWindowsSandboxStatus.ToString()
                : readiness.RawStatus.Trim();

            switch (_lastWindowsSandboxStatus)
            {
                case AppServerWindowsSandboxReadinessStatus.Ready:
                    SetWindowsSandboxStatus(string.Format(
                        UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Status.Ready.Template", "Windows Sandbox: ready ({0})"),
                        rawStatus));
                    SetWindowsSandboxDetails(UiLocalization.Text(
                        "WslAppServerSettingsForm.WindowsSandbox.Detail.Ready",
                        "Windows Sandbox is ready."));
                    break;
                case AppServerWindowsSandboxReadinessStatus.NotConfigured:
                    SetWindowsSandboxStatus(string.Format(
                        UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Status.NotConfigured.Template", "Windows Sandbox: not configured ({0})"),
                        rawStatus));
                    SetWindowsSandboxDetails(UiLocalization.Text(
                        "WslAppServerSettingsForm.WindowsSandbox.Detail.NotConfigured",
                        "Run configuration to enable Windows Sandbox support."));
                    break;
                case AppServerWindowsSandboxReadinessStatus.UpdateRequired:
                    SetWindowsSandboxStatus(string.Format(
                        UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Status.UpdateRequired.Template", "Windows Sandbox: update required ({0})"),
                        rawStatus));
                    SetWindowsSandboxDetails(UiLocalization.Text(
                        "WslAppServerSettingsForm.WindowsSandbox.Detail.UpdateRequired",
                        "Run configuration to update Windows Sandbox support."));
                    break;
                default:
                    SetWindowsSandboxStatus(string.Format(
                        UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Status.Unknown.Template", "Windows Sandbox: unknown ({0})"),
                        rawStatus));
                    SetWindowsSandboxDetails(UiLocalization.Text(
                        "WslAppServerSettingsForm.WindowsSandbox.Detail.Unknown",
                        "The runtime returned an unknown Windows Sandbox status."));
                    break;
            }

            RefreshWindowsSandboxState();
        }

        private async Task ConfirmAndStartWindowsSandboxSetupAsync()
        {
            if (_windowsSandboxClient == null || !_windowsSandboxSetupButton.Enabled)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                UiLocalization.Text(
                    "WslAppServerSettingsForm.WindowsSandbox.Confirm.Message",
                    "This will start Windows Sandbox configuration. It may take some time, and App Server or Windows may need to be restarted after completion. Continue?"),
                UiLocalization.Text(
                    "WslAppServerSettingsForm.WindowsSandbox.Confirm.Title",
                    "Configure Windows Sandbox"),
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
            if (result != DialogResult.OK)
            {
                return;
            }

            await StartWindowsSandboxSetupAsync(AppServerWindowsSandboxSetupMode.Unelevated);
        }

        private async Task StartWindowsSandboxSetupAsync(AppServerWindowsSandboxSetupMode mode)
        {
            if (!IsWindowsSandboxTargetAllowed() || _windowsSandboxClient == null || HasUnsavedRuntimeSourceChange())
            {
                RefreshWindowsSandboxState();
                return;
            }

            SetWindowsSandboxBusy(true);
            _isWindowsSandboxSetupRunning = true;
            SetWindowsSandboxStatus(UiLocalization.Text(
                "WslAppServerSettingsForm.WindowsSandbox.Status.SettingUp",
                "Configuring Windows Sandbox..."));
            SetWindowsSandboxDetails(UiLocalization.Text(
                "WslAppServerSettingsForm.WindowsSandbox.Detail.SettingUp",
                "Waiting for Windows Sandbox setup to complete."));

            try
            {
                var result = await _windowsSandboxClient.StartWindowsSandboxSetupAsync(mode, null);
                if (result == null || !result.Started)
                {
                    _isWindowsSandboxSetupRunning = false;
                    SetWindowsSandboxStatus(UiLocalization.Text(
                        "WslAppServerSettingsForm.WindowsSandbox.Status.SetupNotStarted",
                        "Windows Sandbox setup did not start."));
                    SetWindowsSandboxBusy(false);
                }
            }
            catch (Exception ex)
            {
                _isWindowsSandboxSetupRunning = false;
                SetWindowsSandboxStatus(UiLocalization.Text(
                    "WslAppServerSettingsForm.WindowsSandbox.Status.SetupFailed",
                    "Windows Sandbox setup failed."));
                SetWindowsSandboxDetails(ex.Message);
                SetWindowsSandboxBusy(false);
            }
        }

        internal void ApplyWindowsSandboxSetupCompleted(AppServerWindowsSandboxSetupCompletedEvent result)
        {
            if (!_isWindowsSandboxSetupRunning || result == null)
            {
                return;
            }

            _isWindowsSandboxSetupRunning = false;
            if (result.Success)
            {
                _lastWindowsSandboxStatus = AppServerWindowsSandboxReadinessStatus.Unknown;
                SetWindowsSandboxStatus(UiLocalization.Text(
                    "WslAppServerSettingsForm.WindowsSandbox.Status.SetupCompleted",
                    "Windows Sandbox setup completed."));
                SetWindowsSandboxDetails(UiLocalization.Text(
                    "WslAppServerSettingsForm.WindowsSandbox.Detail.SetupCompleted",
                    "Setup completed. Rechecking Windows Sandbox readiness..."));
                SetWindowsSandboxBusy(false);
                this.BeginInvokeSafe(async () => await DetectWindowsSandboxAsync());
                return;
            }

            SetWindowsSandboxStatus(UiLocalization.Text(
                "WslAppServerSettingsForm.WindowsSandbox.Status.SetupFailed",
                "Windows Sandbox setup failed."));
            SetWindowsSandboxDetails(string.IsNullOrWhiteSpace(result.Error)
                ? UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Detail.SetupFailed", "Setup failed without a detailed error.")
                : result.Error);
            SetWindowsSandboxBusy(false);
        }

        private void SetWindowsSandboxBusy(bool busy)
        {
            _isWindowsSandboxOperationBusy = busy;
            UseWaitCursor = busy;
            RefreshWindowsSandboxState();
            if (_targetEditing)
            {
                _okButton.Enabled = !busy && !_isRuntimeOperationBusy;
            }
        }

        private void SetWindowsSandboxStatus(string text, bool announce = true)
        {
            TextBoxCaretHelper.SetTextAndMoveToStart(_windowsSandboxStatusTextBox, text ?? string.Empty);
            if (announce && !string.IsNullOrWhiteSpace(text))
            {
                _announce?.Invoke(text);
            }
        }

        private void SetWindowsSandboxDetails(string text)
        {
            TextBoxCaretHelper.SetTextAndMoveToStart(_windowsSandboxDetailsTextBox, text ?? string.Empty);
        }

    }
}
