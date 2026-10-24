using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void ApplyMainFormLocalizedText()
        {
            _statusLabel.Text = UiLocalization.Text("Status.Ready", "Ready");
            _fileMenuItem.Text = UiLocalization.Text("MainMenu.File", "&File");
            _gitMenuItem.Text = UiLocalization.Text("MainMenu.Git", "&Git");
            _accountMenuItem.Text = UiLocalization.Text("MainMenu.Account", "A&ccount");
            _helpMenuItem.Text = UiLocalization.Text("MainMenu.Help", "&Help");
            _openWorkspaceFileMenuItem.Text = UiLocalization.Text("MainMenu.OpenWorkspace", "&Open Workspace...");
            _openWslWorkspaceFileMenuItem.Text = UiLocalization.Text("MainMenu.OpenWslWorkspace", "Open &WSL Workspace...");
            _openGlobalFileBrowserMenuItem.Text = UiLocalization.Text("MainMenu.OpenGlobalFileBrowser", "Open Global File Browser(&E)...");
            _newSessionFileMenuItem.Text = UiLocalization.Text("MainMenu.NewSession", "&New Session");
            _automationsMenuItem.Text = UiLocalization.Text("MainMenu.Automations", "Automations(&A)");
            _targetReloadMenuItem.Text = UiLocalization.Text("MainMenu.TargetReload", "Reload Target(&R)");
            _exitMenuItem.Text = UiLocalization.Text("MainMenu.Exit", "E&xit");
            _gitWorkbenchMenuItem.Text = UiLocalization.Text("MainMenu.GitWorkbench", "Git &Workbench...");
            _gitCloneMenuItem.Text = UiLocalization.Text("MainMenu.GitClone", "&Clone Repository...");
            _gitRemoteRepositoriesMenuItem.Text = UiLocalization.Text("MainMenu.GitRemoteRepositories", "Remote Repositories(&P)...");
            _gitMarketMenuItem.Text = UiLocalization.Text("MainMenu.GitMarket", "GitHub Store(&M)...");
            _gitEnvironmentMenuItem.Text = UiLocalization.Text("MainMenu.GitEnvironment", "Git &Environment...");
            _gitRemoteCredentialsMenuItem.Text = UiLocalization.Text("MainMenu.GitRemoteCredentials", "Remote Credentials(&R)...");
            _userCenterMenuItem.Text = UiLocalization.Text("MainMenu.UserCenter", "&User Center");
            if (_authenticationCenterMenuItem != null)
                _authenticationCenterMenuItem.Text = UiLocalization.Text("MainMenu.AuthenticationCenter", "&Authentication Center…");
            _userManualMenuItem.Text = UiLocalization.Text("MainMenu.UserManual", "&User Manual");
            _officialWebsiteMenuItem.Text = UiLocalization.Text("MainMenu.OfficialWebsite", "Visit &Website");
            _checkUpdatesMenuItem.Text = UiLocalization.Text("MainMenu.CheckUpdates", "Check for &Updates...");
            _openLogDirectoryMenuItem.Text = UiLocalization.Text("MainMenu.OpenLogDirectory", "Open &Log Directory");
            _donateMenuItem.Text = UiLocalization.Text("MainMenu.DonateSupport", "Support the Project(&D)...");
            _aboutMenuItem.Text = UiLocalization.Text("MainMenu.About", "A&bout...");
            _planModeMenuItem.Text = UiLocalization.Text("MainMenu.PlanMode", "&Plan Mode");
            _goalMenuItem.Text = UiLocalization.Text("MainForm.Context.Goal", "Goal(&G)");
            _goalSetMenuItem.Text = UiLocalization.Text("MainForm.Goal.Set", "Set Goal(&S)...");
            _goalViewMenuItem.Text = UiLocalization.Text("MainForm.Goal.View", "View Goal(&V)");
            _goalClearMenuItem.Text = UiLocalization.Text("MainForm.Goal.Clear", "Clear Goal(&C)");
            _fileImportSyncMenuItem.Text = UiLocalization.Text("MainForm.ImportSync.Menu", "Import && Sync(&I)");
            _fileSyncThreadsMenuItem.Text = UiLocalization.Text("MainForm.ImportSync.SyncThreadHistory", "S&ync Thread History...");
            _fileImportOtherSessionsMenuItem.Text = UiLocalization.Text("MainForm.ImportSync.ImportOtherSessions", "Import Other Sessions(&O)...");
            _fileExternalAgentConfigImportMenuItem.Text = UiLocalization.Text("MainForm.ImportSync.ExternalAgentConfigImport", "Import External Agent Config(&C)...");
            _workspaceImportSyncMenuItem.Text = UiLocalization.Text("MainForm.ImportSync.Menu", "Import && Sync(&I)");
            _workspaceSyncThreadsMenuItem.Text = UiLocalization.Text("MainForm.ImportSync.SyncThreadHistory", "S&ync Thread History...");
            _workspaceImportOtherSessionsMenuItem.Text = UiLocalization.Text("MainForm.ImportSync.ImportOtherSessions", "Import Other Sessions(&O)...");
            _workspaceExternalAgentConfigImportMenuItem.Text = UiLocalization.Text("MainForm.ImportSync.ExternalAgentConfigImport", "Import External Agent Config(&C)...");
            _commandMenuButton.Text = UiLocalization.Text("MainForm.Button.Command", "Terminal(&K)");
            _commandMenuButton.AccessibleName = UiLocalization.Text("MainForm.Accessible.Command", "Terminal");
            _diffWorkbenchButton.Text = UiLocalization.Text("MainForm.Button.DiffWorkbench", "Git Workbench(&H)");
            _diffWorkbenchButton.AccessibleName = UiLocalization.Text("MainForm.Accessible.DiffWorkbench", "Git Workbench");
            _attachmentButton.Text = UiLocalization.Text("MainForm.Button.Attach", "&Attach");
            _attachmentButton.AccessibleName = UiLocalization.Text("MainForm.Accessible.Attach", "Attach");
            _selectImageAttachmentMenuItem.Text = UiLocalization.Text("MainForm.Attachment.Image", "Image(&I)...");
            _selectFileAttachmentMenuItem.Text = UiLocalization.Text("MainForm.Attachment.File", "File(&F)...");
            _selectFolderAttachmentMenuItem.Text = UiLocalization.Text("MainForm.Attachment.Folder", "Folder(&D)...");
            _selectSkillAttachmentMenuItem.Text = UiLocalization.Text("MainForm.Attachment.Skill", "Skill(&K)...");
            _selectPluginAttachmentMenuItem.Text = UiLocalization.Text("MainForm.Attachment.Plugin", "Plugin(&P)...");
            _removeSelectedAttachmentMenuItem.Text = UiLocalization.Text("MainForm.Attachment.RemoveSelected", "Remove Selected(&R)");
            _clearAttachmentsMenuItem.Text = UiLocalization.Text("MainForm.Attachment.ClearItems", "Clear Items(&C)");
            _workspaceAppServerFilesMenuItem.Text = UiLocalization.Text("MainForm.Workspace.AppServerFiles", "File Browser(&F)...");
            _workspaceProjectInstructionsMenuItem.Text = UiLocalization.Text("MainForm.Workspace.ProjectInstructions", "Project Instructions(&I)...");
            _workspaceCreateAutomationMenuItem.Text = UiLocalization.Text("MainForm.Workspace.CreateAutomation", "Create Project Automation(&A)...");
            _sessionSubAgentsMenuItem.Text = UiLocalization.Text("MainForm.Session.SubAgents", "View Suba&gents...");
            _sessionCreateAutomationMenuItem.Text = UiLocalization.Text("MainForm.SessionMenu.CreateAutomation", "Create Automation(&A)");
            _activitySettingsMenuItem.Text = UiLocalization.Text("MainForm.Activity.Settings", "Message Settings(&S)...");
            _clearActivityMenuItem.Text = UiLocalization.Text("MainForm.Activity.ClearAll", "Clear All Activity(&C)");
            _showActivityLogMenuItem.Text = UiLocalization.Text("MainForm.Log.ShowActivityLog", "Show Log(&S)");
            _clearLogMenuItem.Text = UiLocalization.Text("MainForm.Log.Clear", "Clear Log(&L)");
            _sendButton.Text = UiLocalization.Text("MainForm.Button.Send", "&Send");
            _sendButton.AccessibleName = UiLocalization.Text("MainForm.Accessible.Send", "Send");
            _stopButton.Text = UiLocalization.Text("MainForm.Button.Stop", "&Stop");
            _stopButton.AccessibleName = UiLocalization.Text("MainForm.Accessible.Stop", "Stop");
            RefreshPendingAsyncInputButton();
            _voiceChat.RefreshControls();
            _openWorkspaceButton.Text = UiLocalization.Text("MainForm.Button.NewWorkspace", "&Open Workspace");
            _openWorkspaceButton.AccessibleName = UiLocalization.Text("MainForm.Accessible.NewWorkspace", "Open Workspace");
            _newSessionButton.Text = UiLocalization.Text("MainForm.Button.NewSession", "Ne&w Session");
            _newSessionButton.AccessibleName = UiLocalization.Text("MainForm.Accessible.NewSession", "New Session");

            _workspaceList.AccessibleName = GetWorkspaceListAccessibleName();
            ApplyWorkspaceListColumnText();
            _sessionList.AccessibleName = UiLocalization.Text("MainForm.Accessible.Sessions", "Sessions");
            ApplySessionListColumnText();
            _chatHistoryListBox.AccessibleName = UiLocalization.Text("MainForm.Accessible.ChatHistory", "Messages");
            _pendingAttachmentListBox.AccessibleName = UiLocalization.Text("MainForm.Accessible.PendingItems", "Pending items");
            _inputBox.AccessibleName = UiLocalization.Text("MainForm.Accessible.Input", "Input");
            _activityBrowseControl.AccessibleName = UiLocalization.Text("MainForm.Accessible.Activity", "Activity");
            _activityCurrentStatusLabel.AccessibleName = UiLocalization.Text("MainForm.Accessible.CurrentActivity", "Current activity");

            if (_modeTabControl != null)
            {
                _codingTabPage.Text = UiLocalization.Text("MainForm.ModeTab.Coding", "Coding");
                _codingTabPage.AccessibleName = UiLocalization.Text("MainForm.Accessible.CodingMode", "Coding");
                _officeTabPage.Text = UiLocalization.Text("MainForm.ModeTab.Office", "Office");
                _officeTabPage.AccessibleName = UiLocalization.Text("MainForm.Accessible.OfficeMode", "Office");
            }

            if (_workspaceSectionTitleLabel != null)
            {
                _workspaceSectionTitleLabel.Text = GetWorkspaceSectionTitleText();
            }

            if (_sessionSectionTitleLabel != null)
            {
                _sessionSectionTitleLabel.Text = UiLocalization.Text("MainForm.Label.Session", "Session");
            }

            if (_inputSectionTitleLabel != null)
            {
                _inputSectionTitleLabel.Text = UiLocalization.Text("MainForm.Label.Input", "Input(&I)");
                _inputSectionTitleLabel.AccessibleName = UiLocalization.Text("MainForm.Accessible.Input", "Input");
            }

            if (_pendingAttachmentsSectionTitleLabel != null)
            {
                _pendingAttachmentsSectionTitleLabel.Text = UiLocalization.Text("MainForm.Label.PendingAttachments", "Pending Items");
            }

            if (_repositoryActionsSectionTitleLabel != null)
            {
                _repositoryActionsSectionTitleLabel.Text = UiLocalization.Text("MainForm.Section.Activity", "Activity");
            }

            RefreshMainWindowHotKeyMenuText();
            RefreshWindowTitle();
        }

        private void ConfigureConstructedMainFormUi()
        {
            _workspaceList.TabIndex = 0;
            _openWorkspaceButton.TabIndex = 1;
            _sessionList.TabIndex = 2;
            _newSessionButton.TabIndex = 3;
            _commandMenuButton.TabIndex = 0;
            _diffWorkbenchButton.TabIndex = 1;
            _attachmentButton.TabIndex = 3;
            _commandMenuButton.Text = UiLocalization.Text("MainForm.Button.Command", "Terminal(&K)");
            _commandMenuButton.AccessibleName = UiLocalization.Text("MainForm.Accessible.Command", "Terminal");
            _diffWorkbenchButton.Text = UiLocalization.Text("MainForm.Button.DiffWorkbench", "Git Workbench(&H)");
            _diffWorkbenchButton.AccessibleName = UiLocalization.Text("MainForm.Accessible.DiffWorkbench", "Git Workbench");

            _attachmentButton.Width = 96;
            ApplyMainFormVisualStyle();
            ConfigureChatHistoryContextMenu();
            ConfigureAttachmentUi();
            ConfigureLogContextMenu();
            ConfigureWorkspaceNavigation();
            ConfigureDeletionContextMenus();
            ConfigureTrayIcon();
            ConfigureMainMenu();
            ConfigureSessionSettingsUi();
            _automaticThreadSyncTimer.Tick += AutomaticThreadSyncTimer_Tick;
            _automaticThreadSyncFollowUpTimer.Tick += AutomaticThreadSyncFollowUpTimer_Tick;
            _localThreadSessionWatchDebounceTimer.Tick += LocalThreadSessionWatchDebounceTimer_Tick;
            _localSubAgentThreadSyncTimer.Tick += LocalSubAgentThreadSyncTimer_Tick;
            _rateLimitAutoResetTimer.Tick += RateLimitAutoResetTimer_Tick;
            _logFlushTimer.Tick += LogFlushTimer_Tick;

            _connectionStatus = GetConnectionStatusDisconnectedText();
            _threadSyncSummary = UiLocalization.Text("MainForm.ThreadSync.NotSynced", "Not synced yet");

            _commandMenuButton.KeyDown += CommandMenuButton_KeyDown;
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;
            Shown += MainForm_Shown;
            Resize += MainForm_Resize;
        }

        private void ApplyMainFormVisualStyle()
        {
            BackColor = UiTheme.WindowBackColor;

            _statusStrip.BackColor = UiTheme.PanelBackColor;
            _statusStrip.Padding = new Padding(IsHandleCreated ? UiTheme.Scale(UiTheme.ControlSpacing) : UiTheme.ControlSpacing, 0, IsHandleCreated ? UiTheme.Scale(UiTheme.ControlSpacing) : UiTheme.ControlSpacing, 0);
            _statusLabel.ForeColor = UiTheme.MutedTextColor;
            _statusLabel.Padding = new Padding(UiTheme.Scale(4), 0, 0, 0);

            UiTheme.ApplyListView(_workspaceList);
            UiTheme.ApplyListView(_sessionList);
            UiTheme.ApplyListBox(_pendingAttachmentListBox);
            UiTheme.ApplyListBox(_chatHistoryListBox);
            _inputBox.ApplyTheme();
            UiTheme.ApplyButton(_commandMenuButton, UiGlyph.Command);
            UiTheme.ApplyButton(_diffWorkbenchButton, UiGlyph.FileChange);
            UiTheme.ApplyButton(_attachmentButton, UiGlyph.Attach);
            UiTheme.ApplyButton(_sendButton, UiGlyph.Send);
            UiTheme.ApplyButton(_stopButton, UiGlyph.Stop);
            UiTheme.ApplyButton(_pendingAsyncInputButton, UiGlyph.Info);
            UiTheme.ApplyButton(_voiceChat.ToggleButton, UiGlyph.Microphone);
            UiTheme.ApplyButton(_openWorkspaceButton, UiGlyph.Workspace);
            UiTheme.ApplyButton(_newSessionButton, UiGlyph.NewSession);
        }

        private void ConfigureAttachmentUi()
        {
            _attachmentButton.Click += OnAttachmentButtonClick;
            _attachmentButton.KeyDown += AttachmentButton_KeyDown;
            _selectImageAttachmentMenuItem.Click += OnSelectImageAttachmentMenuItemClick;
            _selectFileAttachmentMenuItem.Click += OnSelectFileAttachmentMenuItemClick;
            _selectFolderAttachmentMenuItem.Click += OnSelectFolderAttachmentMenuItemClick;
            _selectSkillAttachmentMenuItem.Click += OnSelectSkillAttachmentMenuItemClick;
            _selectPluginAttachmentMenuItem.Click += OnSelectPluginAttachmentMenuItemClick;
            _removeSelectedAttachmentMenuItem.Click += OnRemoveSelectedAttachmentMenuItemClick;
            _clearAttachmentsMenuItem.Click += OnClearAttachmentsMenuItemClick;
            _attachmentMenu.MenuItems.Add(_selectImageAttachmentMenuItem);
            _attachmentMenu.MenuItems.Add(_selectFileAttachmentMenuItem);
            _attachmentMenu.MenuItems.Add(_selectFolderAttachmentMenuItem);
            _attachmentMenu.MenuItems.Add(_selectSkillAttachmentMenuItem);
            _attachmentMenu.MenuItems.Add(_selectPluginAttachmentMenuItem);

            _pendingAttachmentContextMenu.MenuItems.Add(_removeSelectedAttachmentMenuItem);
            _pendingAttachmentContextMenu.MenuItems.Add(_clearAttachmentsMenuItem);
            _pendingAttachmentContextMenu.Popup += PendingAttachmentContextMenu_Popup;
            _pendingAttachmentListBox.ContextMenu = _pendingAttachmentContextMenu;
            _pendingAttachmentListBox.MouseDown += PendingAttachmentListBox_MouseDown;
            _pendingAttachmentListBox.KeyDown += PendingAttachmentListBox_KeyDown;
        }

        private void ConfigureLogContextMenu()
        {
            _activitySettingsMenuItem.Click += ActivitySettingsMenuItem_Click;
            _clearActivityMenuItem.Click += ClearActivityMenuItem_Click;
            _logContextMenu.MenuItems.Add(_activitySettingsMenuItem);
            _logContextMenu.MenuItems.Add("-");
            _logContextMenu.MenuItems.Add(_clearActivityMenuItem);
            _logContextMenu.Popup += LogContextMenu_Popup;
            _activityBrowseControl.ContextMenu = _logContextMenu;
        }

        private void ConfigureMainMenu()
        {
            _openWorkspaceFileMenuItem.Click += OnOpenWorkspaceFileMenuItemClick;
            _openWslWorkspaceFileMenuItem.Click += OnOpenWslWorkspaceMenuItemClick;
            _openGlobalFileBrowserMenuItem.Click += OnOpenGlobalFileBrowserMenuItemClick;
            _newSessionFileMenuItem.Click += OnNewSessionButtonClick;
            _automationsMenuItem.Click += (sender, e) => ShowAutomationsForm(null);
            _targetReloadMenuItem.Click += OnTargetReloadMenuItemClick;
            _exitMenuItem.Click += OnExitMenuItemClick;
            _fileSyncThreadsMenuItem.Click += OnSyncThreadsMenuItemClick;
            _fileImportOtherSessionsMenuItem.Click += OnImportOtherSessionsMenuItemClick;
            _fileExternalAgentConfigImportMenuItem.Click += ExternalAgentConfigImportMenuItem_Click;
            _gitWorkbenchMenuItem.Click += OnGitWorkbenchMenuItemClick;
            _gitCloneMenuItem.Click += OnGitCloneMenuItemClick;
            _gitRemoteRepositoriesMenuItem.Click += OnGitRemoteRepositoriesMenuItemClick;
            _gitMarketMenuItem.Click += OnGitMarketMenuItemClick;
            _gitEnvironmentMenuItem.Click += OnGitEnvironmentMenuItemClick;
            _gitRemoteCredentialsMenuItem.Click += OnGitRemoteCredentialsMenuItemClick;
            _userCenterMenuItem.Click += OnUserCenterMenuItemClick;
            _userManualMenuItem.Click += OnUserManualMenuItemClick;
            _officialWebsiteMenuItem.Click += OnOfficialWebsiteMenuItemClick;
            _checkUpdatesMenuItem.Click += OnCheckUpdatesMenuItemClick;
            _openLogDirectoryMenuItem.Click += OnOpenLogDirectoryMenuItemClick;
            _donateMenuItem.Click += OnDonateMenuItemClick;
            _aboutMenuItem.Click += OnAboutMenuItemClick;

            _fileMenuItem.Popup += FileMenuItem_Popup;
            _fileMenuItem.MenuItems.Add(_openWorkspaceFileMenuItem);
            _fileMenuItem.MenuItems.Add(_openWslWorkspaceFileMenuItem);
            _fileMenuItem.MenuItems.Add(_openGlobalFileBrowserMenuItem);
            _fileMenuItem.MenuItems.Add(_newSessionFileMenuItem);
            _fileMenuItem.MenuItems.Add(_automationsMenuItem);
            _fileMenuItem.MenuItems.Add(_fileImportSyncMenuItem);
            _fileMenuItem.MenuItems.Add(_targetReloadMenuItem);
            _fileMenuItem.MenuItems.Add("-");
            _fileMenuItem.MenuItems.Add(_exitMenuItem);
            _fileImportSyncMenuItem.MenuItems.Add(_fileSyncThreadsMenuItem);
            _fileImportSyncMenuItem.MenuItems.Add(_fileImportOtherSessionsMenuItem);
            _fileImportSyncMenuItem.MenuItems.Add(_fileExternalAgentConfigImportMenuItem);
            _gitMenuItem.MenuItems.Add(_gitWorkbenchMenuItem);
            _gitMenuItem.MenuItems.Add(_gitCloneMenuItem);
            _gitMenuItem.MenuItems.Add(_gitRemoteRepositoriesMenuItem);
            _gitMenuItem.MenuItems.Add(_gitMarketMenuItem);
            _gitMenuItem.MenuItems.Add(_gitRemoteCredentialsMenuItem);
            _gitMenuItem.MenuItems.Add(_gitEnvironmentMenuItem);
            _accountMenuItem.MenuItems.Add(_userCenterMenuItem);
            InitializeAuthenticationCenterMenu();
            _helpMenuItem.MenuItems.Add(_userManualMenuItem);
            _helpMenuItem.MenuItems.Add(_officialWebsiteMenuItem);
            _helpMenuItem.MenuItems.Add(_checkUpdatesMenuItem);
            _helpMenuItem.MenuItems.Add(_openLogDirectoryMenuItem);
            _helpMenuItem.MenuItems.Add("-");
            _helpMenuItem.MenuItems.Add(_donateMenuItem);
            _helpMenuItem.MenuItems.Add(_aboutMenuItem);

            Menu = _mainMenu;
            RefreshAuthenticationUi();
        }

        private void ConfigureChatHistoryContextMenu()
        {
            _enableAutoReadMenuItem.Click += OnEnableAutoReadMenuItemClick;
            _copySelectedMenuItem.Click += CopySelectedMenuItem_Click;
            _findChatMessageContextMenuItem.Click += OnFindChatMessageMenuItemClick;
            _planModeMenuItem.Click += OnPlanModeMenuItemClick;
            _forkFullMenuItem.Click += ForkFullMenuItem_Click;
            _forkFromHereMenuItem.Click += ForkFromHereMenuItem_Click;
            _compactChatHistoryMenuItem.Click += CompactChatHistoryMenuItem_Click;
            _reviewChatHistoryMenuItem.Click += ReviewChatHistoryMenuItem_Click;
            _goalSetMenuItem.Click += OnGoalSetMenuItemClick;
            _goalViewMenuItem.Click += OnGoalViewMenuItemClick;
            _goalClearMenuItem.Click += OnGoalClearMenuItemClick;
            _forkMenuItem.MenuItems.Add(_forkFullMenuItem);
            _forkMenuItem.MenuItems.Add(_forkFromHereMenuItem);
            _goalMenuItem.MenuItems.Add(_goalSetMenuItem);
            _goalMenuItem.MenuItems.Add(_goalViewMenuItem);
            _goalMenuItem.MenuItems.Add(_goalClearMenuItem);
            _chatHistoryContextMenu.MenuItems.Add(_enableAutoReadMenuItem);
            _chatHistoryContextMenu.MenuItems.Add(_planModeMenuItem);
            _chatHistoryContextMenu.MenuItems.Add(_goalMenuItem);
            _chatHistoryContextMenu.MenuItems.Add("-");
            _chatHistoryContextMenu.MenuItems.Add(_copySelectedMenuItem);
            _chatHistoryContextMenu.MenuItems.Add(_findChatMessageContextMenuItem);
            _chatHistoryContextMenu.MenuItems.Add("-");
            _chatHistoryContextMenu.MenuItems.Add(_forkMenuItem);
            _chatHistoryContextMenu.MenuItems.Add(_compactChatHistoryMenuItem);
            _chatHistoryContextMenu.MenuItems.Add(_reviewChatHistoryMenuItem);
            _chatHistoryContextMenu.Popup += ChatHistoryContextMenu_Popup;
            _chatHistoryListBox.ContextMenu = _chatHistoryContextMenu;
            _chatHistoryListBox.KeyDown += ChatHistoryListBox_KeyDown;
            _chatHistoryListBox.AnchorNavigationRequested += ChatHistoryListBox_AnchorNavigationRequested;
            _chatHistoryListBox.SelectionSummaryRequested += ChatHistoryListBox_SelectionSummaryRequested;
            _chatHistoryListBox.Enter += ChatHistoryListBox_Enter;
            _chatHistoryListBox.SelectedIndexChanged += ChatHistoryListBox_SelectedIndexChanged;
            _chatHistoryListBox.MouseDown += ChatHistoryListBox_MouseDown;
        }

        private void BuildLayout()
        {
            _mainSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                SplitterWidth = MainFormLayoutPlanner.SplitterWidth,
                TabStop = false
            };

            _contentSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                SplitterWidth = MainFormLayoutPlanner.SplitterWidth,
                TabStop = false
            };

            // Add splitter grip visual
            _mainSplitContainer.Paint += (sender, e) =>
            {
                UiTheme.DrawSplitterGrip(e.Graphics, _mainSplitContainer.SplitterRectangle, Orientation.Vertical);
            };

            _contentSplitContainer.Paint += (sender, e) =>
            {
                UiTheme.DrawSplitterGrip(e.Graphics, _contentSplitContainer.SplitterRectangle, Orientation.Vertical);
            };

            _mainSplitContainer.Panel1.TabStop = false;
            _mainSplitContainer.Panel2.TabStop = false;
            _contentSplitContainer.Panel1.TabStop = false;
            _contentSplitContainer.Panel2.TabStop = false;

            _mainSplitContainer.SplitterMoving += OnMainSplitterMoving;
            _mainSplitContainer.SplitterMoved += OnMainSplitterMoved;
            _contentSplitContainer.SplitterMoving += OnContentSplitterMoving;
            _contentSplitContainer.SplitterMoved += OnContentSplitterMoved;

            _mainSplitContainer.Panel1.Controls.Add(CreateLeftPanel());
            _mainSplitContainer.Panel2.Controls.Add(_contentSplitContainer);

            _contentSplitContainer.Panel1.Controls.Add(CreateCenterPanel());
            _contentSplitContainer.Panel2.Controls.Add(CreateRightPanel());

            _modeTabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                TabIndex = 0
            };
            _modeTabControl.TabStop = true;
            _codingTabPage = new TabPage(UiLocalization.Text("MainForm.ModeTab.Coding", "Coding"))
            {
                AccessibleName = UiLocalization.Text("MainForm.Accessible.CodingMode", "Coding"),
                UseVisualStyleBackColor = true
            };
            _officeTabPage = new TabPage(UiLocalization.Text("MainForm.ModeTab.Office", "Office"))
            {
                AccessibleName = UiLocalization.Text("MainForm.Accessible.OfficeMode", "Office"),
                UseVisualStyleBackColor = true
            };
            _codingTabPage.Controls.Add(_mainSplitContainer);
            _modeTabControl.TabPages.Add(_codingTabPage);
            _modeTabControl.TabPages.Add(_officeTabPage);

            _statusStrip.Items.Add(_statusIndicator);
            _statusStrip.Items.Add(_statusLabel);
            _statusStrip.Items.Add(new ToolStripStatusLabel
            {
                Spring = true
            });

            Controls.Add(_modeTabControl);
            Controls.Add(_statusStrip);
        }

        private Control CreateLeftPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PanelBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };

            _workspaceSectionTitleLabel = CreateSubSectionTitle(UiLocalization.Text("MainForm.Label.Workspace", "Workspace"));
            _sessionSectionTitleLabel = CreateSubSectionTitle(UiLocalization.Text("MainForm.Label.Session", "Session"));

            _workspaceList.Height = 140;
            _workspaceList.Dock = DockStyle.Top;
            _workspaceList.Resize += delegate { UpdateWorkspaceListColumnWidths(); };
            _sessionList.Dock = DockStyle.Fill;
            _sessionList.SelectedIndexChanged += OnSessionListSelectedIndexChanged;
            _sessionList.DoubleClick += SessionList_DoubleClick;
            _sessionList.KeyDown += SessionList_KeyDown;
            _sessionList.Resize += delegate { UpdateSessionListColumnWidth(); };

            panel.Controls.Add(_sessionList);
            panel.Controls.Add(_sessionSectionTitleLabel);
            panel.Controls.Add(_workspaceList);
            panel.Controls.Add(_workspaceSectionTitleLabel);
            return panel;
        }

        private Control CreateRightPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PanelBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                ColumnCount = 1,
                RowCount = 3,
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, MainFormLayoutPlanner.ActivityCommandRowHeight));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _repositoryActionsSectionTitleLabel = CreateSectionTitle(UiLocalization.Text("MainForm.Section.Activity", "Activity"));
            _repositoryActionsSectionTitleLabel.Margin = new Padding(0);

            panel.Controls.Add(_repositoryActionsSectionTitleLabel, 0, 0);
            panel.Controls.Add(CreateCommandPanel(), 0, 1);
            panel.Controls.Add(CreateActivityPanel(), 0, 2);
            return panel;
        }

        private Control CreateCommandPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PanelBackColor,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(0, 4, 0, 0),
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _commandMenuButton.Dock = DockStyle.Fill;
            _diffWorkbenchButton.Dock = DockStyle.Fill;
            _commandMenuButton.Margin = new Padding(0, 0, UiTheme.ControlSpacing, 0);
            _diffWorkbenchButton.Margin = new Padding(0);

            panel.Controls.Add(_commandMenuButton, 0, 0);
            panel.Controls.Add(_diffWorkbenchButton, 1, 0);
            return panel;
        }

        private Control CreateActivityPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.PanelBackColor,
                TabStop = false
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _activityCurrentStatusLabel.Margin = new Padding(0);
            _activityBrowseControl.Dock = DockStyle.Fill;
            _activityBrowseControl.Margin = new Padding(0);

            panel.Controls.Add(_activityCurrentStatusLabel, 0, 0);
            panel.Controls.Add(_activityBrowseControl, 0, 1);
            return panel;
        }

        private Control CreateCenterPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };

            var infoPanel = CreateHeaderPanel();
            var inputPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.PanelBackColor,
                TabStop = false
            };
            _inputSectionTitleLabel = CreateSubSectionTitle(UiLocalization.Text("MainForm.Label.Input", "Input(&I)"));
            _inputSectionTitleLabel.AccessibleName = UiLocalization.Text("MainForm.Accessible.Input", "Input");
            _inputSectionTitleLabel.Height = 24;
            _inputSectionTitleLabel.Padding = new Padding(0);
            _inputSectionTitleLabel.Margin = new Padding(0);
            _inputBox.Dock = DockStyle.Fill;
            inputPanel.Controls.Add(_inputBox);
            inputPanel.Controls.Add(_inputSectionTitleLabel);

            _pendingAttachmentsSectionTitleLabel = CreateSubSectionTitle(UiLocalization.Text("MainForm.Label.PendingAttachments", "Pending Items"));
            _pendingAttachmentsSectionTitleLabel.Height = 24;
            _pendingAttachmentsSectionTitleLabel.Padding = new Padding(0);
            _pendingAttachmentsSectionTitleLabel.Margin = new Padding(0);
            _pendingAttachmentListBox.Dock = DockStyle.Fill;
            _attachmentPanel.Controls.Add(_pendingAttachmentListBox);
            _attachmentPanel.Controls.Add(_pendingAttachmentsSectionTitleLabel);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                BackColor = UiTheme.PanelBackColor,
                Padding = new Padding(UiTheme.ControlSpacing, 0, 0, 0),
                TabStop = false
            };

            buttonPanel.Controls.Add(_attachmentButton);
            buttonPanel.Controls.Add(_sendButton);
            buttonPanel.Controls.Add(_stopButton);
            buttonPanel.Controls.Add(_pendingAsyncInputButton);
            buttonPanel.Controls.Add(_voiceChat.ToggleButton);
            _composePanel.Controls.Add(_attachmentPanel);
            _composePanel.Controls.Add(inputPanel);
            _composePanel.Controls.Add(buttonPanel);

            panel.Controls.Add(_chatHistoryListBox);
            panel.Controls.Add(_composePanel);
            panel.Controls.Add(infoPanel);
            return panel;
        }

        private Control CreateHeaderPanel()
        {
            var container = new Panel
            {
                Dock = DockStyle.Top,
                Height = MainFormLayoutPlanner.HeaderPanelHeight,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, 0, 0, 8),
                TabStop = false
            };

            var summaryPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = UiTheme.CardBackColor,
                Padding = new Padding(
                    UiTheme.InnerPadding,
                    MainFormLayoutPlanner.HeaderSummaryVerticalPadding,
                    UiTheme.InnerPadding,
                    MainFormLayoutPlanner.HeaderSummaryVerticalPadding),
                TabStop = false
            };

            summaryPanel.Paint += (sender, e) =>
            {
                var rect = summaryPanel.ClientRectangle;
                UiTheme.DrawCardShadow(e.Graphics, rect);
            };

            summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            summaryPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            summaryPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            summaryPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            _workspaceNameValueLabel.Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold);
            _workspacePathValueLabel.Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular);
            _branchValueLabel.Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular);
            _branchValueLabel.ForeColor = UiTheme.MutedTextColor;
            _branchValueLabel.Visible = false;

            summaryPanel.Controls.Add(_workspaceNameValueLabel, 0, 0);
            summaryPanel.Controls.Add(_workspacePathValueLabel, 0, 1);
            summaryPanel.Controls.Add(_branchValueLabel, 1, 0);
            summaryPanel.SetColumnSpan(_workspaceNameValueLabel, 2);
            summaryPanel.SetColumnSpan(_workspacePathValueLabel, 2);

            container.Controls.Add(summaryPanel);
            return container;
        }

        private void ApplyWorkspaceListColumnText()
        {
            _workspaceList.Columns.Clear();
            var columnWidths = MainFormLayoutPlanner.ResolveWorkspaceListColumnWidths(
                _workspaceList.ClientSize.Width,
                IsUnixRemoteRuntimeEnabled());
            _workspaceList.Columns.Add(UiLocalization.Text("MainForm.WorkspaceList.Column.Name", "Name"), columnWidths.NameWidth, HorizontalAlignment.Left);
            if (IsUnixRemoteRuntimeEnabled())
            {
                _workspaceList.Columns.Add(UiLocalization.Text("MainForm.WorkspaceList.Column.Source", "Source"), columnWidths.SourceWidth, HorizontalAlignment.Left);
            }

            _workspaceList.Columns.Add(UiLocalization.Text("MainForm.WorkspaceList.Column.Branch", "Branch"), columnWidths.BranchWidth, HorizontalAlignment.Left);
        }

        private string GetWorkspaceListAccessibleName()
        {
            return UiLocalization.Text("MainForm.Accessible.Workspaces", "Workspaces");
        }

        private string GetWorkspaceSectionTitleText()
        {
            return UiLocalization.Text("MainForm.Label.Workspace", "Workspace");
        }

        private void UpdateWorkspaceListColumnWidths()
        {
            var showSource = IsUnixRemoteRuntimeEnabled();
            var expectedColumnCount = showSource ? 3 : 2;
            if (_workspaceList.Columns.Count != expectedColumnCount)
            {
                return;
            }

            var columnWidths = MainFormLayoutPlanner.ResolveWorkspaceListColumnWidths(_workspaceList.ClientSize.Width, showSource);
            _workspaceList.Columns[0].Width = columnWidths.NameWidth;
            if (showSource)
            {
                _workspaceList.Columns[1].Width = columnWidths.SourceWidth;
                _workspaceList.Columns[2].Width = columnWidths.BranchWidth;
                return;
            }

            _workspaceList.Columns[1].Width = columnWidths.BranchWidth;
        }

        private void ApplySessionListColumnText()
        {
            _sessionList.Columns.Clear();
            _sessionList.Columns.Add(UiLocalization.Text("MainForm.SessionList.Column.Session", "Session"), 420, HorizontalAlignment.Left);
            UpdateSessionListColumnWidth();
        }

        private void UpdateSessionListColumnWidth()
        {
            if (_sessionList.Columns.Count == 0)
            {
                return;
            }

            _sessionList.Columns[0].Width = Math.Max(UiTheme.Scale(120), _sessionList.ClientSize.Width - UiTheme.Scale(4));
        }

        private static ListView CreateWorkspaceListView(string accessibleName, int tabIndex)
        {
            return new ListView
            {
                AccessibleName = accessibleName,
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                MultiSelect = false,
                ShowItemToolTips = true,
                TabIndex = tabIndex,
                UseCompatibleStateImageBehavior = false,
                View = View.Details
            };
        }

        private static ListView CreateSessionListView(string accessibleName, int tabIndex)
        {
            return new ListView
            {
                AccessibleName = accessibleName,
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                HideSelection = false,
                MultiSelect = false,
                ShowItemToolTips = true,
                TabIndex = tabIndex,
                UseCompatibleStateImageBehavior = false,
                View = View.Details
            };
        }

        private static ListBox CreatePendingAttachmentListBox()
        {
            return new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("MainForm.Accessible.PendingItems", "Pending items"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                IntegralHeight = false,
                TabIndex = 2
            };
        }

        private static Button CreateActionButton(string text, string accessibleName, int tabIndex, EventHandler clickHandler)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = UiTheme.ButtonWidth,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(0, 0, UiTheme.ControlSpacing, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };

            button.Click += clickHandler;
            return button;
        }
    }
}
