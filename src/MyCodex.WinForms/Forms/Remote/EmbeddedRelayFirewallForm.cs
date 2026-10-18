using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class EmbeddedRelayFirewallForm : Form
    {
        private readonly IEmbeddedRelayFirewallService _service;
        private readonly Label _status;
        private readonly Button _refresh;
        private readonly Button _allow;
        private readonly Button _close;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private EmbeddedRelayFirewallStatus _current;
        private bool _busy;

        public EmbeddedRelayFirewallForm(IEmbeddedRelayFirewallService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            Text = UiLocalization.Text("SharedRemoteService.Firewall.Title", "Local network access");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(640, 240);
            MinimumSize = new Size(480, 200);
            _status = new Label { Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(12), TabStop = false };
            _refresh = CreateButton("TargetManagerForm.Button.Refresh", "Refresh(&R)", "TargetManagerForm.Accessible.Button.Refresh", "Refresh", 0, UiGlyph.Refresh);
            _allow = CreateButton("RemoteServiceManagementForm.Button.AllowFirewall", "Allow Local Network Access(&A)", "RemoteServiceManagementForm.Accessible.Button.AllowFirewall", "Allow local network access", 1, UiGlyph.Approval);
            _close = CreateButton("SharedRemoteService.Button.Close", "Close(&C)", "SharedRemoteService.Accessible.Close", "Close", 2, UiGlyph.Cancel);
            FormInteractionConventions.ConfigureRefreshButton(_refresh);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8), TabStop = false };
            buttons.Controls.AddRange(new Control[] { _refresh, _allow, _close });
            Controls.Add(_status);
            Controls.Add(buttons);
            _refresh.Click += async (s, e) => await RefreshAsync();
            _allow.Click += async (s, e) => await AllowAsync();
            _close.Click += (s, e) => Close();
            CancelButton = _close;
            Shown += async (s, e) => { await RefreshAsync(); if (!IsDisposed) (_allow.Enabled ? _allow : _close).Focus(); };
            FormClosing += (s, e) => { if (_busy) e.Cancel = true; };
            FormClosed += (s, e) => { _lifetime.Cancel(); _lifetime.Dispose(); };
            UiTheme.ApplyForm(this);
            UiTheme.ApplyLabel(_status, false);
            SetBusy(false);
        }

        internal async Task RefreshAsync()
        {
            if (_busy) return;
            SetBusy(true);
            try
            {
                _current = await Task.Run(() => _service.GetStatus(), _lifetime.Token);
                _status.Text = EmbeddedRelayFirewallStatusText.Build(_current);
            }
            catch (Exception ex)
            {
                _current = new EmbeddedRelayFirewallStatus { State = EmbeddedRelayFirewallState.Unavailable, ErrorMessage = RelaySecretRedactor.Redact(ex.Message) };
                _status.Text = EmbeddedRelayFirewallStatusText.Build(_current);
            }
            finally { SetBusy(false); }
        }

        internal async Task AllowAsync()
        {
            if (_busy || _current?.State != EmbeddedRelayFirewallState.RuleMissing) return;
            SetBusy(true);
            try
            {
                _status.Text = UiLocalization.Text("RemoteServiceManagementForm.Status.FirewallRequestingAdministrator", "Requesting administrator permission to update Windows Firewall...");
                var result = await _service.AllowPrivateLocalSubnetAsync(_lifetime.Token);
                if (result == null) throw new InvalidOperationException("Windows Firewall returned no result.");
                _current = result.Status ?? await Task.Run(() => _service.GetStatus(), _lifetime.Token);
                _status.Text = result.Canceled
                    ? UiLocalization.Text("RemoteServiceManagementForm.Status.FirewallCanceled", "Windows Firewall was not changed.")
                    : (result.Success ? string.Empty : UiLocalization.Text("RemoteServiceManagementForm.Message.FirewallAllowFailed", "Failed to allow local-network access through Windows Firewall.") + " ") +
                        EmbeddedRelayFirewallStatusText.Build(_current) + " " + RelaySecretRedactor.Redact(result.ErrorMessage);
            }
            catch (Exception ex) { _status.Text = RelaySecretRedactor.Redact(ex.Message); }
            finally { SetBusy(false); if (!IsDisposed) (_allow.Enabled ? _allow : _close).Focus(); }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _refresh.Enabled = _close.Enabled = !busy;
            _allow.Enabled = !busy && _current?.State == EmbeddedRelayFirewallState.RuleMissing;
        }

        private static Button CreateButton(string key, string fallback, string accessibleKey, string accessibleFallback, int tabIndex, UiGlyph glyph)
        {
            var button = new Button { Text = UiLocalization.Text(key, fallback), AccessibleName = UiLocalization.Text(accessibleKey, accessibleFallback), AutoSize = true, TabIndex = tabIndex, MinimumSize = new Size(90, 32) };
            UiTheme.ApplyDialogButton(button, glyph);
            return button;
        }
    }

    internal static class EmbeddedRelayFirewallStatusText
    {
        internal static string Build(EmbeddedRelayFirewallStatus status)
        {
            switch (status?.State ?? EmbeddedRelayFirewallState.Unknown)
            {
                case EmbeddedRelayFirewallState.Allowed:
                    return UiLocalization.Text("RemoteServiceManagementForm.Status.FirewallAllowed", "Windows Firewall allows the built-in Remote Service to receive connections from the private local subnet.");
                case EmbeddedRelayFirewallState.RuleMissing:
                    return UiLocalization.Text("RemoteServiceManagementForm.Status.FirewallMissing", "Windows Firewall may block phone access to the built-in Remote Service. Select Allow Local Network Access before pairing.");
                case EmbeddedRelayFirewallState.NonPrivateNetwork:
                    return UiLocalization.Text("RemoteServiceManagementForm.Status.FirewallNonPrivateNetwork", "Windows does not identify the active network as Private. Change it to Private before allowing phone access.");
                case EmbeddedRelayFirewallState.Unavailable:
                    var unavailable = UiLocalization.Text("RemoteServiceManagementForm.Status.FirewallUnavailable", "Windows Firewall status could not be checked.");
                    return string.IsNullOrWhiteSpace(status.ErrorMessage) ? unavailable : unavailable + " " + RelaySecretRedactor.Redact(status.ErrorMessage);
                default:
                    return UiLocalization.Text("RemoteServiceManagementForm.Status.FirewallChecking", "Checking Windows Firewall status...");
            }
        }
    }
}
