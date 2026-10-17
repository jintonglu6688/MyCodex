using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitRemoteRepositoriesForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _providerStatesByTab.Values.Any(state => state.RefreshCancellation != null);

        private enum RepositorySortField
        {
            Repository = 0,
            Description,
            Language,
            DefaultBranch,
            UpdatedAt,
            PushedAt,
            Stars,
            Forks,
            Watchers,
            DiskUsage,
            License,
            Archived,
            Fork
        }

        private enum RepositoryVisibilityFilter
        {
            All = 0,
            Public,
            Private
        }

        private sealed class ProviderRepositoryState
        {
            public ProviderRepositoryState(IGitHostingProvider provider)
            {
                Provider = provider ?? throw new ArgumentNullException(nameof(provider));
                Repositories = new List<GitHubRepositoryEntry>();
                FilterKeyword = string.Empty;
                VisibilityFilter = RepositoryVisibilityFilter.All;
                SortField = RepositorySortField.UpdatedAt;
                SortDescending = true;
                ShowOnlyForks = false;
                ShowOnlyArchived = false;
                Id = string.IsNullOrWhiteSpace(provider.Id) ? string.Empty : provider.Id.Trim();
                DisplayName = string.IsNullOrWhiteSpace(provider.DisplayName) ? Id : provider.DisplayName;
            }

            public string Id { get; }

            public string DisplayName { get; }

            public IGitHostingProvider Provider { get; }

            public TabPage TabPage { get; set; }

            public IReadOnlyList<GitHubRepositoryEntry> Repositories { get; set; }

            public RepositoryVisibilityFilter VisibilityFilter { get; set; }

            public bool ShowOnlyForks { get; set; }

            public bool ShowOnlyArchived { get; set; }

            public string FilterKeyword { get; set; }

            public RepositorySortField SortField { get; set; }

            public bool SortDescending { get; set; }

            public CancellationTokenSource RefreshCancellation { get; set; }

            public bool HasLoaded { get; set; }
        }

        private readonly IScreenReaderService _screenReaderService;
        private readonly string _currentWorkspacePath;
        private readonly IGitRepositoryService _gitRepositoryService;
        private readonly IReadOnlyList<IGitHostingProvider> _providers;
        private readonly Dictionary<TabPage, ProviderRepositoryState> _providerStatesByTab;
        private readonly TabControl _platformTabControl;
        private readonly Control _providerContent;
        private ProviderRepositoryState _activeProviderState;
        private bool _isSwitchingProviderState;
        private readonly IGitHostingProviderService _gitHostingProviderService;
        private readonly TextBox _githubFilterTextBox;
        private readonly Button _refreshButton;
        private readonly Button _createRepositoryButton;
        private readonly Button _closeButton;
        private readonly Label _statusLabel;
        private readonly ListView _repositoryListView;
        private readonly ContextMenu _repositoryContextMenu;
        private readonly MenuItem _viewDetailsMenuItem;
        private readonly MenuItem _openRepositoryMenuItem;
        private readonly MenuItem _openReleasesMenuItem;
        private readonly MenuItem _openContributorsMenuItem;
        private readonly MenuItem _renameRepositoryMenuItem;
        private readonly MenuItem _editRepositoryMetadataMenuItem;
        private readonly MenuItem _setDefaultBranchMenuItem;
        private readonly MenuItem _changeVisibilityMenuItem;
        private readonly MenuItem _archiveRepositoryMenuItem;
        private readonly MenuItem _unarchiveRepositoryMenuItem;
        private readonly MenuItem _deleteRepositoryMenuItem;
        private readonly MenuItem _copyHttpsUrlMenuItem;
        private readonly MenuItem _copySshUrlMenuItem;
        private readonly MenuItem _copyFullNameMenuItem;
        private readonly ComboBox _visibilityFilterComboBox;
        private readonly CheckBox _showOnlyForksCheckBox;
        private readonly CheckBox _showOnlyArchivedCheckBox;
        public GitRemoteRepositoriesForm(
            IGitHostingProviderService gitHostingProviderService,
            IScreenReaderService screenReaderService,
            string currentWorkspacePath = null,
            IGitRepositoryService gitRepositoryService = null)
        {
            if (gitHostingProviderService == null)
            {
                throw new ArgumentNullException(nameof(gitHostingProviderService));
            }

            _screenReaderService = screenReaderService;
            _currentWorkspacePath = string.IsNullOrWhiteSpace(currentWorkspacePath) ? null : currentWorkspacePath.Trim();
            _gitRepositoryService = gitRepositoryService;
            _gitHostingProviderService = gitHostingProviderService;
            _providers = _gitHostingProviderService.GetProviders() ?? new List<IGitHostingProvider>();
            if (_providers.Count == 0)
            {
                _providers = new[] { _gitHostingProviderService.DefaultProvider };
            }

            _providerStatesByTab = new Dictionary<TabPage, ProviderRepositoryState>();

            Text = UiLocalization.Text("GitRemoteRepositoriesForm.Title", "Remote Repositories");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(920, 620);
            Size = new Size(1060, 700);
            ShowInTaskbar = true;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _platformTabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                AccessibleName = string.Empty,
                TabIndex = 0
            };
            _platformTabControl.SelectedIndexChanged += (sender, e) => ActivateCurrentProviderStateFromTab();
            _githubFilterTextBox = CreateTextBox(UiLocalization.Text("GitRemoteRepositoriesForm.Accessible.Filter", "Filter"), 3);
            _refreshButton = CreateButton(UiLocalization.Text("GitRemoteRepositoriesForm.Button.Refresh", "Refresh(&R)"), UiLocalization.Text("GitRemoteRepositoriesForm.Accessible.Refresh", "Refresh"), 2, 112);
            _createRepositoryButton = CreateButton(UiLocalization.Text("GitRemoteRepositoriesForm.Button.CreateRepository", "Create Repository(&C)"), UiLocalization.Text("GitRemoteRepositoriesForm.Accessible.CreateRepository", "Create repository"), 1, 160);
            _closeButton = CreateButton(UiLocalization.Text("GitRemoteRepositoriesForm.Button.Close", "Close"), UiLocalization.Text("GitRemoteRepositoriesForm.Accessible.Close", "Close"), 7, 112);
            _visibilityFilterComboBox = CreateFilterComboBox();
            _visibilityFilterComboBox.Items.Add(UiLocalization.Text("GitRemoteRepositoriesForm.Filter.All", "All"));
            _visibilityFilterComboBox.Items.Add(UiLocalization.Text("GitRemoteRepositoriesForm.Filter.Visibility.Public", "Public"));
            _visibilityFilterComboBox.Items.Add(UiLocalization.Text("GitRemoteRepositoriesForm.Filter.Visibility.Private", "Private"));
            _visibilityFilterComboBox.SelectedIndex = 0;
            _showOnlyForksCheckBox = CreateFilterCheckBox(
                UiLocalization.Text("GitRemoteRepositoriesForm.CheckBox.OnlyForks", "Fork(&F)"),
                UiLocalization.Text("GitRemoteRepositoriesForm.Accessible.OnlyForks", "Forked repositories"),
                5);
            _showOnlyArchivedCheckBox = CreateFilterCheckBox(
                UiLocalization.Text("GitRemoteRepositoriesForm.CheckBox.OnlyArchived", "Archived(&A)"),
                UiLocalization.Text("GitRemoteRepositoriesForm.Accessible.OnlyArchived", "Archived repositories"),
                6);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitRemoteRepositoriesForm.Status.Ready", "Remote repositories are not loaded."),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, true);
            _repositoryListView = CreateRepositoryListView();
            _repositoryContextMenu = new ContextMenu();
            _viewDetailsMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.ViewDetails", "View Details(&D)"));
            _openRepositoryMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.OpenRepository", "Open Repository(&O)"));
            _openReleasesMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.OpenReleases", "Open Releases(&R)"));
            _openContributorsMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.OpenContributors", "Open Contributors(&T)"));
            _renameRepositoryMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.RenameRepository", "Rename Repository(&M)"));
            _editRepositoryMetadataMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.EditRepositoryMetadata", "Edit Repository Info(&E)"));
            _setDefaultBranchMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.SetDefaultBranch", "Set Default Branch(&B)"));
            _changeVisibilityMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.ChangeVisibility", "Change Visibility(&V)"));
            _archiveRepositoryMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.ArchiveRepository", "Archive Repository(&A)"));
            _unarchiveRepositoryMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.UnarchiveRepository", "Unarchive Repository(&U)"));
            _deleteRepositoryMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.DeleteRepository", "Delete Repository(&D)"));
            _copyHttpsUrlMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.CopyHttpsUrl", "Copy HTTPS URL(&H)"));
            _copySshUrlMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.CopySshUrl", "Copy SSH URL(&S)"));
            _copyFullNameMenuItem = new MenuItem(UiLocalization.Text("GitRemoteRepositoriesForm.Menu.CopyFullName", "Copy Full Name(&N)"));
            _repositoryListView.ContextMenu = _repositoryContextMenu;
            _providerContent = BuildProviderContent();

            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_createRepositoryButton, UiGlyph.NewSession);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _refreshButton.Click += async (sender, e) => await RefreshRepositoriesAsync(_activeProviderState, false);
            _createRepositoryButton.Click += async (sender, e) => await CreateRepositoryAsync(_activeProviderState);
            _viewDetailsMenuItem.Click += (sender, e) => ShowSelectedRepositoryDetails(_activeProviderState);
            _openRepositoryMenuItem.Click += (sender, e) => OpenSelectedRepositoryPage(
                _activeProviderState,
                null,
                UiLocalization.Text("GitRemoteRepositoriesForm.Status.OpenedRepository", "Repository page opened."));
            _openReleasesMenuItem.Click += (sender, e) => OpenSelectedRepositoryPage(
                _activeProviderState,
                "releases",
                UiLocalization.Text("GitRemoteRepositoriesForm.Status.OpenedReleases", "Repository releases page opened."));
            _openContributorsMenuItem.Click += (sender, e) => OpenSelectedRepositoryPage(
                _activeProviderState,
                "graphs/contributors",
                UiLocalization.Text("GitRemoteRepositoriesForm.Status.OpenedContributors", "Repository contributors page opened."));
            _renameRepositoryMenuItem.Click += async (sender, e) => await RenameSelectedRepositoryAsync(_activeProviderState);
            _editRepositoryMetadataMenuItem.Click += async (sender, e) => await EditSelectedRepositoryMetadataAsync(_activeProviderState);
            _setDefaultBranchMenuItem.Click += async (sender, e) => await SetDefaultBranchForSelectedRepositoryAsync(_activeProviderState);
            _changeVisibilityMenuItem.Click += async (sender, e) => await ChangeVisibilityForSelectedRepositoryAsync(_activeProviderState);
            _archiveRepositoryMenuItem.Click += async (sender, e) => await ArchiveSelectedRepositoryAsync(_activeProviderState);
            _unarchiveRepositoryMenuItem.Click += async (sender, e) => await UnarchiveSelectedRepositoryAsync(_activeProviderState);
            _deleteRepositoryMenuItem.Click += async (sender, e) => await DeleteSelectedRepositoryAsync(_activeProviderState);
            _copyHttpsUrlMenuItem.Click += (sender, e) => CopySelectedRepositoryValue(
                _activeProviderState,
                repository => repository.HttpsUrl,
                UiLocalization.Text("GitRemoteRepositoriesForm.Status.CopiedHttpsUrl", "Copied HTTPS URL."));
            _copySshUrlMenuItem.Click += (sender, e) => CopySelectedRepositoryValue(
                _activeProviderState,
                repository => repository.SshUrl,
                UiLocalization.Text("GitRemoteRepositoriesForm.Status.CopiedSshUrl", "Copied SSH URL."));
            _copyFullNameMenuItem.Click += (sender, e) => CopySelectedRepositoryValue(
                _activeProviderState,
                repository => repository.NameWithOwner,
                UiLocalization.Text("GitRemoteRepositoriesForm.Status.CopiedFullName", "Copied repository full name."));
            _repositoryContextMenu.Popup += (sender, e) => RefreshRepositoryContextMenu(_activeProviderState);
            _repositoryListView.KeyDown += RepositoryListView_KeyDown;
            _repositoryListView.ColumnClick += RepositoryListView_ColumnClick;
            _closeButton.Click += (sender, e) => Close();
            _githubFilterTextBox.KeyDown += async (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    if (_activeProviderState == null)
                    {
                        return;
                    }

                    await RefreshRepositoriesAsync(_activeProviderState, false);
                }
            };
            _githubFilterTextBox.TextChanged += (sender, e) => { if (!_isSwitchingProviderState) UpdateActiveProviderUiStateAndRefresh(); };
            _visibilityFilterComboBox.SelectedIndexChanged += (sender, e) => { if (!_isSwitchingProviderState) UpdateActiveProviderUiStateAndRefresh(); };
            _showOnlyForksCheckBox.CheckedChanged += (sender, e) => { if (!_isSwitchingProviderState) UpdateActiveProviderUiStateAndRefresh(); };
            _showOnlyArchivedCheckBox.CheckedChanged += (sender, e) => { if (!_isSwitchingProviderState) UpdateActiveProviderUiStateAndRefresh(); };
            _repositoryListView.SelectedIndexChanged += (sender, e) =>
            {
                RefreshActionButtons(_activeProviderState);
            };
            KeyDown += GitRemoteRepositoriesForm_KeyDown;
            FormClosing += GitRemoteRepositoriesForm_FormClosing;
            Shown += async (sender, e) =>
            {
                if (_activeProviderState == null)
                {
                    return;
                }

                if (!_activeProviderState.HasLoaded)
                {
                    await RefreshRepositoriesAsync(_activeProviderState, false);
                }
                else
                {
                    PopulateRepositories(_activeProviderState, ApplyFilterAndSortRepositories(_activeProviderState));
                }
            };

            foreach (var provider in _providers)
            {
                var state = new ProviderRepositoryState(provider)
                {
                    TabPage = BuildTabPage(provider.DisplayName)
                };

                if (string.IsNullOrWhiteSpace(state.Id))
                {
                    continue;
                }

                _providerStatesByTab[state.TabPage] = state;
                _platformTabControl.TabPages.Add(state.TabPage);
            }

            _activeProviderState = _providerStatesByTab.Values.FirstOrDefault();
            if (_activeProviderState != null)
            {
                ActivateProviderState(_activeProviderState);
            }

            BuildLayout();
            CancelButton = _closeButton;
            RefreshActionButtons(_activeProviderState);
        }

        private static TabPage BuildTabPage(string text)
        {
            return new TabPage(string.IsNullOrWhiteSpace(text) ? UiLocalization.Text("GitRemoteRepositoriesForm.Tab.GitHub", "GitHub") : text);
        }

        private void ActivateCurrentProviderStateFromTab()
        {
            if (_platformTabControl == null || _platformTabControl.SelectedTab == null)
            {
                return;
            }

            if (!_providerStatesByTab.TryGetValue(_platformTabControl.SelectedTab, out var state))
            {
                return;
            }

            if (ReferenceEquals(_activeProviderState, state))
            {
                return;
            }

            ActivateProviderState(state);
            if (state.HasLoaded)
            {
                PopulateRepositories(state, ApplyFilterAndSortRepositories(state));
            }
        }

        private void ActivateProviderState(ProviderRepositoryState state)
        {
            if (state == null)
            {
                return;
            }

            if (_activeProviderState != null && _activeProviderState.TabPage != null &&
                _activeProviderState.TabPage.Controls.Contains(_providerContent))
            {
                _activeProviderState.TabPage.Controls.Remove(_providerContent);
            }

            _isSwitchingProviderState = true;
            try
            {
                _activeProviderState = state;
                if (state.TabPage != null && !_providerContent.IsDisposed)
                {
                    state.TabPage.Controls.Add(_providerContent);
                }

                if (state.VisibilityFilter == RepositoryVisibilityFilter.All)
                {
                    _visibilityFilterComboBox.SelectedIndex = 0;
                }
                else if (state.VisibilityFilter == RepositoryVisibilityFilter.Public)
                {
                    _visibilityFilterComboBox.SelectedIndex = 1;
                }
                else if (state.VisibilityFilter == RepositoryVisibilityFilter.Private)
                {
                    _visibilityFilterComboBox.SelectedIndex = 2;
                }
                else
                {
                    _visibilityFilterComboBox.SelectedIndex = 0;
                }

                _githubFilterTextBox.Text = state.FilterKeyword ?? string.Empty;
                _showOnlyForksCheckBox.Checked = state.ShowOnlyForks;
                _showOnlyArchivedCheckBox.Checked = state.ShowOnlyArchived;
                RefreshRepositoryContextMenu(state);
                PopulateRepositories(state, ApplyFilterAndSortRepositories(state));
            }
            finally
            {
                _isSwitchingProviderState = false;
                RestoreRepositoryFocus(state);
                _platformTabControl.SelectTab(state.TabPage);
            }
        }

        private void UpdateActiveProviderUiStateAndRefresh()
        {
            SaveCurrentProviderUiState();
            RefreshDisplayedRepositories(_activeProviderState);
        }

        private void SaveCurrentProviderUiState()
        {
            if (_activeProviderState == null)
            {
                return;
            }

            _activeProviderState.FilterKeyword = _githubFilterTextBox.Text?.Trim() ?? string.Empty;
            _activeProviderState.VisibilityFilter = GetVisibilityFilterFromUi(_visibilityFilterComboBox);
            _activeProviderState.ShowOnlyForks = _showOnlyForksCheckBox.Checked;
            _activeProviderState.ShowOnlyArchived = _showOnlyArchivedCheckBox.Checked;
        }

        private static RepositoryVisibilityFilter GetVisibilityFilterFromUi(ComboBox visibilityFilterComboBox)
        {
            if (visibilityFilterComboBox == null || visibilityFilterComboBox.SelectedIndex <= 0)
            {
                return RepositoryVisibilityFilter.All;
            }

            return visibilityFilterComboBox.SelectedIndex == 1
                ? RepositoryVisibilityFilter.Public
                : RepositoryVisibilityFilter.Private;
        }

        private async void GitRemoteRepositoriesForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 && _activeProviderState != null && _activeProviderState.RefreshCancellation == null)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await RefreshRepositoriesAsync(_activeProviderState, false);
            }
        }

        private void GitRemoteRepositoriesForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            foreach (var state in _providerStatesByTab.Values)
            {
                if (state.RefreshCancellation != null)
                {
                    state.RefreshCancellation.Cancel();
                }
            }
        }

        private async Task RefreshRepositoriesAsync(ProviderRepositoryState state, bool speakLoadedStatus)
        {
            if (state == null || state.RefreshCancellation != null)
            {
                return;
            }

            var provider = state.Provider;
            if (provider == null || !provider.Supports(GitHostingProviderCapabilities.RepositoryList))
            {
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.Failed", "Failed to load GitHub repositories."), true);
                return;
            }

            state.RefreshCancellation = new CancellationTokenSource();
            SetLoading(state, true);
            if (_repositoryListView.CanFocus)
            {
                _repositoryListView.Focus();
            }

            SaveCurrentProviderUiState();
            RestoreRepositoryFocus(state);
            SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.Loading", "Loading GitHub repositories..."), speakLoadedStatus);
            try
            {
                var repositories = await provider.ListRepositoriesAsync(
                        state.FilterKeyword,
                        200,
                        state.RefreshCancellation.Token)
                    .ConfigureAwait(true);
                state.Repositories = (repositories ?? new RemoteRepositoryEntry[0])
                    .Select(RemoteRepositoryEntryMapper.ToGitHubRepository)
                    .Where(repository => repository != null)
                    .ToList();
                state.HasLoaded = true;
                var displayed = ApplyFilterAndSortRepositories(state);
                PopulateRepositories(state, displayed);
                SetStatus(string.Format(
                    UiLocalization.Text("GitRemoteRepositoriesForm.Status.Loaded.Template", "Loaded {0} GitHub repositories."),
                    displayed == null ? 0 : displayed.Count),
                    speakLoadedStatus && ReferenceEquals(_activeProviderState, state));
                if (ReferenceEquals(_activeProviderState, state))
                {
                    RestoreRepositoryFocus(state);
                }
            }
            catch (OperationCanceledException)
            {
                state.HasLoaded = false;
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.Canceled", "GitHub repository loading canceled."), false);
            }
            catch (Exception ex)
            {
                state.Repositories = new List<GitHubRepositoryEntry>();
                if (ReferenceEquals(_activeProviderState, state))
                {
                    PopulateRepositories(state, state.Repositories);
                }

                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.Failed", "Failed to load GitHub repositories."), true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (state.RefreshCancellation != null)
                {
                    state.RefreshCancellation.Dispose();
                    state.RefreshCancellation = null;
                }

                SetLoading(state, false);
                if (ReferenceEquals(_activeProviderState, state))
                {
                    RefreshRepositoryContextMenu(state);
                }
            }
        }

        private void PopulateRepositories(ProviderRepositoryState state, IReadOnlyList<GitHubRepositoryEntry> repositories)
        {
            if (state == null || !ReferenceEquals(_activeProviderState, state))
            {
                return;
            }

            _repositoryListView.BeginUpdate();
            try
            {
                _repositoryListView.Items.Clear();
                foreach (var repository in (repositories ?? new GitHubRepositoryEntry[0]).Where(item => item != null))
                {
                    _repositoryListView.Items.Add(CreateRepositoryItem(repository));
                }

                if (_repositoryListView.Items.Count > 0)
                {
                    _repositoryListView.Items[0].Selected = true;
                    _repositoryListView.Items[0].Focused = true;
                }
            }
            finally
            {
                _repositoryListView.EndUpdate();
            }

            UpdateSortColumnHeaders(state);
            RefreshActionButtons(state);
        }

        private void RefreshDisplayedRepositories(ProviderRepositoryState state)
        {
            if (state == null || state.RefreshCancellation != null)
            {
                return;
            }

            var displayed = ApplyFilterAndSortRepositories(state);
            PopulateRepositories(state, displayed);
        }

        private async Task CreateRepositoryAsync(ProviderRepositoryState state)
        {
            if (state == null || state.RefreshCancellation != null)
            {
                return;
            }

            var cliService = state.Provider == null ? null : state.Provider.GetCapability<IGitHubHostingProviderCapability>()?.GitHubCliService;
            if (cliService == null)
            {
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.Failed", "Failed to create GitHub repository. See details."), true);
                return;
            }

            using (var form = new GitHubRepositoryCreateForm(
                       UiLocalization.Text("GitHubRepositoryCreateForm.Title.CreateRemote", "Create GitHub Repository"),
                       string.Empty,
                       true))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreRepositoryFocus(state);
                    return;
                }

                state.RefreshCancellation = new CancellationTokenSource();
                SetLoading(state, true);
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.CreatingRepository", "Creating GitHub repository..."), true);
                GitHubCreateRepositoryResult result = null;
                try
                {
                    result = await cliService.CreateRepositoryAsync(form.Request, state.RefreshCancellation.Token).ConfigureAwait(true);
                    if (result != null && result.Success)
                    {
                        SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryCreated", "GitHub repository created."), false);
                    }
                    else
                    {
                        SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryCreateFailed", "Failed to create GitHub repository. See details."), true);
                        MessageBox.Show(
                            this,
                            result == null ? UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryCreateFailed", "Failed to create GitHub repository. See details.") : result.Output,
                            AppConstants.AppName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    }
                }
                catch (OperationCanceledException)
                {
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryCreateCanceled", "GitHub repository creation canceled."), true);
                }
                catch (Exception ex)
                {
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryCreateFailed", "Failed to create GitHub repository. See details."), true);
                    MessageBox.Show(this, FormatRepositoryOperationError(ex), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (state.RefreshCancellation != null)
                    {
                        state.RefreshCancellation.Dispose();
                        state.RefreshCancellation = null;
                    }

                    SetLoading(state, false);
                }

                if (result != null && result.Success)
                {
                    await RefreshRepositoriesAsync(state, false).ConfigureAwait(true);
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryCreated", "GitHub repository created."), true);
                }
                else
                {
                    RestoreRepositoryFocus(state);
                }
            }
        }

        private void ShowSelectedRepositoryDetails(ProviderRepositoryState state)
        {
            var repository = GetSelectedRepository(state);
            if (repository == null)
            {
                RestoreRepositoryFocus(state);
                return;
            }

            var cliService = state?.Provider == null ? null : state.Provider.GetCapability<IGitHubHostingProviderCapability>()?.GitHubCliService;
            if (cliService == null)
            {
                return;
            }

            var form = new GitHubRepositoryDetailsForm(
                cliService,
                repository,
                _screenReaderService,
                _currentWorkspacePath,
                _gitRepositoryService,
                GitHubRepositoryDetailsMode.Manage);
            form.FormClosed += (sender, e) => RestoreRepositoryFocus(state);
            form.Show(this);
        }

        private void OpenSelectedRepositoryPage(
            ProviderRepositoryState state,
            string relativePath,
            string status)
        {
            var repository = GetSelectedRepository(state);
            var url = BuildGitHubWebUrl(repository, relativePath);
            if (string.IsNullOrWhiteSpace(url))
            {
                RestoreRepositoryFocus(state);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                SetStatus(status, true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                RestoreRepositoryFocus(state);
            }
        }

        private void CopySelectedRepositoryValue(
            ProviderRepositoryState state,
            Func<GitHubRepositoryEntry, string> valueSelector,
            string status)
        {
            var repository = GetSelectedRepository(state);
            var value = repository == null || valueSelector == null ? null : valueSelector(repository);
            if (string.IsNullOrWhiteSpace(value))
            {
                RestoreRepositoryFocus(state);
                return;
            }

            Clipboard.SetText(value.Trim());
            SetStatus(status, true);
            RestoreRepositoryFocus(state);
        }

        private GitHubRepositoryEntry GetSelectedRepository(ProviderRepositoryState state)
        {
            if (state == null || !ReferenceEquals(_activeProviderState, state))
            {
                return null;
            }

            return _repositoryListView.SelectedItems.Count == 0
                ? null
                : _repositoryListView.SelectedItems[0].Tag as GitHubRepositoryEntry;
        }

        private async Task RenameSelectedRepositoryAsync(ProviderRepositoryState state)
        {
            var repository = GetSelectedRepository(state);
            var cliService = state?.Provider == null ? null : state.Provider.GetCapability<IGitHubHostingProviderCapability>()?.GitHubCliService;
            var fullName = GetRepositoryFullName(repository);
            if (repository == null || cliService == null || string.IsNullOrWhiteSpace(fullName) || state.RefreshCancellation != null)
            {
                RestoreRepositoryFocus(state);
                return;
            }

            using (var form = new RepositoryRenameForm(repository))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreRepositoryFocus(state);
                    return;
                }

                var confirmation = string.Format(
                    UiLocalization.Text("GitRemoteRepositoriesForm.Confirm.RenameRepository.Template", "Rename repository {0} to {1}?"),
                    fullName,
                    form.NewRepositoryName);
                if (MessageBox.Show(this, confirmation, AppConstants.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    RestoreRepositoryFocus(state);
                    return;
                }

                state.RefreshCancellation = new CancellationTokenSource();
                SetLoading(state, true);
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RenamingRepository", "Renaming GitHub repository..."), true);
                try
                {
                    await cliService.RenameRepositoryAsync(repository, form.NewRepositoryName, state.RefreshCancellation.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationCanceled", "GitHub repository operation canceled."), true);
                    RestoreRepositoryFocus(state);
                    return;
                }
                catch (Exception ex)
                {
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationFailed", "GitHub repository operation failed."), true);
                    MessageBox.Show(this, FormatRepositoryOperationError(ex), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RestoreRepositoryFocus(state);
                    return;
                }
                finally
                {
                    if (state.RefreshCancellation != null)
                    {
                        state.RefreshCancellation.Dispose();
                        state.RefreshCancellation = null;
                    }

                    SetLoading(state, false);
                }
            }

            await RefreshRepositoriesAsync(state, false).ConfigureAwait(true);
            SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryRenamed", "GitHub repository renamed."), true);
        }

        private async Task EditSelectedRepositoryMetadataAsync(ProviderRepositoryState state)
        {
            var repository = GetSelectedRepository(state);
            var cliService = state?.Provider == null ? null : state.Provider.GetCapability<IGitHubHostingProviderCapability>()?.GitHubCliService;
            var fullName = GetRepositoryFullName(repository);
            if (repository == null || cliService == null || string.IsNullOrWhiteSpace(fullName) || state.RefreshCancellation != null)
            {
                RestoreRepositoryFocus(state);
                return;
            }

            using (var form = new RepositoryMetadataEditForm(repository))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreRepositoryFocus(state);
                    return;
                }

                state.RefreshCancellation = new CancellationTokenSource();
                SetLoading(state, true);
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.EditingRepositoryMetadata", "Updating GitHub repository info..."), true);
                try
                {
                    await cliService.EditRepositoryMetadataAsync(repository, form.DescriptionText, form.HomepageUrl, state.RefreshCancellation.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationCanceled", "GitHub repository operation canceled."), true);
                    RestoreRepositoryFocus(state);
                    return;
                }
                catch (Exception ex)
                {
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationFailed", "GitHub repository operation failed."), true);
                    MessageBox.Show(this, FormatRepositoryOperationError(ex), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RestoreRepositoryFocus(state);
                    return;
                }
                finally
                {
                    if (state.RefreshCancellation != null)
                    {
                        state.RefreshCancellation.Dispose();
                        state.RefreshCancellation = null;
                    }

                    SetLoading(state, false);
                }
            }

            await RefreshRepositoriesAsync(state, false).ConfigureAwait(true);
            SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryMetadataEdited", "GitHub repository info updated."), true);
        }

        private async Task SetDefaultBranchForSelectedRepositoryAsync(ProviderRepositoryState state)
        {
            var repository = GetSelectedRepository(state);
            var cliService = state?.Provider == null ? null : state.Provider.GetCapability<IGitHubHostingProviderCapability>()?.GitHubCliService;
            var fullName = GetRepositoryFullName(repository);
            if (repository == null || cliService == null || string.IsNullOrWhiteSpace(fullName) || state.RefreshCancellation != null)
            {
                RestoreRepositoryFocus(state);
                return;
            }

            IReadOnlyList<GitHubBranchEntry> branches = null;
            state.RefreshCancellation = new CancellationTokenSource();
            SetLoading(state, true);
            SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.LoadingBranches", "Loading GitHub branches..."), true);
            try
            {
                branches = await cliService.ListBranchesAsync(repository, 100, state.RefreshCancellation.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationCanceled", "GitHub repository operation canceled."), true);
                RestoreRepositoryFocus(state);
                return;
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                RestoreRepositoryFocus(state);
                return;
            }
            finally
            {
                if (state.RefreshCancellation != null)
                {
                    state.RefreshCancellation.Dispose();
                    state.RefreshCancellation = null;
                }

                SetLoading(state, false);
            }

            if (branches == null || branches.Count == 0)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitRemoteRepositoriesForm.Message.NoBranches", "No branches are available for this repository."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                RestoreRepositoryFocus(state);
                return;
            }

            using (var form = new RepositoryDefaultBranchForm(repository, branches))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreRepositoryFocus(state);
                    return;
                }

                if (string.Equals(form.BranchName, repository.DefaultBranch, StringComparison.Ordinal))
                {
                    RestoreRepositoryFocus(state);
                    return;
                }

                var confirmation = string.Format(
                    UiLocalization.Text("GitRemoteRepositoriesForm.Confirm.SetDefaultBranch.Template", "Set {0} as the default branch for {1}?"),
                    form.BranchName,
                    fullName);
                if (MessageBox.Show(this, confirmation, AppConstants.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    RestoreRepositoryFocus(state);
                    return;
                }

                state.RefreshCancellation = new CancellationTokenSource();
                SetLoading(state, true);
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.SettingDefaultBranch", "Setting GitHub default branch..."), true);
                try
                {
                    await cliService.SetDefaultBranchAsync(repository, form.BranchName, state.RefreshCancellation.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationCanceled", "GitHub repository operation canceled."), true);
                    RestoreRepositoryFocus(state);
                    return;
                }
                catch (Exception ex)
                {
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationFailed", "GitHub repository operation failed."), true);
                    MessageBox.Show(this, FormatRepositoryOperationError(ex), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RestoreRepositoryFocus(state);
                    return;
                }
                finally
                {
                    if (state.RefreshCancellation != null)
                    {
                        state.RefreshCancellation.Dispose();
                        state.RefreshCancellation = null;
                    }

                    SetLoading(state, false);
                }
            }

            await RefreshRepositoriesAsync(state, false).ConfigureAwait(true);
            SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.DefaultBranchSet", "GitHub default branch updated."), true);
        }

        private async Task ChangeVisibilityForSelectedRepositoryAsync(ProviderRepositoryState state)
        {
            var repository = GetSelectedRepository(state);
            var cliService = state?.Provider == null ? null : state.Provider.GetCapability<IGitHubHostingProviderCapability>()?.GitHubCliService;
            var fullName = GetRepositoryFullName(repository);
            if (repository == null || cliService == null || string.IsNullOrWhiteSpace(fullName) || state.RefreshCancellation != null)
            {
                RestoreRepositoryFocus(state);
                return;
            }

            using (var form = new RepositoryVisibilityForm(repository))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreRepositoryFocus(state);
                    return;
                }

                if (string.Equals(form.Visibility, NormalizeVisibilityForComparison(repository.Visibility), StringComparison.OrdinalIgnoreCase))
                {
                    RestoreRepositoryFocus(state);
                    return;
                }

                var confirmation = string.Format(
                    UiLocalization.Text("GitRemoteRepositoriesForm.Confirm.ChangeVisibility.Template", "Change visibility for {0} to {1}?\r\n\r\nThis can affect forks, stars, watchers, push rulesets, and GitHub Actions history. Continue?"),
                    fullName,
                    form.Visibility);
                if (MessageBox.Show(this, confirmation, AppConstants.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    RestoreRepositoryFocus(state);
                    return;
                }

                state.RefreshCancellation = new CancellationTokenSource();
                SetLoading(state, true);
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.ChangingVisibility", "Changing GitHub repository visibility..."), true);
                try
                {
                    await cliService.ChangeRepositoryVisibilityAsync(repository, form.Visibility, state.RefreshCancellation.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationCanceled", "GitHub repository operation canceled."), true);
                    RestoreRepositoryFocus(state);
                    return;
                }
                catch (Exception ex)
                {
                    SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationFailed", "GitHub repository operation failed."), true);
                    MessageBox.Show(this, FormatRepositoryOperationError(ex), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RestoreRepositoryFocus(state);
                    return;
                }
                finally
                {
                    if (state.RefreshCancellation != null)
                    {
                        state.RefreshCancellation.Dispose();
                        state.RefreshCancellation = null;
                    }

                    SetLoading(state, false);
                }
            }

            await RefreshRepositoriesAsync(state, false).ConfigureAwait(true);
            SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.VisibilityChanged", "GitHub repository visibility updated."), true);
        }

        private async Task ArchiveSelectedRepositoryAsync(ProviderRepositoryState state)
        {
            await ChangeRepositoryArchiveStateAsync(
                state,
                true,
                UiLocalization.Text("GitRemoteRepositoriesForm.Confirm.ArchiveRepository.Template", "Archive repository {0}?\r\n\r\nAn archived repository becomes read-only until it is unarchived."),
                UiLocalization.Text("GitRemoteRepositoriesForm.Status.ArchivingRepository", "Archiving GitHub repository..."),
                UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryArchived", "GitHub repository archived.")).ConfigureAwait(true);
        }

        private async Task UnarchiveSelectedRepositoryAsync(ProviderRepositoryState state)
        {
            await ChangeRepositoryArchiveStateAsync(
                state,
                false,
                UiLocalization.Text("GitRemoteRepositoriesForm.Confirm.UnarchiveRepository.Template", "Unarchive repository {0}?"),
                UiLocalization.Text("GitRemoteRepositoriesForm.Status.UnarchivingRepository", "Unarchiving GitHub repository..."),
                UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryUnarchived", "GitHub repository unarchived.")).ConfigureAwait(true);
        }

        private async Task ChangeRepositoryArchiveStateAsync(
            ProviderRepositoryState state,
            bool archive,
            string confirmationTemplate,
            string runningStatus,
            string successStatus)
        {
            var repository = GetSelectedRepository(state);
            var cliService = state?.Provider == null ? null : state.Provider.GetCapability<IGitHubHostingProviderCapability>()?.GitHubCliService;
            var fullName = GetRepositoryFullName(repository);
            if (repository == null || cliService == null || string.IsNullOrWhiteSpace(fullName) || state.RefreshCancellation != null)
            {
                RestoreRepositoryFocus(state);
                return;
            }

            if (MessageBox.Show(this, string.Format(confirmationTemplate, fullName), AppConstants.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                RestoreRepositoryFocus(state);
                return;
            }

            state.RefreshCancellation = new CancellationTokenSource();
            SetLoading(state, true);
            SetStatus(runningStatus, true);
            try
            {
                if (archive)
                {
                    await cliService.ArchiveRepositoryAsync(repository, state.RefreshCancellation.Token).ConfigureAwait(true);
                }
                else
                {
                    await cliService.UnarchiveRepositoryAsync(repository, state.RefreshCancellation.Token).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationCanceled", "GitHub repository operation canceled."), true);
                RestoreRepositoryFocus(state);
                return;
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                RestoreRepositoryFocus(state);
                return;
            }
            finally
            {
                if (state.RefreshCancellation != null)
                {
                    state.RefreshCancellation.Dispose();
                    state.RefreshCancellation = null;
                }

                SetLoading(state, false);
            }

            await RefreshRepositoriesAsync(state, false).ConfigureAwait(true);
            SetStatus(successStatus, true);
        }

        private async Task DeleteSelectedRepositoryAsync(ProviderRepositoryState state)
        {
            var repository = GetSelectedRepository(state);
            var cliService = state?.Provider == null ? null : state.Provider.GetCapability<IGitHubHostingProviderCapability>()?.GitHubCliService;
            var fullName = GetRepositoryFullName(repository);
            if (repository == null || cliService == null || string.IsNullOrWhiteSpace(fullName) || state.RefreshCancellation != null)
            {
                RestoreRepositoryFocus(state);
                return;
            }

            var firstConfirmation = string.Format(
                UiLocalization.Text("GitRemoteRepositoriesForm.Confirm.DeleteRepository.Template", "Delete repository {0}?\r\n\r\nThis permanently deletes the repository from GitHub. Local clones and local Git remotes are not removed."),
                fullName);
            if (MessageBox.Show(this, firstConfirmation, AppConstants.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                RestoreRepositoryFocus(state);
                return;
            }

            using (var form = new RepositoryDeleteConfirmationForm(fullName))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreRepositoryFocus(state);
                    return;
                }
            }

            state.RefreshCancellation = new CancellationTokenSource();
            SetLoading(state, true);
            SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.DeletingRepository", "Deleting GitHub repository..."), true);
            try
            {
                await cliService.DeleteRepositoryAsync(repository, state.RefreshCancellation.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationCanceled", "GitHub repository operation canceled."), true);
                RestoreRepositoryFocus(state);
                return;
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                RestoreRepositoryFocus(state);
                return;
            }
            finally
            {
                if (state.RefreshCancellation != null)
                {
                    state.RefreshCancellation.Dispose();
                    state.RefreshCancellation = null;
                }

                SetLoading(state, false);
            }

            await RefreshRepositoriesAsync(state, false).ConfigureAwait(true);
            SetStatus(UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryDeleted", "GitHub repository deleted."), true);
        }

        private void RefreshActionButtons(ProviderRepositoryState state)
        {
            RefreshRepositoryContextMenu(state);
        }

        private void SetLoading(ProviderRepositoryState state, bool loading)
        {
            _githubFilterTextBox.Enabled = !loading;
            _refreshButton.Enabled = !loading;
            _createRepositoryButton.Enabled = !loading;
            _visibilityFilterComboBox.Enabled = !loading;
            _showOnlyForksCheckBox.Enabled = !loading;
            _showOnlyArchivedCheckBox.Enabled = !loading;
            RefreshRepositoryContextMenu(state);
        }

        private void SetStatus(string text, bool speak)
        {
            _statusLabel.Text = text ?? string.Empty;
            if (!speak || _screenReaderService == null)
            {
                return;
            }

            _screenReaderService.Speak(_statusLabel.Text, true, false, true);
        }

        private void RestoreRepositoryFocus(ProviderRepositoryState state)
        {
            if (!ReferenceEquals(_activeProviderState, state))
            {
                return;
            }

            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                if (_repositoryListView.Items.Count > 0)
                {
                    if (_repositoryListView.SelectedItems.Count == 0)
                    {
                        _repositoryListView.Items[0].Selected = true;
                    }

                    _repositoryListView.SelectedItems[0].Focused = true;
                    _repositoryListView.SelectedItems[0].EnsureVisible();
                    _repositoryListView.Focus();
                    return;
                }

                _githubFilterTextBox.Focus();
            });
        }

        private void RefreshRepositoryContextMenu(ProviderRepositoryState state)
        {
            if (state == null || !ReferenceEquals(_activeProviderState, state))
            {
                return;
            }

            var loading = state.RefreshCancellation != null;
            var repository = GetSelectedRepository(state);
            var hasWebUrl = !string.IsNullOrWhiteSpace(BuildGitHubWebUrl(repository, null));
            _repositoryContextMenu.MenuItems.Clear();
            if (loading || repository == null)
            {
                return;
            }

            _repositoryContextMenu.MenuItems.Add(_viewDetailsMenuItem);
            if (hasWebUrl)
            {
                AddRepositoryContextSeparator();
                _repositoryContextMenu.MenuItems.Add(_openRepositoryMenuItem);
                _repositoryContextMenu.MenuItems.Add(_openReleasesMenuItem);
                _repositoryContextMenu.MenuItems.Add(_openContributorsMenuItem);
            }

            AddRepositoryContextSeparator();
            if (!string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                _repositoryContextMenu.MenuItems.Add(_renameRepositoryMenuItem);
            }

            _repositoryContextMenu.MenuItems.Add(_editRepositoryMetadataMenuItem);
            _repositoryContextMenu.MenuItems.Add(_setDefaultBranchMenuItem);
            _repositoryContextMenu.MenuItems.Add(_changeVisibilityMenuItem);

            AddRepositoryContextSeparator();
            _repositoryContextMenu.MenuItems.Add(repository.IsArchived
                ? _unarchiveRepositoryMenuItem
                : _archiveRepositoryMenuItem);
            if (!string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                _repositoryContextMenu.MenuItems.Add(_deleteRepositoryMenuItem);
            }

            AddRepositoryContextSeparator();
            if (!string.IsNullOrWhiteSpace(repository.HttpsUrl))
            {
                _repositoryContextMenu.MenuItems.Add(_copyHttpsUrlMenuItem);
            }

            if (!string.IsNullOrWhiteSpace(repository.SshUrl))
            {
                _repositoryContextMenu.MenuItems.Add(_copySshUrlMenuItem);
            }

            if (!string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                _repositoryContextMenu.MenuItems.Add(_copyFullNameMenuItem);
            }
        }

        private void AddRepositoryContextSeparator()
        {
            if (_repositoryContextMenu.MenuItems.Count == 0)
            {
                return;
            }

            var last = _repositoryContextMenu.MenuItems[_repositoryContextMenu.MenuItems.Count - 1];
            if (last.Text != "-")
            {
                _repositoryContextMenu.MenuItems.Add("-");
            }
        }

        private void RepositoryListView_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            if (_activeProviderState == null || _activeProviderState.RefreshCancellation != null)
            {
                return;
            }

            var targetSortField = GetSortFieldForColumn(e.Column);
            if (!targetSortField.HasValue)
            {
                return;
            }

            if (_activeProviderState.SortField == targetSortField.Value)
            {
                _activeProviderState.SortDescending = !_activeProviderState.SortDescending;
            }
            else
            {
                _activeProviderState.SortField = targetSortField.Value;
                _activeProviderState.SortDescending = IsDefaultSortDescending(_activeProviderState.SortField);
            }

            RefreshDisplayedRepositories(_activeProviderState);
        }

        private static RepositorySortField? GetSortFieldForColumn(int columnIndex)
        {
            if (columnIndex < 0)
            {
                return null;
            }

            switch (columnIndex)
            {
                case 0:
                    return RepositorySortField.Repository;
                case 1:
                    return RepositorySortField.Description;
                case 2:
                    return RepositorySortField.Language;
                case 3:
                    return RepositorySortField.DefaultBranch;
                case 4:
                    return RepositorySortField.UpdatedAt;
                case 5:
                    return RepositorySortField.PushedAt;
                case 6:
                    return RepositorySortField.Stars;
                case 7:
                    return RepositorySortField.Forks;
                case 8:
                    return RepositorySortField.Watchers;
                case 9:
                    return RepositorySortField.DiskUsage;
                case 10:
                    return RepositorySortField.License;
                case 11:
                    return RepositorySortField.Archived;
                case 12:
                    return RepositorySortField.Fork;
                default:
                    return null;
            }
        }

        private static bool IsDefaultSortDescending(RepositorySortField sortField)
        {
            return sortField == RepositorySortField.UpdatedAt ||
                sortField == RepositorySortField.PushedAt ||
                sortField == RepositorySortField.Stars ||
                sortField == RepositorySortField.Forks ||
                sortField == RepositorySortField.Watchers ||
                sortField == RepositorySortField.DiskUsage;
        }

        private IReadOnlyList<GitHubRepositoryEntry> ApplyFilterAndSortRepositories(ProviderRepositoryState state)
        {
            if (state == null)
            {
                return new GitHubRepositoryEntry[0];
            }

            return ApplyRepositorySort(ApplyRepositoryFilter(
                state.Repositories,
                state.FilterKeyword,
                state.VisibilityFilter,
                state.ShowOnlyForks,
                state.ShowOnlyArchived),
                state);
        }

        private static IReadOnlyList<GitHubRepositoryEntry> ApplyRepositoryFilter(
            IReadOnlyList<GitHubRepositoryEntry> repositories,
            string keyword,
            RepositoryVisibilityFilter visibilityFilter,
            bool onlyForks,
            bool onlyArchived)
        {
            if (repositories == null)
            {
                return new GitHubRepositoryEntry[0];
            }

            if (visibilityFilter == RepositoryVisibilityFilter.All &&
                !onlyForks &&
                !onlyArchived &&
                string.IsNullOrWhiteSpace(keyword))
            {
                return repositories;
            }

            var filter = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
            var result = new List<GitHubRepositoryEntry>();
            foreach (var repository in repositories)
            {
                if (repository == null)
                {
                    continue;
                }

                if (!IsRepositoryVisibilityMatch(repository, visibilityFilter))
                {
                    continue;
                }

                if (onlyForks && !repository.IsFork)
                {
                    continue;
                }

                if (onlyArchived && !repository.IsArchived)
                {
                    continue;
                }

                if (filter == null)
                {
                    result.Add(repository);
                    continue;
                }

                if (Contains(repository.NameWithOwner, filter) ||
                    Contains(repository.Description, filter) ||
                    Contains(repository.PrimaryLanguage, filter) ||
                    Contains(repository.DefaultBranch, filter) ||
                    Contains(repository.Visibility, filter) ||
                    Contains(repository.License, filter) ||
                    Contains(repository.HomepageUrl, filter) ||
                    Contains(repository.UpdatedAt.HasValue ? repository.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm") : null, filter) ||
                    Contains(repository.CreatedAt.HasValue ? repository.CreatedAt.Value.ToString("yyyy-MM-dd HH:mm") : null, filter) ||
                    Contains(repository.PushedAt.HasValue ? repository.PushedAt.Value.ToString("yyyy-MM-dd HH:mm") : null, filter) ||
                    ContainsNumeric(repository.DiskUsage, filter) ||
                    ContainsNumeric(repository.Stars, filter) ||
                    ContainsNumeric(repository.Forks, filter) ||
                    ContainsNumeric(repository.Watchers, filter))
                {
                    result.Add(repository);
                }
            }

            return result;
        }

        private static bool IsRepositoryVisibilityMatch(GitHubRepositoryEntry repository, RepositoryVisibilityFilter visibilityFilter)
        {
            if (visibilityFilter == RepositoryVisibilityFilter.All)
            {
                return true;
            }

            if (repository == null)
            {
                return false;
            }

            var isPrivate = IsRepositoryPrivate(repository);
            return visibilityFilter == RepositoryVisibilityFilter.Private
                ? isPrivate
                : !isPrivate;
        }

        private static bool IsRepositoryPrivate(GitHubRepositoryEntry repository)
        {
            if (repository == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(repository.Visibility))
            {
                return string.Equals(repository.Visibility, "PRIVATE", StringComparison.OrdinalIgnoreCase);
            }

            return repository.IsPrivate;
        }

        private IReadOnlyList<GitHubRepositoryEntry> ApplyRepositorySort(
            IReadOnlyList<GitHubRepositoryEntry> repositories,
            ProviderRepositoryState state)
        {
            var items = repositories == null
                ? new List<GitHubRepositoryEntry>()
                : new List<GitHubRepositoryEntry>(repositories);
            if (items.Count <= 1)
            {
                return items;
            }

            items.Sort((left, right) => CompareCurrentSort(left, right, state));

            return items;
        }

        private static int CompareCurrentSort(
            GitHubRepositoryEntry left,
            GitHubRepositoryEntry right,
            ProviderRepositoryState state)
        {
            if (state == null)
            {
                return CompareBySortField(left, right, RepositorySortField.Repository);
            }

            var value = CompareBySortField(left, right, state.SortField);
            if (value != 0)
            {
                return state.SortDescending ? -value : value;
            }

            value = CompareBySortField(left, right, RepositorySortField.Repository);
            return state.SortDescending ? -value : value;
        }

        private static int CompareBySortField(GitHubRepositoryEntry left, GitHubRepositoryEntry right, RepositorySortField sortField)
        {
            switch (sortField)
            {
                case RepositorySortField.Repository:
                {
                    return CompareText(
                        GetRepositorySortValue(left),
                        GetRepositorySortValue(right));
                }

                case RepositorySortField.Description:
                    return CompareText(left?.Description, right?.Description);
                case RepositorySortField.Language:
                    return CompareText(left?.PrimaryLanguage, right?.PrimaryLanguage);
                case RepositorySortField.DefaultBranch:
                    return CompareText(left?.DefaultBranch, right?.DefaultBranch);
                case RepositorySortField.UpdatedAt:
                    return Nullable.Compare(left?.UpdatedAt, right?.UpdatedAt);
                case RepositorySortField.PushedAt:
                    return Nullable.Compare(left?.PushedAt, right?.PushedAt);
                case RepositorySortField.Stars:
                    return CompareNumeric(left?.Stars, right?.Stars);
                case RepositorySortField.Forks:
                    return CompareNumeric(left?.Forks, right?.Forks);
                case RepositorySortField.Watchers:
                    return CompareNumeric(left?.Watchers, right?.Watchers);
                case RepositorySortField.DiskUsage:
                    return CompareNumeric(left?.DiskUsage, right?.DiskUsage);
                case RepositorySortField.License:
                    return CompareText(left?.License, right?.License);
                case RepositorySortField.Archived:
                    return CompareBoolean(left?.IsArchived, right?.IsArchived);
                case RepositorySortField.Fork:
                    return CompareBoolean(left?.IsFork, right?.IsFork);
                default:
                    return CompareText(GetRepositorySortValue(left), GetRepositorySortValue(right));
            }
        }

        private static string GetRepositorySortValue(GitHubRepositoryEntry repository)
        {
            return repository == null ? null : GetRepositoryDisplayNameWithVisibility(repository);
        }

        private static int CompareText(string left, string right)
        {
            return string.Compare(
                left ?? string.Empty,
                right ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareNumeric(long? left, long? right)
        {
            var leftValue = left ?? 0;
            var rightValue = right ?? 0;
            return leftValue.CompareTo(rightValue);
        }

        private static int CompareNumeric(int? left, int? right)
        {
            var leftValue = left ?? 0;
            var rightValue = right ?? 0;
            return leftValue.CompareTo(rightValue);
        }

        private static int CompareBoolean(bool? left, bool? right)
        {
            var leftValue = left.GetValueOrDefault();
            var rightValue = right.GetValueOrDefault();
            return leftValue.CompareTo(rightValue);
        }

        private static bool Contains(string value, string keyword)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(keyword) &&
                   value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsNumeric(long? value, string keyword)
        {
            return value.HasValue && !string.IsNullOrWhiteSpace(keyword) &&
                value.Value.ToString().IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsNumeric(int? value, string keyword)
        {
            return value.HasValue && !string.IsNullOrWhiteSpace(keyword) &&
                value.Value.ToString().IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateSortColumnHeaders(ProviderRepositoryState state)
        {
            if (_repositoryListView == null)
            {
                return;
            }

            var sortField = state == null ? RepositorySortField.UpdatedAt : state.SortField;
            var sortDescending = state != null && state.SortDescending;
            for (var i = 0; i < _repositoryListView.Columns.Count; i++)
            {
                var column = _repositoryListView.Columns[i];
                var baseText = column.Tag as string ?? column.Text;
                if (GetSortFieldForColumn(i).HasValue &&
                    GetSortFieldForColumn(i).Value == sortField)
                {
                    column.Text = string.Format(
                        "{0} {1}",
                        baseText,
                        sortDescending ? "▼" : "▲");
                    continue;
                }

                column.Text = baseText;
            }
        }

        private void RepositoryListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ShowSelectedRepositoryDetails(_activeProviderState);
                return;
            }

            if (e.KeyCode != Keys.Apps && !(e.Shift && e.KeyCode == Keys.F10))
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            ShowRepositoryContextMenuForKeyboard();
        }

        private void ShowRepositoryContextMenuForKeyboard()
        {
            if (_repositoryListView.Items.Count == 0)
            {
                _repositoryContextMenu.Show(_repositoryListView, new Point(8, 8));
                return;
            }

            var item = _repositoryListView.SelectedItems.Count == 0
                ? _repositoryListView.Items[0]
                : _repositoryListView.SelectedItems[0];
            if (_repositoryListView.SelectedItems.Count == 0)
            {
                item.Selected = true;
            }

            item.Focused = true;
            item.EnsureVisible();
            _repositoryContextMenu.Show(_repositoryListView, new Point(Math.Max(8, item.Bounds.Left), item.Bounds.Bottom));
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

            root.Controls.Add(CreateHeading(), 0, 0);
            root.Controls.Add(_platformTabControl, 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);
            Controls.Add(root);
        }

        private Label CreateHeading()
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitRemoteRepositoriesForm.Heading", "Remote Repositories"),
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            return label;
        }

        private Control BuildProviderContent()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(8),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var filterRow = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 1
            };
            filterRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            filterRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var primaryFilterRow = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            primaryFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            primaryFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            primaryFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            primaryFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            primaryFilterRow.Controls.Add(CreateLabel(UiLocalization.Text("GitRemoteRepositoriesForm.Label.Filter", "Filter")), 0, 0);
            primaryFilterRow.Controls.Add(_githubFilterTextBox, 1, 0);
            primaryFilterRow.Controls.Add(_refreshButton, 2, 0);
            primaryFilterRow.Controls.Add(_createRepositoryButton, 3, 0);

            var secondaryFilterRow = new TableLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            secondaryFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            secondaryFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            secondaryFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            secondaryFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            secondaryFilterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            secondaryFilterRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            secondaryFilterRow.Controls.Add(CreateLabel(UiLocalization.Text("GitRemoteRepositoriesForm.Label.Visibility", "&Visibility")), 0, 0);
            secondaryFilterRow.Controls.Add(_visibilityFilterComboBox, 1, 0);
            secondaryFilterRow.Controls.Add(_showOnlyForksCheckBox, 2, 0);
            secondaryFilterRow.Controls.Add(_showOnlyArchivedCheckBox, 3, 0);

            filterRow.Controls.Add(primaryFilterRow, 0, 0);
            filterRow.Controls.Add(secondaryFilterRow, 0, 1);

            root.Controls.Add(filterRow, 0, 0);
            root.Controls.Add(_repositoryListView, 0, 1);
            return root;
        }

        private Control BuildFooter()
        {
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.Controls.Add(_statusLabel, 0, 0);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            buttons.Controls.Add(_closeButton);
            footer.Controls.Add(buttons, 1, 0);
            return footer;
        }

        private static ListView CreateRepositoryListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitRemoteRepositoriesForm.Accessible.Repositories", "Repositories"),
                TabIndex = 0,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Clickable
            };
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.Repository", "Repository"),
                220);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.Description", "Description"),
                320);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.Language", "Language"),
                110);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.DefaultBranch", "Default branch"),
                120);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.UpdatedAt", "Updated"),
                145);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.PushedAt", "Pushed"),
                145);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.Stars", "Stars"),
                70);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.Forks", "Forks"),
                70);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.Watchers", "Watchers"),
                80);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.DiskUsage", "Size (KB)"),
                100);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.License", "License"),
                110);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.Archived", "Archived"),
                90);
            AddRepositoryListColumn(
                listView,
                UiLocalization.Text("GitRemoteRepositoriesForm.Column.Fork", "Fork"),
                65);
            UiTheme.ApplyListView(listView);
            return listView;
        }

        private static void AddRepositoryListColumn(ListView listView, string text, int width)
        {
            listView.Columns.Add(new ColumnHeader
            {
                Text = text,
                Width = width,
                Tag = text
            });
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

        private static ComboBox CreateFilterComboBox()
        {
            var comboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 150,
                AccessibleName = UiLocalization.Text("GitRemoteRepositoriesForm.Accessible.Visibility", "Visibility"),
                TabIndex = 4
            };
            UiTheme.ApplyComboBox(comboBox);
            return comboBox;
        }

        private static CheckBox CreateFilterCheckBox(
            string text,
            string accessibleName,
            int tabIndex)
        {
            var checkBox = new CheckBox
            {
                Text = text,
                AccessibleName = accessibleName,
                Dock = DockStyle.Fill,
                AutoSize = true,
                UseMnemonic = true,
                TabIndex = tabIndex
            };
            UiTheme.ApplyCheckBox(checkBox);
            return checkBox;
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
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }

        private static ListViewItem CreateRepositoryItem(GitHubRepositoryEntry repository)
        {
            var item = new ListViewItem(GetRepositoryDisplayNameWithVisibility(repository));
            item.SubItems.Add(DisplayValue(repository != null ? repository.Description : null));
            item.SubItems.Add(DisplayValue(repository != null ? repository.PrimaryLanguage : null));
            item.SubItems.Add(DisplayValue(repository != null ? repository.DefaultBranch : null));
            item.SubItems.Add(DisplayDateValue(repository != null ? repository.UpdatedAt : null));
            item.SubItems.Add(DisplayDateValue(repository != null ? repository.PushedAt : null));
            item.SubItems.Add(DisplayNumeric(repository != null ? repository.Stars : null));
            item.SubItems.Add(DisplayNumeric(repository != null ? repository.Forks : null));
            item.SubItems.Add(DisplayNumeric(repository != null ? repository.Watchers : null));
            item.SubItems.Add(DisplayNumeric(repository != null ? repository.DiskUsage : null));
            item.SubItems.Add(DisplayValue(repository != null ? repository.License : null));
            item.SubItems.Add(DisplayBooleanValue(repository != null && repository.IsArchived));
            item.SubItems.Add(DisplayBooleanValue(repository != null && repository.IsFork));
            item.Tag = repository;
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
                    return UiLocalization.Text("GitRemoteRepositoriesForm.Value.Private", "Private");
                }

                if (string.Equals(repository.Visibility, "PUBLIC", StringComparison.OrdinalIgnoreCase))
                {
                    return UiLocalization.Text("GitRemoteRepositoriesForm.Value.Public", "Public");
                }

                return repository.Visibility;
            }

            return repository.IsPrivate
                ? UiLocalization.Text("GitRemoteRepositoriesForm.Value.Private", "Private")
                : UiLocalization.Text("GitRemoteRepositoriesForm.Value.Public", "Public");
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

        private static string GetRepositoryDisplayNameWithVisibility(GitHubRepositoryEntry repository)
        {
            var name = GetRepositoryDisplayName(repository);
            var visibility = DisplayRepositoryVisibility(repository);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(visibility))
            {
                return name;
            }

            return string.Format(
                UiLocalization.Text("GitRemoteRepositoriesForm.Value.RepositoryWithVisibility.Template", "{0} ({1})"),
                name,
                visibility);
        }

        private static string GetRepositoryFullName(GitHubRepositoryEntry repository)
        {
            return repository == null || string.IsNullOrWhiteSpace(repository.NameWithOwner)
                ? string.Empty
                : repository.NameWithOwner.Trim().Trim('/');
        }

        private static string NormalizeVisibilityForComparison(string visibility)
        {
            return string.IsNullOrWhiteSpace(visibility)
                ? string.Empty
                : visibility.Trim().ToLowerInvariant();
        }

        private static string FormatRepositoryOperationError(Exception ex)
        {
            var message = ex == null ? string.Empty : ex.Message ?? string.Empty;
            var result = message;
            if (message.IndexOf("Must have admin rights", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("admin rights to Repository", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result += Environment.NewLine + Environment.NewLine +
                    UiLocalization.Text("GitRemoteRepositoriesForm.Message.AdminRightsRequired", "This operation requires repository administrator permission.");
            }

            if (message.IndexOf("delete_repo", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result += Environment.NewLine + Environment.NewLine +
                    UiLocalization.Text("GitRemoteRepositoriesForm.Message.DeleteRepoScopeRequired", "If you are deleting a repository, run: gh auth refresh -h github.com -s delete_repo");
            }

            return string.IsNullOrWhiteSpace(result) ? UiLocalization.Text("GitRemoteRepositoriesForm.Status.RepositoryOperationFailed", "GitHub repository operation failed.") : result.Trim();
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? UiLocalization.Text("GitRemoteRepositoriesForm.Value.NotAvailable", "Not available")
                : value.Trim();
        }

        private static string DisplayDateValue(DateTimeOffset? value)
        {
            return value.HasValue
                ? value.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : DisplayValue(null);
        }

        private static string DisplayNumeric(long? value)
        {
            return value == null
                ? DisplayValue(null)
                : value.Value.ToString();
        }

        private static string DisplayNumeric(int? value)
        {
            return value == null
                ? DisplayValue(null)
                : value.Value.ToString();
        }

        private static string DisplayBooleanValue(bool value)
        {
            return value
                ? UiLocalization.Text("GitRemoteRepositoriesForm.Value.Yes", "Yes")
                : UiLocalization.Text("GitRemoteRepositoriesForm.Value.No", "No");
        }

        private static string BuildGitHubWebUrl(GitHubRepositoryEntry repository, string relativePath)
        {
            if (repository == null)
            {
                return string.Empty;
            }

            var baseUrl = NormalizeGitHubHttpsUrl(repository.HttpsUrl);
            if (string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                var fullName = repository.NameWithOwner.Trim().Trim('/');
                if (fullName.IndexOf('/') > 0)
                {
                    baseUrl = "https://github.com/" + fullName;
                }
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return baseUrl;
            }

            return baseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/');
        }

        private static string NormalizeGitHubHttpsUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            var value = url.Trim();
            if (!value.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - 4)
                : value;
        }

        private sealed class RepositoryRenameForm : Form
        {
            private readonly TextBox _nameTextBox;
            private readonly Button _saveButton;
            private readonly Button _cancelButton;

            public RepositoryRenameForm(GitHubRepositoryEntry repository)
            {
                NewRepositoryName = GetRepositoryDisplayName(repository);
                Text = UiLocalization.Text("GitRemoteRepositoriesForm.RenameForm.Title", "Rename Repository");
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(520, 142);
                UiTheme.ApplyForm(this);

                var label = CreateDialogLabel(UiLocalization.Text("GitRemoteRepositoriesForm.RenameForm.Label.Name", "New name(&N)"));
                _nameTextBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    AccessibleName = UiLocalization.Text("GitRemoteRepositoriesForm.RenameForm.Accessible.Name", "New name"),
                    Text = NewRepositoryName,
                    TabIndex = 0
                };
                UiTheme.ApplyTextBox(_nameTextBox, false, false);

                _saveButton = CreateDialogButton(
                    UiLocalization.Text("GitRemoteRepositoriesForm.RenameForm.Button.Save", "Rename(&R)"),
                    UiLocalization.Text("GitRemoteRepositoriesForm.RenameForm.Accessible.Save", "Rename"),
                    1,
                    UiGlyph.Ok);
                _cancelButton = CreateDialogButton(
                    UiLocalization.Text("Common.Cancel", "Cancel(&A)"),
                    UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                    2,
                    UiGlyph.Cancel);

                _saveButton.Click += (sender, e) => Save();
                _cancelButton.Click += (sender, e) =>
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                AcceptButton = _saveButton;
                CancelButton = _cancelButton;
                BuildDialogLayout(this, label, _nameTextBox, _saveButton, _cancelButton);
            }

            public string NewRepositoryName { get; private set; }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _nameTextBox.SelectAll();
                _nameTextBox.Focus();
            }

            private void Save()
            {
                var name = _nameTextBox.Text == null ? string.Empty : _nameTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitRemoteRepositoriesForm.RenameForm.Message.NameRequired", "Enter the repository name first."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    _nameTextBox.Focus();
                    return;
                }

                if (name.StartsWith("-", StringComparison.Ordinal) ||
                    name.IndexOf('/') >= 0 ||
                    name.IndexOf('\\') >= 0 ||
                    name.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitRemoteRepositoriesForm.RenameForm.Message.NameInvalid", "Repository name cannot contain spaces, slashes, or start with a dash."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    _nameTextBox.Focus();
                    return;
                }

                NewRepositoryName = name;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private sealed class RepositoryMetadataEditForm : Form
        {
            private readonly TextBox _descriptionTextBox;
            private readonly TextBox _homepageTextBox;
            private readonly Button _saveButton;
            private readonly Button _cancelButton;

            public RepositoryMetadataEditForm(GitHubRepositoryEntry repository)
            {
                DescriptionText = repository == null ? string.Empty : repository.Description ?? string.Empty;
                HomepageUrl = repository == null ? string.Empty : repository.HomepageUrl ?? string.Empty;
                Text = UiLocalization.Text("GitRemoteRepositoriesForm.MetadataForm.Title", "Edit Repository Info");
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(560, 184);
                UiTheme.ApplyForm(this);

                _descriptionTextBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    AccessibleName = UiLocalization.Text("GitRemoteRepositoriesForm.MetadataForm.Accessible.Description", "Description"),
                    Text = DescriptionText,
                    TabIndex = 0
                };
                UiTheme.ApplyTextBox(_descriptionTextBox, false, false);

                _homepageTextBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    AccessibleName = UiLocalization.Text("GitRemoteRepositoriesForm.MetadataForm.Accessible.Homepage", "Homepage"),
                    Text = HomepageUrl,
                    TabIndex = 1
                };
                UiTheme.ApplyTextBox(_homepageTextBox, false, false);

                _saveButton = CreateDialogButton(
                    UiLocalization.Text("GitRemoteRepositoriesForm.MetadataForm.Button.Save", "Save(&S)"),
                    UiLocalization.Text("GitRemoteRepositoriesForm.MetadataForm.Accessible.Save", "Save"),
                    2,
                    UiGlyph.Ok);
                _cancelButton = CreateDialogButton(
                    UiLocalization.Text("Common.Cancel", "Cancel(&A)"),
                    UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                    3,
                    UiGlyph.Cancel);

                _saveButton.Click += (sender, e) => Save();
                _cancelButton.Click += (sender, e) =>
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                AcceptButton = _saveButton;
                CancelButton = _cancelButton;
                BuildLayout();
            }

            public string DescriptionText { get; private set; }

            public string HomepageUrl { get; private set; }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _descriptionTextBox.Focus();
            }

            private void Save()
            {
                var homepage = _homepageTextBox.Text == null ? string.Empty : _homepageTextBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(homepage) &&
                    !homepage.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !homepage.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitRemoteRepositoriesForm.MetadataForm.Message.HomepageInvalid", "Homepage must start with http:// or https://."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    _homepageTextBox.Focus();
                    return;
                }

                DescriptionText = _descriptionTextBox.Text == null ? string.Empty : _descriptionTextBox.Text.Trim();
                HomepageUrl = homepage;
                DialogResult = DialogResult.OK;
                Close();
            }

            private void BuildLayout()
            {
                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 3,
                    Padding = new Padding(UiTheme.OuterPadding),
                    BackColor = UiTheme.WindowBackColor,
                    TabStop = false
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

                root.Controls.Add(CreateDialogLabel(UiLocalization.Text("GitRemoteRepositoriesForm.MetadataForm.Label.Description", "Description(&D)")), 0, 0);
                root.Controls.Add(_descriptionTextBox, 1, 0);
                root.Controls.Add(CreateDialogLabel(UiLocalization.Text("GitRemoteRepositoriesForm.MetadataForm.Label.Homepage", "Homepage(&H)")), 0, 1);
                root.Controls.Add(_homepageTextBox, 1, 1);

                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false,
                    TabStop = false
                };
                buttons.Controls.Add(_cancelButton);
                buttons.Controls.Add(_saveButton);
                root.SetColumnSpan(buttons, 2);
                root.Controls.Add(buttons, 0, 2);
                Controls.Add(root);
            }
        }

        private sealed class RepositoryDefaultBranchForm : Form
        {
            private readonly ComboBox _branchComboBox;
            private readonly Button _saveButton;
            private readonly Button _cancelButton;

            public RepositoryDefaultBranchForm(GitHubRepositoryEntry repository, IReadOnlyList<GitHubBranchEntry> branches)
            {
                Text = UiLocalization.Text("GitRemoteRepositoriesForm.DefaultBranchForm.Title", "Set Default Branch");
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(520, 142);
                UiTheme.ApplyForm(this);

                _branchComboBox = new ComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    AccessibleName = UiLocalization.Text("GitRemoteRepositoriesForm.DefaultBranchForm.Accessible.Branch", "Branch"),
                    TabIndex = 0
                };
                UiTheme.ApplyComboBox(_branchComboBox);

                foreach (var branch in branches ?? new GitHubBranchEntry[0])
                {
                    if (branch != null && !string.IsNullOrWhiteSpace(branch.Name))
                    {
                        _branchComboBox.Items.Add(branch.Name.Trim());
                    }
                }

                var current = repository == null ? string.Empty : repository.DefaultBranch ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(current) && _branchComboBox.Items.Contains(current.Trim()))
                {
                    _branchComboBox.SelectedItem = current.Trim();
                }
                else if (_branchComboBox.Items.Count > 0)
                {
                    _branchComboBox.SelectedIndex = 0;
                }

                _saveButton = CreateDialogButton(
                    UiLocalization.Text("GitRemoteRepositoriesForm.DefaultBranchForm.Button.Save", "Save(&S)"),
                    UiLocalization.Text("GitRemoteRepositoriesForm.DefaultBranchForm.Accessible.Save", "Save"),
                    1,
                    UiGlyph.Ok);
                _cancelButton = CreateDialogButton(
                    UiLocalization.Text("Common.Cancel", "Cancel(&A)"),
                    UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                    2,
                    UiGlyph.Cancel);

                _saveButton.Click += (sender, e) => Save();
                _cancelButton.Click += (sender, e) =>
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                AcceptButton = _saveButton;
                CancelButton = _cancelButton;
                BuildDialogLayout(
                    this,
                    CreateDialogLabel(UiLocalization.Text("GitRemoteRepositoriesForm.DefaultBranchForm.Label.Branch", "Branch(&B)")),
                    _branchComboBox,
                    _saveButton,
                    _cancelButton);
            }

            public string BranchName { get; private set; }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _branchComboBox.Focus();
            }

            private void Save()
            {
                var branch = _branchComboBox.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(branch))
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitRemoteRepositoriesForm.DefaultBranchForm.Message.BranchRequired", "Select the default branch first."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    _branchComboBox.Focus();
                    return;
                }

                BranchName = branch.Trim();
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private sealed class RepositoryVisibilityForm : Form
        {
            private readonly ComboBox _visibilityComboBox;
            private readonly Button _saveButton;
            private readonly Button _cancelButton;

            public RepositoryVisibilityForm(GitHubRepositoryEntry repository)
            {
                Text = UiLocalization.Text("GitRemoteRepositoriesForm.VisibilityForm.Title", "Change Visibility");
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(520, 142);
                UiTheme.ApplyForm(this);

                _visibilityComboBox = new ComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    AccessibleName = UiLocalization.Text("GitRemoteRepositoriesForm.VisibilityForm.Accessible.Visibility", "Visibility"),
                    TabIndex = 0
                };
                UiTheme.ApplyComboBox(_visibilityComboBox);
                _visibilityComboBox.Items.Add(UiLocalization.Text("GitRemoteRepositoriesForm.VisibilityForm.Value.Public", "Public"));
                _visibilityComboBox.Items.Add(UiLocalization.Text("GitRemoteRepositoriesForm.VisibilityForm.Value.Private", "Private"));
                _visibilityComboBox.Items.Add(UiLocalization.Text("GitRemoteRepositoriesForm.VisibilityForm.Value.Internal", "Internal"));

                var current = NormalizeVisibilityForComparison(repository == null ? null : repository.Visibility);
                if (current == "private")
                {
                    _visibilityComboBox.SelectedIndex = 1;
                }
                else if (current == "internal")
                {
                    _visibilityComboBox.SelectedIndex = 2;
                }
                else
                {
                    _visibilityComboBox.SelectedIndex = 0;
                }

                _saveButton = CreateDialogButton(
                    UiLocalization.Text("GitRemoteRepositoriesForm.VisibilityForm.Button.Save", "Save(&S)"),
                    UiLocalization.Text("GitRemoteRepositoriesForm.VisibilityForm.Accessible.Save", "Save"),
                    1,
                    UiGlyph.Ok);
                _cancelButton = CreateDialogButton(
                    UiLocalization.Text("Common.Cancel", "Cancel(&A)"),
                    UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                    2,
                    UiGlyph.Cancel);

                _saveButton.Click += (sender, e) => Save();
                _cancelButton.Click += (sender, e) =>
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                AcceptButton = _saveButton;
                CancelButton = _cancelButton;
                BuildDialogLayout(
                    this,
                    CreateDialogLabel(UiLocalization.Text("GitRemoteRepositoriesForm.VisibilityForm.Label.Visibility", "Visibility(&V)")),
                    _visibilityComboBox,
                    _saveButton,
                    _cancelButton);
            }

            public string Visibility { get; private set; }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _visibilityComboBox.Focus();
            }

            private void Save()
            {
                Visibility = _visibilityComboBox.SelectedIndex == 1
                    ? "private"
                    : (_visibilityComboBox.SelectedIndex == 2 ? "internal" : "public");
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private sealed class RepositoryDeleteConfirmationForm : Form
        {
            private readonly string _repositoryFullName;
            private readonly TextBox _confirmationTextBox;
            private readonly Button _deleteButton;
            private readonly Button _cancelButton;

            public RepositoryDeleteConfirmationForm(string repositoryFullName)
            {
                _repositoryFullName = repositoryFullName ?? string.Empty;
                Text = UiLocalization.Text("GitRemoteRepositoriesForm.DeleteConfirmation.Title", "Confirm Repository Deletion");
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(520, 176);
                UiTheme.ApplyForm(this);

                var label = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = string.Format(
                        UiLocalization.Text("GitRemoteRepositoriesForm.DeleteConfirmation.Label.Template", "Type {0} to confirm permanent deletion."),
                        _repositoryFullName),
                    TextAlign = ContentAlignment.MiddleLeft,
                    TabStop = false
                };
                UiTheme.ApplyLabel(label, true);

                _confirmationTextBox = new TextBox
                {
                    Dock = DockStyle.Fill,
                    AccessibleName = UiLocalization.Text("GitRemoteRepositoriesForm.DeleteConfirmation.Accessible.Confirmation", "Confirmation"),
                    TabIndex = 0
                };
                UiTheme.ApplyTextBox(_confirmationTextBox, false, false);

                _deleteButton = new Button
                {
                    Text = UiLocalization.Text("GitRemoteRepositoriesForm.DeleteConfirmation.Button.Delete", "Delete(&D)"),
                    AccessibleName = UiLocalization.Text("GitRemoteRepositoriesForm.DeleteConfirmation.Accessible.Delete", "Delete"),
                    Width = 112,
                    Height = UiTheme.ButtonHeight,
                    UseMnemonic = true,
                    TabIndex = 1
                };
                UiTheme.ApplyButton(_deleteButton, UiGlyph.Error);

                _cancelButton = new Button
                {
                    Text = UiLocalization.Text("Common.Cancel", "Cancel(&A)"),
                    AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                    Width = 112,
                    Height = UiTheme.ButtonHeight,
                    UseMnemonic = true,
                    TabIndex = 2
                };
                UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

                _deleteButton.Click += (sender, e) => ConfirmDelete();
                _cancelButton.Click += (sender, e) =>
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                AcceptButton = _deleteButton;
                CancelButton = _cancelButton;
                BuildLayout(label);
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _confirmationTextBox.Focus();
            }

            private void ConfirmDelete()
            {
                var text = _confirmationTextBox.Text == null ? string.Empty : _confirmationTextBox.Text.Trim();
                if (string.Equals(text, _repositoryFullName, StringComparison.Ordinal))
                {
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitRemoteRepositoriesForm.DeleteConfirmation.Message.Mismatch", "The entered repository name does not match."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                _confirmationTextBox.SelectAll();
                _confirmationTextBox.Focus();
            }

            private void BuildLayout(Control label)
            {
                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    Padding = new Padding(UiTheme.OuterPadding),
                    BackColor = UiTheme.WindowBackColor,
                    TabStop = false
                };
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = FlowDirection.RightToLeft,
                    WrapContents = false,
                    TabStop = false
                };
                buttons.Controls.Add(_cancelButton);
                buttons.Controls.Add(_deleteButton);

                root.Controls.Add(label, 0, 0);
                root.Controls.Add(_confirmationTextBox, 0, 1);
                root.Controls.Add(buttons, 0, 2);
                Controls.Add(root);
            }
        }

        private static Label CreateDialogLabel(string text)
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private static Button CreateDialogButton(string text, string accessibleName, int tabIndex, UiGlyph glyph)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 112,
                Height = UiTheme.ButtonHeight,
                UseMnemonic = true,
                TabIndex = tabIndex
            };
            UiTheme.ApplyButton(button, glyph);
            return button;
        }

        private static void BuildDialogLayout(Form form, Control label, Control input, Button saveButton, Button cancelButton)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                TabStop = false
            };
            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(saveButton);

            root.Controls.Add(label, 0, 0);
            root.Controls.Add(input, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            form.Controls.Add(root);
        }
    }
}
