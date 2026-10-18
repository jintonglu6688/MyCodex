using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class RemoteServiceManagementForm : Form
    {
        private readonly IRemoteServiceConfigStore _configStore;
        private readonly IRelayHostClient _relayClient;
        private readonly IRelayHostRealtimeClient _realtimeClient;
        private readonly IEmbeddedRelayService _embeddedRelayService;
        private readonly IRemoteIdentityStore _identityStore;
        private readonly IRemoteBindingStore _bindingStore;
        private readonly bool _ownsRealtimeClient;
        private readonly Func<CancellationToken, Task> _applyConfiguredModeAsync;
        private readonly RemoteServiceConnectionWorkflow _workflow;
        private RemoteServiceConfigSnapshot _snapshot;
        private readonly Label _serverStateLabel;
        private readonly Button _embeddedRelayActionButton;
        private readonly Button _serverSettingsButton;
        private readonly Button _generatePairingCodeButton;
        private readonly ListView _devicesListView;
        private readonly Button _refreshDevicesButton;
        private readonly ContextMenu _devicesContextMenu;
        private readonly MenuItem _reviewPairingRequestMenuItem;
        private readonly MenuItem _removeDeviceMenuItem;
        private readonly Button _diagnosticsButton;
        private readonly Button _closeButton;
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private bool _isBusy;
        private Control _initialFocusControl;
        private readonly bool _targetOnly;
        private readonly Action<IWin32Window> _showConnectionSettings;
        private RelayRealtimeConnectionState _realtimeState = RelayRealtimeConnectionState.Disconnected;
        private string _realtimeStatusMessage;

        public RemoteServiceManagementForm(
            IRemoteServiceConfigStore configStore,
            IRelayHostClient relayClient,
            IScreenReaderService screenReaderService,
            IRelayHostRealtimeClient realtimeClient = null,
            bool ownsRealtimeClient = true,
            IEmbeddedRelayService embeddedRelayService = null,
            IRemoteIdentityStore identityStore = null,
            ISecureRemotePairingWorkflow pairingWorkflow = null,
            IRemoteBindingStore bindingStore = null,
            IEmbeddedRelayFirewallService embeddedRelayFirewallService = null,
            Func<CancellationToken, Task> applyConfiguredModeAsync = null,
            bool targetOnly = false,
            Action<IWin32Window> showConnectionSettings = null)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _relayClient = relayClient ?? throw new ArgumentNullException(nameof(relayClient));
            _realtimeClient = realtimeClient;
            _embeddedRelayService = embeddedRelayService;
            _identityStore = identityStore ?? new CngRemoteIdentityStore();
            _screenReaderService = screenReaderService;
            _ownsRealtimeClient = ownsRealtimeClient;
            _applyConfiguredModeAsync = applyConfiguredModeAsync;
            _targetOnly = targetOnly;
            _showConnectionSettings = showConnectionSettings;
            _workflow = new RemoteServiceConnectionWorkflow(_configStore, _relayClient);
            _bindingStore = bindingStore;
            _pairingWorkflow = pairingWorkflow ?? new SecureRemotePairingWorkflow(
                _relayClient,
                _identityStore,
                _bindingStore ?? throw new ArgumentNullException(nameof(bindingStore)));
            _snapshot = _configStore.Load() ?? new RemoteServiceConfigSnapshot();
            RemoteServiceConfigPlanner.EnsureDefaults(_snapshot, Environment.MachineName);

            Text = UiLocalization.Text("RemoteServiceManagementForm.Title", "Phone Connection");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(860, 620);
            ShowInTaskbar = false;
            KeyPreview = true;
            TabStop = false;
            UiTheme.ApplyForm(this);

            _serverStateLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(_serverStateLabel, false);

            _embeddedRelayActionButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("RemoteServiceManagementForm.Button.StartEmbeddedRelay", "Start Remote Service(&T)"),
                UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.StartEmbeddedRelay", "Start remote service"),
                0,
                UiGlyph.Ok);
            _generatePairingCodeButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("RemoteServiceManagementForm.Button.GeneratePairingCode", "Generate Pairing Code(&G)"),
                UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.GeneratePairingCode", "Generate pairing code"),
                2,
                UiGlyph.NewSession);
            InitializeFirewallControls(embeddedRelayFirewallService);
            _serverSettingsButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("RemoteServiceManagementForm.Button.ServerSettings", "Connection Settings(&S)..."),
                UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.ServerSettings", "Connection settings"),
                0,
                UiGlyph.FileChange);
            _devicesListView = CreateDevicesListView();
            _reviewPairingRequestMenuItem = new MenuItem(
                UiLocalization.Text(
                    "RemoteServiceManagementForm.Menu.ReviewPairingRequest",
                    "Review Pairing Request(&R)..."));
            _removeDeviceMenuItem = new MenuItem(
                UiLocalization.Text(
                    "RemoteServiceManagementForm.Menu.RemoveDevice",
                    "Remove Device(&D)..."));
            _devicesContextMenu = new ContextMenu(
                new[]
                {
                    _reviewPairingRequestMenuItem,
                    _removeDeviceMenuItem
                });
            _devicesListView.ContextMenu = _devicesContextMenu;
            _refreshDevicesButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("RemoteServiceManagementForm.Button.RefreshDevices", "Refresh(&F)"),
                UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.RefreshDevices", "Refresh"),
                0,
                UiGlyph.Refresh);
            _diagnosticsButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("RemoteServiceManagementForm.Button.Diagnostics", "Diagnostics(&I)"),
                UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.Diagnostics", "Diagnostics"),
                0,
                UiGlyph.Info);
            _closeButton = UiControlFactory.CreateDialogButton(
                UiLocalization.Text("RemoteServiceManagementForm.Button.Close", "C&lose"),
                UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.Close", "Close"),
                0,
                UiGlyph.Cancel);

            _refreshDevicesButton.TabStop = false;
            _diagnosticsButton.TabStop = false;
            _closeButton.TabStop = false;

            _embeddedRelayActionButton.Click += async (sender, e) => await ToggleEmbeddedRelayAsync();
            _serverSettingsButton.Click += ServerSettingsButton_Click;
            _generatePairingCodeButton.Click += async (sender, e) => await GeneratePairingCodeAsync();
            _allowFirewallButton.Click += async (sender, e) => await AllowFirewallAsync();
            _refreshDevicesButton.Click += async (sender, e) => await RefreshDevicesAsync(_devicesListView);
            _reviewPairingRequestMenuItem.Click += async (sender, e) => await ReviewSelectedPairingClaimAsync();
            _removeDeviceMenuItem.Click += async (sender, e) => await RemoveSelectedDeviceAsync();
            _diagnosticsButton.Click += (sender, e) => ShowDiagnostics();
            _closeButton.Click += (sender, e) => Close();
            _devicesListView.SelectedIndexChanged += (sender, e) => RefreshButtons();
            _devicesListView.DoubleClick += async (sender, e) => await ReviewSelectedPairingClaimAsync();
            _devicesListView.MouseDown += DevicesListView_MouseDown;
            _devicesContextMenu.Popup += (sender, e) => RefreshDeviceContextMenu();
            _pairingPollTimer = new System.Windows.Forms.Timer
            {
                Interval = 2000
            };
            _pairingPollTimer.Tick += async (sender, e) => await PollPairingClaimsAsync();
            if (_realtimeClient != null)
            {
                _realtimeClient.StatusChanged += RealtimeClient_StatusChanged;
            }

            BuildLayout();
            UpdateMainState();
            RefreshButtons();
            CancelButton = _closeButton;
        }

        internal Action<string> ClipboardWriterForTests { get; set; }

        internal Action<string, string> MessageBoxForTests { get; set; }

        internal Control InitialFocusControlForTests
        {
            get { return _initialFocusControl ?? (_targetOnly ? _embeddedRelayActionButton : _serverSettingsButton); }
        }

        internal int DeviceCountForTests
        {
            get { return _devicesListView.Items.Count; }
        }

        internal string DeviceListTextForTests
        {
            get { return string.Join(" ", _devicesListView.Items.Cast<ListViewItem>().Select(item => item.Text).ToArray()); }
        }

        internal ContextMenu DevicesContextMenuForTests
        {
            get { return _devicesContextMenu; }
        }


        internal string ServerStateTextForTests
        {
            get { return _serverStateLabel.Text; }
        }

        internal void SetDevicesForTests(IEnumerable<RelayDeviceSummary> devices)
        {
            BindDevices(devices ?? new RelayDeviceSummary[0]);
        }

        internal void SelectFirstDeviceForTests()
        {
            if (_devicesListView.Items.Count > 0)
            {
                CreateControl();
                var handle = _devicesListView.Handle;
                _devicesListView.Items[0].Selected = true;
                _devicesListView.Items[0].Focused = true;
                RefreshButtons();
            }
        }

        internal Task ClickGeneratePairingCodeForTests()
        {
            return GeneratePairingCodeAsync();
        }

        internal Task ClickStartEmbeddedRelayForTests()
        {
            return StartEmbeddedRelayAsync();
        }

        internal Task ClickStopEmbeddedRelayForTests()
        {
            return StopEmbeddedRelayAsync();
        }

        internal Task ClickRefreshDevicesForTests()
        {
            return RefreshDevicesAsync(_devicesListView);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FocusControl(InitialFocusControlForTests);
            BeginFirewallStatusRefresh();
            BeginDeviceRefreshIfConfigured();
            BeginHostRealtimeConnectionIfConfigured();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                BeginDeviceRefreshIfConfigured(ActiveControl ?? _devicesListView);
                return true;
            }

            if (keyData == Keys.Enter && _devicesListView.ContainsFocus && GetSelectedPairingClaim() != null)
            {
                _ = ReviewSelectedPairingClaimAsync();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopPairingPolling();
            _pairingPollTimer.Dispose();
            _devicesContextMenu.Dispose();
            _lifetimeCancellation.Cancel();
            if (_realtimeClient != null)
            {
                _realtimeClient.StatusChanged -= RealtimeClient_StatusChanged;
                if (_ownsRealtimeClient)
                {
                    _realtimeClient.Dispose();
                }
            }

            base.OnFormClosed(e);
        }

        private void DevicesListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var item = _devicesListView.GetItemAt(e.X, e.Y);
            if (item != null)
            {
                item.Selected = true;
                item.Focused = true;
            }
        }

        private async void ServerSettingsButton_Click(object sender, EventArgs e)
        {
            if (_showConnectionSettings != null)
            {
                _showConnectionSettings(this);
                ReloadSnapshot();
                UpdateMainState();
                FocusControl(_serverSettingsButton);
                return;
            }

            using (var form = new RemoteServiceSettingsForm(_configStore, _relayClient, null))
            {
                var result = form.ShowDialog(this);
                var applied = false;
                var applyAttempted = result == DialogResult.OK && _applyConfiguredModeAsync != null;
                if (applyAttempted)
                {
                    try
                    {
                        SetBusy(true);
                        await _applyConfiguredModeAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
                        applied = true;
                    }
                    catch (Exception ex)
                    {
                        ShowError(
                            UiLocalization.Text(
                                "RemoteServiceManagementForm.Message.ApplyServerSettingsFailed",
                                "Failed to apply server settings.") + " " + RelaySecretRedactor.Redact(ex.Message));
                    }
                    finally
                    {
                        SetBusy(false);
                    }
                }

                ReloadSnapshot();
                UpdateMainState();
                BeginFirewallStatusRefresh();
                if (!applyAttempted)
                {
                    BeginHostRealtimeConnectionIfConfigured();
                }

                if (applied)
                {
                    _screenReaderService?.Speak(_serverStateLabel.Text, false, false, true);
                }

                FocusControl(applied && HasSavedConfig() ? _generatePairingCodeButton : _serverSettingsButton);
            }
        }

        private async Task StartEmbeddedRelayAsync()
        {
            if (_isBusy || _embeddedRelayService == null)
            {
                return;
            }

            try
            {
                SetBusy(true);
                _serverStateLabel.Text = UiLocalization.Text("RemoteServiceManagementForm.Status.StartingEmbeddedRelay", "Remote service is starting...");
                var result = await _embeddedRelayService.StartAsync(_snapshot, _lifetimeCancellation.Token).ConfigureAwait(true);
                if (!result.Success)
                {
                    ShowError(UiLocalization.Text("RemoteServiceManagementForm.Message.StartEmbeddedRelayFailed", "Failed to start remote service.") + " " + RelaySecretRedactor.Redact(result.ErrorMessage));
                    return;
                }

                ReloadSnapshot();
                SaveRemoteEnabled(true);
                UpdateMainState();
                await RefreshFirewallStatusAsync().ConfigureAwait(true);
                await EnsureHostRealtimeConnectionAsync().ConfigureAwait(true);
                ShowInfo(
                    AppendFirewallGuidance(UiLocalization.Text("RemoteServiceManagementForm.Message.StartEmbeddedRelaySucceeded", "Remote service started.")),
                    UiLocalization.Text("RemoteServiceManagementForm.Title", "Phone Connection"));
            }
            catch (Exception ex)
            {
                ShowError(UiLocalization.Text("RemoteServiceManagementForm.Message.StartEmbeddedRelayFailed", "Failed to start remote service.") + " " + RelaySecretRedactor.Redact(ex.Message));
            }
            finally
            {
                SetBusy(false);
                FocusControl(_embeddedRelayActionButton);
            }
        }

        private async Task StopEmbeddedRelayAsync()
        {
            if (_isBusy || _embeddedRelayService == null)
            {
                return;
            }

            try
            {
                SetBusy(true);
                await _embeddedRelayService.StopAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
                if (_realtimeClient != null)
                {
                    await _realtimeClient.StopAsync().ConfigureAwait(true);
                }

                ReloadSnapshot();
                SaveRemoteEnabled(false);
                UpdateMainState();
            }
            catch (Exception ex)
            {
                ShowError(UiLocalization.Text("RemoteServiceManagementForm.Message.StopEmbeddedRelayFailed", "Failed to stop remote service.") + " " + RelaySecretRedactor.Redact(ex.Message));
            }
            finally
            {
                SetBusy(false);
                FocusControl(_embeddedRelayActionButton);
            }
        }

        private Task ToggleEmbeddedRelayAsync()
        {
            if (_targetOnly) return ToggleTargetConnectionAsync();
            return IsEmbeddedRelayRunning() && _snapshot.RemoteEnabled != false
                ? StopEmbeddedRelayAsync()
                : StartEmbeddedRelayAsync();
        }

        internal async Task ToggleTargetConnectionAsync()
        {
            if (_isBusy) return;
            try
            {
                SetBusy(true);
                SaveRemoteEnabled(_snapshot.RemoteEnabled == false);
                if (_applyConfiguredModeAsync != null)
                    await _applyConfiguredModeAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
                ReloadSnapshot();
                UpdateMainState();
            }
            catch (Exception ex)
            {
                _serverStateLabel.Text = RelaySecretRedactor.Redact(ex.Message);
            }
            finally { SetBusy(false); FocusControl(_embeddedRelayActionButton); }
        }

        private void ShowDiagnostics()
        {
            if (_embeddedRelayService == null)
            {
                return;
            }

            var diagnostics = _embeddedRelayService.GetDiagnostics(_snapshot);
            var lines = diagnostics == null || diagnostics.Lines == null
                ? new string[0]
                : diagnostics.Lines.ToArray();
            using (var form = new RemoteServiceDiagnosticsForm(string.Join(Environment.NewLine, lines)))
            {
                form.ShowDialog(this);
            }
            FocusControl(_diagnosticsButton);
        }

        private async Task GeneratePairingCodeAsync()
        {
            if (_isBusy)
            {
                return;
            }

            try
            {
                SetBusy(true);
                await RefreshFirewallStatusAsync().ConfigureAwait(true);
                var identity = _identityStore.LoadOrCreateHostIdentity(_snapshot.HostId);
                var registration = await _workflow.ConnectAndRegisterAsync(
                    _snapshot,
                    null,
                    Environment.MachineName,
                    identity,
                    _lifetimeCancellation.Token).ConfigureAwait(true);
                if (!registration.Success)
                {
                    ShowError(UiLocalization.Text("RemoteServiceManagementForm.Message.GeneratePairingCodeFailed", "Failed to generate pairing code. Check Connection Settings.") + " " + RelaySecretRedactor.Redact(registration.Error != null ? registration.Error.Message : string.Empty));
                    FocusControl(_generatePairingCodeButton);
                    return;
                }

                _snapshot = registration.Snapshot;
                var config = BuildClientConfig();
                var invite = await _pairingWorkflow.CreatePairingInviteAsync(
                    config,
                    _snapshot.HostId,
                    _snapshot.ThisPcName,
                    _lifetimeCancellation.Token).ConfigureAwait(true);
                SaveRemoteEnabled(true);
                _activePairingInvite = invite;
                _verifiedPairingClaims.Clear();
                _pairingPollingExpiresAtUtc = UnixMillisecondsToUtc(invite.Package.ExpiresAt);
                _pairingPollTimer.Start();
                WriteClipboard(invite.PairingUri);
                UpdateMainState();
                await EnsureHostRealtimeConnectionAsync().ConfigureAwait(true);
                ShowInfo(
                    AppendFirewallGuidance(UiLocalization.Text("RemoteServiceManagementForm.Message.PairingCodeCopied", "Pairing code copied to clipboard. Import it on your phone.")),
                    UiLocalization.Text("RemoteServiceManagementForm.Title", "Phone Connection"));
                FocusControl(_generatePairingCodeButton);
            }
            catch (Exception ex)
            {
                ShowError(UiLocalization.Text("RemoteServiceManagementForm.Message.GeneratePairingCodeFailed", "Failed to generate pairing code. Check Connection Settings.") + " " + RelaySecretRedactor.Redact(ex.Message));
                FocusControl(_generatePairingCodeButton);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task RefreshDevicesAsync(Control focusAfterRefresh)
        {
            if (_isBusy)
            {
                return;
            }

            Control focusTarget = focusAfterRefresh ?? _devicesListView;
            try
            {
                SetBusy(true);
                var devices = await _relayClient.ListDevicesAsync(BuildClientConfig(), _snapshot.HostId, _lifetimeCancellation.Token).ConfigureAwait(true);
                BindDevices(devices);
            }
            catch (Exception ex)
            {
                ShowError(UiLocalization.Text("RemoteServiceManagementForm.Status.DevicesRefreshFailed", "Failed to refresh devices.") + " " + RelaySecretRedactor.Redact(ex.Message));
            }
            finally
            {
                SetBusy(false);
                FocusControl(focusTarget);
            }
        }

        private async Task RemoveSelectedDeviceAsync()
        {
            if (_isBusy || _devicesListView.SelectedItems.Count == 0)
            {
                return;
            }

            var device = _devicesListView.SelectedItems[0].Tag as RelayDeviceSummary;
            if (device == null)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(UiLocalization.Text("RemoteServiceManagementForm.Message.ConfirmRevokeDevice.Template", "Revoke access for \"{0}\"?"), _devicesListView.SelectedItems[0].Text),
                UiLocalization.Text("RemoteServiceManagementForm.Title", "Phone Connection"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                SetBusy(true);
                // Local binding revocation is the authorization linearization point.  Relay cleanup is best effort.
                if (_bindingStore == null)
                {
                    throw new InvalidOperationException("The local remote binding store is unavailable.");
                }
                _bindingStore.Revoke(_snapshot.TenantId, _snapshot.HostId, device.DeviceId);
                Exception restartError = null;
                Exception relayError = null;
                Exception refreshError = null;
                try
                {
                    if (_realtimeClient != null)
                    {
                        await _realtimeClient.StopAsync().ConfigureAwait(true);
                        await _realtimeClient.StartAsync(BuildClientConfig(), _snapshot.HostId, _lifetimeCancellation.Token).ConfigureAwait(true);
                    }
                }
                catch (Exception ex) { restartError = ex; }

                try { await _relayClient.RevokeDeviceAsync(BuildClientConfig(), _snapshot.HostId, device.DeviceId, _lifetimeCancellation.Token).ConfigureAwait(true); }
                catch (Exception ex) { relayError = ex; }
                try
                {
                    var devices = await _relayClient.ListDevicesAsync(BuildClientConfig(), _snapshot.HostId, _lifetimeCancellation.Token).ConfigureAwait(true);
                    BindDevices(devices);
                }
                catch (Exception ex) { refreshError = ex; }

                if (restartError != null || relayError != null || refreshError != null)
                {
                    var state = UiLocalization.Text("RemoteServiceManagementForm.Status.DeviceRevokedLocally", "Device access was revoked locally.");
                    if (restartError != null) state += " " + UiLocalization.Text("RemoteServiceManagementForm.Status.ChannelRestartPending", "Remaining device channels need reconnection.");
                    if (relayError != null) state += " " + UiLocalization.Text("RemoteServiceManagementForm.Status.RelayRevokePending", "Relay cleanup is pending.");
                    if (refreshError != null) state += " " + UiLocalization.Text("RemoteServiceManagementForm.Status.RefreshPending", "The device list may be out of date.");
                    _serverStateLabel.Text = state;
                    _screenReaderService?.Speak(state, true, false, true);
                }
                FocusControl(_devicesListView.Items.Count > 0 ? (Control)_devicesListView : _refreshDevicesButton);
            }
            catch (Exception ex)
            {
                ShowError(UiLocalization.Text("RemoteServiceManagementForm.Status.DeviceRevokeFailed", "Failed to revoke device.") + " " + RelaySecretRedactor.Redact(ex.Message));
                FocusControl(_devicesListView);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void BindDevices(IEnumerable<RelayDeviceSummary> devices)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            var pendingItems = _devicesListView.Items.Cast<ListViewItem>()
                .Where(item => item.Tag is VerifiedPairingClaim)
                .ToArray();
            _devicesListView.BeginUpdate();
            try
            {
                _devicesListView.Items.Clear();
                foreach (var device in devices)
                {
                    var binding = FindDeviceBinding(device);
                    var item = new ListViewItem(GetDeviceDisplayName(device, binding));
                    item.SubItems.Add(_snapshot.ThisPcName ?? _snapshot.HostId ?? string.Empty);
                    item.SubItems.Add(FirstNonBlank(device.Platform, binding?.Platform));
                    var boundAtUtc = GetDeviceBoundAtUtc(device);
                    item.SubItems.Add(boundAtUtc == default(DateTime) ? string.Empty : boundAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                    item.SubItems.Add(device.Online
                        ? UiLocalization.Text("RemoteServiceManagementForm.DeviceStatus.Online", "Online")
                        : UiLocalization.Text("RemoteServiceManagementForm.DeviceStatus.Offline", "Offline"));
                    item.Tag = device;
                    _devicesListView.Items.Add(item);
                }

                _devicesListView.Items.AddRange(pendingItems);
            }
            finally
            {
                _devicesListView.EndUpdate();
            }

            RefreshButtons();
        }

        private RemoteDeviceBinding FindDeviceBinding(RelayDeviceSummary device)
        {
            if (_bindingStore == null || string.IsNullOrWhiteSpace(device?.DeviceId))
            {
                return null;
            }

            return _bindingStore.FindActive(
                FirstNonBlank(device.TenantId, _snapshot.TenantId),
                FirstNonBlank(device.HostId, _snapshot.HostId),
                device.DeviceId);
        }

        private static string FirstNonBlank(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first)
                ? first
                : second ?? string.Empty;
        }

        private static DateTime GetDeviceBoundAtUtc(RelayDeviceSummary device)
        {
            if (device.BoundAtUtc != default(DateTime))
            {
                return device.BoundAtUtc;
            }

            if (device.ApprovedAt <= 0)
            {
                return default(DateTime);
            }

            try
            {
                return UnixMillisecondsToUtc(device.ApprovedAt);
            }
            catch (ArgumentOutOfRangeException)
            {
                return default(DateTime);
            }
        }

        private static string GetDeviceDisplayName(
            RelayDeviceSummary device,
            RemoteDeviceBinding binding)
        {
            var displayName = FirstNonBlank(device.DisplayName, binding?.DeviceName);
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return displayName;
            }

            var deviceId = device.DeviceId ?? string.Empty;
            var suffix = deviceId.Length > 8
                ? deviceId.Substring(deviceId.Length - 8)
                : deviceId;
            return string.Format(
                UiLocalization.Text(
                    "RemoteServiceManagementForm.DeviceNameFallback.Template",
                    "Device {0}"),
                suffix);
        }

        private void ReloadSnapshot()
        {
            _snapshot = _configStore.Load() ?? new RemoteServiceConfigSnapshot();
            RemoteServiceConfigPlanner.EnsureDefaults(_snapshot, Environment.MachineName);
        }

        private void SaveRemoteEnabled(bool enabled)
        {
            ReloadSnapshot();
            _snapshot.RemoteEnabled = enabled;
            _configStore.Save(_snapshot);
        }

        private void UpdateMainState()
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            var configured = HasSavedConfig();
            _serverStateLabel.Text = BuildServerStatusText(configured);
            _initialFocusControl = _devicesListView;
            RefreshButtons();
        }

        private void RefreshButtons()
        {
            var configured = HasSavedConfig();
            var embeddedAvailable = _embeddedRelayService != null && IsEmbeddedMode();
            var embeddedRunning = embeddedAvailable && IsEmbeddedRelayRunning();
            _embeddedRelayActionButton.Visible = _targetOnly || embeddedAvailable;
            _embeddedRelayActionButton.Enabled = (_targetOnly || embeddedAvailable) && !_isBusy;
            RefreshEmbeddedRelayActionButton(embeddedRunning);
            _diagnosticsButton.Visible = embeddedAvailable;
            _diagnosticsButton.Enabled = embeddedAvailable && !_isBusy;
            _serverSettingsButton.Enabled = !_isBusy;
            _serverSettingsButton.Visible = !_targetOnly || _showConnectionSettings != null;
            _generatePairingCodeButton.Enabled = configured && !_isBusy;
            _refreshDevicesButton.Enabled = configured && !_isBusy;
            RefreshFirewallControls();
            if (_targetOnly)
            {
                _allowFirewallButton.Visible = false;
                _firewallStateLabel.Visible = false;
            }
            RefreshDeviceContextMenu();
        }

        private void RefreshDeviceContextMenu()
        {
            var selectedTag = _devicesListView.SelectedItems.Count == 1
                ? _devicesListView.SelectedItems[0].Tag
                : null;
            _reviewPairingRequestMenuItem.Enabled =
                selectedTag is VerifiedPairingClaim && !_isBusy;
            _removeDeviceMenuItem.Enabled =
                selectedTag is RelayDeviceSummary && !_isBusy;
        }

        private void RefreshEmbeddedRelayActionButton(bool embeddedRunning)
        {
            if (_targetOnly)
            {
                _embeddedRelayActionButton.Text = _snapshot.RemoteEnabled == false
                    ? UiLocalization.Text("RemoteServiceManagementForm.Button.EnableTarget", "Enable phone connection(&T)")
                    : UiLocalization.Text("RemoteServiceManagementForm.Button.DisableTarget", "Disable phone connection(&T)");
                _embeddedRelayActionButton.AccessibleName = _snapshot.RemoteEnabled == false
                    ? UiLocalization.Text("RemoteServiceManagementForm.Accessible.EnableTarget", "Enable phone connection")
                    : UiLocalization.Text("RemoteServiceManagementForm.Accessible.DisableTarget", "Disable phone connection");
                return;
            }
            if (embeddedRunning && _snapshot.RemoteEnabled != false)
            {
                _embeddedRelayActionButton.Text = UiLocalization.Text("RemoteServiceManagementForm.Button.StopEmbeddedRelay", "Stop Remote Service(&P)");
                _embeddedRelayActionButton.AccessibleName = UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.StopEmbeddedRelay", "Stop remote service");
                UiTheme.ApplyDialogButton(_embeddedRelayActionButton, UiGlyph.Stop);
                return;
            }

            _embeddedRelayActionButton.Text = UiLocalization.Text("RemoteServiceManagementForm.Button.StartEmbeddedRelay", "Start Remote Service(&T)");
            _embeddedRelayActionButton.AccessibleName = UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.StartEmbeddedRelay", "Start remote service");
            UiTheme.ApplyDialogButton(_embeddedRelayActionButton, UiGlyph.Ok);
        }

        private RelayHostConfig BuildClientConfig()
        {
            return RemoteServiceConfigPlanner.BuildRelayHostConfig(_snapshot, _configStore.UnprotectAccessSecret(_snapshot));
        }

        private string BuildServerStatusText(bool configured)
        {
            if (_targetOnly && _snapshot.RemoteEnabled == false)
                return UiLocalization.Text("RemoteServiceManagementForm.Status.TargetDisabled", "Phone connection for this target is disabled.");
            if (_embeddedRelayService != null && IsEmbeddedMode())
            {
                var status = GetEmbeddedRelayStatus();
                if (status != null)
                {
                    switch (status.State)
                    {
                        case EmbeddedRelayRunState.Running:
                            if (_snapshot.RemoteEnabled == false)
                            {
                                return UiLocalization.Text("RemoteServiceManagementForm.Status.SharedRunningTargetDisabled",
                                    "Phone connection for this target is disabled. Other targets' connections remain available.");
                            }

                            var endpoint = string.IsNullOrWhiteSpace(status.RelayHost) || status.RelayPort <= 0
                                ? string.Empty
                                : status.RelayHost + ":" + status.RelayPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            return string.IsNullOrWhiteSpace(endpoint)
                                ? UiLocalization.Text("RemoteServiceManagementForm.Status.EmbeddedRunning", "Status: running.")
                                : string.Format(UiLocalization.Text("RemoteServiceManagementForm.Status.EmbeddedRunningWithEndpoint.Template", "Status: running. Local address: {0}."), endpoint);
                        case EmbeddedRelayRunState.Stopped:
                            return UiLocalization.Text("RemoteServiceManagementForm.Status.EmbeddedStopped", "Status: stopped.");
                        case EmbeddedRelayRunState.Failed:
                            return string.IsNullOrWhiteSpace(status.Message)
                                ? UiLocalization.Text("RemoteServiceManagementForm.Status.EmbeddedFailed", "Status: failed.")
                                : status.Message;
                        default:
                            return UiLocalization.Text("RemoteServiceManagementForm.Status.EmbeddedNotStarted", "Status: not started.");
                    }
                }
            }

            if (!configured)
            {
                return UiLocalization.Text("RemoteServiceManagementForm.Status.ConfigMissing", "Open Connection Settings to configure the service.");
            }

            var baseText = UiLocalization.Text("RemoteServiceManagementForm.Status.ServerConfigured", "Server is configured.");
            var realtimeText = RealtimeStateText();
            return string.IsNullOrWhiteSpace(realtimeText) ? baseText : baseText + " " + realtimeText;
        }

        private string RealtimeStateText()
        {
            switch (_realtimeState)
            {
                case RelayRealtimeConnectionState.Connecting:
                    return UiLocalization.Text("RemoteServiceManagementForm.Status.HostChannelConnecting", "Windows channel is connecting.");
                case RelayRealtimeConnectionState.Connected:
                    return UiLocalization.Text("RemoteServiceManagementForm.Status.HostChannelConnected", "Windows channel is connected.");
                case RelayRealtimeConnectionState.Failed:
                    var failed = UiLocalization.Text("RemoteServiceManagementForm.Status.HostChannelFailed", "Windows channel connection failed.");
                    return string.IsNullOrWhiteSpace(_realtimeStatusMessage) ? failed : failed + " " + _realtimeStatusMessage;
                default:
                    return UiLocalization.Text("RemoteServiceManagementForm.Status.HostChannelDisconnected", "Windows channel is not connected.");
            }
        }

        private void BeginHostRealtimeConnectionIfConfigured()
        {
            if (!CanContactRelay())
            {
                return;
            }

            this.BeginInvokeSafe(async () => await EnsureHostRealtimeConnectionAsync().ConfigureAwait(true));
        }

        private void BeginDeviceRefreshIfConfigured(Control focusAfterRefresh = null)
        {
            if (!CanContactRelay())
            {
                return;
            }

            this.BeginInvokeSafe(async () => await RefreshDevicesAsync(focusAfterRefresh ?? _devicesListView).ConfigureAwait(true));
        }

        private async Task EnsureHostRealtimeConnectionAsync()
        {
            if (_realtimeClient == null || _snapshot.RemoteEnabled == false || !CanContactRelay())
            {
                return;
            }

            try
            {
                if (IsEmbeddedMode() && _embeddedRelayService != null)
                {
                    var result = await _embeddedRelayService.StartAsync(_snapshot, _lifetimeCancellation.Token).ConfigureAwait(true);
                    if (!result.Success) throw new InvalidOperationException(result.ErrorMessage);
                    ReloadSnapshot();
                }

                await _realtimeClient.StartAsync(BuildClientConfig(), _snapshot.HostId, _lifetimeCancellation.Token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _realtimeState = RelayRealtimeConnectionState.Failed;
                _realtimeStatusMessage = RelaySecretRedactor.Redact(ex.Message);
                UpdateMainState();
            }
        }

        private void RealtimeClient_StatusChanged(object sender, RelayRealtimeConnectionStatusEventArgs e)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                this.BeginInvokeSafe(() => ApplyRealtimeStatus(e));
                return;
            }

            ApplyRealtimeStatus(e);
        }

        private void ApplyRealtimeStatus(RelayRealtimeConnectionStatusEventArgs e)
        {
            _realtimeState = e.State;
            _realtimeStatusMessage = e.Message;
            UpdateMainState();
        }

        private bool HasSavedConfig()
        {
            return !string.IsNullOrWhiteSpace(_snapshot.RelayHost) &&
                   _snapshot.RelayPort >= 1 &&
                   !string.IsNullOrWhiteSpace(_snapshot.TenantId) &&
                   !string.IsNullOrWhiteSpace(_snapshot.EncryptedAccessSecret);
        }

        private bool IsEmbeddedMode()
        {
            return string.IsNullOrWhiteSpace(_snapshot.RemoteServiceMode) ||
                   string.Equals(_snapshot.RemoteServiceMode, "Embedded", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsEmbeddedRelayRunning()
        {
            if (_embeddedRelayService != null && IsEmbeddedMode())
            {
                var status = GetEmbeddedRelayStatus();
                return status != null && status.State == EmbeddedRelayRunState.Running;
            }

            return _snapshot.EmbeddedRelayProcessId > 0;
        }

        private bool CanContactRelay()
        {
            if (!HasSavedConfig() || !_snapshot.LastRegisteredAt.HasValue)
            {
                return false;
            }

            if (_embeddedRelayService != null && IsEmbeddedMode())
            {
                return IsEmbeddedRelayRunning();
            }

            return true;
        }

        private EmbeddedRelayStatus GetEmbeddedRelayStatus()
        {
            return _embeddedRelayService == null || !IsEmbeddedMode()
                ? null
                : _embeddedRelayService.GetStatus(_snapshot);
        }

        private void SetBusy(bool busy)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            _isBusy = busy;
            RefreshButtons();
        }

        private void WriteClipboard(string text)
        {
            if (ClipboardWriterForTests != null)
            {
                ClipboardWriterForTests(text);
                return;
            }

            Clipboard.SetText(text);
        }

        private void ShowInfo(string message, string title)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (MessageBoxForTests != null)
            {
                MessageBoxForTests(title, message);
                return;
            }

            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowError(string message)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            var title = UiLocalization.Text("RemoteServiceManagementForm.Title", "Phone Connection");
            if (MessageBoxForTests != null)
            {
                MessageBoxForTests(title, message);
                return;
            }

            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void FocusControl(Control control)
        {
            if (control == null || IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            ActiveControl = control;
            control.Focus();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var serverSection = CreateServerSection();
            var devicesSection = CreateDevicesSection();
            var bottomButtons = CreateBottomButtons();
            devicesSection.TabIndex = 0;
            serverSection.TabIndex = 1;
            bottomButtons.TabIndex = 2;

            root.Controls.Add(serverSection, 0, 0);
            root.Controls.Add(devicesSection, 0, 1);
            root.Controls.Add(bottomButtons, 0, 2);
            Controls.Add(root);
        }

        private Control CreateServerSection()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                RowCount = 4,
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(CreateSectionLabel(UiLocalization.Text("RemoteServiceManagementForm.Label.Server", "Server")), 0, 0);
            root.Controls.Add(_serverStateLabel, 0, 1);
            root.Controls.Add(_firewallStateLabel, 0, 2);
            root.Controls.Add(CreateServiceButtons(), 0, 3);
            return root;
        }

        private Control CreateDevicesSection()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(CreateSectionLabel(UiLocalization.Text("RemoteServiceManagementForm.Label.Devices", "Devices")), 0, 0);
            root.Controls.Add(_devicesListView, 0, 1);
            root.Controls.Add(CreateDeviceButtons(), 0, 2);
            return root;
        }

        private Control CreateServiceButtons()
        {
            var buttons = CreateActionButtonPanel(FlowDirection.LeftToRight);
            buttons.Controls.Add(_embeddedRelayActionButton);
            buttons.Controls.Add(_allowFirewallButton);
            buttons.Controls.Add(_generatePairingCodeButton);
            return buttons;
        }

        private Control CreateDeviceButtons()
        {
            var buttons = CreateActionButtonPanel(FlowDirection.LeftToRight);
            buttons.Controls.Add(_refreshDevicesButton);
            return buttons;
        }

        private Control CreateBottomButtons()
        {
            var buttons = CreateActionButtonPanel(FlowDirection.RightToLeft);
            buttons.Controls.Add(_closeButton);
            buttons.Controls.Add(_serverSettingsButton);
            buttons.Controls.Add(_diagnosticsButton);
            return buttons;
        }

        private static FlowLayoutPanel CreateActionButtonPanel(FlowDirection flowDirection)
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = flowDirection,
                WrapContents = true,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 6),
                TabStop = false
            };
        }

        private static Label CreateSectionLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                AccessibleName = text,
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = UiTheme.CreateUiFont(UiTheme.SubtitleFontSize, FontStyle.Bold),
                Margin = new Padding(0, 8, 0, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private static ListView CreateDevicesListView()
        {
            var listView = new ListView
            {
                AccessibleName = UiLocalization.Text("RemoteServiceManagementForm.Accessible.Devices", "Devices"),
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                TabIndex = 0
            };
            listView.Columns.Add(UiLocalization.Text("RemoteServiceManagementForm.Column.Device", "Device"), 220);
            listView.Columns.Add(UiLocalization.Text("RemoteServiceManagementForm.Column.Target", "Target"), 160);
            listView.Columns.Add(UiLocalization.Text("RemoteServiceManagementForm.Column.Platform", "Platform"), 120);
            listView.Columns.Add(UiLocalization.Text("RemoteServiceManagementForm.Column.BoundAt", "Verification / bound at"), 170);
            listView.Columns.Add(UiLocalization.Text("RemoteServiceManagementForm.Column.Status", "Status"), 100);
            UiTheme.ApplyListView(listView);
            return listView;
        }
    }
}
