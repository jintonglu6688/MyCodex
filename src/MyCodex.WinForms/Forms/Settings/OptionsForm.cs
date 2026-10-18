using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Office;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;
using LanguagePreference = MyCodex.WinForms.Localization.UiLanguagePreference;
using MessagePreviewMode = MyCodex.Shared.Constants.MessageDetailPreviewMode;
using SessionDisplayOrder = MyCodex.Shared.Constants.SessionListDisplayOrder;
using SendShortcutMode = MyCodex.Shared.Constants.SendMessageShortcutMode;
using TextFileMode = MyCodex.Shared.Constants.TextFileOpenMode;
using ThemePreference = MyCodex.Shared.Constants.UiThemePreference;
using FollowUpMode = MyCodex.Shared.Constants.ActiveTurnFollowUpMode;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class OptionsForm : Form
    {
        private readonly TabControl _tabControl;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly NotificationSettingsForm _notificationSettingsForm;
        private readonly ScreenReaderSettingsForm _screenReaderSettingsForm;
        private readonly ComboBox _themeComboBox;
        private readonly ComboBox _languageComboBox;
        private readonly ComboBox _uiFontScaleComboBox;
        private readonly ComboBox _messageDetailPreviewModeComboBox;
        private readonly ComboBox _sessionListDisplayOrderComboBox;
        private readonly ComboBox _sendMessageShortcutModeComboBox;
        private readonly ComboBox _activeTurnFollowUpModeComboBox;
        private readonly TextBox _mainWindowHotKeyTextBox;
        private readonly ComboBox _textFileOpenModeComboBox;
        private readonly TextBox _textFileExternalEditorPathTextBox;
        private readonly TextBox _textFileExternalEditorArgumentsTextBox;
        private readonly CheckBox _closeSessionWorkbenchOnEscapeCheckBox;
        private readonly ComboBox _startupModeComboBox;
        private readonly Label _officeRuntimeDisplayNameLabel;
        private readonly TextBox _officeRootPathTextBox;
        private readonly Button _officeRootBrowseButton;
        private readonly Button _officeRootRestoreDefaultButton;
        private readonly Label _officeRootStatusLabel;
        private readonly CheckBox _officeShowActivityPaneCheckBox;
        private readonly TextBox _officeCliStatusTextBox;
        private readonly Button _officeCliRefreshButton;
        private readonly Button _officeCliInstallButton;
        private readonly Button _officeCliUninstallButton;
        private readonly ListView _customPermissionsListView;
        private readonly ListView _customCommandPrefixesListView;
        private readonly Button _addCustomCommandPrefixButton;
        private readonly Button _editCustomCommandPrefixButton;
        private readonly Button _deleteCustomCommandPrefixButton;
        private readonly OfficeCliInstallationService _officeCliInstallationService;
        private readonly OfficeCliIntegrationPaths _officeCliPaths;
        private readonly OfficeDirectoryOptionsContext _officeDirectoryOptionsContext;
        private bool _officeRootRepresentsDefault;
        private bool _isUpdatingOfficeRootPath;

        public OptionsForm(AppConfigSnapshot appConfig, AppServerConfigSnapshot officialConfigSnapshot)
            : this(appConfig, officialConfigSnapshot, null, new OfficeCliInstallationService())
        {
        }

        public OptionsForm(
            AppConfigSnapshot appConfig,
            AppServerConfigSnapshot officialConfigSnapshot,
            AppServerPermissionProfileListResult permissionProfiles)
            : this(appConfig, officialConfigSnapshot, permissionProfiles, new OfficeCliInstallationService())
        {
        }

        internal OptionsForm(
            AppConfigSnapshot appConfig,
            AppServerConfigSnapshot officialConfigSnapshot,
            OfficeCliInstallationService officeCliInstallationService)
            : this(appConfig, officialConfigSnapshot, null, officeCliInstallationService)
        {
        }

        internal OptionsForm(
            AppConfigSnapshot appConfig,
            AppServerConfigSnapshot officialConfigSnapshot,
            AppServerPermissionProfileListResult permissionProfiles,
            OfficeCliInstallationService officeCliInstallationService)
            : this(appConfig, officialConfigSnapshot, permissionProfiles, null, officeCliInstallationService)
        {
        }

        internal OptionsForm(
            AppConfigSnapshot appConfig,
            AppServerConfigSnapshot officialConfigSnapshot,
            AppServerPermissionProfileListResult permissionProfiles,
            IEnumerable<AppServerModelOption> availableModels)
            : this(appConfig, officialConfigSnapshot, permissionProfiles, availableModels, new OfficeCliInstallationService())
        {
        }

        internal OptionsForm(
            AppConfigSnapshot appConfig,
            AppServerConfigSnapshot officialConfigSnapshot,
            AppServerPermissionProfileListResult permissionProfiles,
            IEnumerable<AppServerModelOption> availableModels,
            OfficeCliInstallationService officeCliInstallationService)
            : this(
                appConfig,
                officialConfigSnapshot,
                permissionProfiles,
                availableModels,
                null,
                new ModeSettingsSnapshot(),
                officeCliInstallationService)
        {
        }

        internal OptionsForm(
            AppConfigSnapshot appConfig,
            AppServerConfigSnapshot officialConfigSnapshot,
            AppServerPermissionProfileListResult permissionProfiles,
            IEnumerable<AppServerModelOption> availableModels,
            OfficeDirectoryOptionsContext officeDirectoryOptionsContext)
            : this(
                appConfig,
                officialConfigSnapshot,
                permissionProfiles,
                availableModels,
                officeDirectoryOptionsContext,
                new ModeSettingsSnapshot(),
                new OfficeCliInstallationService())
        {
        }

        internal OptionsForm(
            AppConfigSnapshot appConfig,
            AppServerConfigSnapshot officialConfigSnapshot,
            AppServerPermissionProfileListResult permissionProfiles,
            IEnumerable<AppServerModelOption> availableModels,
            OfficeDirectoryOptionsContext officeDirectoryOptionsContext,
            ModeSettingsSnapshot modeSettings)
            : this(
                appConfig,
                officialConfigSnapshot,
                permissionProfiles,
                availableModels,
                officeDirectoryOptionsContext,
                modeSettings,
                new OfficeCliInstallationService())
        {
        }

        private OptionsForm(
            AppConfigSnapshot appConfig,
            AppServerConfigSnapshot officialConfigSnapshot,
            AppServerPermissionProfileListResult permissionProfiles,
            IEnumerable<AppServerModelOption> availableModels,
            OfficeDirectoryOptionsContext officeDirectoryOptionsContext,
            ModeSettingsSnapshot modeSettings,
            OfficeCliInstallationService officeCliInstallationService)
        {
            appConfig = appConfig ?? new AppConfigSnapshot();
            modeSettings = modeSettings ?? new ModeSettingsSnapshot();
            _officeCliInstallationService = officeCliInstallationService ?? new OfficeCliInstallationService();
            _officeCliPaths = OfficeCliWinFormsPathResolver.BuildPaths();
            _officeDirectoryOptionsContext = officeDirectoryOptionsContext ??
                CreateLocalOfficeDirectoryOptionsContext(modeSettings);

            Text = UiLocalization.Text("OptionsForm.Title", "Options");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(820, 560);
            Size = new Size(940, 680);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                TabIndex = 0
            };

            _uiFontScaleComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("OptionsForm.Accessible.UiFontScale", "Interface font size"), 11, DockStyle.Top);
            _uiFontScaleComboBox.Items.AddRange(new object[] { "100%", "125%", "150%" });
            _uiFontScaleComboBox.SelectedIndex = appConfig.UiFontScalePercent == 150 ? 2 : appConfig.UiFontScalePercent == 125 ? 1 : 0;
            UiFontScalePercent = appConfig.UiFontScalePercent;
            _themeComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("OptionsForm.Accessible.Theme", "Theme"), 0, DockStyle.Top);
            _languageComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("OptionsForm.Accessible.Language", "Language"), 1, DockStyle.Top);
            _messageDetailPreviewModeComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("OptionsForm.Accessible.MessageDetailPreviewMode", "Message detail preview mode"), 2, DockStyle.Top);
            _sessionListDisplayOrderComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("OptionsForm.Accessible.SessionListDisplayOrder", "Session information order"), 10, DockStyle.Top);
            _sendMessageShortcutModeComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("OptionsForm.Accessible.SendMessageShortcutMode", "Send message shortcut"), 3, DockStyle.Top);
            _activeTurnFollowUpModeComboBox = UiControlFactory.CreateDropDownList(
                UiLocalization.Text("OptionsForm.Accessible.ActiveTurnFollowUpMode", "Follow-up messages while working"),
                4,
                DockStyle.Top);
            _mainWindowHotKeyTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.MainWindowHotKey", "Main window global shortcut"),
                Dock = DockStyle.Fill,
                ReadOnly = true,
                ShortcutsEnabled = false,
                TabIndex = 5
            };
            UiTheme.ApplyTextBox(_mainWindowHotKeyTextBox, false, false);
            InitializeTargetPreferenceControls(appConfig);
            _textFileOpenModeComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("OptionsForm.Accessible.TextFileOpenMode", "Text file opener"), 6, DockStyle.Top);
            _textFileExternalEditorPathTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.TextFileExternalEditorPath", "External editor path"),
                Dock = DockStyle.Fill,
                TabIndex = 7
            };
            UiTheme.ApplyTextBox(_textFileExternalEditorPathTextBox, false, false);
            _textFileExternalEditorArgumentsTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.TextFileExternalEditorArguments", "External editor arguments"),
                Dock = DockStyle.Fill,
                TabIndex = 8
            };
            UiTheme.ApplyTextBox(_textFileExternalEditorArgumentsTextBox, false, false);
            _closeSessionWorkbenchOnEscapeCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("OptionsForm.Check.CloseSessionWorkbenchOnEscape", "Allow Esc to close the main and independent windows"),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.CloseSessionWorkbenchOnEscape", "Allow Esc to close the main and independent windows"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                TabIndex = 9
            };
            UiTheme.ApplyCheckBox(_closeSessionWorkbenchOnEscapeCheckBox);
            _startupModeComboBox = UiControlFactory.CreateDropDownList(UiLocalization.Text("OptionsForm.Accessible.DefaultStartupMode", "Default startup mode"), 0, DockStyle.Top);
            _officeRuntimeDisplayNameLabel = new Label
            {
                Text = string.IsNullOrWhiteSpace(_officeDirectoryOptionsContext.RuntimeDisplayName)
                    ? UiLocalization.Text("MainForm.Runtime.Windows", "Windows")
                    : _officeDirectoryOptionsContext.RuntimeDisplayName.Trim(),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.OfficeRuntimeValue", "Current runtime"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(_officeRuntimeDisplayNameLabel, false);
            _officeRootPathTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.OfficeRootPath", "Office directory"),
                Dock = DockStyle.Fill,
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(_officeRootPathTextBox, false, false);
            _officeRootBrowseButton = new Button
            {
                Text = UiLocalization.Text("Common.Browse", "&Browse..."),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.BrowseOfficeRootPath", "Browse office directory"),
                AutoSize = true,
                TabIndex = 2
            };
            UiTheme.ApplyDialogButton(_officeRootBrowseButton, UiGlyph.Workspace);
            _officeRootBrowseButton.Click += OfficeRootBrowseButton_Click;
            _officeRootRestoreDefaultButton = new Button
            {
                Text = UiLocalization.Text("OptionsForm.Button.RestoreDefaultOfficeRootPath", "Restore Default(&D)"),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.RestoreDefaultOfficeRootPath", "Restore default office directory"),
                AutoSize = true,
                TabIndex = 3
            };
            UiTheme.ApplyDialogButton(_officeRootRestoreDefaultButton, UiGlyph.Refresh);
            _officeRootRestoreDefaultButton.Click += OfficeRootRestoreDefaultButton_Click;
            _officeRootStatusLabel = new Label
            {
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.OfficeRootPathStatus", "Office directory status"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(_officeRootStatusLabel, false);
            _officeShowActivityPaneCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("OptionsForm.Label.OfficeShowActivityPane", "Show activity area"),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.OfficeShowActivityPane", "Show activity area"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                TabIndex = 4
            };
            UiTheme.ApplyCheckBox(_officeShowActivityPaneCheckBox);
            _officeCliStatusTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.OfficeCliStatus", "OfficeCLI status"),
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabIndex = 5,
                MinimumSize = new Size(0, 96)
            };
            UiTheme.ApplyTextBox(_officeCliStatusTextBox, false, false);
            _officeCliRefreshButton = new Button
            {
                Text = UiLocalization.Text("OptionsForm.Button.OfficeCliRefresh", "Refresh(&R)"),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.OfficeCliRefresh", "Refresh OfficeCLI status"),
                AutoSize = true,
                TabIndex = 6
            };
            UiTheme.ApplyDialogButton(_officeCliRefreshButton, UiGlyph.Refresh);
            _officeCliRefreshButton.Click += OfficeCliRefreshButton_Click;
            _officeCliInstallButton = new Button
            {
                Text = UiLocalization.Text("OptionsForm.Button.OfficeCliInstall", "Install / Update(&I)"),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.OfficeCliInstall", "Install or update OfficeCLI"),
                AutoSize = true,
                TabIndex = 7
            };
            UiTheme.ApplyDialogButton(_officeCliInstallButton, UiGlyph.Download);
            _officeCliInstallButton.Click += OfficeCliInstallButton_Click;
            _officeCliUninstallButton = new Button
            {
                Text = UiLocalization.Text("OptionsForm.Button.OfficeCliUninstall", "Uninstall(&U)"),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.OfficeCliUninstall", "Uninstall OfficeCLI"),
                AutoSize = true,
                TabIndex = 8
            };
            UiTheme.ApplyDialogButton(_officeCliUninstallButton, UiGlyph.Cancel);
            _officeCliUninstallButton.Click += OfficeCliUninstallButton_Click;
            _customPermissionsListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.CustomPermissions", "Custom permissions"),
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                UseCompatibleStateImageBehavior = false,
                LabelWrap = false,
                TabIndex = 0
            };
            UiTheme.ApplyListView(_customPermissionsListView);
            _customPermissionsListView.Columns.Add(
                UiLocalization.Text("OptionsForm.Label.CustomPermissions", "Custom permissions"),
                420,
                HorizontalAlignment.Left);
            _customCommandPrefixesListView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = false,
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.CustomCommandPrefixes", "Custom command prefixes"),
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                UseCompatibleStateImageBehavior = false,
                LabelWrap = false,
                TabIndex = 1
            };
            UiTheme.ApplyListView(_customCommandPrefixesListView);
            _customCommandPrefixesListView.Columns.Add(
                UiLocalization.Text("OptionsForm.Label.CustomCommandPrefixes", "Custom command prefixes"),
                520,
                HorizontalAlignment.Left);
            _customCommandPrefixesListView.KeyDown += CustomCommandPrefixesListView_KeyDown;
            _customCommandPrefixesListView.DoubleClick += CustomCommandPrefixesListView_DoubleClick;
            _addCustomCommandPrefixButton = CreateCommandPrefixButton(
                UiLocalization.Text("OptionsForm.Button.AddCustomCommandPrefix", "Add(&A)..."),
                UiLocalization.Text("OptionsForm.Accessible.AddCustomCommandPrefix", "Add custom command prefix"),
                UiGlyph.NewSession,
                2,
                AddCustomCommandPrefixButton_Click);
            _editCustomCommandPrefixButton = CreateCommandPrefixButton(
                UiLocalization.Text("OptionsForm.Button.EditCustomCommandPrefix", "Edit(&E)..."),
                UiLocalization.Text("OptionsForm.Accessible.EditCustomCommandPrefix", "Edit custom command prefix"),
                UiGlyph.FileChange,
                3,
                EditCustomCommandPrefixButton_Click);
            _deleteCustomCommandPrefixButton = CreateCommandPrefixButton(
                UiLocalization.Text("OptionsForm.Button.DeleteCustomCommandPrefix", "Delete(&D)"),
                UiLocalization.Text("OptionsForm.Accessible.DeleteCustomCommandPrefix", "Delete custom command prefix"),
                UiGlyph.Cancel,
                4,
                DeleteCustomCommandPrefixButton_Click);
            PopulateThemeChoices(ThemePreference.Normalize(appConfig.UiThemePreference));
            PopulateLanguageChoices(UiLocalization.NormalizePreference(appConfig.UiLanguagePreference));
            PopulateMessageDetailPreviewModeChoices(MessagePreviewMode.Normalize(appConfig.MessageDetailPreviewMode));
            PopulateSessionListDisplayOrderChoices(SessionDisplayOrder.Normalize(appConfig.SessionListDisplayOrder));
            PopulateSendMessageShortcutModeChoices(SendShortcutMode.Normalize(appConfig.SendMessageShortcutMode));
            PopulateActiveTurnFollowUpModeChoices(FollowUpMode.Normalize(appConfig.ActiveTurnFollowUpMode));
            MainWindowHotKeyModifiers = MainWindowHotKeyConfig.NormalizeModifiers(appConfig.MainWindowHotKeyModifiers);
            MainWindowHotKeyVirtualKey = MainWindowHotKeyConfig.NormalizeVirtualKey(appConfig.MainWindowHotKeyVirtualKey);
            RefreshMainWindowHotKeyText();
            PopulateTextFileOpenModeChoices(TextFileMode.Normalize(appConfig.TextFileOpenMode));
            PopulateStartupModeChoices(modeSettings.DefaultStartupMode);
            PopulateCustomPermissionChoices(appConfig.CustomApprovalSettings);
            PopulateCustomCommandPrefixes(appConfig.CustomApprovalSettings);
            _textFileExternalEditorPathTextBox.Text = appConfig.TextFileExternalEditorPath ?? string.Empty;
            _textFileExternalEditorArgumentsTextBox.Text = appConfig.TextFileExternalEditorArguments ?? string.Empty;
            _closeSessionWorkbenchOnEscapeCheckBox.Checked = appConfig.CloseSessionWorkbenchOnEscape;
            InitializeOfficeRootPath();
            ApplyModeSettingsAvailability();
            _officeRootPathTextBox.TextChanged += OfficeRootPathTextBox_TextChanged;
            _officeShowActivityPaneCheckBox.Checked = appConfig.OfficeShowActivityPane;
            RefreshOfficeCliStatus();

            AddPage("appearance", UiLocalization.Text("OptionsForm.Category.Appearance", "Appearance"), CreateAppearancePage());
            AddPage("permissions", UiLocalization.Text("OptionsForm.Category.Permissions", "Permissions"), CreatePermissionsPage());
            AddPage("office", UiLocalization.Text("OptionsForm.Category.Office", "Office"), CreateOfficePage());
            _notificationSettingsForm = new NotificationSettingsForm(
                appConfig.TurnCompletedNotificationMode,
                appConfig.SubAgentTurnCompletedNotificationMode,
                appConfig.ApprovalRequestNotificationMode,
                appConfig.SendMessageSoundEnabled,
                false);
            AddPage("notification", UiLocalization.Text("OptionsForm.Category.Notification", "Sound"), PrepareEmbeddedForm(_notificationSettingsForm));
            _screenReaderSettingsForm = new ScreenReaderSettingsForm(
                appConfig.ScreenReaderProvider,
                appConfig.ManualBrowseUseReaderMainChannel,
                appConfig.ScreenReaderIndependentChannelName,
                false);
            AddPage("screenReader", UiLocalization.Text("OptionsForm.Category.ScreenReader", "Screen Reader"), CreateTargetScreenReaderPage());
            AddOfficialCodexSettingsPage(
                officialConfigSnapshot,
                availableModels,
                permissionProfiles == null ? null : permissionProfiles.Profiles);

            _okButton = new Button
            {
                Text = UiLocalization.Text("Common.OK", "&OK"),
                AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = 2
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 3,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            BuildLayout();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        public string UiThemePreference { get; private set; }

        public string UiLanguagePreference { get; private set; }

        public int UiFontScalePercent { get; private set; }

        public string MessageDetailPreviewMode { get; private set; }

        public string SessionListDisplayOrder { get; private set; }

        public string SendMessageShortcutMode { get; private set; }

        public string ActiveTurnFollowUpMode { get; private set; }

        public string TextFileOpenMode { get; private set; }

        public string TextFileExternalEditorPath { get; private set; }

        public string TextFileExternalEditorArguments { get; private set; }

        public AppInteractionMode DefaultStartupMode { get; private set; }

        public string OfficeRootPath { get; private set; }

        public bool OfficeShowActivityPane { get; private set; }

        public bool OfficeCliEnvironmentChanged { get; private set; }

        public CustomApprovalSettingsSnapshot CustomApprovalSettings { get; private set; }

        public NotificationPromptMode TurnCompletedNotificationMode { get; private set; }

        public NotificationPromptMode SubAgentTurnCompletedNotificationMode { get; private set; }

        public NotificationPromptMode ApprovalRequestNotificationMode { get; private set; }

        public bool SendMessageSoundEnabled { get; private set; }

        public bool CloseSessionWorkbenchOnEscape { get; private set; }

        public int MainWindowHotKeyModifiers { get; private set; }

        public int MainWindowHotKeyVirtualKey { get; private set; }

        public bool MainWindowHotKeyEnabled { get; private set; }

        public bool EnableAutomaticChatReading { get; private set; }

        internal Func<bool> ValidateSelection { get; set; }

        public ScreenReaderProviderKind ScreenReaderProvider { get; private set; }

        public bool ManualBrowseUseReaderMainChannel { get; private set; }

        public string ScreenReaderIndependentChannelName { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_tabControl.TabPages.Count > 0)
            {
                _tabControl.SelectedIndex = 0;
            }

            _tabControl.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_mainWindowHotKeyTextBox != null && _mainWindowHotKeyTextBox.ContainsFocus)
            {
                if (keyData == Keys.Tab ||
                    keyData == (Keys.Shift | Keys.Tab) ||
                    keyData == Keys.Escape)
                {
                    return base.ProcessCmdKey(ref msg, keyData);
                }

                int modifiers;
                int virtualKey;
                if (MainWindowHotKeyShortcut.TryCreate(
                    keyData,
                    MainWindowHotKeyShortcut.IsWindowsKeyPressed(),
                    out modifiers,
                    out virtualKey))
                {
                    MainWindowHotKeyModifiers = modifiers;
                    MainWindowHotKeyVirtualKey = virtualKey;
                    RefreshMainWindowHotKeyText();
                }

                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
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

            var buttonPanel = UiControlFactory.CreateDialogButtonBar();
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);

            root.Controls.Add(_targetOptionsLabel, 0, 0);
            root.Controls.Add(_tabControl, 0, 1);
            root.Controls.Add(buttonPanel, 0, 2);
            Controls.Add(root);
        }

        private void AddPage(string name, string text, Control content)
        {
            var page = new TabPage(text)
            {
                Name = name,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0)
            };
            content.Dock = DockStyle.Fill;
            page.Controls.Add(content);
            _tabControl.TabPages.Add(page);
        }

        private Panel CreateAppearancePage()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 13,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
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
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            root.RowStyles.Insert(11, new RowStyle(SizeType.AutoSize));
            AddAppearanceRow(root, 11,
                UiLocalization.Text("OptionsForm.Label.UiFontScale", "Font size (restart required)(&Z)"),
                UiLocalization.Text("OptionsForm.Accessible.UiFontScale", "Interface font size"),
                _uiFontScaleComboBox);

            AddAppearanceRow(
                root,
                0,
                UiLocalization.Text("OptionsForm.Label.Theme", "Theme"),
                UiLocalization.Text("OptionsForm.Accessible.Theme", "Theme"),
                _themeComboBox);
            AddAppearanceRow(
                root,
                1,
                UiLocalization.Text("OptionsForm.Label.Language", "Language"),
                UiLocalization.Text("OptionsForm.Accessible.Language", "Language"),
                _languageComboBox);
            AddAppearanceRow(
                root,
                2,
                UiLocalization.Text("OptionsForm.Label.MessageDetailPreviewMode", "Message detail preview mode"),
                UiLocalization.Text("OptionsForm.Accessible.MessageDetailPreviewMode", "Message detail preview mode"),
                _messageDetailPreviewModeComboBox);
            AddAppearanceRow(
                root,
                3,
                UiLocalization.Text("OptionsForm.Label.SendMessageShortcutMode", "Send message shortcut"),
                UiLocalization.Text("OptionsForm.Accessible.SendMessageShortcutMode", "Send message shortcut"),
                _sendMessageShortcutModeComboBox);
            AddAppearanceRow(
                root,
                4,
                UiLocalization.Text("OptionsForm.Label.ActiveTurnFollowUpMode", "Follow-up messages while working"),
                UiLocalization.Text("OptionsForm.Accessible.ActiveTurnFollowUpMode", "Follow-up messages while working"),
                _activeTurnFollowUpModeComboBox);
            AddAppearanceRow(
                root,
                5,
                UiLocalization.Text("OptionsForm.Label.MainWindowHotKey", "Main window global shortcut"),
                UiLocalization.Text("OptionsForm.Accessible.MainWindowHotKey", "Main window global shortcut"),
                CreateTargetHotKeyPanel());
            AddAppearanceRow(
                root,
                6,
                UiLocalization.Text("OptionsForm.Label.TextFileOpenMode", "Text file opener"),
                UiLocalization.Text("OptionsForm.Accessible.TextFileOpenMode", "Text file opener"),
                _textFileOpenModeComboBox);
            AddAppearanceRow(
                root,
                7,
                UiLocalization.Text("OptionsForm.Label.TextFileExternalEditorPath", "External editor path"),
                UiLocalization.Text("OptionsForm.Accessible.TextFileExternalEditorPath", "External editor path"),
                _textFileExternalEditorPathTextBox);
            AddAppearanceRow(
                root,
                8,
                UiLocalization.Text("OptionsForm.Label.TextFileExternalEditorArguments", "External editor arguments"),
                UiLocalization.Text("OptionsForm.Accessible.TextFileExternalEditorArguments", "External editor arguments"),
                _textFileExternalEditorArgumentsTextBox);
            root.Controls.Add(new Label { TabStop = false, AutoSize = false }, 0, 9);
            root.Controls.Add(_closeSessionWorkbenchOnEscapeCheckBox, 1, 9);
            AddAppearanceRow(
                root,
                10,
                UiLocalization.Text("OptionsForm.Label.SessionListDisplayOrder", "Session information order"),
                UiLocalization.Text("OptionsForm.Accessible.SessionListDisplayOrder", "Session information order"),
                _sessionListDisplayOrderComboBox);

            return new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.WindowBackColor,
                Controls = { root },
                TabStop = false
            };
        }

        private Panel CreatePermissionsPage()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));

            var description = new Label
            {
                Text = UiLocalization.Text(
                    "OptionsForm.Description.CustomPermissions",
                    "Custom mode starts from Read Only. Checked categories are allowed automatically before the approval dialog is shown."),
                AutoSize = true,
                Dock = DockStyle.Top,
                TabStop = false
            };
            UiTheme.ApplyLabel(description, false);

            var label = new Label
            {
                Text = UiLocalization.Text("OptionsForm.Label.CustomPermissions", "Custom permissions"),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.CustomPermissions", "Custom permissions"),
                AutoSize = true,
                Dock = DockStyle.Top,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);

            var prefixLabel = new Label
            {
                Text = UiLocalization.Text("OptionsForm.Label.CustomCommandPrefixes", "Custom command prefixes"),
                AccessibleName = UiLocalization.Text("OptionsForm.Accessible.CustomCommandPrefixes", "Custom command prefixes"),
                AutoSize = true,
                Dock = DockStyle.Top,
                TabStop = false
            };
            UiTheme.ApplyLabel(prefixLabel, true);

            root.Controls.Add(description, 0, 0);
            root.Controls.Add(label, 0, 1);
            root.Controls.Add(_customPermissionsListView, 0, 2);
            root.Controls.Add(prefixLabel, 0, 3);
            root.Controls.Add(CreateCustomCommandPrefixesPanel(), 0, 4);

            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Controls = { root },
                TabStop = false
            };
        }

        private Control CreateCustomCommandPrefixesPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Controls.Add(_customCommandPrefixesListView, 0, 0);
            panel.Controls.Add(CreateCustomCommandPrefixButtonPanel(), 1, 0);
            return panel;
        }

        private Control CreateCustomCommandPrefixButtonPanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                TabStop = false
            };
            panel.Controls.Add(_addCustomCommandPrefixButton);
            panel.Controls.Add(_editCustomCommandPrefixButton);
            panel.Controls.Add(_deleteCustomCommandPrefixButton);
            return panel;
        }

        private Panel CreateOfficePage()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            AddAppearanceRow(
                root,
                0,
                UiLocalization.Text("OptionsForm.Label.OfficeRuntime", "Current runtime"),
                UiLocalization.Text("OptionsForm.Accessible.OfficeRuntimeValue", "Current runtime"),
                _officeRuntimeDisplayNameLabel);
            AddAppearanceRow(
                root,
                1,
                UiLocalization.Text("OptionsForm.Label.DefaultStartupMode", "Default startup mode"),
                UiLocalization.Text("OptionsForm.Accessible.DefaultStartupMode", "Default startup mode"),
                _startupModeComboBox);
            AddAppearanceRow(
                root,
                2,
                UiLocalization.Text("OptionsForm.Label.OfficeRootPath", "Office directory"),
                UiLocalization.Text("OptionsForm.Accessible.OfficeRootPath", "Office directory"),
                CreateOfficeRootPathPanel());
            root.Controls.Add(new Label { TabStop = false, AutoSize = false }, 0, 3);
            root.Controls.Add(_officeRootStatusLabel, 1, 3);
            root.Controls.Add(new Label { TabStop = false, AutoSize = false }, 0, 4);
            root.Controls.Add(_officeShowActivityPaneCheckBox, 1, 4);
            AddAppearanceRow(
                root,
                5,
                UiLocalization.Text("OptionsForm.Label.OfficeCliStatus", "OfficeCLI"),
                UiLocalization.Text("OptionsForm.Accessible.OfficeCliStatus", "OfficeCLI status"),
                _officeCliStatusTextBox);
            root.Controls.Add(new Label { TabStop = false, AutoSize = false }, 0, 6);
            root.Controls.Add(CreateOfficeCliButtonPanel(), 1, 6);

            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Controls = { root },
                TabStop = false
            };
        }

        private Control CreateOfficeRootPathPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _officeRootPathTextBox.Margin = new Padding(0, 3, 8, 3);
            _officeRootBrowseButton.Margin = new Padding(0, 0, 8, 0);
            _officeRootRestoreDefaultButton.Margin = new Padding(0);
            panel.Controls.Add(_officeRootPathTextBox, 0, 0);
            panel.Controls.Add(_officeRootBrowseButton, 1, 0);
            panel.Controls.Add(_officeRootRestoreDefaultButton, 2, 0);
            return panel;
        }

        private Control CreateOfficeCliButtonPanel()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                TabStop = false
            };
            _officeCliRefreshButton.Margin = new Padding(0, 3, 8, 3);
            _officeCliInstallButton.Margin = new Padding(0, 3, 8, 3);
            _officeCliUninstallButton.Margin = new Padding(0, 3, 8, 3);
            panel.Controls.Add(_officeCliRefreshButton);
            panel.Controls.Add(_officeCliInstallButton);
            panel.Controls.Add(_officeCliUninstallButton);
            return panel;
        }

        private static void AddAppearanceRow(TableLayoutPanel root, int row, string text, string accessibleName, Control control)
        {
            var label = new Label
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 6, 8, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            root.Controls.Add(label, 0, row);
            root.Controls.Add(control, 1, row);
        }

        private static Control PrepareEmbeddedForm(Form form)
        {
            if (form.Controls.Count == 0)
            {
                return new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = UiTheme.WindowBackColor,
                    TabStop = false
                };
            }

            var content = form.Controls[0];
            form.Controls.Remove(content);
            content.Dock = DockStyle.Fill;
            RemoveDescriptionControls(content);
            return content;
        }

        private static void RemoveDescriptionControls(Control root)
        {
            if (root == null)
            {
                return;
            }

            RemoveDescriptionControlsRecursive(root);
        }

        private static void RemoveDescriptionControlsRecursive(Control control)
        {
            for (var i = 0; i < control.Controls.Count; i++)
            {
                RemoveDescriptionControlsRecursive(control.Controls[i]);
            }

            if (!IsDescriptionControl(control))
            {
                return;
            }

            var table = control.Parent as TableLayoutPanel;
            if (table != null)
            {
                var row = table.GetRow(control);
                if (row >= 0 && row < table.RowStyles.Count)
                {
                    table.RowStyles[row].SizeType = SizeType.Absolute;
                    table.RowStyles[row].Height = 0F;
                }
            }

            control.TabStop = false;
            control.Visible = false;
        }

        private static bool IsDescriptionControl(Control control)
        {
            if (control == null)
            {
                return false;
            }

            var accessibleName = control.AccessibleName ?? string.Empty;
            if (!accessibleName.Equals("Description", StringComparison.OrdinalIgnoreCase) &&
                !accessibleName.Equals("说明", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return control is Label || control is TextBox;
        }

        private static Panel CreateUnavailablePage(string title, string body)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var titleLabel = new Label
            {
                Text = title,
                AccessibleName = title,
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                TabStop = false
            };
            UiTheme.ApplyLabel(titleLabel, true);

            var bodyLabel = new Label
            {
                Text = body,
                AccessibleName = title,
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.TopLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(bodyLabel, false);

            root.Controls.Add(titleLabel, 0, 0);
            root.Controls.Add(bodyLabel, 0, 1);
            return new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Controls = { root },
                TabStop = false
            };
        }

        private static Button CreateCommandPrefixButton(
            string text,
            string accessibleName,
            UiGlyph glyph,
            int tabIndex,
            EventHandler clickHandler)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                TabIndex = tabIndex
            };
            UiTheme.ApplyDialogButton(button, glyph);
            button.Click += clickHandler;
            return button;
        }

        private void PopulateThemeChoices(string selectedPreference)
        {
            _themeComboBox.Items.Add(new ChoiceItem(ThemePreference.Auto, UiLocalization.Text("OptionsForm.Theme.FollowSystem", "Follow System")));
            _themeComboBox.Items.Add(new ChoiceItem(ThemePreference.Light, UiLocalization.Text("OptionsForm.Theme.Light", "Light")));
            _themeComboBox.Items.Add(new ChoiceItem(ThemePreference.Dark, UiLocalization.Text("OptionsForm.Theme.Dark", "Dark")));
            SelectChoice(_themeComboBox, selectedPreference);
        }

        private void PopulateLanguageChoices(string selectedPreference)
        {
            _languageComboBox.Items.Add(new ChoiceItem(LanguagePreference.Auto, UiLocalization.Text("OptionsForm.Language.FollowSystem", "Follow System")));
            _languageComboBox.Items.Add(new ChoiceItem(LanguagePreference.ZhCn, "简体中文"));
            _languageComboBox.Items.Add(new ChoiceItem(LanguagePreference.EnUs, "English"));
            SelectChoice(_languageComboBox, selectedPreference);
        }

        private void PopulateStartupModeChoices(AppInteractionMode selectedMode)
        {
            _startupModeComboBox.Items.Add(new ChoiceItem(AppInteractionMode.Coding.ToString(), UiLocalization.Text("OptionsForm.StartupMode.Coding", "Coding Mode")));
            _startupModeComboBox.Items.Add(new ChoiceItem(AppInteractionMode.Office.ToString(), UiLocalization.Text("OptionsForm.StartupMode.Office", "Office Mode")));
            SelectChoice(_startupModeComboBox, selectedMode.ToString());
        }

        private void PopulateCustomPermissionChoices(CustomApprovalSettingsSnapshot settings)
        {
            _customPermissionsListView.BeginUpdate();
            _customPermissionsListView.Items.Clear();
            var enabled = new HashSet<CustomApprovalPermissionKind>(
                settings == null ? new CustomApprovalPermissionKind[0] : settings.EnabledPermissions);
            var permissions = CustomApprovalPermissionCatalog.GetOrderedPermissions();
            for (var i = 0; i < permissions.Length; i++)
            {
                var permission = permissions[i];
                var item = new ListViewItem(GetCustomPermissionText(permission))
                {
                    Checked = enabled.Contains(permission),
                    Tag = permission
                };
                _customPermissionsListView.Items.Add(item);
            }

            _customPermissionsListView.EndUpdate();
        }

        private void PopulateCustomCommandPrefixes(CustomApprovalSettingsSnapshot settings)
        {
            _customCommandPrefixesListView.BeginUpdate();
            _customCommandPrefixesListView.Items.Clear();
            var prefixes = settings == null ? new string[0] : settings.CommandPrefixes.ToArray();
            for (var i = 0; i < prefixes.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(prefixes[i]))
                {
                    continue;
                }

                _customCommandPrefixesListView.Items.Add(new ListViewItem(prefixes[i].Trim()));
            }

            _customCommandPrefixesListView.EndUpdate();
        }

        private CustomApprovalSettingsSnapshot BuildCustomApprovalSettings()
        {
            var settings = new CustomApprovalSettingsSnapshot();
            foreach (ListViewItem item in _customPermissionsListView.Items)
            {
                if (!item.Checked || !(item.Tag is CustomApprovalPermissionKind))
                {
                    continue;
                }

                settings.EnabledPermissions.Add((CustomApprovalPermissionKind)item.Tag);
            }

            settings.CommandPrefixes = BuildCustomCommandPrefixes();
            return settings;
        }

        private List<string> BuildCustomCommandPrefixes()
        {
            var prefixes = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ListViewItem item in _customCommandPrefixesListView.Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Text))
                {
                    continue;
                }

                var prefix = item.Text.Trim();
                if (seen.Add(prefix))
                {
                    prefixes.Add(prefix);
                }
            }

            return prefixes;
        }

        private static string GetCustomPermissionText(CustomApprovalPermissionKind permission)
        {
            return CustomApprovalPermissionDisplayText.Get(permission);
        }

        private void AddCustomCommandPrefixButton_Click(object sender, EventArgs e)
        {
            string prefix;
            if (!CustomCommandPrefixEditForm.TryEdit(this, null, out prefix))
            {
                return;
            }

            AddOrReplaceCustomCommandPrefix(null, prefix);
        }

        private void EditCustomCommandPrefixButton_Click(object sender, EventArgs e)
        {
            EditSelectedCustomCommandPrefix();
        }

        private void DeleteCustomCommandPrefixButton_Click(object sender, EventArgs e)
        {
            DeleteSelectedCustomCommandPrefix();
        }

        private void CustomCommandPrefixesListView_DoubleClick(object sender, EventArgs e)
        {
            EditSelectedCustomCommandPrefix();
        }

        private void CustomCommandPrefixesListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedCustomCommandPrefix();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2 || e.KeyCode == Keys.Enter)
            {
                EditSelectedCustomCommandPrefix();
                e.Handled = true;
            }
        }

        private void EditSelectedCustomCommandPrefix()
        {
            if (_customCommandPrefixesListView.SelectedItems.Count == 0)
            {
                return;
            }

            var selected = _customCommandPrefixesListView.SelectedItems[0];
            string prefix;
            if (!CustomCommandPrefixEditForm.TryEdit(this, selected.Text, out prefix))
            {
                return;
            }

            AddOrReplaceCustomCommandPrefix(selected, prefix);
        }

        private void DeleteSelectedCustomCommandPrefix()
        {
            if (_customCommandPrefixesListView.SelectedItems.Count == 0)
            {
                return;
            }

            _customCommandPrefixesListView.Items.Remove(_customCommandPrefixesListView.SelectedItems[0]);
        }

        private void AddOrReplaceCustomCommandPrefix(ListViewItem existingItem, string prefix)
        {
            var normalized = NormalizeOptionalText(prefix);
            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            foreach (ListViewItem item in _customCommandPrefixesListView.Items)
            {
                if (!ReferenceEquals(item, existingItem) &&
                    string.Equals(item.Text, normalized, StringComparison.Ordinal))
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    return;
                }
            }

            if (existingItem == null)
            {
                _customCommandPrefixesListView.Items.Add(new ListViewItem(normalized));
            }
            else
            {
                existingItem.Text = normalized;
            }
        }

        private void PopulateMessageDetailPreviewModeChoices(string selectedMode)
        {
            _messageDetailPreviewModeComboBox.Items.Add(new ChoiceItem(MessagePreviewMode.Web, UiLocalization.Text("OptionsForm.MessageDetailPreviewMode.Web", "Web")));
            _messageDetailPreviewModeComboBox.Items.Add(new ChoiceItem(MessagePreviewMode.Text, UiLocalization.Text("OptionsForm.MessageDetailPreviewMode.Text", "Text")));
            SelectChoice(_messageDetailPreviewModeComboBox, selectedMode);
        }

        private void PopulateSessionListDisplayOrderChoices(string selectedOrder)
        {
            _sessionListDisplayOrderComboBox.Items.Add(new ChoiceItem(SessionDisplayOrder.AgentsTimeTitle, UiLocalization.Text("OptionsForm.SessionListDisplayOrder.AgentsTimeTitle", "Subagent count, time, title")));
            _sessionListDisplayOrderComboBox.Items.Add(new ChoiceItem(SessionDisplayOrder.TitleTimeAgents, UiLocalization.Text("OptionsForm.SessionListDisplayOrder.TitleTimeAgents", "Title, time, subagent count")));
            _sessionListDisplayOrderComboBox.Items.Add(new ChoiceItem(SessionDisplayOrder.TitleAgentsTime, UiLocalization.Text("OptionsForm.SessionListDisplayOrder.TitleAgentsTime", "Title, subagent count, time")));
            SelectChoice(_sessionListDisplayOrderComboBox, selectedOrder);
        }

        private void PopulateSendMessageShortcutModeChoices(string selectedMode)
        {
            _sendMessageShortcutModeComboBox.Items.Add(new ChoiceItem(
                SendShortcutMode.EnterNewLineCtrlEnterSend,
                UiLocalization.Text("OptionsForm.SendMessageShortcutMode.EnterNewLineCtrlEnterSend", "Enter inserts a new line, Ctrl+Enter sends")));
            _sendMessageShortcutModeComboBox.Items.Add(new ChoiceItem(
                SendShortcutMode.CtrlEnterNewLineEnterSend,
                UiLocalization.Text("OptionsForm.SendMessageShortcutMode.CtrlEnterNewLineEnterSend", "Ctrl+Enter inserts a new line, Enter sends")));
            SelectChoice(_sendMessageShortcutModeComboBox, selectedMode);
        }

        private void PopulateActiveTurnFollowUpModeChoices(string selectedMode)
        {
            _activeTurnFollowUpModeComboBox.Items.Add(new ChoiceItem(
                FollowUpMode.Steer,
                UiLocalization.Text("OptionsForm.ActiveTurnFollowUpMode.Steer", "Steer")));
            _activeTurnFollowUpModeComboBox.Items.Add(new ChoiceItem(
                FollowUpMode.Queue,
                UiLocalization.Text("OptionsForm.ActiveTurnFollowUpMode.Queue", "Queue")));
            SelectChoice(_activeTurnFollowUpModeComboBox, FollowUpMode.Normalize(selectedMode));
        }

        private void PopulateTextFileOpenModeChoices(string selectedMode)
        {
            _textFileOpenModeComboBox.Items.Clear();
            _textFileOpenModeComboBox.Items.Add(new ChoiceItem(TextFileMode.Internal, UiLocalization.Text("OptionsForm.TextFileOpenMode.Internal", "Built-in text editor")));
            _textFileOpenModeComboBox.Items.Add(new ChoiceItem(TextFileMode.System, UiLocalization.Text("OptionsForm.TextFileOpenMode.System", "System default app")));
            _textFileOpenModeComboBox.Items.Add(new ChoiceItem(TextFileMode.VsCode, UiLocalization.Text("OptionsForm.TextFileOpenMode.VsCode", "Visual Studio Code")));
            _textFileOpenModeComboBox.Items.Add(new ChoiceItem(TextFileMode.NotepadPlusPlus, UiLocalization.Text("OptionsForm.TextFileOpenMode.NotepadPlusPlus", "Notepad++")));
            _textFileOpenModeComboBox.Items.Add(new ChoiceItem(TextFileMode.Custom, UiLocalization.Text("OptionsForm.TextFileOpenMode.Custom", "Custom command")));
            SelectChoice(_textFileOpenModeComboBox, TextFileMode.Normalize(selectedMode));
        }

        private static void SelectChoice(ComboBox comboBox, string value)
        {
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                var item = comboBox.Items[i] as ChoiceItem;
                if (item != null && string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            comboBox.SelectedIndex = comboBox.Items.Count > 0 ? 0 : -1;
        }

        private static string GetSelectedChoice(ComboBox comboBox, string fallback)
        {
            var item = comboBox.SelectedItem as ChoiceItem;
            return item != null ? item.Value : fallback;
        }

        private void RefreshMainWindowHotKeyText()
        {
            _mainWindowHotKeyTextBox.Text = MainWindowHotKeyShortcut.Format(
                MainWindowHotKeyModifiers,
                MainWindowHotKeyVirtualKey);
            _mainWindowHotKeyTextBox.SelectAll();
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            string normalizedOfficeRootPath = null;
            if (_officeDirectoryOptionsContext.ModeSettingsAvailable &&
                !_officeRootRepresentsDefault &&
                !TryNormalizeOfficeRootPath(
                    _officeDirectoryOptionsContext.HostPlatform,
                    _officeRootPathTextBox.Text,
                    out normalizedOfficeRootPath))
            {
                var message = _officeDirectoryOptionsContext.HostPlatform == AppServerHostPlatform.Windows
                    ? UiLocalization.Text(
                        "OptionsForm.OfficeRootPath.Validation.WindowsAbsoluteRequired",
                        "Enter an absolute local Windows directory, such as C:\\Users\\name\\Documents.")
                    : UiLocalization.Text(
                        "OptionsForm.OfficeRootPath.Validation.UnixAbsoluteRequired",
                        "Enter an absolute Unix directory or a home expression such as ~/MyCodex-office.");
                MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _officeRootPathTextBox.Focus();
                _officeRootPathTextBox.SelectAll();
                return;
            }

            if (!_notificationSettingsForm.ApplySelection(this))
            {
                return;
            }

            ApplyOfficialCodexSettingsSelection();

            _screenReaderSettingsForm.ApplySelection();
            UiFontScalePercent = _uiFontScaleComboBox.SelectedIndex == 2 ? 150 : _uiFontScaleComboBox.SelectedIndex == 1 ? 125 : 100;
            UiThemePreference = ThemePreference.Normalize(GetSelectedChoice(_themeComboBox, ThemePreference.Auto));
            UiLanguagePreference = UiLocalization.NormalizePreference(GetSelectedChoice(_languageComboBox, LanguagePreference.Auto));
            MessageDetailPreviewMode = MessagePreviewMode.Normalize(GetSelectedChoice(_messageDetailPreviewModeComboBox, MessagePreviewMode.Web));
            SessionListDisplayOrder = SessionDisplayOrder.Normalize(GetSelectedChoice(_sessionListDisplayOrderComboBox, SessionDisplayOrder.AgentsTimeTitle));
            SendMessageShortcutMode = SendShortcutMode.Normalize(GetSelectedChoice(_sendMessageShortcutModeComboBox, SendShortcutMode.EnterNewLineCtrlEnterSend));
            ActiveTurnFollowUpMode = FollowUpMode.Normalize(GetSelectedChoice(_activeTurnFollowUpModeComboBox, FollowUpMode.Steer));
            TextFileOpenMode = TextFileMode.Normalize(GetSelectedChoice(_textFileOpenModeComboBox, TextFileMode.Internal));
            TextFileExternalEditorPath = NormalizeOptionalText(_textFileExternalEditorPathTextBox.Text);
            TextFileExternalEditorArguments = NormalizeOptionalText(_textFileExternalEditorArgumentsTextBox.Text);
            DefaultStartupMode = ParseStartupMode(GetSelectedChoice(_startupModeComboBox, AppInteractionMode.Coding.ToString()));
            OfficeRootPath = _officeRootRepresentsDefault ? null : normalizedOfficeRootPath;
            OfficeShowActivityPane = _officeShowActivityPaneCheckBox.Checked;
            TurnCompletedNotificationMode = _notificationSettingsForm.TurnCompletedMode;
            SubAgentTurnCompletedNotificationMode = _notificationSettingsForm.SubAgentTurnCompletedMode;
            ApprovalRequestNotificationMode = _notificationSettingsForm.ApprovalRequestMode;
            SendMessageSoundEnabled = _notificationSettingsForm.SendMessageSoundEnabled;
            CloseSessionWorkbenchOnEscape = _closeSessionWorkbenchOnEscapeCheckBox.Checked;
            MainWindowHotKeyEnabled = _mainWindowHotKeyEnabledCheckBox.Checked;
            EnableAutomaticChatReading = _automaticChatReadingCheckBox.Checked;
            ScreenReaderProvider = _screenReaderSettingsForm.SelectedProvider;
            ManualBrowseUseReaderMainChannel = _screenReaderSettingsForm.ManualBrowseUseReaderMainChannel;
            ScreenReaderIndependentChannelName = _screenReaderSettingsForm.ScreenReaderIndependentChannelName;
            CustomApprovalSettings = BuildCustomApprovalSettings();
            if (ValidateSelection != null && !ValidateSelection())
            {
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void OfficeCliRefreshButton_Click(object sender, EventArgs e)
        {
            RefreshOfficeCliStatus();
        }

        private async void OfficeCliInstallButton_Click(object sender, EventArgs e)
        {
            SetOfficeCliBusy(true, UiLocalization.Text("OptionsForm.OfficeCli.Status.Installing", "Installing OfficeCLI..."));
            try
            {
                var result = await _officeCliInstallationService.InstallLatestAsync(_officeCliPaths, default(System.Threading.CancellationToken));
                if (!CanUpdateOfficeCliUi())
                {
                    return;
                }

                if (result.Success)
                {
                    OfficeCliEnvironmentChanged = true;
                }

                RefreshOfficeCliStatus(result.Status);
                ShowOfficeCliMessage(
                    result.Message,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (!CanUpdateOfficeCliUi())
                {
                    return;
                }

                RefreshOfficeCliStatus();
                ShowOfficeCliMessage(
                    ex.Message,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (CanUpdateOfficeCliUi())
                {
                    SetOfficeCliBusy(false, null);
                }
            }
        }

        private async void OfficeCliUninstallButton_Click(object sender, EventArgs e)
        {
            var confirm = MessageBox.Show(
                this,
                UiLocalization.Text("OptionsForm.OfficeCli.UninstallConfirm", "Uninstall OfficeCLI and the OfficeCLI Codex skill?"),
                UiLocalization.Text("OptionsForm.OfficeCli.Message.Title", "OfficeCLI"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                return;
            }

            SetOfficeCliBusy(true, UiLocalization.Text("OptionsForm.OfficeCli.Status.Uninstalling", "Uninstalling OfficeCLI..."));
            try
            {
                var result = await _officeCliInstallationService.UninstallAsync(_officeCliPaths, default(System.Threading.CancellationToken));
                if (!CanUpdateOfficeCliUi())
                {
                    return;
                }

                if (result.Success)
                {
                    OfficeCliEnvironmentChanged = true;
                }

                RefreshOfficeCliStatus(result.Status);
                ShowOfficeCliMessage(
                    result.Message,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (!CanUpdateOfficeCliUi())
                {
                    return;
                }

                RefreshOfficeCliStatus();
                ShowOfficeCliMessage(
                    ex.Message,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (CanUpdateOfficeCliUi())
                {
                    SetOfficeCliBusy(false, null);
                }
            }
        }

        private void RefreshOfficeCliStatus()
        {
            if (!CanUpdateOfficeCliUi())
            {
                return;
            }

            RefreshOfficeCliStatus(_officeCliInstallationService.GetStatus(_officeCliPaths));
        }

        private void RefreshOfficeCliStatus(OfficeCliIntegrationStatus status)
        {
            if (!CanUpdateOfficeCliUi())
            {
                return;
            }

            status = status ?? _officeCliInstallationService.GetStatus(_officeCliPaths);
            _officeCliStatusTextBox.Text = BuildOfficeCliStatusText(status);
            _officeCliInstallButton.Enabled = true;
            _officeCliUninstallButton.Enabled = status.IsExecutableInstalled || status.IsUserPathConfigured || !status.AreSkillsInstalled;
        }

        private void SetOfficeCliBusy(bool busy, string statusText)
        {
            if (!CanUpdateOfficeCliUi())
            {
                return;
            }

            _officeCliRefreshButton.Enabled = !busy;
            _officeCliInstallButton.Enabled = !busy;
            _officeCliUninstallButton.Enabled = !busy;
            if (!string.IsNullOrWhiteSpace(statusText))
            {
                _officeCliStatusTextBox.Text = statusText;
            }
        }

        private bool CanUpdateOfficeCliUi()
        {
            return !IsDisposed && !Disposing;
        }

        private void ShowOfficeCliMessage(string message, MessageBoxIcon icon)
        {
            if (!CanUpdateOfficeCliUi())
            {
                return;
            }

            MessageBox.Show(
                this,
                message,
                UiLocalization.Text("OptionsForm.OfficeCli.Message.Title", "OfficeCLI"),
                MessageBoxButtons.OK,
                icon);
        }

        private static string BuildOfficeCliStatusText(OfficeCliIntegrationStatus status)
        {
            if (status == null)
            {
                return UiLocalization.Text("OptionsForm.OfficeCli.Status.Unknown", "OfficeCLI status is unknown.");
            }

            var lines = new System.Collections.Generic.List<string>();
            lines.Add(status.IsReady
                ? UiLocalization.Text("OptionsForm.OfficeCli.Status.Ready", "OfficeCLI is installed and ready.")
                : UiLocalization.Text("OptionsForm.OfficeCli.Status.NotReady", "OfficeCLI is not fully installed."));
            lines.Add(string.Format(
                UiLocalization.Text("OptionsForm.OfficeCli.Status.Executable.Template", "Executable: {0}"),
                status.IsExecutableInstalled ? status.ExecutablePath : UiLocalization.Text("Common.Value.NotFound", "Not found")));
            lines.Add(string.Format(
                UiLocalization.Text("OptionsForm.OfficeCli.Status.UserPath.Template", "User PATH: {0}"),
                status.IsUserPathConfigured
                    ? UiLocalization.Text("Common.Value.Configured", "Configured")
                    : UiLocalization.Text("Common.Value.NotConfigured", "Not configured")));
            lines.Add(string.Format(
                UiLocalization.Text("OptionsForm.OfficeCli.Status.Skills.Template", "Skills: {0}"),
                status.AreSkillsInstalled
                    ? UiLocalization.Text("Common.Value.Configured", "Configured")
                    : string.Format(
                        UiLocalization.Text("OptionsForm.OfficeCli.Status.MissingSkills.Template", "{0} missing"),
                        status.MissingSkillDirectories == null ? 0 : status.MissingSkillDirectories.Count)));
            lines.Add(string.Format(
                UiLocalization.Text("OptionsForm.OfficeCli.Status.InstallDirectory.Template", "Install directory: {0}"),
                status.InstallDirectory));
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private static AppInteractionMode ParseStartupMode(string value)
        {
            AppInteractionMode mode;
            return Enum.TryParse(value, true, out mode) ? mode : AppInteractionMode.Coding;
        }

        private static string NormalizeOptionalText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class ChoiceItem
        {
            public ChoiceItem(string value, string text)
            {
                Value = value;
                Text = text;
            }

            public string Value { get; }

            public string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }

    }
}
