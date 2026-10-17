using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitRemoteCredentialsForm : Form
    {
        private readonly IGitHubCliService _gitHubCliService;
        private readonly IGitSshKeyService _sshKeyService;
        private readonly IScreenReaderService _screenReaderService;
        private readonly TabControl _tabs;
        private readonly TabPage _githubTabPage;
        private readonly TabPage _sshTabPage;
        private readonly Label _statusLabel;
        private readonly Button _closeButton;

        private readonly ListBox _githubStatusListBox;
        private readonly TextBox _githubDetailsTextBox;
        private readonly Button _githubRefreshButton;
        private readonly Button _githubLoginButton;
        private readonly Button _githubDownloadButton;
        private readonly Button _githubCopySummaryButton;
        private readonly Button _githubCopyDetailsButton;
        private CancellationTokenSource _githubCancellation;
        private GitHubCliAccountSnapshot _githubSnapshot;

        private readonly ListView _sshKeysListView;
        private readonly TextBox _sshDetailsTextBox;
        private readonly Button _sshRefreshButton;
        private readonly Button _sshCopyPublicKeyButton;
        private readonly Button _sshOpenFolderButton;
        private readonly Button _sshOpenGitHubSettingsButton;
        private readonly Button _sshTestGitHubButton;
        private readonly Button _sshCopyDetailsButton;
        private ContextMenu _sshKeyContextMenu;
        private MenuItem _sshCopyPublicKeyMenuItem;
        private MenuItem _sshCopyPathMenuItem;
        private CancellationTokenSource _sshCancellation;
        private GitSshKeySnapshot _sshSnapshot;

        public GitRemoteCredentialsForm(
            IGitHubCliService gitHubCliService,
            IGitSshKeyService sshKeyService,
            IScreenReaderService screenReaderService)
        {
            _gitHubCliService = gitHubCliService ?? throw new ArgumentNullException(nameof(gitHubCliService));
            _sshKeyService = sshKeyService ?? throw new ArgumentNullException(nameof(sshKeyService));
            _screenReaderService = screenReaderService;

            Text = UiLocalization.Text("GitRemoteCredentialsForm.Title", "Remote Credentials");
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimumSize = new Size(820, 540);
            Size = new Size(980, 660);
            UiTheme.ApplyForm(this);

            _tabs = CreateTabs();
            _githubTabPage = new TabPage(UiLocalization.Text("GitRemoteCredentialsForm.Tab.GitHub", "GitHub"));
            _sshTabPage = new TabPage(UiLocalization.Text("GitRemoteCredentialsForm.Tab.Ssh", "SSH"));

            _githubStatusListBox = CreateListBox(UiLocalization.Text("GitRemoteAccountsForm.Accessible.Status", "Status"), 0);
            _githubDetailsTextBox = CreateDetailsTextBox(UiLocalization.Text("GitRemoteAccountsForm.Accessible.Details", "Details"), 1);
            _githubRefreshButton = CreateButton(UiLocalization.Text("GitRemoteAccountsForm.Button.Refresh", "Refresh(&R)"), UiLocalization.Text("GitRemoteAccountsForm.Accessible.Refresh", "Refresh"), 0, 112);
            _githubLoginButton = CreateButton(UiLocalization.Text("GitRemoteAccountsForm.Button.Login", "gh Login(&L)"), UiLocalization.Text("GitRemoteAccountsForm.Accessible.Login", "gh login"), 1, 112);
            _githubDownloadButton = CreateButton(UiLocalization.Text("GitRemoteAccountsForm.Button.DownloadGh", "Download gh(&D)"), UiLocalization.Text("GitRemoteAccountsForm.Accessible.DownloadGh", "Download gh"), 2, 130);
            _githubCopySummaryButton = CreateButton(UiLocalization.Text("GitRemoteAccountsForm.Button.CopySummary", "Copy Summary(&S)"), UiLocalization.Text("GitRemoteAccountsForm.Accessible.CopySummary", "Copy summary"), 3, 140);
            _githubCopyDetailsButton = CreateButton(UiLocalization.Text("GitRemoteAccountsForm.Button.CopyDetails", "Copy Details(&E)"), UiLocalization.Text("GitRemoteAccountsForm.Accessible.CopyDetails", "Copy details"), 4, 132);

            _sshKeysListView = CreateSshKeysListView();
            _sshDetailsTextBox = CreateDetailsTextBox(UiLocalization.Text("GitSshKeyForm.Accessible.Details", "Details"), 1);
            _sshRefreshButton = CreateButton(UiLocalization.Text("GitSshKeyForm.Button.Refresh", "Refresh(&R)"), UiLocalization.Text("GitSshKeyForm.Accessible.Refresh", "Refresh"), 0, 112);
            _sshCopyPublicKeyButton = CreateButton(UiLocalization.Text("GitSshKeyForm.Button.CopyPublicKey", "Copy Public Key(&C)"), UiLocalization.Text("GitSshKeyForm.Accessible.CopyPublicKey", "Copy public key"), 1, 152);
            _sshOpenFolderButton = CreateButton(UiLocalization.Text("GitSshKeyForm.Button.OpenSshFolder", "Open .ssh(&O)"), UiLocalization.Text("GitSshKeyForm.Accessible.OpenSshFolder", "Open .ssh"), 2, 120);
            _sshOpenGitHubSettingsButton = CreateButton(UiLocalization.Text("GitSshKeyForm.Button.OpenGitHubSettings", "GitHub Settings(&G)"), UiLocalization.Text("GitSshKeyForm.Accessible.OpenGitHubSettings", "GitHub settings"), 3, 150);
            _sshTestGitHubButton = CreateButton(UiLocalization.Text("GitSshKeyForm.Button.TestGitHub", "Test GitHub(&T)"), UiLocalization.Text("GitSshKeyForm.Accessible.TestGitHub", "Test GitHub"), 4, 132);
            _sshCopyDetailsButton = CreateButton(UiLocalization.Text("GitSshKeyForm.Button.CopyDetails", "Copy Details(&D)"), UiLocalization.Text("GitSshKeyForm.Accessible.CopyDetails", "Copy details"), 5, 132);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitRemoteCredentialsForm.Status.Ready", "Remote credentials are not loaded."),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, true);

            _closeButton = CreateButton(UiLocalization.Text("GitRemoteCredentialsForm.Button.Close", "Close(&X)"), UiLocalization.Text("GitRemoteCredentialsForm.Accessible.Close", "Close"), 10, 112);
            FormInteractionConventions.ConfigureRefreshButton(_githubRefreshButton);
            FormInteractionConventions.ConfigureRefreshButton(_sshRefreshButton);

            ApplyButtonStyles();
            WireEvents();
            BuildLayout();
            CancelButton = _closeButton;
            FormClosing += GitRemoteCredentialsForm_FormClosing;
            KeyPreview = true;
            KeyDown += GitRemoteCredentialsForm_KeyDown;
            Shown += async (sender, e) =>
            {
                Activate();
                await RefreshGitHubAsync();
            };
        }

        private async void GitRemoteCredentialsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F5)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;

            if (ReferenceEquals(_tabs.SelectedTab, _githubTabPage))
            {
                if (_githubCancellation == null)
                {
                    FormInteractionConventions.SpeakRefresh(_screenReaderService);
                    await RefreshGitHubAsync();
                }

                return;
            }

            if (ReferenceEquals(_tabs.SelectedTab, _sshTabPage) && _sshCancellation == null)
            {
                FormInteractionConventions.SpeakRefresh(_screenReaderService);
                await RefreshSshKeysAsync();
            }
        }

        private void ApplyButtonStyles()
        {
            UiTheme.ApplyButton(_githubRefreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_githubLoginButton, UiGlyph.Account);
            UiTheme.ApplyButton(_githubDownloadButton, UiGlyph.Download);
            UiTheme.ApplyButton(_githubCopySummaryButton, UiGlyph.Info);
            UiTheme.ApplyButton(_githubCopyDetailsButton, UiGlyph.CommandOutput);

            UiTheme.ApplyButton(_sshRefreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_sshCopyPublicKeyButton, UiGlyph.Info);
            UiTheme.ApplyButton(_sshOpenFolderButton, UiGlyph.Workspace);
            UiTheme.ApplyButton(_sshOpenGitHubSettingsButton, UiGlyph.Account);
            UiTheme.ApplyButton(_sshTestGitHubButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_sshCopyDetailsButton, UiGlyph.CommandOutput);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
        }

        private void WireEvents()
        {
            _githubRefreshButton.Click += async (sender, e) => await RefreshGitHubAsync();
            _githubLoginButton.Click += (sender, e) => LoginWithGh();
            _githubDownloadButton.Click += (sender, e) => OpenGhDownloadPage();
            _githubCopySummaryButton.Click += (sender, e) => CopyGitHubSummary();
            _githubCopyDetailsButton.Click += (sender, e) => CopyText(_githubDetailsTextBox, UiLocalization.Text("GitRemoteAccountsForm.Status.CopiedDetails", "Copied remote account details."));

            _sshRefreshButton.Click += async (sender, e) => await RefreshSshKeysAsync();
            _sshCopyPublicKeyButton.Click += (sender, e) => CopySelectedSshPublicKey();
            _sshOpenFolderButton.Click += (sender, e) => OpenSshFolder();
            _sshOpenGitHubSettingsButton.Click += (sender, e) => OpenGitHubSshSettings();
            _sshTestGitHubButton.Click += async (sender, e) => await TestGitHubSshAsync();
            _sshCopyDetailsButton.Click += (sender, e) => CopyText(_sshDetailsTextBox, UiLocalization.Text("GitSshKeyForm.Status.CopiedDetails", "Copied SSH key details."));
            _sshKeysListView.SelectedIndexChanged += (sender, e) =>
            {
                PopulateSelectedSshKeyDetails();
                RefreshSshActionState();
            };

            _sshKeyContextMenu = new ContextMenu();
            _sshCopyPublicKeyMenuItem = new MenuItem(UiLocalization.Text("GitSshKeyForm.Menu.CopyPublicKey", "Copy Public Key(&C)"));
            _sshCopyPathMenuItem = new MenuItem(UiLocalization.Text("GitSshKeyForm.Menu.CopyPath", "Copy Path(&P)"));
            _sshKeyContextMenu.MenuItems.Add(_sshCopyPublicKeyMenuItem);
            _sshKeyContextMenu.MenuItems.Add(_sshCopyPathMenuItem);
            _sshKeysListView.ContextMenu = _sshKeyContextMenu;
            _sshKeyContextMenu.Popup += (sender, e) => RefreshSshContextMenu();
            _sshCopyPublicKeyMenuItem.Click += (sender, e) => CopySelectedSshPublicKey();
            _sshCopyPathMenuItem.Click += (sender, e) => CopySelectedSshPath();

            _closeButton.Click += (sender, e) => Close();
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var heading = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitRemoteCredentialsForm.Heading", "Remote Credentials"),
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _githubTabPage.Controls.Add(CreateGitHubPage());
            _sshTabPage.Controls.Add(CreateSshPage());
            _tabs.TabPages.Add(_githubTabPage);
            _tabs.TabPages.Add(_sshTabPage);

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(_tabs, 0, 1);
            root.Controls.Add(_statusLabel, 0, 2);
            root.Controls.Add(CreateFooter(), 0, 3);
            Controls.Add(root);
        }

        private Control CreateGitHubPage()
        {
            var page = CreatePageLayout();
            page.Controls.Add(_githubStatusListBox, 0, 0);
            page.Controls.Add(_githubDetailsTextBox, 0, 1);
            page.Controls.Add(CreateGitHubActions(), 0, 2);
            return page;
        }

        private Control CreateGitHubActions()
        {
            var actions = CreateActionPanel();
            actions.Controls.Add(_githubCopyDetailsButton);
            actions.Controls.Add(_githubCopySummaryButton);
            actions.Controls.Add(_githubDownloadButton);
            actions.Controls.Add(_githubLoginButton);
            actions.Controls.Add(_githubRefreshButton);
            return actions;
        }

        private Control CreateSshPage()
        {
            var page = CreatePageLayout();
            page.Controls.Add(_sshKeysListView, 0, 0);
            page.Controls.Add(_sshDetailsTextBox, 0, 1);
            page.Controls.Add(CreateSshActions(), 0, 2);
            return page;
        }

        private Control CreateSshActions()
        {
            var actions = CreateActionPanel();
            actions.Controls.Add(_sshCopyDetailsButton);
            actions.Controls.Add(_sshTestGitHubButton);
            actions.Controls.Add(_sshOpenGitHubSettingsButton);
            actions.Controls.Add(_sshOpenFolderButton);
            actions.Controls.Add(_sshCopyPublicKeyButton);
            actions.Controls.Add(_sshRefreshButton);
            return actions;
        }

        private Control CreateFooter()
        {
            var footer = CreateActionPanel();
            return footer;
        }

        private async Task RefreshGitHubAsync()
        {
            if (_githubCancellation != null)
            {
                return;
            }

            _githubCancellation = new CancellationTokenSource();
            SetGitHubBusy(true);
            SetStatus(UiLocalization.Text("GitRemoteAccountsForm.Status.Loading", "Checking remote account..."), false);
            try
            {
                _githubSnapshot = await _gitHubCliService.ReadAccountAsync(_githubCancellation.Token);
                PopulateGitHubSnapshot(_githubSnapshot);
                SetStatus(_githubSnapshot.Status ?? UiLocalization.Text("GitRemoteAccountsForm.Status.Loaded", "Remote account status loaded."), false);
                FocusGitHubStatus();
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitRemoteAccountsForm.Status.Canceled", "Remote account check canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitRemoteAccountsForm.Status.Failed", "Remote account check failed."), true);
                _githubDetailsTextBox.Text = ex.Message;
            }
            finally
            {
                _githubCancellation.Dispose();
                _githubCancellation = null;
                SetGitHubBusy(false);
            }
        }

        private void PopulateGitHubSnapshot(GitHubCliAccountSnapshot snapshot)
        {
            _githubStatusListBox.BeginUpdate();
            _githubStatusListBox.Items.Clear();
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.Provider", "Provider"), "GitHub");
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.Primary", "Primary"), UiLocalization.Text("GitRemoteAccountsForm.Value.GhCli", "GitHub CLI"));
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.GhInstalled", "gh installed"), snapshot != null && snapshot.IsInstalled
                ? UiLocalization.Text("GitRemoteAccountsForm.Value.Yes", "Yes")
                : UiLocalization.Text("GitRemoteAccountsForm.Value.No", "No"));
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.Status", "Status"), snapshot != null ? snapshot.Status : string.Empty);
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.Login", "Login"), DisplayValue(snapshot != null ? snapshot.Login : null, "GitRemoteAccountsForm.Value.NotAvailable"));
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.Name", "Name"), DisplayValue(snapshot != null ? snapshot.DisplayName : null, "GitRemoteAccountsForm.Value.NotAvailable"));
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.Email", "Email"), DisplayValue(snapshot != null ? snapshot.Email : null, "GitRemoteAccountsForm.Value.NotAvailable"));
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.Profile", "Profile"), DisplayValue(snapshot != null ? snapshot.ProfileUrl : null, "GitRemoteAccountsForm.Value.NotAvailable"));
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.Protocol", "Protocol"), DisplayValue(snapshot != null ? snapshot.GitProtocol : null, "GitRemoteAccountsForm.Value.NotAvailable"));
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.Scopes", "Scopes"), DisplayValue(snapshot != null ? snapshot.TokenScopes : null, "GitRemoteAccountsForm.Value.NotAvailable"));
            AddGitHubItem(UiLocalization.Text("GitRemoteAccountsForm.Item.Version", "Version"), DisplayValue(snapshot != null ? snapshot.Version : null, "GitRemoteAccountsForm.Value.NotAvailable"));
            _githubStatusListBox.EndUpdate();
            if (_githubStatusListBox.Items.Count > 0)
            {
                _githubStatusListBox.SelectedIndex = 0;
            }

            _githubDetailsTextBox.Text = snapshot == null ? string.Empty : snapshot.Details ?? string.Empty;
        }

        private void AddGitHubItem(string name, string value)
        {
            _githubStatusListBox.Items.Add(string.Format(
                UiLocalization.Text("GitRemoteAccountsForm.Item.Template", "{0}: {1}"),
                name ?? string.Empty,
                value ?? string.Empty));
        }

        private void LoginWithGh()
        {
            try
            {
                _gitHubCliService.StartInteractiveLogin();
                SetStatus(UiLocalization.Text("GitRemoteAccountsForm.Status.GhLoginStarted", "GitHub CLI login started. Complete the login in the opened terminal, then refresh."), true);
            }
            catch (NotSupportedException ex)
            {
                SetStatus(ex.Message, true);
                MessageBox.Show(
                    this,
                    ex.Message,
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void OpenGhDownloadPage()
        {
            OpenUrl("https://cli.github.com/", UiLocalization.Text("GitRemoteAccountsForm.Status.DownloadPageOpened", "GitHub CLI download page opened."), _githubDetailsTextBox);
        }

        private void CopyGitHubSummary()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < _githubStatusListBox.Items.Count; i++)
            {
                builder.AppendLine(_githubStatusListBox.Items[i].ToString());
            }

            var text = builder.ToString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Clipboard.SetText(text);
            SetStatus(UiLocalization.Text("GitRemoteAccountsForm.Status.CopiedSummary", "Copied remote account summary."), true);
        }

        private async Task RefreshSshKeysAsync()
        {
            if (_sshCancellation != null)
            {
                return;
            }

            _sshCancellation = new CancellationTokenSource();
            SetSshBusy(true);
            if (_sshKeysListView.CanFocus)
            {
                _sshKeysListView.Focus();
            }

            FocusSshKeys();
            SetStatus(UiLocalization.Text("GitSshKeyForm.Status.Loading", "Checking SSH keys..."), false);
            var shouldFocusSshKeys = false;
            try
            {
                _sshSnapshot = await _sshKeyService.ReadKeysAsync(_sshCancellation.Token);
                PopulateSshKeys(_sshSnapshot);
                SetStatus(string.Format(
                    UiLocalization.Text("GitSshKeyForm.Status.Loaded.Template", "Loaded {0} SSH keys."),
                    _sshSnapshot.Keys == null ? 0 : _sshSnapshot.Keys.Count), false);
                shouldFocusSshKeys = true;
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitSshKeyForm.Status.Canceled", "SSH key check canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitSshKeyForm.Status.Failed", "SSH key check failed."), true);
                _sshDetailsTextBox.Text = ex.Message;
            }
            finally
            {
                _sshCancellation.Dispose();
                _sshCancellation = null;
                SetSshBusy(false);
                if (shouldFocusSshKeys)
                {
                    FocusSshKeys();
                }
            }
        }

        private void PopulateSshKeys(GitSshKeySnapshot snapshot)
        {
            _sshKeysListView.BeginUpdate();
            _sshKeysListView.Items.Clear();
            if (snapshot != null && snapshot.Keys != null)
            {
                foreach (var key in snapshot.Keys)
                {
                    _sshKeysListView.Items.Add(CreateSshKeyItem(key));
                }
            }

            _sshKeysListView.EndUpdate();
            if (_sshKeysListView.Items.Count > 0)
            {
                _sshKeysListView.Items[0].Selected = true;
                _sshKeysListView.Items[0].Focused = true;
            }

            _sshDetailsTextBox.Text = snapshot == null ? string.Empty : snapshot.Details ?? string.Empty;
            RefreshSshActionState();
        }

        private async Task TestGitHubSshAsync()
        {
            if (_sshCancellation != null)
            {
                return;
            }

            _sshCancellation = new CancellationTokenSource();
            SetSshBusy(true);
            SetStatus(UiLocalization.Text("GitSshKeyForm.Status.TestingGitHub", "Testing GitHub SSH connection..."), true);
            try
            {
                var result = await _sshKeyService.TestGitHubSshAsync(_sshCancellation.Token);
                _sshDetailsTextBox.Text = result.Details ?? result.Output ?? string.Empty;
                SetStatus(result.Succeeded
                    ? UiLocalization.Text("GitSshKeyForm.Status.GitHubTestSucceeded", "GitHub SSH authentication succeeded.")
                    : UiLocalization.Text("GitSshKeyForm.Status.GitHubTestFailed", "GitHub SSH authentication failed. See details."), true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitSshKeyForm.Status.GitHubTestCanceled", "GitHub SSH test canceled."), true);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitSshKeyForm.Status.GitHubTestFailed", "GitHub SSH authentication failed. See details."), true);
                _sshDetailsTextBox.Text = ex.ToString();
            }
            finally
            {
                _sshCancellation.Dispose();
                _sshCancellation = null;
                SetSshBusy(false);
            }
        }

        private void PopulateSelectedSshKeyDetails()
        {
            var key = GetSelectedSshKey();
            if (key == null)
            {
                _sshDetailsTextBox.Text = _sshSnapshot == null ? string.Empty : _sshSnapshot.Details ?? string.Empty;
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitSshKeyForm.Detail.Name.Template", "Name: {0}"), DisplayValue(key.Name, "GitSshKeyForm.Value.NotAvailable")));
            builder.AppendLine(string.Format(UiLocalization.Text("GitSshKeyForm.Detail.Type.Template", "Type: {0}"), DisplayValue(key.KeyType, "GitSshKeyForm.Value.NotAvailable")));
            builder.AppendLine(string.Format(UiLocalization.Text("GitSshKeyForm.Detail.Fingerprint.Template", "Fingerprint: {0}"), DisplayValue(key.Fingerprint, "GitSshKeyForm.Value.NotAvailable")));
            builder.AppendLine(string.Format(UiLocalization.Text("GitSshKeyForm.Detail.Comment.Template", "Comment: {0}"), DisplayValue(key.Comment, "GitSshKeyForm.Value.NotAvailable")));
            builder.AppendLine(string.Format(UiLocalization.Text("GitSshKeyForm.Detail.PublicPath.Template", "Public key path: {0}"), DisplayValue(key.PublicKeyPath, "GitSshKeyForm.Value.NotAvailable")));
            builder.AppendLine(string.Format(UiLocalization.Text("GitSshKeyForm.Detail.PrivateFound.Template", "Private key found: {0}"), key.HasPrivateKey
                ? UiLocalization.Text("GitSshKeyForm.Value.Yes", "Yes")
                : UiLocalization.Text("GitSshKeyForm.Value.No", "No")));
            _sshDetailsTextBox.Text = builder.ToString();
        }

        private void CopySelectedSshPublicKey()
        {
            var key = GetSelectedSshKey();
            if (key == null || string.IsNullOrWhiteSpace(key.PublicKeyText))
            {
                SetStatus(UiLocalization.Text("GitSshKeyForm.Status.NoPublicKey", "Select an SSH public key first."), true);
                FocusSshKeys();
                return;
            }

            Clipboard.SetText(key.PublicKeyText);
            SetStatus(UiLocalization.Text("GitSshKeyForm.Status.CopiedPublicKey", "Copied SSH public key."), true);
            FocusSshKeys();
        }

        private void CopySelectedSshPath()
        {
            var key = GetSelectedSshKey();
            if (key == null || string.IsNullOrWhiteSpace(key.PublicKeyPath))
            {
                return;
            }

            Clipboard.SetText(key.PublicKeyPath);
            SetStatus(UiLocalization.Text("GitSshKeyForm.Status.CopiedPath", "Copied SSH public key path."), true);
            FocusSshKeys();
        }

        private void OpenSshFolder()
        {
            var directory = _sshSnapshot == null ? null : _sshSnapshot.SshDirectory;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                SetStatus(UiLocalization.Text("GitSshKeyForm.Status.SshFolderMissing", "SSH directory was not found."), true);
                return;
            }

            OpenPath(directory, UiLocalization.Text("GitSshKeyForm.Status.OpenedSshFolder", "SSH directory opened."), _sshDetailsTextBox);
        }

        private void OpenGitHubSshSettings()
        {
            OpenUrl("https://github.com/settings/keys", UiLocalization.Text("GitSshKeyForm.Status.OpenedGitHubSettings", "GitHub SSH key settings opened."), _sshDetailsTextBox);
        }

        private void SetGitHubBusy(bool busy)
        {
            _githubRefreshButton.Enabled = !busy;
            _githubLoginButton.Enabled = !busy;
            _githubDownloadButton.Enabled = !busy;
            _githubCopySummaryButton.Enabled = !busy;
            _githubCopyDetailsButton.Enabled = !busy;
        }

        private void SetSshBusy(bool busy)
        {
            _sshRefreshButton.Enabled = !busy;
            _sshCopyPublicKeyButton.Enabled = !busy && GetSelectedSshKey() != null;
            _sshOpenFolderButton.Enabled = !busy;
            _sshOpenGitHubSettingsButton.Enabled = !busy;
            _sshTestGitHubButton.Enabled = !busy;
            _sshCopyDetailsButton.Enabled = !busy;
        }

        private void RefreshSshActionState()
        {
            var busy = _sshCancellation != null;
            _sshCopyPublicKeyButton.Enabled = !busy && GetSelectedSshKey() != null;
            _sshCopyDetailsButton.Enabled = !busy && !string.IsNullOrWhiteSpace(_sshDetailsTextBox.Text);
        }

        private void RefreshSshContextMenu()
        {
            var hasSelection = GetSelectedSshKey() != null;
            _sshCopyPublicKeyMenuItem.Enabled = hasSelection;
            _sshCopyPathMenuItem.Enabled = hasSelection;
        }

        private GitSshKeyEntry GetSelectedSshKey()
        {
            if (_sshKeysListView.SelectedItems.Count == 0)
            {
                return null;
            }

            return _sshKeysListView.SelectedItems[0].Tag as GitSshKeyEntry;
        }

        private void CopyText(TextBox textBox, string status)
        {
            if (textBox == null || string.IsNullOrWhiteSpace(textBox.Text))
            {
                return;
            }

            Clipboard.SetText(textBox.Text);
            SetStatus(status, true);
        }

        private void OpenUrl(string url, string status, TextBox detailsTextBox)
        {
            OpenPath(url, status, detailsTextBox);
        }

        private void OpenPath(string path, string status, TextBox detailsTextBox)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                SetStatus(status, true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                if (detailsTextBox != null)
                {
                    detailsTextBox.Text = ex.ToString();
                }
            }
        }

        private void FocusGitHubStatus()
        {
            if (_githubStatusListBox.Items.Count == 0)
            {
                return;
            }

            if (_githubStatusListBox.SelectedIndex < 0)
            {
                _githubStatusListBox.SelectedIndex = 0;
            }

            _githubStatusListBox.Focus();
        }

        private void FocusSshKeys()
        {
            if (_sshKeysListView.Items.Count > 0 && _sshKeysListView.SelectedItems.Count == 0)
            {
                _sshKeysListView.Items[0].Selected = true;
            }

            if (_sshKeysListView.SelectedItems.Count > 0)
            {
                _sshKeysListView.SelectedItems[0].Focused = true;
                _sshKeysListView.SelectedItems[0].EnsureVisible();
            }

            _sshKeysListView.Focus();
        }

        private void SetStatus(string text, bool speak)
        {
            _statusLabel.Text = text;
            if (speak && _screenReaderService != null)
            {
                _screenReaderService.Speak(text, true, false, true);
            }
        }

        private void GitRemoteCredentialsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_githubCancellation != null)
            {
                e.Cancel = true;
                _githubCancellation.Cancel();
                return;
            }

            if (_sshCancellation != null)
            {
                e.Cancel = true;
                _sshCancellation.Cancel();
            }
        }

        private static string DisplayValue(string value, string notAvailableKey)
        {
            return string.IsNullOrWhiteSpace(value)
                ? UiLocalization.Text(notAvailableKey, "Not available")
                : value;
        }

        private static ListViewItem CreateSshKeyItem(GitSshKeyEntry key)
        {
            var item = new ListViewItem(DisplayValue(key.Name, "GitSshKeyForm.Value.NotAvailable"));
            item.SubItems.Add(DisplayValue(key.KeyType, "GitSshKeyForm.Value.NotAvailable"));
            item.SubItems.Add(key.HasPrivateKey
                ? UiLocalization.Text("GitSshKeyForm.Value.Yes", "Yes")
                : UiLocalization.Text("GitSshKeyForm.Value.No", "No"));
            item.SubItems.Add(DisplayValue(key.Fingerprint, "GitSshKeyForm.Value.NotAvailable"));
            item.SubItems.Add(DisplayValue(key.Comment, "GitSshKeyForm.Value.NotAvailable"));
            item.Tag = key;
            return item;
        }

        private static TableLayoutPanel CreatePageLayout()
        {
            var page = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(8),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 52F));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 48F));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return page;
        }

        private static FlowLayoutPanel CreateActionPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = true,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
        }

        private static TabControl CreateTabs()
        {
            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                AccessibleName = string.Empty,
                TabIndex = 0
            };
            return tabs;
        }

        private static ListBox CreateListBox(string accessibleName, int tabIndex)
        {
            var listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
            UiTheme.ApplyListBox(listBox);
            return listBox;
        }

        private static ListView CreateSshKeysListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitSshKeyForm.Accessible.Keys", "SSH keys"),
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                TabIndex = 0
            };
            listView.Columns.Add(UiLocalization.Text("GitSshKeyForm.Column.Name", "Name"), 180);
            listView.Columns.Add(UiLocalization.Text("GitSshKeyForm.Column.Type", "Type"), 110);
            listView.Columns.Add(UiLocalization.Text("GitSshKeyForm.Column.PrivateKey", "Private"), 80);
            listView.Columns.Add(UiLocalization.Text("GitSshKeyForm.Column.Fingerprint", "Fingerprint"), 360);
            listView.Columns.Add(UiLocalization.Text("GitSshKeyForm.Column.Comment", "Comment"), 220);
            UiTheme.ApplyListView(listView);
            return listView;
        }

        private static TextBox CreateDetailsTextBox(string accessibleName, int tabIndex)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, true, true);
            return textBox;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex, int width)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = width,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 4, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }
    }
}
