using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Dialogs;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitCloneForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _cloneCancellation != null || _repositoriesCancellation != null;

        private readonly IGitCloneService _cloneService;
        private readonly IGitHubCliService _gitHubCliService;
        private readonly IScreenReaderService _screenReaderService;
        private readonly Func<string, Task> _openWorkspaceAsync;
        private readonly Func<string, Task> _openGitWorkbenchAsync;
        private readonly Func<string, Task<string>> _browseTargetParentDirectoryAsync;
        private readonly bool _usesRemoteTargetDirectory;
        private readonly TabControl _sourceTabControl;
        private readonly TabPage _manualTabPage;
        private readonly TabPage _githubTabPage;
        private readonly TextBox _repositoryUrlTextBox;
        private readonly TextBox _githubFilterTextBox;
        private readonly ListView _githubRepositoriesListView;
        private readonly TextBox _targetParentTextBox;
        private readonly TextBox _targetFolderTextBox;
        private readonly TextBox _outputTextBox;
        private readonly Label _statusLabel;
        private readonly CheckBox _recurseSubmodulesCheckBox;
        private readonly CheckBox _useSshCloneUrlCheckBox;
        private readonly Button _refreshRepositoriesButton;
        private readonly Button _browseButton;
        private readonly Button _cloneButton;
        private readonly Button _cancelCloneButton;
        private readonly Button _copyOutputButton;
        private readonly Button _openWorkspaceButton;
        private readonly Button _openGitWorkbenchButton;
        private readonly Button _closeButton;
        private CancellationTokenSource _cloneCancellation;
        private CancellationTokenSource _repositoriesCancellation;
        private IReadOnlyList<GitHubRepositoryEntry> _githubRepositories = new List<GitHubRepositoryEntry>();
        private string _lastClonedPath;
        private bool _targetFolderEditedByUser;
        private bool _isUpdatingTargetFolder;

        public GitCloneForm(
            IGitCloneService cloneService,
            IGitHubCliService gitHubCliService,
            IScreenReaderService screenReaderService,
            Func<string, Task> openWorkspaceAsync,
            Func<string, Task> openGitWorkbenchAsync,
            string initialTargetParentDirectory,
            bool usesRemoteTargetDirectory = false,
            Func<string, Task<string>> browseTargetParentDirectoryAsync = null)
        {
            _cloneService = cloneService ?? throw new ArgumentNullException(nameof(cloneService));
            _gitHubCliService = gitHubCliService ?? throw new ArgumentNullException(nameof(gitHubCliService));
            _screenReaderService = screenReaderService;
            _openWorkspaceAsync = openWorkspaceAsync;
            _openGitWorkbenchAsync = openGitWorkbenchAsync;
            _browseTargetParentDirectoryAsync = browseTargetParentDirectoryAsync;
            _usesRemoteTargetDirectory = usesRemoteTargetDirectory;

            Text = UiLocalization.Text("GitCloneForm.Title", "Clone Repository");
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            MinimumSize = new Size(760, 520);
            Size = new Size(900, 640);
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _manualTabPage = new TabPage(UiLocalization.Text("GitCloneForm.Tab.Manual", "Manual"));
            _githubTabPage = new TabPage(UiLocalization.Text("GitCloneForm.Tab.GitHub", "GitHub"));
            _sourceTabControl = CreateSourceTabControl();
            _repositoryUrlTextBox = CreateTextBox(UiLocalization.Text("GitCloneForm.Accessible.RepositoryUrl", "Repository URL"), 0);
            _githubFilterTextBox = CreateTextBox(UiLocalization.Text("GitCloneForm.Accessible.GitHubFilter", "GitHub filter"), 1);
            _githubRepositoriesListView = CreateGitHubRepositoriesListView();
            _targetParentTextBox = CreateTextBox(UiLocalization.Text("GitCloneForm.Accessible.TargetParent", "Target directory"), 5);
            _targetFolderTextBox = CreateTextBox(UiLocalization.Text("GitCloneForm.Accessible.TargetFolder", "Target folder"), 7);
            _outputTextBox = CreateOutputTextBox();
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitCloneForm.Status.Ready", "Ready to clone."),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, true);

            _recurseSubmodulesCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("GitCloneForm.Check.RecurseSubmodules", "Recurse submodules(&S)"),
                AccessibleName = UiLocalization.Text("GitCloneForm.Accessible.RecurseSubmodules", "Recurse submodules"),
                AutoSize = true,
                TabIndex = 8
            };
            UiTheme.ApplyCheckBox(_recurseSubmodulesCheckBox);

            _useSshCloneUrlCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("GitCloneForm.Check.UseSshCloneUrl", "Use SSH URL(&H)"),
                AccessibleName = UiLocalization.Text("GitCloneForm.Accessible.UseSshCloneUrl", "Use SSH URL"),
                AutoSize = true,
                TabIndex = 3
            };
            UiTheme.ApplyCheckBox(_useSshCloneUrlCheckBox);

            _refreshRepositoriesButton = CreateButton(UiLocalization.Text("GitCloneForm.Button.RefreshRepositories", "Load Repositories(&L)"), UiLocalization.Text("GitCloneForm.Accessible.RefreshRepositories", "Load repositories"), 2, 132);
            _browseButton = CreateButton(UiLocalization.Text("GitCloneForm.Button.Browse", "Browse(&B)..."), UiLocalization.Text("GitCloneForm.Accessible.Browse", "Browse"), 6, 112);
            _cloneButton = CreateButton(UiLocalization.Text("GitCloneForm.Button.Clone", "Clone(&C)"), UiLocalization.Text("GitCloneForm.Accessible.Clone", "Clone"), 10, 112);
            _cancelCloneButton = CreateButton(UiLocalization.Text("GitCloneForm.Button.CancelClone", "Cancel(&N)"), UiLocalization.Text("GitCloneForm.Accessible.CancelClone", "Cancel clone"), 11, 112);
            _copyOutputButton = CreateButton(UiLocalization.Text("GitCloneForm.Button.CopyOutput", "Copy Output(&O)"), UiLocalization.Text("GitCloneForm.Accessible.CopyOutput", "Copy output"), 12, 128);
            _openWorkspaceButton = CreateButton(UiLocalization.Text("GitCloneForm.Button.OpenWorkspace", "Open Workspace(&W)"), UiLocalization.Text("GitCloneForm.Accessible.OpenWorkspace", "Open workspace"), 13, 150);
            _openGitWorkbenchButton = CreateButton(UiLocalization.Text("GitCloneForm.Button.OpenGitWorkbench", "Open Git Workbench(&G)"), UiLocalization.Text("GitCloneForm.Accessible.OpenGitWorkbench", "Open Git Workbench"), 14, 170);
            _closeButton = CreateButton(UiLocalization.Text("GitCloneForm.Button.Close", "Close(&E)"), UiLocalization.Text("GitCloneForm.Accessible.Close", "Close"), 15, 112);
            FormInteractionConventions.ConfigureRefreshButton(_refreshRepositoriesButton, true);

            UiTheme.ApplyButton(_refreshRepositoriesButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_browseButton, UiGlyph.Workspace);
            UiTheme.ApplyButton(_cloneButton, UiGlyph.Download);
            UiTheme.ApplyButton(_cancelCloneButton, UiGlyph.Cancel);
            UiTheme.ApplyButton(_copyOutputButton, UiGlyph.CommandOutput);
            UiTheme.ApplyButton(_openWorkspaceButton, UiGlyph.NewSession);
            UiTheme.ApplyButton(_openGitWorkbenchButton, UiGlyph.Model);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _targetParentTextBox.Text = NormalizeInitialTargetParent(initialTargetParentDirectory, _usesRemoteTargetDirectory);
            if (_usesRemoteTargetDirectory)
            {
                _browseButton.Enabled = _browseTargetParentDirectoryAsync != null;
                _browseButton.TabStop = _browseTargetParentDirectoryAsync != null;
            }
            _cancelCloneButton.Enabled = false;
            _copyOutputButton.Enabled = false;
            _openWorkspaceButton.Enabled = false;
            _openGitWorkbenchButton.Enabled = false;

            _repositoryUrlTextBox.TextChanged += RepositoryUrlTextBox_TextChanged;
            _targetFolderTextBox.TextChanged += TargetFolderTextBox_TextChanged;
            _githubRepositoriesListView.DoubleClick += (sender, e) => ApplySelectedGitHubRepository();
            _githubRepositoriesListView.KeyDown += GitHubRepositoriesListView_KeyDown;
            _useSshCloneUrlCheckBox.CheckedChanged += (sender, e) => ApplySelectedGitHubRepository();
            _refreshRepositoriesButton.Click += async (sender, e) => await RefreshGitHubRepositoriesAsync();
            _browseButton.Click += async (sender, e) => await BrowseButton_ClickAsync();
            _cloneButton.Click += async (sender, e) => await StartCloneAsync();
            _cancelCloneButton.Click += (sender, e) => CancelClone();
            _copyOutputButton.Click += (sender, e) => CopyOutput();
            _openWorkspaceButton.Click += async (sender, e) => await OpenWorkspaceAsync();
            _openGitWorkbenchButton.Click += async (sender, e) => await OpenGitWorkbenchAsync();
            _closeButton.Click += (sender, e) => Close();
            FormClosing += GitCloneForm_FormClosing;
            KeyDown += GitCloneForm_KeyDown;
            Shown += (sender, e) =>
            {
                Activate();
                _repositoryUrlTextBox.Focus();
            };

            BuildLayout();
            AcceptButton = _cloneButton;
            CancelButton = _closeButton;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 224F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var heading = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitCloneForm.Heading", "Clone Repository"),
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(CreateSourceTabs(), 0, 1);
            root.Controls.Add(CreateTargetRow(), 0, 2);
            root.Controls.Add(_recurseSubmodulesCheckBox, 0, 3);
            root.Controls.Add(_outputTextBox, 0, 4);
            root.Controls.Add(_statusLabel, 0, 5);
            root.Controls.Add(CreateFooter(), 0, 6);
            Controls.Add(root);
        }

        private Control CreateSourceTabs()
        {
            _manualTabPage.Controls.Add(CreateRepositoryRow());
            _githubTabPage.Controls.Add(CreateGitHubRepositoriesRow());
            _sourceTabControl.TabPages.Add(_manualTabPage);
            _sourceTabControl.TabPages.Add(_githubTabPage);
            return _sourceTabControl;
        }

        private Control CreateRepositoryRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            row.Controls.Add(CreateLabel(UiLocalization.Text("GitCloneForm.Label.RepositoryUrl", "Git URL")), 0, 0);
            row.Controls.Add(_repositoryUrlTextBox, 0, 1);
            return row;
        }

        private Control CreateGitHubRepositoriesRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 3,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            row.Controls.Add(CreateLabel(UiLocalization.Text("GitCloneForm.Label.GitHubRepositories", "GitHub repositories")), 0, 0);
            row.Controls.Add(_githubFilterTextBox, 0, 1);
            row.Controls.Add(_refreshRepositoriesButton, 1, 1);
            row.Controls.Add(_useSshCloneUrlCheckBox, 2, 1);
            row.SetColumnSpan(_githubRepositoriesListView, 3);
            row.Controls.Add(_githubRepositoriesListView, 0, 2);
            return row;
        }

        private Control CreateTargetRow()
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            row.Controls.Add(CreateLabel(UiLocalization.Text("GitCloneForm.Label.TargetParent", "Target directory")), 0, 0);
            row.Controls.Add(CreateLabel(UiLocalization.Text("GitCloneForm.Label.TargetFolder", "Target folder")), 2, 0);
            row.Controls.Add(_targetParentTextBox, 0, 1);
            row.Controls.Add(_browseButton, 1, 1);
            row.Controls.Add(_targetFolderTextBox, 2, 1);
            return row;
        }

        private Control CreateFooter()
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = true,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_openGitWorkbenchButton);
            footer.Controls.Add(_openWorkspaceButton);
            footer.Controls.Add(_copyOutputButton);
            footer.Controls.Add(_cancelCloneButton);
            footer.Controls.Add(_cloneButton);
            return footer;
        }

        private async void GitCloneForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F5 || !IsGitHubSourceSelected() || _repositoriesCancellation != null)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            FormInteractionConventions.SpeakRefresh(_screenReaderService);
            await RefreshGitHubRepositoriesAsync();
        }

        private async Task RefreshGitHubRepositoriesAsync()
        {
            if (_repositoriesCancellation != null)
            {
                return;
            }

            _repositoriesCancellation = new CancellationTokenSource();
            SetRepositoryLoading(true);
            if (_githubRepositoriesListView.CanFocus)
            {
                _githubRepositoriesListView.Focus();
            }

            RestoreGitHubRepositoryFocus();
            SetStatus(UiLocalization.Text("GitCloneForm.Status.LoadingRepositories", "Loading GitHub repositories..."), false);
            var shouldFocusRepositories = false;
            try
            {
                _githubRepositories = await _gitHubCliService.ListRepositoriesAsync(
                    _githubFilterTextBox.Text,
                    100,
                    _repositoriesCancellation.Token);
                PopulateGitHubRepositories(_githubRepositories);
                SetStatus(string.Format(
                    UiLocalization.Text("GitCloneForm.Status.RepositoriesLoaded.Template", "Loaded {0} GitHub repositories."),
                    _githubRepositories.Count), false);
                shouldFocusRepositories = true;
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitCloneForm.Status.RepositoriesCanceled", "GitHub repository loading canceled."), false);
            }
            catch (Exception ex)
            {
                _githubRepositories = new List<GitHubRepositoryEntry>();
                _githubRepositoriesListView.Items.Clear();
                AppendOutput(ex.Message);
                _copyOutputButton.Enabled = true;
                SetStatus(UiLocalization.Text("GitCloneForm.Status.RepositoriesFailed", "Failed to load GitHub repositories. Check GitHub CLI login in remote accounts."), true);
            }
            finally
            {
                if (_repositoriesCancellation != null)
                {
                    _repositoriesCancellation.Dispose();
                    _repositoriesCancellation = null;
                }

                SetRepositoryLoading(false);
                if (shouldFocusRepositories)
                {
                    RestoreGitHubRepositoryFocus();
                }
            }
        }

        private void PopulateGitHubRepositories(IReadOnlyList<GitHubRepositoryEntry> repositories)
        {
            _githubRepositoriesListView.BeginUpdate();
            _githubRepositoriesListView.Items.Clear();
            if (repositories != null)
            {
                foreach (var repository in repositories)
                {
                    _githubRepositoriesListView.Items.Add(CreateGitHubRepositoryItem(repository));
                }
            }

            _githubRepositoriesListView.EndUpdate();
            if (_githubRepositoriesListView.Items.Count > 0)
            {
                _githubRepositoriesListView.Items[0].Selected = true;
                _githubRepositoriesListView.Items[0].Focused = true;
            }
        }

        private void RestoreGitHubRepositoryFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                if (_githubRepositoriesListView.Items.Count > 0)
                {
                    if (_githubRepositoriesListView.SelectedItems.Count == 0)
                    {
                        _githubRepositoriesListView.Items[0].Selected = true;
                    }

                    _githubRepositoriesListView.SelectedItems[0].Focused = true;
                    _githubRepositoriesListView.SelectedItems[0].EnsureVisible();
                }

                if (_githubRepositoriesListView.Enabled && _githubRepositoriesListView.CanFocus)
                {
                    _githubRepositoriesListView.Focus();
                }
            });
        }

        private void ApplySelectedGitHubRepository()
        {
            var repository = GetSelectedGitHubRepository();
            if (repository == null)
            {
                return;
            }

            var url = _useSshCloneUrlCheckBox.Checked ? repository.SshUrl : repository.HttpsUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                url = repository.HttpsUrl ?? repository.SshUrl;
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            _repositoryUrlTextBox.Text = url;
            _targetFolderEditedByUser = false;
            RepositoryUrlTextBox_TextChanged(_repositoryUrlTextBox, EventArgs.Empty);
            SetStatus(string.Format(
                UiLocalization.Text("GitCloneForm.Status.RepositorySelected.Template", "Selected repository: {0}"),
                GetRepositoryDisplayName(repository)), true);
            _cloneButton.Focus();
        }

        private GitHubRepositoryEntry GetSelectedGitHubRepository()
        {
            var index = _githubRepositoriesListView.SelectedIndices.Count > 0
                ? _githubRepositoriesListView.SelectedIndices[0]
                : -1;
            return _githubRepositories != null && index >= 0 && index < _githubRepositories.Count
                ? _githubRepositories[index]
                : null;
        }

        private void GitHubRepositoriesListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            ApplySelectedGitHubRepository();
        }

        private async Task StartCloneAsync()
        {
            if (_cloneCancellation != null)
            {
                return;
            }

            var githubRepository = IsGitHubSourceSelected() ? GetSelectedGitHubRepository() : null;
            if (IsGitHubSourceSelected() && githubRepository == null)
            {
                SetStatus(UiLocalization.Text("GitCloneForm.Status.GitHubRepositoryRequired", "Select a GitHub repository first."), true);
                _githubRepositoriesListView.Focus();
                return;
            }

            if (IsGitHubSourceSelected() && string.IsNullOrWhiteSpace(_targetFolderTextBox.Text))
            {
                _isUpdatingTargetFolder = true;
                _targetFolderTextBox.Text = GetRepositoryDisplayName(githubRepository);
                _isUpdatingTargetFolder = false;
            }

            SetCloneRunning(true);
            _lastClonedPath = null;
            _outputTextBox.Clear();
            AppendOutput(string.Format(
                UiLocalization.Text("GitCloneForm.Output.Starting.Template", "Starting clone: {0}"),
                IsGitHubSourceSelected()
                    ? GetRepositoryDisplayName(githubRepository)
                    : GitOutputSanitizer.RedactSensitiveText(_repositoryUrlTextBox.Text)));
            SetStatus(UiLocalization.Text("GitCloneForm.Status.Cloning", "Cloning repository..."), true);
            _cloneCancellation = new CancellationTokenSource();

            try
            {
                var progress = new Progress<GitCloneProgress>(OnCloneProgress);
                var result = IsGitHubSourceSelected()
                    ? await _gitHubCliService.CloneRepositoryAsync(
                        githubRepository,
                        _targetParentTextBox.Text,
                        _targetFolderTextBox.Text,
                        _recurseSubmodulesCheckBox.Checked,
                        _useSshCloneUrlCheckBox.Checked,
                        progress,
                        _cloneCancellation.Token)
                    : await _cloneService.CloneAsync(new GitCloneRequest
                    {
                        RepositoryUrl = _repositoryUrlTextBox.Text,
                        TargetParentDirectory = _targetParentTextBox.Text,
                        TargetFolderName = _targetFolderTextBox.Text,
                        RecurseSubmodules = _recurseSubmodulesCheckBox.Checked
                    }, progress, _cloneCancellation.Token);
                _copyOutputButton.Enabled = !string.IsNullOrWhiteSpace(_outputTextBox.Text);
                if (result.Success)
                {
                    _lastClonedPath = result.TargetPath;
                    _openWorkspaceButton.Enabled = true;
                    _openGitWorkbenchButton.Enabled = true;
                    SetStatus(
                        string.Format(UiLocalization.Text("GitCloneForm.Status.CloneSucceeded.Template", "Clone completed: {0}"), result.TargetPath),
                        true);
                }
                else
                {
                    SetStatus(
                        string.Format(UiLocalization.Text("GitCloneForm.Status.CloneFailed.Template", "Clone failed. Exit code: {0}"), result.ExitCode),
                        true);
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitCloneForm.Status.CloneCanceled", "Clone canceled."), true);
            }
            catch (Exception ex)
            {
                AppendOutput(ex.Message);
                _copyOutputButton.Enabled = true;
                SetStatus(UiLocalization.Text("GitCloneForm.Status.CloneFailed", "Clone failed."), true);
                MessageBox.Show(
                    this,
                    ex.Message,
                    UiLocalization.Text("GitCloneForm.DialogTitle.CloneFailed", "Clone failed"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                if (IsGitHubSourceSelected())
                {
                    _githubRepositoriesListView.Focus();
                }
                else
                {
                    _repositoryUrlTextBox.Focus();
                }
            }
            finally
            {
                if (_cloneCancellation != null)
                {
                    _cloneCancellation.Dispose();
                    _cloneCancellation = null;
                }

                SetCloneRunning(false);
            }
        }

        private async Task OpenWorkspaceAsync()
        {
            if (string.IsNullOrWhiteSpace(_lastClonedPath) || _openWorkspaceAsync == null)
            {
                return;
            }

            await _openWorkspaceAsync(_lastClonedPath);
        }

        private async Task OpenGitWorkbenchAsync()
        {
            if (string.IsNullOrWhiteSpace(_lastClonedPath) || _openGitWorkbenchAsync == null)
            {
                return;
            }

            await _openGitWorkbenchAsync(_lastClonedPath);
        }

        private void OnCloneProgress(GitCloneProgress progress)
        {
            if (progress == null || string.IsNullOrWhiteSpace(progress.Text))
            {
                return;
            }

            AppendOutput(progress.Text);
        }

        private void AppendOutput(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _outputTextBox.AppendText(text);
            _outputTextBox.AppendText(Environment.NewLine);
        }

        private void SetCloneRunning(bool running)
        {
            _sourceTabControl.Enabled = !running;
            _repositoryUrlTextBox.Enabled = !running;
            _githubFilterTextBox.Enabled = !running;
            _githubRepositoriesListView.Enabled = !running;
            _refreshRepositoriesButton.Enabled = !running && _repositoriesCancellation == null;
            _useSshCloneUrlCheckBox.Enabled = !running;
            _targetParentTextBox.Enabled = !running;
            _targetFolderTextBox.Enabled = !running;
            _recurseSubmodulesCheckBox.Enabled = !running;
            _browseButton.Enabled = !running;
            _cloneButton.Enabled = !running;
            _cancelCloneButton.Enabled = running;
            _closeButton.Enabled = !running;
            if (running)
            {
                _openWorkspaceButton.Enabled = false;
                _openGitWorkbenchButton.Enabled = false;
            }
        }

        private void SetRepositoryLoading(bool loading)
        {
            _refreshRepositoriesButton.Enabled = !loading && _cloneCancellation == null;
            _cloneButton.Enabled = !loading && _cloneCancellation == null;
            _githubFilterTextBox.Enabled = !loading && _cloneCancellation == null;
            _useSshCloneUrlCheckBox.Enabled = !loading && _cloneCancellation == null;
        }

        private bool IsGitHubSourceSelected()
        {
            return ReferenceEquals(_sourceTabControl.SelectedTab, _githubTabPage);
        }

        private void SetStatus(string text, bool speak)
        {
            _statusLabel.Text = text;
            if (speak && _screenReaderService != null)
            {
                _screenReaderService.Speak(text, true, false, true);
            }
        }

        private static ListViewItem CreateGitHubRepositoryItem(GitHubRepositoryEntry repository)
        {
            var item = new ListViewItem(GetRepositoryDisplayName(repository));
            item.SubItems.Add(DisplayRepositoryVisibility(repository));
            item.SubItems.Add(string.IsNullOrWhiteSpace(repository != null ? repository.DefaultBranch : null)
                ? UiLocalization.Text("GitCloneForm.Value.NotAvailable", "Not available")
                : repository.DefaultBranch);
            item.SubItems.Add(repository != null && repository.UpdatedAt.HasValue
                ? repository.UpdatedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : UiLocalization.Text("GitCloneForm.Value.NotAvailable", "Not available"));
            item.ToolTipText = string.Format(
                UiLocalization.Text("GitCloneForm.GitHubRepository.Template", "{0}; {1}; default: {2}; updated: {3}"),
                GetRepositoryDisplayName(repository),
                DisplayRepositoryVisibility(repository),
                string.IsNullOrWhiteSpace(repository != null ? repository.DefaultBranch : null)
                    ? UiLocalization.Text("GitCloneForm.Value.NotAvailable", "Not available")
                    : repository.DefaultBranch,
                repository != null && repository.UpdatedAt.HasValue
                    ? repository.UpdatedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                    : UiLocalization.Text("GitCloneForm.Value.NotAvailable", "Not available"));
            return item;
        }

        private static string DisplayRepositoryVisibility(GitHubRepositoryEntry repository)
        {
            if (repository == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(repository.Visibility))
            {
                if (string.Equals(repository.Visibility, "PRIVATE", StringComparison.OrdinalIgnoreCase))
                {
                    return UiLocalization.Text("GitCloneForm.Value.Private", "Private");
                }

                if (string.Equals(repository.Visibility, "PUBLIC", StringComparison.OrdinalIgnoreCase))
                {
                    return UiLocalization.Text("GitCloneForm.Value.Public", "Public");
                }

                return repository.Visibility;
            }

            return repository.IsPrivate
                ? UiLocalization.Text("GitCloneForm.Value.Private", "Private")
                : UiLocalization.Text("GitCloneForm.Value.Public", "Public");
        }

        private static string GetRepositoryDisplayName(GitHubRepositoryEntry repository)
        {
            if (repository == null || string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                return string.Empty;
            }

            var value = repository.NameWithOwner.Trim();
            var slashIndex = value.LastIndexOf('/');
            return slashIndex >= 0 && slashIndex < value.Length - 1
                ? value.Substring(slashIndex + 1)
                : value;
        }

        private async Task BrowseButton_ClickAsync()
        {
            if (_usesRemoteTargetDirectory)
            {
                if (_browseTargetParentDirectoryAsync == null)
                {
                    return;
                }

                var selectedPath = await _browseTargetParentDirectoryAsync(_targetParentTextBox.Text);
                if (!string.IsNullOrWhiteSpace(selectedPath))
                {
                    _targetParentTextBox.Text = selectedPath;
                    _targetFolderTextBox.Focus();
                }

                return;
            }

            string selectedTargetParentPath;
            if (WindowsFolderPicker.TryPickFolder(
                this,
                UiLocalization.Text("GitCloneForm.DialogDescription.SelectTargetParent", "Select the directory that will contain the cloned repository"),
                Directory.Exists(_targetParentTextBox.Text)
                    ? _targetParentTextBox.Text
                    : NormalizeInitialTargetParent(null, false),
                out selectedTargetParentPath))
            {
                _targetParentTextBox.Text = selectedTargetParentPath;
                _targetFolderTextBox.Focus();
            }
        }

        private void RepositoryUrlTextBox_TextChanged(object sender, EventArgs e)
        {
            if (_targetFolderEditedByUser)
            {
                return;
            }

            var folderName = GitClonePathPlanner.InferTargetFolderName(_repositoryUrlTextBox.Text);
            _isUpdatingTargetFolder = true;
            _targetFolderTextBox.Text = folderName;
            _isUpdatingTargetFolder = false;
        }

        private void TargetFolderTextBox_TextChanged(object sender, EventArgs e)
        {
            if (!_isUpdatingTargetFolder)
            {
                _targetFolderEditedByUser = true;
            }
        }

        private void CancelClone()
        {
            if (_cloneCancellation == null)
            {
                return;
            }

            _cloneCancellation.Cancel();
            SetStatus(UiLocalization.Text("GitCloneForm.Status.Canceling", "Canceling clone..."), true);
        }

        private void CopyOutput()
        {
            if (string.IsNullOrWhiteSpace(_outputTextBox.Text))
            {
                return;
            }

            Clipboard.SetText(_outputTextBox.Text);
            SetStatus(UiLocalization.Text("GitCloneForm.Status.CopiedOutput", "Copied clone output."), true);
        }

        private void GitCloneForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_cloneCancellation != null)
            {
                e.Cancel = true;
                CancelClone();
                return;
            }

            if (_repositoriesCancellation != null)
            {
                e.Cancel = true;
                _repositoriesCancellation.Cancel();
                SetStatus(UiLocalization.Text("GitCloneForm.Status.RepositoriesCanceled", "GitHub repository loading canceled."), false);
            }
        }

        private static TextBox CreateTextBox(string accessibleName, int tabIndex)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, false, false);
            return textBox;
        }

        private static TabControl CreateSourceTabControl()
        {
            var tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                AccessibleName = string.Empty,
                TabIndex = 0
            };
            return tabControl;
        }

        private static TextBox CreateOutputTextBox()
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitCloneForm.Accessible.Output", "Output"),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                TabIndex = 9
            };
            UiTheme.ApplyTextBox(textBox, true, true);
            return textBox;
        }

        private static ListView CreateGitHubRepositoriesListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitCloneForm.Accessible.GitHubRepositories", "GitHub repositories"),
                TabIndex = 4,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            listView.Columns.Add(UiLocalization.Text("GitCloneForm.Column.Repository", "Repository"), 240);
            listView.Columns.Add(UiLocalization.Text("GitCloneForm.Column.Visibility", "Visibility"), 90);
            listView.Columns.Add(UiLocalization.Text("GitCloneForm.Column.DefaultBranch", "Default branch"), 120);
            listView.Columns.Add(UiLocalization.Text("GitCloneForm.Column.UpdatedAt", "Updated"), 150);
            UiTheme.ApplyListView(listView);
            return listView;
        }

        private static Label CreateLabel(string text)
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
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

        private static string NormalizeInitialTargetParent(string value, bool usesRemoteTargetDirectory)
        {
            if (usesRemoteTargetDirectory)
            {
                return string.IsNullOrWhiteSpace(value) ? "~" : value.Trim();
            }

            if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            {
                return value;
            }

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(documents) ? Environment.CurrentDirectory : documents;
        }
    }
}
