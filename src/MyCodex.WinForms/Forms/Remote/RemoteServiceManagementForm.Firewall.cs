using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class RemoteServiceManagementForm
    {
        private IEmbeddedRelayFirewallService _embeddedRelayFirewallService;
        private Label _firewallStateLabel;
        private Button _allowFirewallButton;
        private EmbeddedRelayFirewallStatus _firewallStatus;
        private bool _firewallStatusRefreshInProgress;

        internal string FirewallStateTextForTests
        {
            get { return _firewallStateLabel.Text; }
        }

        internal bool FirewallButtonEnabledForTests
        {
            get { return _allowFirewallButton.Enabled; }
        }

        internal Task RefreshFirewallStatusForTests()
        {
            return RefreshFirewallStatusAsync();
        }

        internal Task ClickAllowFirewallForTests()
        {
            return AllowFirewallAsync();
        }

        private void InitializeFirewallControls(
            IEmbeddedRelayFirewallService embeddedRelayFirewallService)
        {
            _embeddedRelayFirewallService = embeddedRelayFirewallService;
            _firewallStatus = new EmbeddedRelayFirewallStatus
            {
                State = EmbeddedRelayFirewallState.Unknown
            };
            _firewallStateLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 0),
                TabStop = false
            };
            UiTheme.ApplyLabel(_firewallStateLabel, false);
            _allowFirewallButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text(
                    "RemoteServiceManagementForm.Button.AllowFirewall",
                    "Allow Local Network Access(&A)"),
                UiLocalization.Text(
                    "RemoteServiceManagementForm.Accessible.Button.AllowFirewall",
                    "Allow local network access"),
                1,
                UiGlyph.Approval);
            RefreshFirewallControls();
        }

        private void BeginFirewallStatusRefresh()
        {
            if (_targetOnly || _embeddedRelayFirewallService == null || !IsEmbeddedMode())
            {
                return;
            }

            this.BeginInvokeSafe(
                async () => await RefreshFirewallStatusAsync().ConfigureAwait(true));
        }

        private async Task RefreshFirewallStatusAsync()
        {
            if (_embeddedRelayFirewallService == null ||
                !IsEmbeddedMode() ||
                _firewallStatusRefreshInProgress ||
                IsDisposed ||
                Disposing ||
                _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            _firewallStatusRefreshInProgress = true;
            _firewallStatus = new EmbeddedRelayFirewallStatus
            {
                State = EmbeddedRelayFirewallState.Unknown
            };
            RefreshFirewallControls();
            try
            {
                _firewallStatus = await Task.Run(
                    () => _embeddedRelayFirewallService.GetStatus(),
                    _lifetimeCancellation.Token).ConfigureAwait(true);
                Trace.TraceInformation(
                    "[RemoteRelay] Windows Firewall status checked. State={0}; Scope=program/private/local-subnet",
                    _firewallStatus == null
                        ? EmbeddedRelayFirewallState.Unknown
                        : _firewallStatus.State);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _firewallStatus = new EmbeddedRelayFirewallStatus
                {
                    State = EmbeddedRelayFirewallState.Unavailable,
                    ErrorMessage = RelaySecretRedactor.Redact(ex.Message)
                };
            }
            finally
            {
                _firewallStatusRefreshInProgress = false;
                RefreshFirewallControls();
            }
        }

        private async Task AllowFirewallAsync()
        {
            if (_isBusy ||
                _embeddedRelayFirewallService == null ||
                !IsEmbeddedMode() ||
                _firewallStatus == null ||
                _firewallStatus.State != EmbeddedRelayFirewallState.RuleMissing)
            {
                return;
            }

            var succeeded = false;
            try
            {
                SetBusy(true);
                SetFirewallStateText(UiLocalization.Text(
                    "RemoteServiceManagementForm.Status.FirewallRequestingAdministrator",
                    "Requesting administrator permission to update Windows Firewall..."));
                var result = await _embeddedRelayFirewallService
                    .AllowPrivateLocalSubnetAsync(_lifetimeCancellation.Token)
                    .ConfigureAwait(true);
                if (result == null)
                {
                    throw new InvalidOperationException("Windows Firewall returned no result.");
                }

                if (result.Canceled)
                {
                    await RefreshFirewallStatusAsync().ConfigureAwait(true);
                    SpeakFirewallState(UiLocalization.Text(
                        "RemoteServiceManagementForm.Status.FirewallCanceled",
                        "Windows Firewall was not changed."));
                    return;
                }

                if (!result.Success)
                {
                    _firewallStatus = result.Status ?? new EmbeddedRelayFirewallStatus
                    {
                        State = EmbeddedRelayFirewallState.Unavailable
                    };
                    RefreshFirewallControls();
                    ShowError(
                        UiLocalization.Text(
                            "RemoteServiceManagementForm.Message.FirewallAllowFailed",
                            "Failed to allow local-network access through Windows Firewall.") +
                        " " + RelaySecretRedactor.Redact(result.ErrorMessage));
                    return;
                }

                _firewallStatus = result.Status;
                if (_firewallStatus == null ||
                    _firewallStatus.State != EmbeddedRelayFirewallState.Allowed)
                {
                    await RefreshFirewallStatusAsync().ConfigureAwait(true);
                }

                succeeded = _firewallStatus != null &&
                    _firewallStatus.State == EmbeddedRelayFirewallState.Allowed;
                RefreshFirewallControls();
                var message = UiLocalization.Text(
                    "RemoteServiceManagementForm.Message.FirewallAllowSucceeded",
                    "Windows Firewall now allows devices on the private local network to connect to the built-in Remote Service.");
                SpeakFirewallState(message);
                ShowInfo(
                    message,
                    UiLocalization.Text("RemoteServiceManagementForm.Title", "Phone Connection"));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ShowError(
                    UiLocalization.Text(
                        "RemoteServiceManagementForm.Message.FirewallAllowFailed",
                        "Failed to allow local-network access through Windows Firewall.") +
                    " " + RelaySecretRedactor.Redact(ex.Message));
            }
            finally
            {
                SetBusy(false);
                FocusControl(succeeded && _generatePairingCodeButton.Enabled
                    ? (Control)_generatePairingCodeButton
                    : _allowFirewallButton.Enabled
                        ? _allowFirewallButton
                        : _serverSettingsButton);
            }
        }

        private void RefreshFirewallControls()
        {
            if (_firewallStateLabel == null || _allowFirewallButton == null)
            {
                return;
            }

            var visible = !_targetOnly && _embeddedRelayFirewallService != null && IsEmbeddedMode();
            _firewallStateLabel.Visible = visible;
            _allowFirewallButton.Visible = visible;
            if (!visible)
            {
                _allowFirewallButton.Enabled = false;
                return;
            }

            SetFirewallStateText(BuildFirewallStatusText());
            _allowFirewallButton.Enabled = !_isBusy &&
                !_firewallStatusRefreshInProgress &&
                _firewallStatus != null &&
                _firewallStatus.State == EmbeddedRelayFirewallState.RuleMissing;
        }

        private string BuildFirewallStatusText()
        {
            return EmbeddedRelayFirewallStatusText.Build(_firewallStatus);
        }

        private string AppendFirewallGuidance(string message)
        {
            if (_embeddedRelayFirewallService == null || !IsEmbeddedMode() || _firewallStatus == null)
            {
                return message;
            }

            switch (_firewallStatus.State)
            {
                case EmbeddedRelayFirewallState.RuleMissing:
                    return message + " " + UiLocalization.Text(
                        "RemoteServiceManagementForm.Message.FirewallGuidance",
                        "Before pairing, select Allow Local Network Access so the phone can reach this PC.");
                case EmbeddedRelayFirewallState.NonPrivateNetwork:
                    return message + " " + UiLocalization.Text(
                        "RemoteServiceManagementForm.Message.FirewallNonPrivateNetworkGuidance",
                        "Windows does not currently identify this network as Private; change it to Private before pairing.");
                default:
                    return message;
            }
        }

        private void SetFirewallStateText(string text)
        {
            _firewallStateLabel.Text = text ?? string.Empty;
            _firewallStateLabel.AccessibleName = _firewallStateLabel.Text;
        }

        private void SpeakFirewallState(string text)
        {
            if (_screenReaderService != null && !string.IsNullOrWhiteSpace(text))
            {
                _screenReaderService.Speak(text, false, false, true);
            }
        }
    }
}
