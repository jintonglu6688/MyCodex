using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class WslAppServerSettingsForm : Form
    {
        private readonly CodexRuntimeManager _runtimeManager;
        private readonly CodexRuntimeUpdateService _runtimeUpdateService;
        private readonly IAppServerWindowsSandboxClient _windowsSandboxClient;
        private readonly Action<string> _announce;
        private readonly string _initialRuntimeSource;
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();
        private readonly List<RemoteUnixSshProfileConfigSnapshot> _remoteProfiles;
        private readonly TableLayoutPanel _settingsLayout;
        private readonly Label _introLabel;
        private readonly FlowLayoutPanel _buttonPanel;
        private readonly Label _windowsSandboxLabel;
        private readonly TableLayoutPanel _windowsSandboxPanel;
        private readonly ComboBox _runtimeSourceComboBox;
        private readonly Label _windowsShellLabel;
        private readonly ComboBox _windowsShellComboBox;
        private readonly Label _customExecutableLabel;
        private readonly TextBox _customExecutableTextBox;
        private readonly TableLayoutPanel _customExecutablePanel;
        private readonly Button _browseCustomButton;
        private readonly Label _distributionLabel;
        private readonly TextBox _distributionTextBox;
        private readonly Label _codexCommandLabel;
        private readonly TextBox _codexCommandTextBox;
        private readonly Label _shellCommandLabel;
        private readonly TextBox _shellCommandTextBox;
        private readonly Label _remoteProfileLabel;
        private readonly ComboBox _remoteProfileComboBox;
        private readonly TableLayoutPanel _remoteProfilePanel;
        private readonly Button _addRemoteProfileButton;
        private readonly Button _deleteRemoteProfileButton;
        private readonly Label _remoteAliasLabel;
        private readonly TextBox _remoteAliasTextBox;
        private readonly Label _remoteHostLabel;
        private readonly TextBox _remoteHostTextBox;
        private readonly Label _remotePortLabel;
        private readonly TextBox _remotePortTextBox;
        private readonly Label _remoteUserNameLabel;
        private readonly TextBox _remoteUserNameTextBox;
        private readonly Label _remoteAuthenticationLabel;
        private readonly ComboBox _remoteAuthenticationComboBox;
        private readonly Label _remotePrivateKeyLabel;
        private readonly TextBox _remotePrivateKeyTextBox;
        private readonly TableLayoutPanel _remotePrivateKeyPanel;
        private readonly Button _browseRemotePrivateKeyButton;
        private readonly Label _remotePasswordLabel;
        private readonly TextBox _remotePasswordTextBox;
        private readonly Label _remoteHostKeyLabel;
        private readonly TextBox _remoteHostKeyTextBox;
        private readonly Label _remotePlinkLabel;
        private readonly TextBox _remotePlinkTextBox;
        private readonly Label _remoteShellLabel;
        private readonly TextBox _remoteShellTextBox;
        private readonly Label _remoteCodexCommandLabel;
        private readonly TextBox _remoteCodexCommandTextBox;
        private readonly TextBox _windowsSandboxStatusTextBox;
        private readonly TextBox _windowsSandboxDetailsTextBox;
        private readonly Button _windowsSandboxDetectButton;
        private readonly Button _windowsSandboxSetupButton;
        private readonly TextBox _diagnosticsTextBox;
        private readonly Button _detectButton;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private bool _isRuntimeOperationBusy;
        private bool _isWindowsSandboxOperationBusy;
        private bool _isWindowsSandboxSetupRunning;
        private bool _isLoadingRemoteProfile;
        private string _loadedRemoteProfileId;
        private AppServerWindowsSandboxReadinessStatus _lastWindowsSandboxStatus;

        public WslAppServerSettingsForm(
            string runtimeSource,
            string customExecutablePath,
            string windowsShellPreference,
            WslAppServerConfigSnapshot wslConfig,
            RemoteMacSshConfigSnapshot remoteMacConfig,
            IList<RemoteUnixSshProfileConfigSnapshot> remoteProfiles,
            string selectedRemoteProfileId,
            CodexRuntimeManager runtimeManager,
            CodexRuntimeUpdateService runtimeUpdateService,
            IAppServerWindowsSandboxClient windowsSandboxClient,
            Action<string> announce)
        {
            _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
            _runtimeUpdateService = runtimeUpdateService ?? throw new ArgumentNullException(nameof(runtimeUpdateService));
            _windowsSandboxClient = windowsSandboxClient;
            _announce = announce;
            _initialRuntimeSource = CodexRuntimeSources.CanonicalizeForExecution(runtimeSource);
            _lastWindowsSandboxStatus = AppServerWindowsSandboxReadinessStatus.Unknown;
            wslConfig = CloneWslConfig(wslConfig);
            remoteMacConfig = CloneRemoteMacConfig(remoteMacConfig);
            _remoteProfiles = NormalizeRemoteProfiles(remoteProfiles, selectedRemoteProfileId, remoteMacConfig);

            Text = UiLocalization.Text("WslAppServerSettingsForm.Title", "App Server Runtime");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(860, 640);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 24,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var introLabel = new Label
            {
                Text = UiLocalization.Text(
                    "WslAppServerSettingsForm.Intro",
                    "Choose where MyCodex starts the Codex App Server. WSL mode keeps the Windows interface and runs only the backend inside Ubuntu."),
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 12),
                TabStop = false
            };
            UiTheme.ApplyLabel(introLabel, true);

            _runtimeSourceComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.RuntimeSource", "Runtime source"),
                TabIndex = 0
            };
            UiTheme.ApplyComboBox(_runtimeSourceComboBox);
            AddRuntimeSourceItems();

            _windowsShellComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.WindowsShell", "Windows shell"),
                TabIndex = 1
            };
            UiTheme.ApplyComboBox(_windowsShellComboBox);
            AddWindowsShellItems();

            _customExecutableTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.CustomExecutable", "Custom Codex path"),
                2);
            _customExecutableTextBox.Text = customExecutablePath;
            _customExecutableTextBox.Dock = DockStyle.Fill;
            _customExecutableTextBox.TextChanged += CustomExecutableTextBox_TextChanged;

            _browseCustomButton = new Button
            {
                Text = UiLocalization.Text("WslAppServerSettingsForm.Button.BrowseCustom", "Browse(&B)..."),
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.BrowseCustom", "Browse"),
                AutoSize = true,
                TabIndex = 3
            };
            UiTheme.ApplyDialogButton(_browseCustomButton, UiGlyph.Workspace);
            _browseCustomButton.Click += BrowseCustomButton_Click;

            _customExecutablePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                TabStop = false
            };
            _customExecutablePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _customExecutablePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _customExecutablePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _customExecutablePanel.Controls.Add(_customExecutableTextBox, 0, 0);
            _customExecutablePanel.Controls.Add(_browseCustomButton, 1, 0);

            _distributionTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.Distribution", "Distribution"),
                4);
            _distributionTextBox.Text = wslConfig.DistributionName;

            _codexCommandTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.CodexCommand", "Codex command"),
                5);
            _codexCommandTextBox.Text = wslConfig.CodexCommand;

            _shellCommandTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.Shell", "Shell"),
                6);
            _shellCommandTextBox.Text = wslConfig.ShellCommand;

            _remoteProfileComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemoteProfile", "Remote profile"),
                TabIndex = 9
            };
            UiTheme.ApplyComboBox(_remoteProfileComboBox);

            _addRemoteProfileButton = new Button
            {
                Text = UiLocalization.Text("WslAppServerSettingsForm.Button.AddRemoteProfile", "New(&N)"),
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.AddRemoteProfile", "New"),
                AutoSize = true,
                TabIndex = 10
            };
            UiTheme.ApplyDialogButton(_addRemoteProfileButton, UiGlyph.NewSession);
            _addRemoteProfileButton.Click += AddRemoteProfileButton_Click;

            _deleteRemoteProfileButton = new Button
            {
                Text = UiLocalization.Text("WslAppServerSettingsForm.Button.DeleteRemoteProfile", "Delete(&D)"),
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.DeleteRemoteProfile", "Delete"),
                AutoSize = true,
                TabIndex = 11
            };
            UiTheme.ApplyDialogButton(_deleteRemoteProfileButton, UiGlyph.Cancel);
            _deleteRemoteProfileButton.Click += DeleteRemoteProfileButton_Click;

            _remoteProfilePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                RowCount = 1,
                TabStop = false
            };
            _remoteProfilePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _remoteProfilePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _remoteProfilePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _remoteProfilePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _remoteProfilePanel.Controls.Add(_remoteProfileComboBox, 0, 0);
            _remoteProfilePanel.Controls.Add(_addRemoteProfileButton, 1, 0);
            _remoteProfilePanel.Controls.Add(_deleteRemoteProfileButton, 2, 0);
            RefreshRemoteProfileItems(selectedRemoteProfileId);

            _remoteAliasTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemoteAlias", "Alias"),
                12);
            _remoteAliasTextBox.TextChanged += RemoteField_TextChanged;

            _remoteHostTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemoteHost", "Host"),
                13);
            _remoteHostTextBox.Text = remoteMacConfig.HostName;
            _remoteHostTextBox.TextChanged += RemoteField_TextChanged;

            _remotePortTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemotePort", "Port"),
                14);
            _remotePortTextBox.Text = remoteMacConfig.Port.ToString();
            _remotePortTextBox.TextChanged += RemoteField_TextChanged;

            _remoteUserNameTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemoteUserName", "User name"),
                15);
            _remoteUserNameTextBox.Text = remoteMacConfig.UserName;
            _remoteUserNameTextBox.TextChanged += RemoteField_TextChanged;

            _remoteAuthenticationComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemoteAuthentication", "Authentication"),
                TabIndex = 16
            };
            UiTheme.ApplyComboBox(_remoteAuthenticationComboBox);
            AddRemoteAuthenticationItems();
            SelectRemoteAuthentication(remoteMacConfig.AuthenticationMode);
            _remoteAuthenticationComboBox.SelectedIndexChanged += RemoteAuthenticationComboBox_SelectedIndexChanged;

            _remotePrivateKeyTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemotePrivateKey", "SSH key"),
                17);
            _remotePrivateKeyTextBox.Text = string.IsNullOrWhiteSpace(remoteMacConfig.PrivateKeyPath)
                ? ResolveDefaultSshPrivateKeyPath()
                : remoteMacConfig.PrivateKeyPath;
            _remotePrivateKeyTextBox.TextChanged += RemoteField_TextChanged;

            _browseRemotePrivateKeyButton = new Button
            {
                Text = UiLocalization.Text("WslAppServerSettingsForm.Button.BrowseRemotePrivateKey", "Browse(&K)..."),
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.BrowseRemotePrivateKey", "Browse"),
                AutoSize = true,
                TabIndex = 18
            };
            UiTheme.ApplyDialogButton(_browseRemotePrivateKeyButton, UiGlyph.Workspace);
            _browseRemotePrivateKeyButton.Click += BrowseRemotePrivateKeyButton_Click;

            _remotePrivateKeyPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 1,
                TabStop = false
            };
            _remotePrivateKeyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _remotePrivateKeyPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _remotePrivateKeyPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _remotePrivateKeyPanel.Controls.Add(_remotePrivateKeyTextBox, 0, 0);
            _remotePrivateKeyPanel.Controls.Add(_browseRemotePrivateKeyButton, 1, 0);

            _remotePasswordTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemotePassword", "Password"),
                19);
            _remotePasswordTextBox.UseSystemPasswordChar = true;
            _remotePasswordTextBox.TextChanged += RemoteField_TextChanged;

            _remoteHostKeyTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemoteHostKey", "Host key fingerprint"),
                20);
            _remoteHostKeyTextBox.Text = remoteMacConfig.HostKeyFingerprint;
            _remoteHostKeyTextBox.TextChanged += RemoteField_TextChanged;

            _remotePlinkTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemotePlink", "Plink path"),
                21);
            _remotePlinkTextBox.Text = remoteMacConfig.PlinkExecutablePath;
            _remotePlinkTextBox.TextChanged += RemoteField_TextChanged;

            _remoteShellTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemoteShell", "Remote shell"),
                22);
            _remoteShellTextBox.Text = remoteMacConfig.ShellCommand;
            _remoteShellTextBox.TextChanged += RemoteField_TextChanged;

            _remoteCodexCommandTextBox = CreateTextBox(
                UiLocalization.Text("WslAppServerSettingsForm.Accessible.RemoteCodexCommand", "Remote Codex command"),
                23);
            _remoteCodexCommandTextBox.Text = remoteMacConfig.CodexCommand;
            _remoteCodexCommandTextBox.TextChanged += RemoteField_TextChanged;

            _windowsSandboxStatusTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Accessible.Status", "Windows Sandbox status"),
                TabIndex = 24,
                TabStop = false
            };
            UiTheme.ApplyTextBox(_windowsSandboxStatusTextBox, false, true);

            _windowsSandboxDetailsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Accessible.Details", "Windows Sandbox details"),
                Height = 56,
                TabIndex = 25,
                TabStop = false
            };
            UiTheme.ApplyTextBox(_windowsSandboxDetailsTextBox, false, true);

            _windowsSandboxDetectButton = new Button
            {
                Text = UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Button.Detect", "Detect(&W)"),
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Accessible.Detect", "Detect"),
                AutoSize = true,
                TabIndex = 26
            };
            UiTheme.ApplyDialogButton(_windowsSandboxDetectButton, UiGlyph.Refresh);
            _windowsSandboxDetectButton.Click += async (sender, e) => await DetectWindowsSandboxAsync();

            _windowsSandboxSetupButton = new Button
            {
                Text = UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Button.Setup", "Configure(&S)..."),
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Accessible.Setup", "Configure"),
                AutoSize = true,
                TabIndex = 27
            };
            UiTheme.ApplyDialogButton(_windowsSandboxSetupButton, UiGlyph.Ok);
            _windowsSandboxSetupButton.Click += async (sender, e) => await ConfirmAndStartWindowsSandboxSetupAsync();

            _diagnosticsTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.Diagnostics", "Diagnostics"),
                TabIndex = 28
            };
            UiTheme.ApplyTextBox(_diagnosticsTextBox, false, true);


            _detectButton = new Button
            {
                Text = UiLocalization.Text("WslAppServerSettingsForm.Button.Detect", "Detect(&T)"),
                AccessibleName = UiLocalization.Text("WslAppServerSettingsForm.Accessible.Detect", "Detect"),
                AutoSize = true,
                TabIndex = 30
            };
            UiTheme.ApplyDialogButton(_detectButton, UiGlyph.Refresh);
            _detectButton.Click += DetectButton_Click;

            _okButton = new Button
            {
                Text = UiLocalization.Text("Common.OK", "&OK"),
                AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = 31
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 32,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            var buttonPanel = UiControlFactory.CreateDialogButtonBar();
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);
            buttonPanel.Controls.Add(_detectButton);

            var sandboxButtonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                TabStop = false
            };
            sandboxButtonsPanel.Controls.Add(_windowsSandboxDetectButton);
            sandboxButtonsPanel.Controls.Add(_windowsSandboxSetupButton);

            var sandboxPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 3,
                AutoSize = true,
                TabStop = false
            };
            sandboxPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sandboxPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sandboxPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            sandboxPanel.Controls.Add(_windowsSandboxStatusTextBox, 0, 0);
            sandboxPanel.Controls.Add(_windowsSandboxDetailsTextBox, 0, 1);
            sandboxPanel.Controls.Add(sandboxButtonsPanel, 0, 2);

            root.Controls.Add(introLabel, 0, 0);
            root.SetColumnSpan(introLabel, 2);
            AddRow(root, 1, UiLocalization.Text("WslAppServerSettingsForm.Label.RuntimeSource", "Runtime Source"), _runtimeSourceComboBox);
            _windowsShellLabel = AddRow(root, 2, UiLocalization.Text("WslAppServerSettingsForm.Label.WindowsShell", "Windows Shell"), _windowsShellComboBox);
            _customExecutableLabel = AddRow(root, 3, UiLocalization.Text("WslAppServerSettingsForm.Label.CustomExecutable", "Custom Codex"), _customExecutablePanel);
            _distributionLabel = AddRow(root, 4, UiLocalization.Text("WslAppServerSettingsForm.Label.Distribution", "Distribution"), _distributionTextBox);
            _codexCommandLabel = AddRow(root, 5, UiLocalization.Text("WslAppServerSettingsForm.Label.CodexCommand", "Codex Command"), _codexCommandTextBox);
            _shellCommandLabel = AddRow(root, 6, UiLocalization.Text("WslAppServerSettingsForm.Label.Shell", "Shell"), _shellCommandTextBox);
            _remoteProfileLabel = AddRow(root, 9, UiLocalization.Text("WslAppServerSettingsForm.Label.RemoteProfile", "Remote Profile"), _remoteProfilePanel);
            _remoteAliasLabel = AddRow(root, 10, UiLocalization.Text("WslAppServerSettingsForm.Label.RemoteAlias", "Alias"), _remoteAliasTextBox);
            _remoteHostLabel = AddRow(root, 11, UiLocalization.Text("WslAppServerSettingsForm.Label.RemoteHost", "Host"), _remoteHostTextBox);
            _remotePortLabel = AddRow(root, 12, UiLocalization.Text("WslAppServerSettingsForm.Label.RemotePort", "Port"), _remotePortTextBox);
            _remoteUserNameLabel = AddRow(root, 13, UiLocalization.Text("WslAppServerSettingsForm.Label.RemoteUserName", "User Name"), _remoteUserNameTextBox);
            _remoteAuthenticationLabel = AddRow(root, 14, UiLocalization.Text("WslAppServerSettingsForm.Label.RemoteAuthentication", "Authentication"), _remoteAuthenticationComboBox);
            _remotePrivateKeyLabel = AddRow(root, 15, UiLocalization.Text("WslAppServerSettingsForm.Label.RemotePrivateKey", "SSH Key"), _remotePrivateKeyPanel);
            _remotePasswordLabel = AddRow(root, 16, UiLocalization.Text("WslAppServerSettingsForm.Label.RemotePassword", "Password"), _remotePasswordTextBox);
            _remoteHostKeyLabel = AddRow(root, 17, UiLocalization.Text("WslAppServerSettingsForm.Label.RemoteHostKey", "Host Key"), _remoteHostKeyTextBox);
            _remotePlinkLabel = AddRow(root, 18, UiLocalization.Text("WslAppServerSettingsForm.Label.RemotePlink", "Plink"), _remotePlinkTextBox);
            _remoteShellLabel = AddRow(root, 19, UiLocalization.Text("WslAppServerSettingsForm.Label.RemoteShell", "Remote Shell"), _remoteShellTextBox);
            _remoteCodexCommandLabel = AddRow(root, 20, UiLocalization.Text("WslAppServerSettingsForm.Label.RemoteCodexCommand", "Remote Codex"), _remoteCodexCommandTextBox);
            AddRow(root, 21, UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Label", "Windows Sandbox"), sandboxPanel);
            AddRow(root, 22, UiLocalization.Text("WslAppServerSettingsForm.Label.Diagnostics", "Diagnostics"), _diagnosticsTextBox);
            root.Controls.Add(buttonPanel, 0, 23);
            root.SetColumnSpan(buttonPanel, 2);

            Controls.Add(root);
            _settingsLayout = root;
            _introLabel = introLabel;
            _buttonPanel = buttonPanel;
            _windowsSandboxLabel = root.GetControlFromPosition(0, 21) as Label;
            _windowsSandboxPanel = sandboxPanel;
            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            SelectRuntimeSource(runtimeSource);
            SelectWindowsShellPreference(windowsShellPreference);
            _runtimeSourceComboBox.SelectedIndexChanged += RuntimeSourceComboBox_SelectedIndexChanged;
            _remoteProfileComboBox.SelectedIndexChanged += RemoteProfileComboBox_SelectedIndexChanged;
            LoadSelectedRemoteProfileToFields();
            UpdateRuntimeSourceControls();
            UpdateDiagnosticsText(null);
            SetWindowsSandboxStatus(
                UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Status.NotChecked", "Windows Sandbox: not checked"),
                announce: false);
            SetWindowsSandboxDetails(UiLocalization.Text("WslAppServerSettingsForm.WindowsSandbox.Detail.NotChecked", "Use Detect to check Windows Sandbox readiness."));
            RefreshWindowsSandboxState();
        }

        public string SelectedRuntimeSource { get; private set; }

        public string CustomExecutablePath { get; private set; }

        public string SelectedWindowsShellPreference { get; private set; }

        public WslAppServerConfigSnapshot WslConfig { get; private set; }

        public RemoteMacSshConfigSnapshot RemoteMacConfig { get; private set; }

        public List<RemoteUnixSshProfileConfigSnapshot> RemoteUnixProfiles { get; private set; }

        public string SelectedRemoteUnixProfileId { get; private set; }

        public bool BundledRuntimeUpdated { get; private set; }

        public string UpdatedBundledRuntimeVersion { get; private set; }

        public string UpdatedBundledRuntimeTargetTriple { get; private set; }

        internal string WindowsSandboxStatusTextForTests
        {
            get { return _windowsSandboxStatusTextBox.Text; }
        }

        internal string WindowsSandboxDetailsTextForTests
        {
            get { return _windowsSandboxDetailsTextBox.Text; }
        }

        internal bool WindowsSandboxDetectEnabledForTests
        {
            get { return _windowsSandboxDetectButton.Enabled; }
        }

        internal bool WindowsSandboxSetupEnabledForTests
        {
            get { return _windowsSandboxSetupButton.Enabled; }
        }

        internal Task DetectWindowsSandboxForTestsAsync()
        {
            return DetectWindowsSandboxAsync();
        }

        internal Task StartWindowsSandboxSetupForTestsAsync()
        {
            return StartWindowsSandboxSetupAsync(AppServerWindowsSandboxSetupMode.Unelevated);
        }

        internal void ApplyWindowsSandboxSetupCompletedForTests(AppServerWindowsSandboxSetupCompletedEvent result)
        {
            ApplyWindowsSandboxSetupCompleted(result);
        }

        internal void SelectRuntimeSourceForTests(string source)
        {
            SelectRuntimeSource(source);
            UpdateRuntimeSourceControls();
            UpdateDiagnosticsText(null);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_targetNameTextBox != null)
            {
                _targetNameTextBox.Focus();
            }
            else
            {
                _runtimeSourceComboBox.Focus();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _lifetimeCts.Cancel();
            _lifetimeCts.Dispose();
            base.OnFormClosed(e);
        }

        private void RuntimeSourceComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateRuntimeSourceControls();
            UpdateDiagnosticsText(null);
        }

        private void RemoteAuthenticationComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateRemoteAuthenticationControls();
            UpdateDiagnosticsText(null);
        }

        private async void DetectButton_Click(object sender, EventArgs e)
        {
            if (IsWslSelected())
            {
                await DetectWslCodexAsync();
                return;
            }

            if (IsRemoteMacSelected())
            {
                await DetectRemoteMacCodexAsync();
                return;
            }
        }

        private void BrowseCustomButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = UiLocalization.Text("WslAppServerSettingsForm.OpenFileDialog.CustomExecutableTitle", "Select Codex executable");
                dialog.Filter = UiLocalization.Text(
                    "WslAppServerSettingsForm.OpenFileDialog.CustomExecutableFilter",
                    "Codex Executable|codex.exe;codex.cmd|Executable Files|*.exe;*.cmd|All Files|*.*");
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;

                if (!string.IsNullOrWhiteSpace(_customExecutableTextBox.Text))
                {
                    var currentPath = _customExecutableTextBox.Text.Trim();
                    if (File.Exists(currentPath))
                    {
                        dialog.FileName = currentPath;
                    }
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _customExecutableTextBox.Text = dialog.FileName;
                    UpdateDiagnosticsText(null);
                }
            }
        }

        private void BrowseRemotePrivateKeyButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = UiLocalization.Text("WslAppServerSettingsForm.OpenFileDialog.RemotePrivateKeyTitle", "Select SSH private key");
                dialog.Filter = UiLocalization.Text(
                    "WslAppServerSettingsForm.OpenFileDialog.RemotePrivateKeyFilter",
                    "SSH Private Key|id_*;*.pem;*.key|All Files|*.*");
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;

                if (!string.IsNullOrWhiteSpace(_remotePrivateKeyTextBox.Text))
                {
                    var currentPath = _remotePrivateKeyTextBox.Text.Trim();
                    if (File.Exists(currentPath))
                    {
                        dialog.FileName = currentPath;
                    }
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _remotePrivateKeyTextBox.Text = dialog.FileName;
                    UpdateDiagnosticsText(null);
                }
            }
        }

        private void CustomExecutableTextBox_TextChanged(object sender, EventArgs e)
        {
            if (IsCustomSelected())
            {
                UpdateDiagnosticsText(null);
            }
        }

        private void RemoteField_TextChanged(object sender, EventArgs e)
        {
            if (_isLoadingRemoteProfile)
            {
                return;
            }

            if (IsRemoteMacSelected())
            {
                if (_targetEditing && ReferenceEquals(sender, _remoteHostTextBox) &&
                    !TargetRuntimeSettingsPlanner.IsSameSshHost(GetSelectedRemoteProfile()?.Ssh.HostName, _remoteHostTextBox.Text))
                    _remoteHostKeyTextBox.Clear();
                SaveCurrentRemoteProfileFromFields();
                if (ShouldRefreshRemoteProfileDisplayName(sender))
                {
                    RefreshSelectedRemoteProfileText();
                }

                UpdateDiagnosticsText(null);
            }
        }

        private bool ShouldRefreshRemoteProfileDisplayName(object sender)
        {
            return ReferenceEquals(sender, _remoteAliasTextBox) ||
                ReferenceEquals(sender, _remoteHostTextBox) ||
                ReferenceEquals(sender, _remotePortTextBox) ||
                ReferenceEquals(sender, _remoteUserNameTextBox);
        }

        private void RemoteProfileComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isLoadingRemoteProfile)
            {
                return;
            }

            SaveCurrentRemoteProfileFromFields();
            LoadSelectedRemoteProfileToFields();
            UpdateRuntimeSourceControls();
            UpdateDiagnosticsText(null);
        }

        private void AddRemoteProfileButton_Click(object sender, EventArgs e)
        {
            if (_targetEditing)
            {
                return;
            }

            SaveCurrentRemoteProfileFromFields();
            var profile = new RemoteUnixSshProfileConfigSnapshot
            {
                Id = "remote-unix-" + Guid.NewGuid().ToString("N"),
                Name = UiLocalization.Text("WslAppServerSettingsForm.RemoteProfile.NewName", "New remote"),
                Ssh = new RemoteMacSshConfigSnapshot
                {
                    PrivateKeyPath = ResolveDefaultSshPrivateKeyPath(),
                    ShellCommand = "bash"
                }
            };
            _remoteProfiles.Add(profile);
            RefreshRemoteProfileItems(profile.Id);
            LoadSelectedRemoteProfileToFields();
            UpdateRuntimeSourceControls();
            UpdateDiagnosticsText(null);
            _remoteAliasTextBox.Focus();
            _remoteAliasTextBox.SelectAll();
        }

        private void DeleteRemoteProfileButton_Click(object sender, EventArgs e)
        {
            if (_targetEditing)
            {
                return;
            }

            var profile = GetSelectedRemoteProfile();
            if (profile == null)
            {
                return;
            }

            if (_remoteProfiles.Count <= 1)
            {
                profile.Name = UiLocalization.Text("WslAppServerSettingsForm.RemoteProfile.NewName", "New remote");
                profile.Ssh = new RemoteMacSshConfigSnapshot
                {
                    PrivateKeyPath = ResolveDefaultSshPrivateKeyPath(),
                    ShellCommand = "bash"
                };
                RefreshRemoteProfileItems(profile.Id);
                LoadSelectedRemoteProfileToFields();
                UpdateDiagnosticsText(null);
                return;
            }

            var selectedIndex = _remoteProfiles.IndexOf(profile);
            _remoteProfiles.Remove(profile);
            var nextIndex = Math.Min(Math.Max(selectedIndex, 0), _remoteProfiles.Count - 1);
            RefreshRemoteProfileItems(_remoteProfiles[nextIndex].Id);
            LoadSelectedRemoteProfileToFields();
            UpdateRuntimeSourceControls();
            UpdateDiagnosticsText(null);
        }

        private async Task DetectWslCodexAsync()
        {
            var config = BuildWslConfigFromFields();
            SetRuntimeUpdateBusy(true);
            SetDiagnosticsText(UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Checking", "Checking WSL Codex..."));

            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, timeoutCts.Token))
            {
                try
                {
                    var version = await _runtimeManager.VerifyWslCodexAsync(config, linkedCts.Token);
                    SetDiagnosticsText(BuildSuccessDiagnostics(config, version));
                }
                catch (OperationCanceledException)
                {
                    SetDiagnosticsText(UiLocalization.Text(
                        "WslAppServerSettingsForm.Diagnostics.Timeout",
                        "Detection timed out. WSL may still be starting; wait a moment and try again."));
                }
                catch (Exception ex)
                {
                    SetDiagnosticsText(BuildFailureDiagnostics(config, ex));
                }
                finally
                {
                    if (!IsDisposed)
                    {
                        SetRuntimeUpdateBusy(false);
                    }
                }
            }
        }

        private async Task DetectRemoteMacCodexAsync()
        {
            if (!ValidateRemoteMacSettings(false))
            {
                return;
            }

            var config = BuildRemoteMacConfigFromFields();
            SetRuntimeUpdateBusy(true);
            SetDiagnosticsText(UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.RemoteChecking", "Checking Remote Unix Codex..."));

            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token, timeoutCts.Token))
            {
                try
                {
                    var version = await VerifyRemoteMacCodexWithHostKeyDiscoveryAsync(config, linkedCts.Token);
                    SetDiagnosticsText(BuildRemoteMacSuccessDiagnostics(config, version));
                }
                catch (OperationCanceledException)
                {
                    SetDiagnosticsText(UiLocalization.Text(
                        "WslAppServerSettingsForm.Diagnostics.RemoteTimeout",
                        "Detection timed out. Check the remote network connection and SSH settings."));
                }
                catch (Exception ex)
                {
                    SetDiagnosticsText(BuildRemoteMacFailureDiagnostics(config, ex));
                }
                finally
                {
                    if (!IsDisposed)
                    {
                        SetRuntimeUpdateBusy(false);
                    }
                }
            }
        }

        private async Task<string> VerifyRemoteMacCodexWithHostKeyDiscoveryAsync(
            RemoteMacSshConfigSnapshot config,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _runtimeManager.VerifyRemoteUnixCodexAsync(config, cancellationToken);
            }
            catch (Exception ex)
            {
                if (!TryConfirmAndApplyRemoteHostKey(config, ex))
                {
                    throw;
                }
            }

            var updatedConfig = BuildRemoteMacConfigFromFields();
            return await _runtimeManager.VerifyRemoteUnixCodexAsync(updatedConfig, cancellationToken);
        }

        private bool TryConfirmAndApplyRemoteHostKey(RemoteMacSshConfigSnapshot config, Exception ex)
        {
            string hostKey;
            if (!RemoteSshHostKeyTrustPlanner.ShouldPromptForTrust(config, ex != null ? ex.Message : null, out hostKey))
            {
                return false;
            }

            var confirm = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(
                        "WslAppServerSettingsForm.Message.RemoteHostKeyTrustConfirm.Template",
                        "The remote host key was reported as:\r\n\r\n{0}\r\n\r\nTrust this host key and retry detection?"),
                    hostKey),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return false;
            }

            _remoteHostKeyTextBox.Text = hostKey;
            SaveCurrentRemoteProfileFromFields();
            SetDiagnosticsText(UiLocalization.Text(
                "WslAppServerSettingsForm.Diagnostics.RemoteHostKeyTrusted",
                "Host key trusted. Retrying Remote Unix Codex detection..."));
            return true;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            if (_targetEditing)
            {
                SaveTargetFromButtonAsync();
                return;
            }

            if (!ValidateSelectedRuntimeSettings())
            {
                return;
            }

            SelectedRuntimeSource = GetSelectedRuntimeSource();
            CustomExecutablePath = string.IsNullOrWhiteSpace(_customExecutableTextBox.Text)
                ? null
                : _customExecutableTextBox.Text.Trim();
            SelectedWindowsShellPreference = GetSelectedWindowsShellPreference();
            WslConfig = BuildWslConfigFromFields();
            SaveCurrentRemoteProfileFromFields();
            RemoteUnixProfiles = CloneRemoteProfiles(_remoteProfiles);
            var selectedProfile = GetSelectedRemoteProfile();
            SelectedRemoteUnixProfileId = selectedProfile != null ? selectedProfile.Id : null;
            RemoteMacConfig = selectedProfile != null ? CloneRemoteMacConfig(selectedProfile.Ssh) : BuildRemoteMacConfigFromFields();
            DialogResult = DialogResult.OK;
            Close();
        }

        private TextBox CreateTextBox(string accessibleName, int tabIndex)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Top,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, false, false);
            return textBox;
        }

        private static Label AddRow(TableLayoutPanel root, int row, string labelText, Control control)
        {
            var label = new Label
            {
                Text = labelText,
                AccessibleName = labelText,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);

            root.Controls.Add(label, 0, row);
            root.Controls.Add(control, 1, row);
            return label;
        }

        private void AddRuntimeSourceItems()
        {
            _runtimeSourceComboBox.Items.Add(new RuntimeSourceItem(
                CodexRuntimeSources.Bundled,
                UiLocalization.Text("WslAppServerSettingsForm.Source.Bundled", "Bundled")));
            _runtimeSourceComboBox.Items.Add(new RuntimeSourceItem(
                CodexRuntimeSources.System,
                UiLocalization.Text("WslAppServerSettingsForm.Source.System", "System")));
            _runtimeSourceComboBox.Items.Add(new RuntimeSourceItem(
                CodexRuntimeSources.Custom,
                UiLocalization.Text("WslAppServerSettingsForm.Source.Custom", "Custom")));
            _runtimeSourceComboBox.Items.Add(new RuntimeSourceItem(
                CodexRuntimeSources.Wsl,
                UiLocalization.Text("WslAppServerSettingsForm.Source.Wsl", "WSL")));
            _runtimeSourceComboBox.Items.Add(new RuntimeSourceItem(
                CodexRuntimeSources.RemoteUnixSsh,
                UiLocalization.Text("WslAppServerSettingsForm.Source.RemoteUnixSsh", "Remote Unix (SSH)")));
        }

        private void AddWindowsShellItems()
        {
            _windowsShellComboBox.Items.Add(new WindowsShellItem(
                WindowsShellPreferences.Auto,
                UiLocalization.Text("WslAppServerSettingsForm.WindowsShell.Auto", "Auto (cmd compatible)")));
            _windowsShellComboBox.Items.Add(new WindowsShellItem(
                WindowsShellPreferences.PowerShell7,
                UiLocalization.Text("WslAppServerSettingsForm.WindowsShell.PowerShell7", "PowerShell 7 (pwsh)")));
            _windowsShellComboBox.Items.Add(new WindowsShellItem(
                WindowsShellPreferences.WindowsPowerShell,
                UiLocalization.Text("WslAppServerSettingsForm.WindowsShell.WindowsPowerShell", "Windows PowerShell 5.1")));
        }

        private void AddRemoteAuthenticationItems()
        {
            _remoteAuthenticationComboBox.Items.Add(new RemoteAuthenticationItem(
                RemoteSshAuthenticationMode.PrivateKey,
                UiLocalization.Text("WslAppServerSettingsForm.RemoteAuthentication.PrivateKey", "SSH key")));
            _remoteAuthenticationComboBox.Items.Add(new RemoteAuthenticationItem(
                RemoteSshAuthenticationMode.Password,
                UiLocalization.Text("WslAppServerSettingsForm.RemoteAuthentication.Password", "Password")));
        }

        private void SelectRuntimeSource(string source)
        {
            var normalized = CodexRuntimeSources.CanonicalizeForExecution(source);
            for (var i = 0; i < _runtimeSourceComboBox.Items.Count; i++)
            {
                var item = _runtimeSourceComboBox.Items[i] as RuntimeSourceItem;
                if (item != null && string.Equals(item.Value, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    _runtimeSourceComboBox.SelectedIndex = i;
                    return;
                }
            }

            _runtimeSourceComboBox.SelectedIndex = 0;
        }

        private void SelectWindowsShellPreference(string preference)
        {
            var normalized = WindowsShellPreferences.Normalize(preference);
            for (var i = 0; i < _windowsShellComboBox.Items.Count; i++)
            {
                var item = _windowsShellComboBox.Items[i] as WindowsShellItem;
                if (item != null && string.Equals(item.Value, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    _windowsShellComboBox.SelectedIndex = i;
                    return;
                }
            }

            _windowsShellComboBox.SelectedIndex = 0;
        }

        private void SelectRemoteAuthentication(RemoteSshAuthenticationMode mode)
        {
            for (var i = 0; i < _remoteAuthenticationComboBox.Items.Count; i++)
            {
                var item = _remoteAuthenticationComboBox.Items[i] as RemoteAuthenticationItem;
                if (item != null && item.Value == NormalizeRemoteAuthenticationMode(mode))
                {
                    _remoteAuthenticationComboBox.SelectedIndex = i;
                    return;
                }
            }

            _remoteAuthenticationComboBox.SelectedIndex = 0;
        }

        private void UpdateRuntimeSourceControls()
        {
            var isWsl = IsWslSelected();
            var isCustom = IsCustomSelected();
            var isBundled = IsBundledFamilySelected();
            var isRemoteUnix = IsRemoteMacSelected();
            var isWindowsRuntime = !isWsl && !isRemoteUnix;

            SetRowVisible(_windowsShellLabel, _windowsShellComboBox, isWindowsRuntime);
            SetRowVisible(_customExecutableLabel, _customExecutablePanel, isCustom);
            SetRowVisible(_distributionLabel, _distributionTextBox, isWsl);
            SetRowVisible(_codexCommandLabel, _codexCommandTextBox, isWsl);
            SetRowVisible(_shellCommandLabel, _shellCommandTextBox, isWsl);
            SetRowVisible(_remoteProfileLabel, _remoteProfilePanel, isRemoteUnix);
            SetRowVisible(_remoteAliasLabel, _remoteAliasTextBox, isRemoteUnix);
            SetRowVisible(_remoteHostLabel, _remoteHostTextBox, isRemoteUnix);
            SetRowVisible(_remotePortLabel, _remotePortTextBox, isRemoteUnix);
            SetRowVisible(_remoteUserNameLabel, _remoteUserNameTextBox, isRemoteUnix);
            SetRowVisible(_remoteAuthenticationLabel, _remoteAuthenticationComboBox, isRemoteUnix);
            SetRowVisible(_remoteShellLabel, _remoteShellTextBox, isRemoteUnix);
            SetRowVisible(_remoteCodexCommandLabel, _remoteCodexCommandTextBox, isRemoteUnix);

            _runtimeSourceComboBox.Enabled = !_isRuntimeOperationBusy;
            _windowsShellComboBox.Enabled = isWindowsRuntime && !_isRuntimeOperationBusy;
            _customExecutableTextBox.Enabled = isCustom && !_isRuntimeOperationBusy;
            _browseCustomButton.Enabled = isCustom && !_isRuntimeOperationBusy;
            _distributionTextBox.Enabled = isWsl && !_isRuntimeOperationBusy;
            _codexCommandTextBox.Enabled = isWsl && !_isRuntimeOperationBusy;
            _shellCommandTextBox.Enabled = isWsl && !_isRuntimeOperationBusy;
            _remoteProfileComboBox.Enabled = isRemoteUnix && !_isRuntimeOperationBusy;
            _addRemoteProfileButton.Enabled = isRemoteUnix && !_isRuntimeOperationBusy;
            _deleteRemoteProfileButton.Enabled = isRemoteUnix && !_isRuntimeOperationBusy;
            _remoteAliasTextBox.Enabled = isRemoteUnix && !_isRuntimeOperationBusy;
            _remoteHostTextBox.Enabled = isRemoteUnix && !_isRuntimeOperationBusy;
            _remotePortTextBox.Enabled = isRemoteUnix && !_isRuntimeOperationBusy;
            _remoteUserNameTextBox.Enabled = isRemoteUnix && !_isRuntimeOperationBusy;
            _remoteAuthenticationComboBox.Enabled = isRemoteUnix && !_isRuntimeOperationBusy;
            _remoteShellTextBox.Enabled = isRemoteUnix && !_isRuntimeOperationBusy;
            _remoteCodexCommandTextBox.Enabled = isRemoteUnix && !_isRuntimeOperationBusy;
            _detectButton.Visible = isWsl || isRemoteUnix;
            _detectButton.Enabled = (isWsl || isRemoteUnix) && !_isRuntimeOperationBusy;
            UpdateRemoteAuthenticationControls();
            UpdateTargetEditingControls();
            RefreshWindowsSandboxState();
        }

        private void UpdateRemoteAuthenticationControls()
        {
            var isRemoteUnix = IsRemoteMacSelected();
            var usesPrivateKey = GetSelectedRemoteAuthenticationMode() == RemoteSshAuthenticationMode.PrivateKey;
            if (isRemoteUnix && usesPrivateKey && string.IsNullOrWhiteSpace(_remotePrivateKeyTextBox.Text))
            {
                _remotePrivateKeyTextBox.Text = ResolveDefaultSshPrivateKeyPath();
            }

            SetRowVisible(_remotePrivateKeyLabel, _remotePrivateKeyPanel, isRemoteUnix && usesPrivateKey);
            SetRowVisible(_remotePasswordLabel, _remotePasswordTextBox, isRemoteUnix && !usesPrivateKey);
            SetRowVisible(_remoteHostKeyLabel, _remoteHostKeyTextBox, false);
            SetRowVisible(_remotePlinkLabel, _remotePlinkTextBox, false);
            _remotePrivateKeyTextBox.Enabled = isRemoteUnix && usesPrivateKey && !_isRuntimeOperationBusy;
            _browseRemotePrivateKeyButton.Enabled = isRemoteUnix && usesPrivateKey && !_isRuntimeOperationBusy;
            _remotePasswordTextBox.Enabled = isRemoteUnix && !usesPrivateKey && !_isRuntimeOperationBusy;
            _remoteHostKeyTextBox.Enabled = false;
            _remotePlinkTextBox.Enabled = false;
        }

        private void SetRuntimeUpdateBusy(bool busy)
        {
            _isRuntimeOperationBusy = busy;
            UpdateRuntimeSourceControls();
            RefreshWindowsSandboxState();
            _okButton.Enabled = !busy;
        }

        private static void SetRowVisible(Control label, Control control, bool visible)
        {
            if (label != null)
            {
                label.Visible = visible;
            }

            if (control != null)
            {
                control.Visible = visible;
            }
        }

        private void UpdateDiagnosticsText(string detectionText)
        {
            if (!string.IsNullOrWhiteSpace(detectionText))
            {
                SetDiagnosticsText(detectionText);
                return;
            }

            var config = BuildWslConfigFromFields();
            var item = _runtimeSourceComboBox.SelectedItem as RuntimeSourceItem;
            var source = item != null ? item.Value : CodexRuntimeSources.Bundled;
            var sourceText = item != null ? item.ToString() : source;

            var builder = new StringBuilder();
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Source.Template", "Current runtime source: {0}"),
                sourceText));
            builder.AppendLine();

            if (string.Equals(source, CodexRuntimeSources.Custom, StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine(UiLocalization.Text(
                    "WslAppServerSettingsForm.Diagnostics.CustomRuntime",
                    "Custom mode starts the App Server from the selected Codex executable."));
                builder.AppendLine(string.Format(
                    UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.CustomPath.Template", "Custom path: {0}"),
                    string.IsNullOrWhiteSpace(_customExecutableTextBox.Text)
                        ? UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.CustomPathMissing", "Not configured")
                        : _customExecutableTextBox.Text.Trim()));
                SetDiagnosticsText(builder.ToString());
                return;
            }

            if (string.Equals(source, CodexRuntimeSources.System, StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine(UiLocalization.Text(
                    "WslAppServerSettingsForm.Diagnostics.SystemRuntime",
                    "System mode starts the Codex command found on the Windows PATH."));
                SetDiagnosticsText(builder.ToString());
                return;
            }

            if (string.Equals(source, CodexRuntimeSources.Bundled, StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine(UiLocalization.Text(
                    "WslAppServerSettingsForm.Diagnostics.BundledRuntime",
                    "Bundled mode starts the packaged Windows Codex runtime."));
                AppendBundledRuntimeDiagnostics(builder);
                SetDiagnosticsText(builder.ToString());
                return;
            }

            if (CodexRuntimeSources.IsRemoteUnixSsh(source))
            {
                var remoteConfig = BuildRemoteMacConfigFromFields();
                builder.AppendLine(UiLocalization.Text(
                    "WslAppServerSettingsForm.Diagnostics.RemoteUnixRuntime",
                    "Remote Unix mode starts the App Server over SSH. Workspaces, history, skills, plugins, and MCP are all read from the remote machine."));
                builder.AppendLine(string.Format(
                    UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.RemoteTarget.Template", "Target: {0}"),
                    BuildRemoteTargetText(remoteConfig)));
                builder.AppendLine(string.Format(
                    UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Command.Template", "Command: {0}"),
                    remoteConfig.CodexCommand));
                if (remoteConfig.AuthenticationMode == RemoteSshAuthenticationMode.Password)
                {
                    builder.AppendLine(UiLocalization.Text(
                        "WslAppServerSettingsForm.Diagnostics.RemotePasswordBackend",
                        "Password authentication uses the bundled Plink runtime. Host key trust is detected during remote checks."));
                }
                else
                {
                    builder.AppendLine(UiLocalization.Text(
                        "WslAppServerSettingsForm.Diagnostics.RemotePrivateKeyBackend",
                        "SSH key authentication uses Windows OpenSSH and its known_hosts trust policy. The saved host key fingerprint is used only by password authentication through Plink."));
                }

                builder.AppendLine(UiLocalization.Text(
                    "WslAppServerSettingsForm.Diagnostics.RemotePathNote",
                    "Remote workspaces must use absolute Unix paths such as /home/name/repo or /Users/name/repo."));
                SetDiagnosticsText(builder.ToString());
                return;
            }

            builder.AppendLine(UiLocalization.Text(
                "WslAppServerSettingsForm.Diagnostics.WslCommand",
                "WSL mode starts the App Server with:"));
            builder.AppendLine("wsl.exe -d " + config.DistributionName + " --exec " + config.CodexCommand + " app-server --enable goals --listen stdio://");
            builder.AppendLine();
            AppendCodexHomeDiagnostics(builder, config);
            builder.AppendLine();
            builder.AppendLine(UiLocalization.Text(
                "WslAppServerSettingsForm.Diagnostics.PathNote",
                "Windows drive workspaces are mapped to /mnt/<drive>/... before they are sent to WSL."));
            SetDiagnosticsText(builder.ToString());
        }

        private void SetDiagnosticsText(string text)
        {
            TextBoxCaretHelper.SetTextAndMoveToStart(_diagnosticsTextBox, text);
        }

        private bool ValidateSelectedRuntimeSettings()
        {
            if (IsRemoteMacSelected())
            {
                return ValidateRemoteMacSettings(true);
            }

            if (!IsCustomSelected())
            {
                return true;
            }

            var customPath = string.IsNullOrWhiteSpace(_customExecutableTextBox.Text)
                ? null
                : _customExecutableTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(customPath))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("WslAppServerSettingsForm.Message.CustomPathRequired", "Select a Codex executable path."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _customExecutableTextBox.Focus();
                return false;
            }

            if (!File.Exists(customPath))
            {
                MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text("WslAppServerSettingsForm.Message.CustomPathNotFound.Template", "The selected Codex executable was not found: {0}"),
                        customPath),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _customExecutableTextBox.Focus();
                return false;
            }

            return true;
        }

        private bool ValidateRemoteMacSettings(bool focusControl)
        {
            if (string.IsNullOrWhiteSpace(_remoteHostTextBox.Text))
            {
                ShowRuntimeValidationMessage(
                    UiLocalization.Text("WslAppServerSettingsForm.Message.RemoteHostRequired", "Enter the remote host name or IP address."),
                    focusControl ? _remoteHostTextBox : null);
                return false;
            }

            int port;
            if (!int.TryParse((_remotePortTextBox.Text ?? string.Empty).Trim(), out port) || port <= 0 || port > 65535)
            {
                ShowRuntimeValidationMessage(
                    UiLocalization.Text("WslAppServerSettingsForm.Message.RemotePortInvalid", "Enter a valid SSH port from 1 to 65535."),
                    focusControl ? _remotePortTextBox : null);
                return false;
            }

            if (string.IsNullOrWhiteSpace(_remoteUserNameTextBox.Text))
            {
                ShowRuntimeValidationMessage(
                    UiLocalization.Text("WslAppServerSettingsForm.Message.RemoteUserNameRequired", "Enter the remote user name."),
                    focusControl ? _remoteUserNameTextBox : null);
                return false;
            }

            if (GetSelectedRemoteAuthenticationMode() == RemoteSshAuthenticationMode.PrivateKey)
            {
                if (string.IsNullOrWhiteSpace(_remotePrivateKeyTextBox.Text))
                {
                    ShowRuntimeValidationMessage(
                        UiLocalization.Text("WslAppServerSettingsForm.Message.RemotePrivateKeyRequired", "Select an SSH private key."),
                        focusControl ? _remotePrivateKeyTextBox : null);
                    return false;
                }

                if (!File.Exists(_remotePrivateKeyTextBox.Text.Trim()))
                {
                    ShowRuntimeValidationMessage(
                        string.Format(
                            UiLocalization.Text("WslAppServerSettingsForm.Message.RemotePrivateKeyNotFound.Template", "The selected SSH private key was not found: {0}"),
                            _remotePrivateKeyTextBox.Text.Trim()),
                        focusControl ? _remotePrivateKeyTextBox : null);
                    return false;
                }
            }
            else if (string.IsNullOrWhiteSpace(_remotePasswordTextBox.Text) &&
                     string.IsNullOrWhiteSpace(GetSelectedRemoteProfileProtectedPassword()))
            {
                ShowRuntimeValidationMessage(
                    UiLocalization.Text("WslAppServerSettingsForm.Message.RemotePasswordRequired", "Enter the SSH password. It will be saved with Windows user protection."),
                    focusControl ? _remotePasswordTextBox : null);
                return false;
            }
            else if (GetSelectedRemoteAuthenticationMode() == RemoteSshAuthenticationMode.Password &&
                     !string.IsNullOrWhiteSpace(_remotePlinkTextBox.Text) &&
                     !File.Exists(_remotePlinkTextBox.Text.Trim()))
            {
                ShowRuntimeValidationMessage(
                    string.Format(
                        UiLocalization.Text("WslAppServerSettingsForm.Message.RemotePlinkNotFound.Template", "The selected Plink executable was not found: {0}"),
                        _remotePlinkTextBox.Text.Trim()),
                    focusControl ? _remotePlinkTextBox : null);
                return false;
            }

            return true;
        }

        private void ShowRuntimeValidationMessage(string message, Control focusControl)
        {
            MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (focusControl != null)
            {
                focusControl.Focus();
            }
        }

        private WslAppServerConfigSnapshot BuildWslConfigFromFields()
        {
            return new WslAppServerConfigSnapshot
            {
                DistributionName = _distributionTextBox.Text,
                CodexCommand = _codexCommandTextBox.Text,
                ShellCommand = _shellCommandTextBox.Text
            };
        }

        private RemoteMacSshConfigSnapshot BuildRemoteMacConfigFromFields()
        {
            int port;
            if (!int.TryParse((_remotePortTextBox.Text ?? string.Empty).Trim(), out port))
            {
                port = RemoteMacSshConfigSnapshot.DefaultPort;
            }

            var config = new RemoteMacSshConfigSnapshot
            {
                HostName = _remoteHostTextBox.Text,
                Port = RemoteSshCommandLine.NormalizePort(port),
                UserName = _remoteUserNameTextBox.Text,
                AuthenticationMode = GetSelectedRemoteAuthenticationMode(),
                PrivateKeyPath = _remotePrivateKeyTextBox.Text,
                HostKeyFingerprint = _remoteHostKeyTextBox.Text,
                PlinkExecutablePath = _remotePlinkTextBox.Text,
                ShellCommand = _remoteShellTextBox.Text,
                CodexCommand = _remoteCodexCommandTextBox.Text,
                ProtectedPassword = GetSelectedRemoteProfileProtectedPassword()
            };

            if (config.AuthenticationMode == RemoteSshAuthenticationMode.Password &&
                !string.IsNullOrEmpty(_remotePasswordTextBox.Text))
            {
                config.ProtectedPassword = RemoteSshPasswordProtector.Protect(_remotePasswordTextBox.Text);
            }

            if (config.AuthenticationMode == RemoteSshAuthenticationMode.PrivateKey)
            {
                config.ProtectedPassword = null;
            }

            return config;
        }

        private RemoteSshAuthenticationMode GetSelectedRemoteAuthenticationMode()
        {
            var item = _remoteAuthenticationComboBox.SelectedItem as RemoteAuthenticationItem;
            return item != null ? item.Value : RemoteSshAuthenticationMode.PrivateKey;
        }

        private static string ResolveDefaultSshPrivateKeyPath()
        {
            var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sshDirectory = Path.Combine(userProfilePath, ".ssh");
            var preferredKeyNames = new[] { "id_ed25519", "id_rsa", "id_ecdsa", "id_dsa" };
            foreach (var keyName in preferredKeyNames)
            {
                var keyPath = Path.Combine(sshDirectory, keyName);
                if (File.Exists(keyPath))
                {
                    return keyPath;
                }
            }

            try
            {
                if (Directory.Exists(sshDirectory))
                {
                    foreach (var keyPath in Directory.GetFiles(sshDirectory, "id_*"))
                    {
                        if (!keyPath.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
                        {
                            return keyPath;
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return Path.Combine(sshDirectory, "id_ed25519");
        }

        private string GetSelectedRuntimeSource()
        {
            var item = _runtimeSourceComboBox.SelectedItem as RuntimeSourceItem;
            return item != null ? item.Value : CodexRuntimeSources.Bundled;
        }

        private string GetSelectedWindowsShellPreference()
        {
            var item = _windowsShellComboBox.SelectedItem as WindowsShellItem;
            return item != null ? item.Value : WindowsShellPreferences.Auto;
        }

        private bool IsWslSelected()
        {
            return string.Equals(GetSelectedRuntimeSource(), CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCustomSelected()
        {
            return string.Equals(GetSelectedRuntimeSource(), CodexRuntimeSources.Custom, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsBundledFamilySelected()
        {
            return CodexRuntimeSources.IsBundledFamily(GetSelectedRuntimeSource());
        }

        private bool IsRemoteMacSelected()
        {
            return CodexRuntimeSources.IsRemoteUnixSsh(GetSelectedRuntimeSource());
        }

        private void RefreshRemoteProfileItems(string selectedProfileId)
        {
            _isLoadingRemoteProfile = true;
            try
            {
                _remoteProfileComboBox.Items.Clear();
                for (var i = 0; i < _remoteProfiles.Count; i++)
                {
                    var profile = _remoteProfiles[i];
                    _remoteProfileComboBox.Items.Add(new RemoteProfileItem(
                        profile.Id,
                        BuildRemoteProfileDisplayName(profile)));
                }

                var selectedIndex = 0;
                if (!string.IsNullOrWhiteSpace(selectedProfileId))
                {
                    for (var i = 0; i < _remoteProfileComboBox.Items.Count; i++)
                    {
                        var item = _remoteProfileComboBox.Items[i] as RemoteProfileItem;
                        if (item != null && string.Equals(item.Id, selectedProfileId, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }

                if (_remoteProfileComboBox.Items.Count > 0)
                {
                    _remoteProfileComboBox.SelectedIndex = selectedIndex;
                }
            }
            finally
            {
                _isLoadingRemoteProfile = false;
            }
        }

        private void RefreshSelectedRemoteProfileText()
        {
            var profile = GetLoadedRemoteProfile();
            if (profile == null)
            {
                return;
            }

            for (var i = 0; i < _remoteProfileComboBox.Items.Count; i++)
            {
                var item = _remoteProfileComboBox.Items[i] as RemoteProfileItem;
                if (item != null && string.Equals(item.Id, profile.Id, StringComparison.OrdinalIgnoreCase))
                {
                    var selectedIndex = _remoteProfileComboBox.SelectedIndex;
                    _isLoadingRemoteProfile = true;
                    try
                    {
                        _remoteProfileComboBox.Items[i] = new RemoteProfileItem(
                            profile.Id,
                            BuildRemoteProfileDisplayName(profile));
                        _remoteProfileComboBox.SelectedIndex = selectedIndex;
                    }
                    finally
                    {
                        _isLoadingRemoteProfile = false;
                    }

                    return;
                }
            }
        }

        private void LoadSelectedRemoteProfileToFields()
        {
            var profile = GetSelectedRemoteProfile();
            var config = profile != null ? profile.Ssh : new RemoteMacSshConfigSnapshot();
            _isLoadingRemoteProfile = true;
            try
            {
                _remoteAliasTextBox.Text = profile != null ? profile.Name : null;
                _remoteHostTextBox.Text = config.HostName;
                _remotePortTextBox.Text = RemoteSshCommandLine.NormalizePort(config.Port).ToString();
                _remoteUserNameTextBox.Text = config.UserName;
                SelectRemoteAuthentication(config.AuthenticationMode);
                _remotePrivateKeyTextBox.Text = string.IsNullOrWhiteSpace(config.PrivateKeyPath)
                    ? ResolveDefaultSshPrivateKeyPath()
                    : config.PrivateKeyPath;
                _remotePasswordTextBox.Text = string.Empty;
                _remoteHostKeyTextBox.Text = config.HostKeyFingerprint;
                _remotePlinkTextBox.Text = config.PlinkExecutablePath;
                _remoteShellTextBox.Text = config.ShellCommand;
                _remoteCodexCommandTextBox.Text = config.CodexCommand;
                _loadedRemoteProfileId = profile != null ? profile.Id : null;
            }
            finally
            {
                _isLoadingRemoteProfile = false;
            }
        }

        private void SaveCurrentRemoteProfileFromFields()
        {
            if (_isLoadingRemoteProfile)
            {
                return;
            }

            var profile = GetLoadedRemoteProfile() ?? GetSelectedRemoteProfile();
            if (profile == null)
            {
                return;
            }

            profile.Name = string.IsNullOrWhiteSpace(_remoteAliasTextBox.Text)
                ? BuildRemoteProfileDisplayName(new RemoteUnixSshProfileConfigSnapshot { Ssh = BuildRemoteMacConfigFromFields() })
                : _remoteAliasTextBox.Text.Trim();
            profile.Ssh = BuildRemoteMacConfigFromFields();
        }

        private RemoteUnixSshProfileConfigSnapshot GetSelectedRemoteProfile()
        {
            var item = _remoteProfileComboBox.SelectedItem as RemoteProfileItem;
            if (item == null)
            {
                return _remoteProfiles.Count > 0 ? _remoteProfiles[0] : null;
            }

            for (var i = 0; i < _remoteProfiles.Count; i++)
            {
                var profile = _remoteProfiles[i];
                if (profile != null && string.Equals(profile.Id, item.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return null;
        }

        private RemoteUnixSshProfileConfigSnapshot GetLoadedRemoteProfile()
        {
            return GetRemoteProfileById(_loadedRemoteProfileId);
        }

        private RemoteUnixSshProfileConfigSnapshot GetRemoteProfileById(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            for (var i = 0; i < _remoteProfiles.Count; i++)
            {
                var profile = _remoteProfiles[i];
                if (profile != null && string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return null;
        }

        private string GetSelectedRemoteProfileProtectedPassword()
        {
            var profile = GetLoadedRemoteProfile() ?? GetSelectedRemoteProfile();
            return profile != null && profile.Ssh != null ? profile.Ssh.ProtectedPassword : null;
        }

        private static List<RemoteUnixSshProfileConfigSnapshot> NormalizeRemoteProfiles(
            IList<RemoteUnixSshProfileConfigSnapshot> profiles,
            string selectedProfileId,
            RemoteMacSshConfigSnapshot fallbackConfig)
        {
            var result = CloneRemoteProfiles(profiles);
            if (result.Count == 0)
            {
                var config = CloneRemoteMacConfig(fallbackConfig);
                if (string.IsNullOrWhiteSpace(config.HostName) &&
                    string.Equals(config.ShellCommand, RemoteMacSshConfigSnapshot.DefaultShellCommand, StringComparison.Ordinal))
                {
                    config.ShellCommand = "bash";
                }

                result.Add(new RemoteUnixSshProfileConfigSnapshot
                {
                    Id = "remote-unix-" + Guid.NewGuid().ToString("N"),
                    Name = BuildRemoteProfileDisplayName(config),
                    Ssh = config
                });
            }

            for (var i = 0; i < result.Count; i++)
            {
                var profile = result[i] ?? new RemoteUnixSshProfileConfigSnapshot();
                if (string.IsNullOrWhiteSpace(profile.Id))
                {
                    profile.Id = "remote-unix-" + Guid.NewGuid().ToString("N");
                }

                profile.Ssh = CloneRemoteMacConfig(profile.Ssh);
                if (string.IsNullOrWhiteSpace(profile.Name))
                {
                    profile.Name = BuildRemoteProfileDisplayName(profile.Ssh);
                }

                result[i] = profile;
            }

            return result;
        }

        private static List<RemoteUnixSshProfileConfigSnapshot> CloneRemoteProfiles(IList<RemoteUnixSshProfileConfigSnapshot> profiles)
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
                    Ssh = CloneRemoteMacConfig(profile.Ssh)
                });
            }

            return result;
        }

        private static string BuildRemoteProfileDisplayName(RemoteUnixSshProfileConfigSnapshot profile)
        {
            if (profile == null)
            {
                return UiLocalization.Text("WslAppServerSettingsForm.RemoteProfile.NewName", "New remote");
            }

            return string.IsNullOrWhiteSpace(profile.Name)
                ? BuildRemoteProfileDisplayName(profile.Ssh)
                : profile.Name.Trim();
        }

        private static string BuildRemoteProfileDisplayName(RemoteMacSshConfigSnapshot config)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            if (string.IsNullOrWhiteSpace(config.HostName))
            {
                return UiLocalization.Text("WslAppServerSettingsForm.RemoteProfile.NewName", "New remote");
            }

            return (string.IsNullOrWhiteSpace(config.UserName) ? string.Empty : config.UserName.Trim() + "@") +
                   config.HostName.Trim() +
                   ":" +
                   RemoteSshCommandLine.NormalizePort(config.Port);
        }

        private static WslAppServerConfigSnapshot CloneWslConfig(WslAppServerConfigSnapshot config)
        {
            config = config ?? new WslAppServerConfigSnapshot();
            return new WslAppServerConfigSnapshot
            {
                DistributionName = config.DistributionName,
                CodexCommand = config.CodexCommand,
                ShellCommand = config.ShellCommand
            };
        }

        private static RemoteMacSshConfigSnapshot CloneRemoteMacConfig(RemoteMacSshConfigSnapshot config)
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

        private static string BuildSuccessDiagnostics(WslAppServerConfigSnapshot config, string version)
        {
            var builder = new StringBuilder();
            builder.AppendLine(UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Success", "WSL Codex is available."));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Version.Template", "Version: {0}"),
                string.IsNullOrWhiteSpace(version) ? UiLocalization.Text("Common.Value.Unknown", "Unknown") : version.Trim()));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Distribution.Template", "Distribution: {0}"),
                config.DistributionName));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Command.Template", "Command: {0}"),
                config.CodexCommand));
            AppendCodexHomeDiagnostics(builder, config);
            return builder.ToString();
        }

        private static string BuildFailureDiagnostics(WslAppServerConfigSnapshot config, Exception ex)
        {
            var builder = new StringBuilder();
            builder.AppendLine(UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Failed", "WSL Codex detection failed."));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Distribution.Template", "Distribution: {0}"),
                config.DistributionName));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Command.Template", "Command: {0}"),
                config.CodexCommand));
            AppendCodexHomeDiagnostics(builder, config);
            builder.AppendLine();
            builder.AppendLine(ex != null ? ex.Message : UiLocalization.Text("Common.Value.UnknownError", "Unknown error"));
            return builder.ToString();
        }

        private static string BuildRemoteMacSuccessDiagnostics(RemoteMacSshConfigSnapshot config, string version)
        {
            var builder = new StringBuilder();
            builder.AppendLine(UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.RemoteSuccess", "Remote Unix Codex is available."));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.RemoteTarget.Template", "Target: {0}"),
                BuildRemoteTargetText(config)));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Version.Template", "Version: {0}"),
                string.IsNullOrWhiteSpace(version) ? UiLocalization.Text("Common.Value.Unknown", "Unknown") : version.Trim()));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Command.Template", "Command: {0}"),
                config.CodexCommand));
            builder.AppendLine(UiLocalization.Text(
                "WslAppServerSettingsForm.Diagnostics.RemoteBoundary",
                "Remote Unix mode uses the remote machine's workspaces, Codex home, history, skills, plugins, and MCP configuration."));
            return builder.ToString();
        }

        private static string BuildRemoteMacFailureDiagnostics(RemoteMacSshConfigSnapshot config, Exception ex)
        {
            var builder = new StringBuilder();
            builder.AppendLine(UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.RemoteFailed", "Remote Unix Codex detection failed."));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.RemoteTarget.Template", "Target: {0}"),
                BuildRemoteTargetText(config)));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.Command.Template", "Command: {0}"),
                config.CodexCommand));
            builder.AppendLine();
            builder.AppendLine(ex != null ? ex.Message : UiLocalization.Text("Common.Value.UnknownError", "Unknown error"));
            return builder.ToString();
        }

        private static string BuildRemoteTargetText(RemoteMacSshConfigSnapshot config)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var host = string.IsNullOrWhiteSpace(config.HostName)
                ? UiLocalization.Text("Common.Value.NotConfigured", "Not configured")
                : config.HostName;
            return (string.IsNullOrWhiteSpace(config.UserName) ? string.Empty : config.UserName + "@") +
                   host +
                   ":" +
                   RemoteSshCommandLine.NormalizePort(config.Port);
        }

        private void AppendBundledRuntimeDiagnostics(StringBuilder builder)
        {
            var source = GetSelectedRuntimeSource();
            var info = _runtimeUpdateService.GetInstalledInfo(source);
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.BundledInstalledVersion.Template", "Installed version: {0}"),
                string.IsNullOrWhiteSpace(info.InstalledVersion) ? UiLocalization.Text("Common.Value.Unknown", "Unknown") : info.InstalledVersion));
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.TargetPlatform.Template", "Target platform: {0}"),
                info.TargetTriple));
        }

        private static void AppendCodexHomeDiagnostics(StringBuilder builder, WslAppServerConfigSnapshot config)
        {
            builder.AppendLine(string.Format(
                UiLocalization.Text("WslAppServerSettingsForm.Diagnostics.CodexHomeStrategy.Template", "Codex Home: {0}"),
                UiLocalization.Text("WslAppServerSettingsForm.CodexHomeStrategy.WslDefault", "WSL default (~/.codex)")));
        }

        private static RemoteSshAuthenticationMode NormalizeRemoteAuthenticationMode(RemoteSshAuthenticationMode mode)
        {
            return mode == RemoteSshAuthenticationMode.Password
                ? RemoteSshAuthenticationMode.Password
                : RemoteSshAuthenticationMode.PrivateKey;
        }

        private sealed class RuntimeSourceItem
        {
            public RuntimeSourceItem(string value, string text)
            {
                Value = value;
                Text = text;
            }

            public string Value { get; private set; }

            private string Text { get; set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class WindowsShellItem
        {
            public WindowsShellItem(string value, string text)
            {
                Value = WindowsShellPreferences.Normalize(value);
                Text = text;
            }

            public string Value { get; private set; }

            private string Text { get; set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class RemoteAuthenticationItem
        {
            public RemoteAuthenticationItem(RemoteSshAuthenticationMode value, string text)
            {
                Value = value;
                Text = text;
            }

            public RemoteSshAuthenticationMode Value { get; private set; }

            private string Text { get; set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class RemoteProfileItem
        {
            public RemoteProfileItem(string id, string text)
            {
                Id = id;
                Text = text;
            }

            public string Id { get; private set; }

            private string Text { get; set; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
