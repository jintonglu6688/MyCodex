using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class RemoteServiceSettingsForm : Form
    {
        private const string SavedSecretPlaceholder = "********";

        private readonly IRemoteServiceConfigStore _configStore;
        private readonly IRelayHostClient _relayClient;
        private readonly IScreenReaderService _screenReaderService;
        private RemoteServiceConfigSnapshot _snapshot;
        private readonly ComboBox _serverModeComboBox;
        private readonly TextBox _serviceHostTextBox;
        private readonly NumericUpDown _portNumericUpDown;
        private readonly CheckBox _useTlsCheckBox;
        private readonly TextBox _tenantIdTextBox;
        private readonly TextBox _accessSecretTextBox;
        private readonly TextBox _thisPcNameTextBox;
        private readonly TextBox _hostIdTextBox;
        private readonly CheckBox _startWithApplicationCheckBox;
        private readonly Label _serverInfoStatusLabel;
        private readonly Button _importServerInfoButton;
        private readonly Button _detectServerButton;
        private readonly Button _saveButton;
        private readonly Button _cancelButton;
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private Label _tenantIdLabel;
        private Label _accessSecretLabel;
        private string _loadedMode;
        private string _externalSecretDraft;
        private bool _loadingFields;
        private bool _isBusy;
        private readonly Func<RemoteServiceConfigSnapshot, Task<bool>> _saveSharedAsync;
        private readonly Button _firewallSettingsButton;
        private readonly bool _hasFirewallSettings;

        public RemoteServiceSettingsForm(
            IRemoteServiceConfigStore configStore,
            IRelayHostClient relayClient,
            IScreenReaderService screenReaderService,
            Func<RemoteServiceConfigSnapshot, Task<bool>> saveSharedAsync = null,
            Action<IWin32Window> configureFirewall = null)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _relayClient = relayClient ?? throw new ArgumentNullException(nameof(relayClient));
            _screenReaderService = screenReaderService;
            _saveSharedAsync = saveSharedAsync;
            _hasFirewallSettings = configureFirewall != null;
            _snapshot = _configStore.Load() ?? new RemoteServiceConfigSnapshot();
            RemoteServiceConfigPlanner.EnsureDefaults(_snapshot, Environment.MachineName);

            Text = _saveSharedAsync != null
                ? UiLocalization.Text("SharedRemoteService.Title", "Phone Connection Settings")
                : UiLocalization.Text("RemoteServiceSettingsForm.Title", "Server Settings");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 480);
            Size = new Size(780, 520);
            ShowInTaskbar = false;
            TabStop = false;
            UiTheme.ApplyForm(this);

            _serverModeComboBox = new ComboBox
            {
                AccessibleName = UiLocalization.Text("RemoteServiceSettingsForm.Accessible.ServerMode", "Service type"),
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                TabIndex = 0
            };
            _serverModeComboBox.Items.Add(UiLocalization.Text("RemoteServiceSettingsForm.Mode.Embedded", "Built-in service"));
            _serverModeComboBox.Items.Add(UiLocalization.Text("RemoteServiceSettingsForm.Mode.External", "Public service"));
            UiTheme.ApplyComboBox(_serverModeComboBox);
            _serviceHostTextBox = CreateTextBox(UiLocalization.Text("RemoteServiceSettingsForm.Accessible.ServiceHost", "Public address"), 1);
            _portNumericUpDown = CreatePortControl();
            _useTlsCheckBox = CreateCheckBox(
                UiLocalization.Text("RemoteServiceSettingsForm.Label.UseTls", "Use TLS"),
                UiLocalization.Text("RemoteServiceSettingsForm.Accessible.UseTls", "Use TLS"),
                3);
            _tenantIdTextBox = CreateTextBox(UiLocalization.Text("RemoteServiceSettingsForm.Accessible.TenantId", "Tenant ID"), 4);
            _accessSecretTextBox = CreateTextBox(UiLocalization.Text("RemoteServiceSettingsForm.Accessible.AccessSecret", "Access secret"), 5);
            _accessSecretTextBox.UseSystemPasswordChar = true;
            _thisPcNameTextBox = CreateTextBox(UiLocalization.Text("RemoteServiceSettingsForm.Accessible.ThisPcName", "This PC name"), 6);
            _hostIdTextBox = CreateTextBox(UiLocalization.Text("RemoteServiceSettingsForm.Accessible.HostId", "Host ID"), 7);
            _hostIdTextBox.ReadOnly = true;
            _startWithApplicationCheckBox = CreateCheckBox(
                UiLocalization.Text(
                    "RemoteServiceSettingsForm.Label.StartEmbeddedWithApplication",
                    "Start the built-in service with MyCodex"),
                UiLocalization.Text(
                    "RemoteServiceSettingsForm.Accessible.StartEmbeddedWithApplication",
                    "Start with MyCodex"),
                4);
            _serverInfoStatusLabel = new Label
            {
                Text = UiLocalization.Text(
                    "RemoteServiceSettingsForm.Status.ServerInfoInstruction",
                    "Import deployment JSON or detect the public server before saving."),
                AccessibleName = UiLocalization.Text(
                    "RemoteServiceSettingsForm.Accessible.ServerInfoStatus",
                    "Server information status"),
                Dock = DockStyle.Fill,
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(_serverInfoStatusLabel, false);
            _importServerInfoButton = CreateButton(
                UiLocalization.Text(
                    "RemoteServiceSettingsForm.Button.ImportServerInfo",
                    "Import Server Info(&I)"),
                UiLocalization.Text(
                    "RemoteServiceSettingsForm.Accessible.Button.ImportServerInfo",
                    "Import server information"),
                8,
                UiGlyph.FileChange);
            _detectServerButton = CreateButton(
                UiLocalization.Text(
                    "RemoteServiceSettingsForm.Button.DetectServer",
                    "Detect Server(&T)"),
                UiLocalization.Text(
                    "RemoteServiceSettingsForm.Accessible.Button.DetectServer",
                    "Detect server"),
                9,
                UiGlyph.Refresh);
            _saveButton = CreateButton(
                UiLocalization.Text("RemoteServiceSettingsForm.Button.Save", "Save(&S)"),
                UiLocalization.Text("RemoteServiceSettingsForm.Accessible.Button.Save", "Save"),
                10,
                UiGlyph.Ok);
            _cancelButton = CreateButton(
                UiLocalization.Text("RemoteServiceSettingsForm.Button.Cancel", "Cancel(&C)"),
                UiLocalization.Text("RemoteServiceSettingsForm.Accessible.Button.Cancel", "Cancel"),
                11,
                UiGlyph.Cancel);

            if (_saveSharedAsync != null)
            {
                _saveButton.Text = UiLocalization.Text("SharedRemoteService.Button.Apply", "Apply(&S)");
                _saveButton.AccessibleName = UiLocalization.Text("SharedRemoteService.Accessible.Apply", "Apply settings");
                _cancelButton.Text = UiLocalization.Text("SharedRemoteService.Button.Close", "Close(&C)");
                _cancelButton.AccessibleName = UiLocalization.Text("SharedRemoteService.Accessible.Close", "Close");
            }
            FormClosing += (sender, e) => { if (_isBusy) e.Cancel = true; };
            _firewallSettingsButton = CreateButton(
                UiLocalization.Text("SharedRemoteService.Button.Firewall", "Local network access(&A)..."),
                UiLocalization.Text("RemoteServiceManagementForm.Accessible.Button.AllowFirewall", "Allow local network access"),
                9, UiGlyph.Approval);
            _firewallSettingsButton.Visible = configureFirewall != null;
            _firewallSettingsButton.Click += (sender, e) => configureFirewall?.Invoke(this);

            _importServerInfoButton.Click += (sender, e) => ImportServerInfoFromClipboard();
            _detectServerButton.Click += async (sender, e) => await DetectServerAsync();
            _saveButton.Click += async (sender, e) => await SaveAsync();
            _cancelButton.Click += (sender, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            _serverModeComboBox.SelectedIndexChanged += ServerModeComboBox_SelectedIndexChanged;

            BuildLayout();
            LoadFields();
            AcceptButton = _saveButton;
            CancelButton = _cancelButton;
        }

        internal bool UsesSharedServiceSettings => _saveSharedAsync != null;

        internal Control InitialFocusControlForTests
        {
            get { return _serverModeComboBox.Enabled ? (Control)_serverModeComboBox : _serviceHostTextBox; }
        }

        internal string SelectedServiceModeForTests
        {
            get { return _serverModeComboBox.SelectedIndex == 1 ? "External" : "Embedded"; }
        }

        internal void SetServiceModeForTests(string mode)
        {
            _serverModeComboBox.SelectedIndex = IsEmbeddedMode(mode) ? 0 : 1;
        }

        internal void SetConnectionFieldsForTests(string host, int port, bool useTls, string tenantId, string accessSecret, string pcName)
        {
            _serviceHostTextBox.Text = host;
            _portNumericUpDown.Value = port;
            _useTlsCheckBox.Checked = useTls;
            _tenantIdTextBox.Text = tenantId;
            _accessSecretTextBox.Text = accessSecret;
            _thisPcNameTextBox.Text = pcName;
        }

        internal Task ClickSaveForTests()
        {
            return SaveAsync();
        }

        internal void ClickImportServerInfoForTests()
        {
            ImportServerInfoFromClipboard();
        }

        internal Func<string> ClipboardReaderForTests { get; set; }

        internal Task ClickDetectServerForTests()
        {
            return DetectServerAsync();
        }

        internal string ServerInfoStatusForTests
        {
            get { return _serverInfoStatusLabel.Text; }
        }

        internal string RelayHostForTests
        {
            get { return _serviceHostTextBox.Text; }
        }

        internal int RelayPortForTests
        {
            get { return Convert.ToInt32(_portNumericUpDown.Value); }
        }

        internal bool UseTlsForTests
        {
            get { return _useTlsCheckBox.Checked; }
        }

        internal bool StartWithApplicationForTests
        {
            get { return _startWithApplicationCheckBox.Checked; }
            set { _startWithApplicationCheckBox.Checked = value; }
        }

        internal bool TenantCredentialsVisibleForTests
        {
            get { return !IsEmbeddedMode(SelectedServiceModeForTests); }
        }

        internal string TenantIdForTests
        {
            get { return _tenantIdTextBox.Text; }
        }

        internal string AccessSecretForTests
        {
            get { return _accessSecretTextBox.Text; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            InitialFocusControlForTests.Focus();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            base.OnFormClosed(e);
        }

        private void ImportServerInfoFromClipboard()
        {
            if (_isBusy)
            {
                return;
            }

            try
            {
                var json = ClipboardReaderForTests != null
                    ? ClipboardReaderForTests()
                    : Clipboard.GetText(TextDataFormat.Text);
                var info = RelayServerInfoParser.Parse(json);
                _serverModeComboBox.SelectedIndex = 1;
                _serviceHostTextBox.Text = info.RelayHost;
                _portNumericUpDown.Value = info.RelayPort;
                _useTlsCheckBox.Checked = info.TlsRequired;
                _tenantIdTextBox.Text = info.TenantId;
                _accessSecretTextBox.Text = info.AccessSecret ?? string.Empty;
                if (string.IsNullOrWhiteSpace(info.AccessSecret))
                {
                    SetServerInfoStatus(
                        UiLocalization.Text(
                            "RemoteServiceSettingsForm.Status.ImportedWithoutSecret",
                            "Server information was imported. Enter or rotate the access secret before saving."),
                        true);
                    _accessSecretTextBox.Focus();
                }
                else
                {
                    SetServerInfoStatus(
                        UiLocalization.Text(
                            "RemoteServiceSettingsForm.Status.Imported",
                            "Server information was imported. Review the settings and save."),
                        true);
                    _saveButton.Focus();
                }
            }
            catch (Exception)
            {
                SetServerInfoStatus(
                    UiLocalization.Text(
                        "RemoteServiceSettingsForm.Status.ImportFailed",
                        "Server information could not be imported. Copy the complete deployment JSON and try again."),
                    true);
                _importServerInfoButton.Focus();
            }
        }

        private async Task DetectServerAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var relayHost = TextValueHelper.TrimToNull(_serviceHostTextBox.Text);
            if (relayHost == null)
            {
                SetServerInfoStatus(
                    UiLocalization.Text(
                        "RemoteServiceSettingsForm.Status.AddressRequired",
                        "Enter the public server address before detection."),
                    true);
                _serviceHostTextBox.Focus();
                return;
            }

            try
            {
                SetBusy(true);
                SetServerInfoStatus(
                    UiLocalization.Text(
                        "RemoteServiceSettingsForm.Status.Detecting",
                        "Detecting the public server..."),
                    false);
                var metadata = await _relayClient.GetMetadataAsync(
                    new RelayHostConfig
                    {
                        RelayHost = relayHost,
                        RelayPort = Convert.ToInt32(_portNumericUpDown.Value),
                        TlsRequired = _useTlsCheckBox.Checked
                    },
                    _lifetimeCancellation.Token).ConfigureAwait(true);
                SetServerInfoStatus(
                    string.Format(
                        UiLocalization.Text(
                            "RemoteServiceSettingsForm.Status.Detected.Template",
                            "Server available. Protocol: {0}; signing fingerprint: {1}; maximum message: {2} bytes."),
                        metadata.ProtocolVersion,
                        metadata.SigningKeyFingerprint,
                        metadata.MaxMessageBytes),
                    true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SetServerInfoStatus(
                    UiLocalization.Text(
                        "RemoteServiceSettingsForm.Status.DetectFailed",
                        "Server detection failed.") +
                    " " +
                    RelaySecretRedactor.Redact(ex.Message),
                    true);
            }
            finally
            {
                SetBusy(false);
                if (!IsDisposed && !Disposing)
                {
                    _detectServerButton.Focus();
                }
            }
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            _importServerInfoButton.Enabled = !busy;
            _detectServerButton.Enabled = !busy;
            _saveButton.Enabled = !busy;
            _cancelButton.Enabled = !busy;
            _serverModeComboBox.Enabled = !busy;
            _firewallSettingsButton.Enabled = !busy;
        }

        private void SetServerInfoStatus(string text, bool announce)
        {
            _serverInfoStatusLabel.Text = text ?? string.Empty;
            if (announce)
            {
                _screenReaderService?.Speak(_serverInfoStatusLabel.Text, false, false, true);
            }
        }

        private void LoadFields()
        {
            _loadingFields = true;
            _serverModeComboBox.SelectedIndex = IsEmbeddedMode(_snapshot.RemoteServiceMode) ? 0 : 1;
            _loadedMode = SelectedServiceModeForTests;
            _externalSecretDraft = string.IsNullOrWhiteSpace(_snapshot.ExternalProfile.EncryptedAccessSecret)
                ? string.Empty
                : SavedSecretPlaceholder;
            LoadSelectedModeFields();
            _thisPcNameTextBox.Text = _snapshot.ThisPcName ?? string.Empty;
            _hostIdTextBox.Text = _snapshot.HostId ?? string.Empty;
            _loadingFields = false;
            ApplyModePresentation(false);
        }

        private async Task SaveAsync()
        {
            CaptureSelectedModeFields();
            var selectedMode = SelectedServiceModeForTests;
            var rawExternalSecret = GetExternalRawSecretForSave();
            if (!string.IsNullOrWhiteSpace(rawExternalSecret))
            {
                RemoteServiceConfigPlanner.SelectMode(_snapshot, "External");
                _configStore.ProtectAccessSecret(_snapshot, rawExternalSecret);
                RemoteServiceConfigPlanner.CaptureActiveProfile(_snapshot);
            }

            RemoteServiceConfigPlanner.SelectMode(_snapshot, selectedMode);
            _snapshot.ThisPcName = TextValueHelper.TrimToNull(_thisPcNameTextBox.Text);
            RemoteServiceConfigPlanner.EnsureDefaults(_snapshot, Environment.MachineName);
            _hostIdTextBox.Text = _snapshot.HostId ?? string.Empty;

            RemoteServiceConfigPlanner.CaptureActiveProfile(_snapshot);
            if (_saveSharedAsync == null) _configStore.Save(_snapshot);
            else
            {
                try
                {
                    SetBusy(true);
                    if (!await _saveSharedAsync(_snapshot)) return;
                }
                catch (Exception ex)
                {
                    _serverInfoStatusLabel.Text = RelaySecretRedactor.Redact(ex.Message);
                    return;
                }
                finally { SetBusy(false); }
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        internal void SetSharedServiceStatus(string status, bool whileBusy = false)
        {
            if (!IsDisposed && !Disposing && (whileBusy || !_isBusy))
                _serverInfoStatusLabel.Text = status ?? string.Empty;
        }

        private string GetExternalRawSecretForSave()
        {
            return string.Equals(_externalSecretDraft, SavedSecretPlaceholder, StringComparison.Ordinal)
                ? null
                : _externalSecretDraft;
        }

        private void CaptureSelectedModeFields()
        {
            var relayHost = TextValueHelper.TrimToNull(_serviceHostTextBox.Text);
            var relayPort = Convert.ToInt32(_portNumericUpDown.Value);
            var mode = string.IsNullOrWhiteSpace(_loadedMode) ? SelectedServiceModeForTests : _loadedMode;
            if (IsEmbeddedMode(mode))
            {
                _snapshot.EmbeddedRelayPublicHostOverride = relayHost;
                _snapshot.EmbeddedRelayPublicPortOverride = relayPort;
                _snapshot.StartEmbeddedRelayWithApplication = _startWithApplicationCheckBox.Checked;
                return;
            }

            var profile = _snapshot.ExternalProfile;
            if (_saveSharedAsync == null && (!string.Equals(TextValueHelper.TrimToNull(profile.RelayHost), relayHost, StringComparison.OrdinalIgnoreCase) ||
                profile.RelayPort != relayPort ||
                profile.UseTls != _useTlsCheckBox.Checked))
            {
                profile.RelaySigningPublicKey = null;
                profile.RelaySigningKeyFingerprint = null;
            }

            profile.RelayHost = relayHost;
            profile.RelayPort = relayPort;
            profile.UseTls = _useTlsCheckBox.Checked;
            profile.TenantId = TextValueHelper.TrimToNull(_tenantIdTextBox.Text);
            _externalSecretDraft = _accessSecretTextBox.Text;
        }

        private void ServerModeComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_loadingFields)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_loadedMode))
            {
                CaptureSelectedModeFields();
            }

            _loadedMode = SelectedServiceModeForTests;
            _loadingFields = true;
            LoadSelectedModeFields();
            _loadingFields = false;
            ApplyModePresentation(true);
        }

        private void LoadSelectedModeFields()
        {
            if (IsEmbeddedMode(SelectedServiceModeForTests))
            {
                _serviceHostTextBox.Text = _snapshot.EmbeddedRelayPublicHostOverride ?? string.Empty;
                _portNumericUpDown.Value = _snapshot.EmbeddedRelayPublicPortOverride > 0
                    ? _snapshot.EmbeddedRelayPublicPortOverride
                    : 38443;
                _useTlsCheckBox.Checked = true;
                _tenantIdTextBox.Text = string.Empty;
                _accessSecretTextBox.Text = string.Empty;
                _startWithApplicationCheckBox.Checked = _snapshot.StartEmbeddedRelayWithApplication;
                return;
            }

            var profile = _snapshot.ExternalProfile;
            _serviceHostTextBox.Text = profile.RelayHost ?? string.Empty;
            _portNumericUpDown.Value = profile.RelayPort <= 0 ? 443 : profile.RelayPort;
            _useTlsCheckBox.Checked = profile.UseTls;
            _tenantIdTextBox.Text = profile.TenantId ?? string.Empty;
            _accessSecretTextBox.Text = _externalSecretDraft ?? string.Empty;
        }

        private void ApplyModePresentation(bool announce)
        {
            var embedded = IsEmbeddedMode(SelectedServiceModeForTests);
            _firewallSettingsButton.Visible = _hasFirewallSettings && embedded;
            _useTlsCheckBox.Enabled = !embedded;
            _tenantIdLabel.Visible = !embedded;
            _tenantIdTextBox.Visible = !embedded;
            _accessSecretLabel.Visible = !embedded;
            _accessSecretTextBox.Visible = !embedded;
            _startWithApplicationCheckBox.Visible = embedded;
            _importServerInfoButton.Visible = !embedded;
            _detectServerButton.Visible = !embedded;
            SetServerInfoStatus(
                embedded
                    ? UiLocalization.Text(
                        "RemoteServiceSettingsForm.Status.EmbeddedSettings",
                        "The built-in service manages TLS, tenant identity, and its access secret automatically.")
                    : UiLocalization.Text(
                        "RemoteServiceSettingsForm.Status.ServerInfoInstruction",
                        "Import deployment JSON or detect the public server before saving."),
                false);
            if (announce)
            {
                _screenReaderService?.Speak(
                    embedded
                        ? UiLocalization.Text("RemoteServiceSettingsForm.Announcement.EmbeddedMode", "Built-in service settings")
                        : UiLocalization.Text("RemoteServiceSettingsForm.Announcement.ExternalMode", "Public service settings"),
                    false,
                    false,
                    true);
            }
        }

        private void BuildLayout()
        {
            var offset = _saveSharedAsync == null ? 0 : 1;
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 11 + offset,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (var i = 0; i < 10 + offset; i++)
            {
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            if (offset != 0)
            {
                var description = new Label
                {
                    Text = UiLocalization.Text("SharedRemoteService.Description", "These settings apply to all targets. Manage pairing and device access under Phone Connection in each target."),
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 0, 12),
                    TabStop = false
                };
                UiTheme.ApplyLabel(description, false);
                root.Controls.Add(description, 0, 0);
                root.SetColumnSpan(description, 2);
            }

            AddLabeledControl(root, offset, UiLocalization.Text("RemoteServiceSettingsForm.Label.ServerMode", "Service type"), _serverModeComboBox);
            AddLabeledControl(root, 1 + offset, UiLocalization.Text("RemoteServiceSettingsForm.Label.ServiceHost", "Public address"), _serviceHostTextBox);
            AddLabeledControl(root, 2 + offset, UiLocalization.Text("RemoteServiceSettingsForm.Label.Port", "Port"), _portNumericUpDown);
            root.Controls.Add(_useTlsCheckBox, 1, 3 + offset);
            root.Controls.Add(_startWithApplicationCheckBox, 1, 4 + offset);
            _tenantIdLabel = AddLabeledControl(root, 5 + offset, UiLocalization.Text("RemoteServiceSettingsForm.Label.TenantId", "Tenant ID"), _tenantIdTextBox);
            _accessSecretLabel = AddLabeledControl(root, 6 + offset, UiLocalization.Text("RemoteServiceSettingsForm.Label.AccessSecret", "Access secret"), _accessSecretTextBox);
            if (_saveSharedAsync == null)
            {
                AddLabeledControl(root, 7, UiLocalization.Text("RemoteServiceSettingsForm.Label.ThisPcName", "This PC name"), _thisPcNameTextBox);
                AddLabeledControl(root, 8, UiLocalization.Text("RemoteServiceSettingsForm.Label.HostId", "Host ID"), _hostIdTextBox);
            }
            root.Controls.Add(_serverInfoStatusLabel, 0, 9 + offset);
            root.SetColumnSpan(_serverInfoStatusLabel, 2);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                WrapContents = false,
                TabIndex = 10,
                TabStop = false
            };
            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(_saveButton);
            buttons.Controls.Add(_detectServerButton);
            buttons.Controls.Add(_importServerInfoButton);
            buttons.Controls.Add(_firewallSettingsButton);
            root.Controls.Add(buttons, 0, 10 + offset);
            root.SetColumnSpan(buttons, 2);
            Controls.Add(root);
        }

        private static Label AddLabeledControl(TableLayoutPanel root, int row, string labelText, Control control)
        {
            var label = new Label
            {
                Text = labelText,
                AccessibleName = labelText,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 4, 8, 4),
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            root.Controls.Add(label, 0, row);
            root.Controls.Add(control, 1, row);
            return label;
        }

        private static TextBox CreateTextBox(string accessibleName, int tabIndex)
        {
            var textBox = new TextBox
            {
                AccessibleName = accessibleName,
                Dock = DockStyle.Fill,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, false, false);
            return textBox;
        }

        private static NumericUpDown CreatePortControl()
        {
            var control = new NumericUpDown
            {
                AccessibleName = UiLocalization.Text("RemoteServiceSettingsForm.Accessible.Port", "Port"),
                Minimum = 1,
                Maximum = 65535,
                Dock = DockStyle.Left,
                TabIndex = 2
            };
            foreach (Control child in control.Controls)
            {
                child.AccessibleName = control.AccessibleName;
            }

            return control;
        }

        private static CheckBox CreateCheckBox(string text, string accessibleName, int tabIndex)
        {
            var checkBox = new CheckBox
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                TabIndex = tabIndex
            };
            UiTheme.ApplyCheckBox(checkBox);
            return checkBox;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex, UiGlyph glyph)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                TabIndex = tabIndex
            };
            UiTheme.ApplyDialogButton(button, glyph);
            return button;
        }

        private static bool IsEmbeddedMode(string mode)
        {
            return string.IsNullOrWhiteSpace(mode) ||
                string.Equals(mode.Trim(), "Embedded", StringComparison.OrdinalIgnoreCase);
        }
    }
}
