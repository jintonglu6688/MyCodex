using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal enum GitHubRepositoryDetailsMode
    {
        Manage = 0,
        PublicBrowse = 1
    }

    internal sealed class GitHubRepositoryDetailsForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _loadCancellation != null;

        private const int DefaultListLimit = 50;

        private readonly IGitHubCliService _gitHubCliService;
        private readonly GitHubRepositoryEntry _repository;
        private readonly IScreenReaderService _screenReaderService;
        private readonly string _currentWorkspacePath;
        private readonly IGitRepositoryService _gitRepositoryService;
        private readonly GitHubRepositoryDetailsMode _mode;
        private readonly TabControl _tabs;
        private readonly TabPage _overviewTabPage;
        private readonly TabPage _branchesTabPage;
        private readonly TabPage _tagsTabPage;
        private readonly TabPage _releasesTabPage;
        private readonly TabPage _issuesTabPage;
        private readonly TabPage _pullRequestsTabPage;
        private readonly TabPage _contributorsTabPage;
        private readonly ListBox _overviewListBox;
        private readonly ListView _branchesListView;
        private readonly ListView _tagsListView;
        private readonly ListView _releasesListView;
        private readonly ListView _issuesListView;
        private readonly ListView _pullRequestsListView;
        private readonly ListView _contributorsListView;
        private readonly TextBox _detailsTextBox;
        private readonly Label _statusLabel;
        private readonly ComboBox _issueStateFilterComboBox;
        private readonly ComboBox _issueScopeFilterComboBox;
        private readonly Button _issueApplyFilterButton;
        private readonly ComboBox _pullRequestStateFilterComboBox;
        private readonly ComboBox _pullRequestScopeFilterComboBox;
        private readonly Button _pullRequestApplyFilterButton;
        private readonly Button _refreshButton;
        private readonly Button _closeButton;
        private readonly ContextMenu _branchesContextMenu;
        private readonly MenuItem _copyBranchNameMenuItem;
        private readonly MenuItem _copyBranchCommitShaMenuItem;
        private readonly MenuItem _openBranchMenuItem;
        private readonly MenuItem _setDefaultBranchMenuItem;
        private readonly MenuItem _deleteBranchMenuItem;
        private readonly MenuItem _checkoutBranchInWorkspaceMenuItem;
        private readonly ContextMenu _tagsContextMenu;
        private readonly MenuItem _copyTagNameMenuItem;
        private readonly MenuItem _copyTagCommitShaMenuItem;
        private readonly MenuItem _openTagMenuItem;
        private readonly MenuItem _openTagZipballMenuItem;
        private readonly MenuItem _openTagTarballMenuItem;
        private readonly ContextMenu _releasesContextMenu;
        private readonly MenuItem _viewReleaseDetailsMenuItem;
        private readonly ContextMenu _issuesContextMenu;
        private readonly MenuItem _createIssueMenuItem;
        private readonly MenuItem _openIssueMenuItem;
        private readonly MenuItem _copyIssueUrlMenuItem;
        private readonly MenuItem _viewIssueDiscussionMenuItem;
        private readonly MenuItem _commentIssueMenuItem;
        private readonly MenuItem _closeIssueMenuItem;
        private readonly MenuItem _reopenIssueMenuItem;
        private readonly ContextMenu _pullRequestsContextMenu;
        private readonly MenuItem _createPullRequestMenuItem;
        private readonly MenuItem _openPullRequestMenuItem;
        private readonly MenuItem _copyPullRequestUrlMenuItem;
        private readonly MenuItem _viewPullRequestDiscussionMenuItem;
        private readonly MenuItem _commentPullRequestMenuItem;
        private readonly MenuItem _checkoutPullRequestMenuItem;
        private readonly MenuItem _closePullRequestMenuItem;
        private readonly MenuItem _reopenPullRequestMenuItem;
        private readonly HashSet<TabPage> _loadedTabs = new HashSet<TabPage>();
        private GitHubRepositoryEntry _repositoryDetails;
        private CancellationTokenSource _loadCancellation;
        private string _matchingLocalRemoteName;
        private string _currentLocalBranch;
        private string _issueStateFilter = "all";
        private string _issueScopeFilter = "all";
        private string _pullRequestStateFilter = "all";
        private string _pullRequestScopeFilter = "all";
        private bool _loadedCurrentTabWithFallback;

        public GitHubRepositoryDetailsForm(
            IGitHubCliService gitHubCliService,
            GitHubRepositoryEntry repository,
            IScreenReaderService screenReaderService,
            string currentWorkspacePath = null,
            IGitRepositoryService gitRepositoryService = null,
            GitHubRepositoryDetailsMode mode = GitHubRepositoryDetailsMode.Manage)
        {
            _gitHubCliService = gitHubCliService ?? throw new ArgumentNullException(nameof(gitHubCliService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _repositoryDetails = repository;
            _screenReaderService = screenReaderService;
            _currentWorkspacePath = string.IsNullOrWhiteSpace(currentWorkspacePath) ? null : currentWorkspacePath.Trim();
            _gitRepositoryService = gitRepositoryService;
            _mode = mode;

            Text = string.Format(
                UiLocalization.Text("GitHubRepositoryDetailsForm.Title.Template", "{0} Details"),
                DisplayValue(_repository.NameWithOwner));
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 620);
            Size = new Size(1060, 720);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                AccessibleName = string.Empty,
                TabIndex = 0
            };
            _overviewTabPage = new TabPage(UiLocalization.Text("GitHubRepositoryDetailsForm.Tab.Overview", "Overview"));
            _branchesTabPage = new TabPage(UiLocalization.Text("GitHubRepositoryDetailsForm.Tab.Branches", "Branches"));
            _tagsTabPage = new TabPage(UiLocalization.Text("GitHubRepositoryDetailsForm.Tab.Tags", "Tags"));
            _releasesTabPage = new TabPage(UiLocalization.Text("GitHubRepositoryDetailsForm.Tab.Releases", "Releases"));
            _issuesTabPage = new TabPage(UiLocalization.Text("GitHubRepositoryDetailsForm.Tab.Issues", "Issues"));
            _pullRequestsTabPage = new TabPage(UiLocalization.Text("GitHubRepositoryDetailsForm.Tab.PullRequests", "Pull Requests"));
            _contributorsTabPage = new TabPage(UiLocalization.Text("GitHubRepositoryDetailsForm.Tab.Contributors", "Contributors"));

            _overviewListBox = CreateListBox(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.Overview", "Overview"));
            _branchesListView = CreateBranchesListView();
            _tagsListView = CreateTagsListView();
            _releasesListView = CreateReleasesListView();
            _issuesListView = CreateIssuesListView();
            _pullRequestsListView = CreatePullRequestsListView();
            _contributorsListView = CreateContributorsListView();
            _detailsTextBox = CreateDetailsTextBox();
            _issueStateFilterComboBox = CreateFilterComboBox(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.IssueStateFilter", "Issue state"));
            AddFilterOption(_issueStateFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.State.All", "All"), "all");
            AddFilterOption(_issueStateFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.State.Open", "Open"), "open");
            AddFilterOption(_issueStateFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.State.Closed", "Closed"), "closed");
            _issueStateFilterComboBox.SelectedIndex = 0;
            _issueScopeFilterComboBox = CreateFilterComboBox(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.IssueScopeFilter", "Issue scope"));
            AddFilterOption(_issueScopeFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.IssueScope.All", "All"), "all");
            AddFilterOption(_issueScopeFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.IssueScope.Assigned", "Assigned to me"), "assigned");
            AddFilterOption(_issueScopeFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.IssueScope.Created", "Created by me"), "authored");
            _issueScopeFilterComboBox.SelectedIndex = 0;
            _issueApplyFilterButton = CreateButton(
                UiLocalization.Text("GitHubRepositoryDetailsForm.Button.ApplyIssueFilter", "Apply(&A)"),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.ApplyIssueFilter", "Apply issue filter"),
                0);
            _pullRequestStateFilterComboBox = CreateFilterComboBox(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.PullRequestStateFilter", "PR state"));
            AddFilterOption(_pullRequestStateFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.State.All", "All"), "all");
            AddFilterOption(_pullRequestStateFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.State.Open", "Open"), "open");
            AddFilterOption(_pullRequestStateFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.State.Closed", "Closed"), "closed");
            AddFilterOption(_pullRequestStateFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.State.Merged", "Merged"), "merged");
            _pullRequestStateFilterComboBox.SelectedIndex = 0;
            _pullRequestScopeFilterComboBox = CreateFilterComboBox(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.PullRequestScopeFilter", "PR scope"));
            AddFilterOption(_pullRequestScopeFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.Scope.All", "All"), "all");
            AddFilterOption(_pullRequestScopeFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.Scope.Authored", "Authored by me"), "authored");
            AddFilterOption(_pullRequestScopeFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.Scope.Assigned", "Assigned to me"), "assigned");
            AddFilterOption(_pullRequestScopeFilterComboBox, UiLocalization.Text("GitHubRepositoryDetailsForm.Filter.Scope.ReviewRequested", "Review requested"), "review-requested");
            _pullRequestScopeFilterComboBox.SelectedIndex = 0;
            _pullRequestApplyFilterButton = CreateButton(
                UiLocalization.Text("GitHubRepositoryDetailsForm.Button.ApplyPullRequestFilter", "Apply(&A)"),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.ApplyPullRequestFilter", "Apply PR filter"),
                0);
            _branchesContextMenu = new ContextMenu();
            _copyBranchNameMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CopyBranchName", "Copy Branch Name(&N)"));
            _copyBranchCommitShaMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CopyCommitSha", "Copy Commit SHA(&S)"));
            _openBranchMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.OpenBranch", "Open Branch(&O)"));
            _setDefaultBranchMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.SetDefaultBranch", "Set Default Branch(&B)"));
            _deleteBranchMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.DeleteBranch", "Delete Remote Branch(&D)"));
            _checkoutBranchInWorkspaceMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CheckoutBranchInWorkspace", "Fetch and Switch in Current Workspace(&W)"));
            _branchesListView.ContextMenu = _branchesContextMenu;
            _tagsContextMenu = new ContextMenu();
            _copyTagNameMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CopyTagName", "Copy Tag Name(&N)"));
            _copyTagCommitShaMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CopyTagCommitSha", "Copy Commit SHA(&S)"));
            _openTagMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.OpenTag", "Open Tag(&O)"));
            _openTagZipballMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.OpenTagZipball", "Open ZIP Archive(&Z)"));
            _openTagTarballMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.OpenTagTarball", "Open TAR Archive(&T)"));
            _tagsListView.ContextMenu = _tagsContextMenu;
            _releasesContextMenu = new ContextMenu();
            _viewReleaseDetailsMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.ViewReleaseDetails", "View Release Details(&D)"));
            _releasesContextMenu.MenuItems.Add(_viewReleaseDetailsMenuItem);
            _releasesListView.ContextMenu = _releasesContextMenu;
            _issuesContextMenu = new ContextMenu();
            _createIssueMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CreateIssue", "Create Issue(&C)"));
            _openIssueMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.OpenIssue", "Open Issue(&O)"));
            _copyIssueUrlMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CopyIssueUrl", "Copy Issue URL(&U)"));
            _viewIssueDiscussionMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.ViewDiscussion", "View Discussion(&D)"));
            _commentIssueMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CommentIssue", "Add Comment(&M)"));
            _closeIssueMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CloseIssue", "Close Issue(&L)"));
            _reopenIssueMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.ReopenIssue", "Reopen Issue(&E)"));
            _issuesListView.ContextMenu = _issuesContextMenu;
            _pullRequestsContextMenu = new ContextMenu();
            _createPullRequestMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CreatePullRequest", "Create PR from Current Branch(&C)"));
            _openPullRequestMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.OpenPullRequest", "Open PR(&O)"));
            _copyPullRequestUrlMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CopyPullRequestUrl", "Copy PR URL(&U)"));
            _viewPullRequestDiscussionMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.ViewDiscussion", "View Discussion(&D)"));
            _commentPullRequestMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CommentPullRequest", "Add Comment(&M)"));
            _checkoutPullRequestMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.CheckoutPullRequest", "Checkout PR in Current Workspace(&W)"));
            _closePullRequestMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.ClosePullRequest", "Close PR(&L)"));
            _reopenPullRequestMenuItem = new MenuItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Menu.ReopenPullRequest", "Reopen PR(&E)"));
            _pullRequestsListView.ContextMenu = _pullRequestsContextMenu;
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, true);

            _refreshButton = CreateButton(
                UiLocalization.Text("GitHubRepositoryDetailsForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.Refresh", "Refresh"),
                2);
            _closeButton = CreateButton(
                UiLocalization.Text("GitHubRepositoryDetailsForm.Button.Close", "Close(&C)"),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.Close", "Close"),
                3);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_issueApplyFilterButton, UiGlyph.Search);
            UiTheme.ApplyButton(_pullRequestApplyFilterButton, UiGlyph.Search);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            ConfigureHiddenCloseButton(_closeButton);

            _refreshButton.Click += async (sender, e) => await LoadCurrentTabAsync(true, true);
            _issueApplyFilterButton.Click += async (sender, e) => await ApplyIssueFiltersAsync();
            _pullRequestApplyFilterButton.Click += async (sender, e) => await ApplyPullRequestFiltersAsync();
            _closeButton.Click += (sender, e) => Close();
            _tabs.SelectedIndexChanged += (sender, e) => UpdateSelectedTabLoadPrompt();
            _overviewListBox.SelectedIndexChanged += (sender, e) => UpdateOverviewDetails();
            _branchesListView.SelectedIndexChanged += (sender, e) => UpdateBranchDetails();
            _tagsListView.SelectedIndexChanged += (sender, e) => UpdateTagDetails();
            _releasesListView.SelectedIndexChanged += (sender, e) => UpdateReleaseDetails();
            _issuesListView.SelectedIndexChanged += (sender, e) => UpdateIssueDetails();
            _pullRequestsListView.SelectedIndexChanged += (sender, e) => UpdatePullRequestDetails();
            _contributorsListView.SelectedIndexChanged += (sender, e) => UpdateContributorDetails();
            _copyBranchNameMenuItem.Click += (sender, e) => CopySelectedBranchName();
            _copyBranchCommitShaMenuItem.Click += (sender, e) => CopySelectedBranchCommitSha();
            _openBranchMenuItem.Click += (sender, e) => OpenSelectedBranch();
            _setDefaultBranchMenuItem.Click += async (sender, e) => await SetSelectedBranchAsDefaultAsync();
            _deleteBranchMenuItem.Click += async (sender, e) => await DeleteSelectedBranchAsync();
            _checkoutBranchInWorkspaceMenuItem.Click += async (sender, e) => await CheckoutSelectedBranchInWorkspaceAsync();
            _copyTagNameMenuItem.Click += (sender, e) => CopySelectedTagName();
            _copyTagCommitShaMenuItem.Click += (sender, e) => CopySelectedTagCommitSha();
            _openTagMenuItem.Click += (sender, e) => OpenSelectedTag();
            _openTagZipballMenuItem.Click += (sender, e) => OpenSelectedTagArchive(true);
            _openTagTarballMenuItem.Click += (sender, e) => OpenSelectedTagArchive(false);
            _viewReleaseDetailsMenuItem.Click += (sender, e) => ShowReleaseDetails();
            _createIssueMenuItem.Click += async (sender, e) => await CreateIssueAsync();
            _openIssueMenuItem.Click += (sender, e) => OpenSelectedIssue();
            _copyIssueUrlMenuItem.Click += (sender, e) => CopySelectedIssueUrl();
            _viewIssueDiscussionMenuItem.Click += (sender, e) => ShowIssueDiscussion();
            _commentIssueMenuItem.Click += async (sender, e) => await AddCommentToSelectedIssueAsync();
            _closeIssueMenuItem.Click += async (sender, e) => await CloseSelectedIssueAsync();
            _reopenIssueMenuItem.Click += async (sender, e) => await ReopenSelectedIssueAsync();
            _createPullRequestMenuItem.Click += async (sender, e) => await CreatePullRequestFromCurrentBranchAsync();
            _openPullRequestMenuItem.Click += (sender, e) => OpenSelectedPullRequest();
            _copyPullRequestUrlMenuItem.Click += (sender, e) => CopySelectedPullRequestUrl();
            _viewPullRequestDiscussionMenuItem.Click += (sender, e) => ShowPullRequestDiscussion();
            _commentPullRequestMenuItem.Click += async (sender, e) => await AddCommentToSelectedPullRequestAsync();
            _checkoutPullRequestMenuItem.Click += async (sender, e) => await CheckoutSelectedPullRequestAsync();
            _closePullRequestMenuItem.Click += async (sender, e) => await CloseSelectedPullRequestAsync();
            _reopenPullRequestMenuItem.Click += async (sender, e) => await ReopenSelectedPullRequestAsync();
            _branchesContextMenu.Popup += (sender, e) => RefreshContextMenus();
            _tagsContextMenu.Popup += (sender, e) => RefreshContextMenus();
            _releasesContextMenu.Popup += (sender, e) => RefreshContextMenus();
            _issuesContextMenu.Popup += (sender, e) => RefreshContextMenus();
            _pullRequestsContextMenu.Popup += (sender, e) => RefreshContextMenus();
            _branchesListView.KeyDown += (sender, e) => HandleListContextMenuKeyDown(_branchesListView, _branchesContextMenu, e);
            _tagsListView.KeyDown += (sender, e) => HandleListContextMenuKeyDown(_tagsListView, _tagsContextMenu, e);
            _releasesListView.KeyDown += (sender, e) => HandleListContextMenuKeyDown(_releasesListView, _releasesContextMenu, e);
            _issuesListView.KeyDown += (sender, e) => HandleListContextMenuKeyDown(_issuesListView, _issuesContextMenu, e);
            _pullRequestsListView.KeyDown += (sender, e) => HandleListContextMenuKeyDown(_pullRequestsListView, _pullRequestsContextMenu, e);
            KeyDown += GitHubRepositoryDetailsForm_KeyDown;
            FormClosing += GitHubRepositoryDetailsForm_FormClosing;
            Shown += async (sender, e) =>
            {
                Activate();
                await LoadCurrentTabAsync(false, true);
            };

            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Ready", "Repository details are ready."), false);
        }

        private async void GitHubRepositoryDetailsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 && _loadCancellation == null)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await LoadCurrentTabAsync(true, true);
            }
        }

        private void GitHubRepositoryDetailsForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_loadCancellation != null)
            {
                _loadCancellation.Cancel();
            }
        }

        private void UpdateSelectedTabLoadPrompt()
        {
            if (_tabs.SelectedTab == null)
            {
                return;
            }

            if (_loadedTabs.Contains(_tabs.SelectedTab))
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Loaded", "Repository section loaded."), false);
                return;
            }

            SetStatus(
                UiLocalization.Text(
                    "GitHubRepositoryDetailsForm.Status.RefreshRequired",
                    "Press Refresh to load this repository section."),
                false);
        }

        private async Task LoadCurrentTabAsync(bool force, bool focusAfterLoad)
        {
            if (_loadCancellation != null)
            {
                return;
            }

            var tab = _tabs.SelectedTab;
            if (!force && _loadedTabs.Contains(tab))
            {
                if (focusAfterLoad)
                {
                    FocusCurrentTabPrimaryControl();
                }

                return;
            }

            _loadCancellation = new CancellationTokenSource();
            _loadedCurrentTabWithFallback = false;
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Loading", "Loading repository section..."), false);
            try
            {
                if (ReferenceEquals(tab, _overviewTabPage))
                {
                    await LoadOverviewAsync(_loadCancellation.Token);
                }
                else if (ReferenceEquals(tab, _branchesTabPage))
                {
                    await LoadBranchesAsync(_loadCancellation.Token);
                }
                else if (ReferenceEquals(tab, _tagsTabPage))
                {
                    await LoadTagsAsync(_loadCancellation.Token);
                }
                else if (ReferenceEquals(tab, _releasesTabPage))
                {
                    await LoadReleasesAsync(_loadCancellation.Token);
                }
                else if (ReferenceEquals(tab, _issuesTabPage))
                {
                    await LoadIssuesAsync(_loadCancellation.Token);
                }
                else if (ReferenceEquals(tab, _pullRequestsTabPage))
                {
                    await LoadPullRequestsAsync(_loadCancellation.Token);
                }
                else if (ReferenceEquals(tab, _contributorsTabPage))
                {
                    await LoadContributorsAsync(_loadCancellation.Token);
                }

                _loadedTabs.Add(tab);
                SetStatus(
                    _loadedCurrentTabWithFallback
                        ? UiLocalization.Text("GitHubRepositoryDetailsForm.Status.LoadedFromFallback", "Repository overview loaded from available search data. Some details may be missing.")
                        : UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Loaded", "Repository section loaded."),
                    false);
                if (focusAfterLoad && ReferenceEquals(_tabs.SelectedTab, tab))
                {
                    FocusCurrentTabPrimaryControl();
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
            }
            catch (Exception ex)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, ex.ToString());
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Failed", "Failed to load repository section."), true);
            }
            finally
            {
                if (_loadCancellation != null)
                {
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                }

                SetBusy(false);
            }
        }

        private async Task LoadOverviewAsync(CancellationToken cancellationToken)
        {
            try
            {
                _repositoryDetails = await _gitHubCliService.GetRepositoryDetailsAsync(_repository, cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (!HasRepositoryOverviewFallback(_repository))
                {
                    throw;
                }

                _repositoryDetails = _repository;
                _loadedCurrentTabWithFallback = true;
            }

            PopulateOverviewList();
        }

        private void PopulateOverviewList()
        {
            _overviewListBox.BeginUpdate();
            try
            {
                _overviewListBox.Items.Clear();
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Name", "Name"), DisplayValue(_repositoryDetails.NameWithOwner));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Description", "Description"), DisplayValue(_repositoryDetails.Description));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Visibility", "Visibility"), DisplayValue(_repositoryDetails.Visibility));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.DefaultBranch", "Default branch"), DisplayValue(_repositoryDetails.DefaultBranch));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.PrimaryLanguage", "Primary language"), DisplayValue(_repositoryDetails.PrimaryLanguage));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.License", "License"), DisplayValue(_repositoryDetails.License));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Stars", "Stars"), FormatNumber(_repositoryDetails.Stars));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Forks", "Forks"), FormatNumber(_repositoryDetails.Forks));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Watchers", "Watchers"), FormatNumber(_repositoryDetails.Watchers));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.OpenIssues", "Open issues"), FormatNumber(_repositoryDetails.OpenIssues));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Topics", "Topics"), DisplayValue(_repositoryDetails.Topics));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.DiskUsage", "Disk usage"), FormatDiskUsage(_repositoryDetails.DiskUsage));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.CreatedAt", "Created"), FormatDate(_repositoryDetails.CreatedAt));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.UpdatedAt", "Updated"), FormatDate(_repositoryDetails.UpdatedAt));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.PushedAt", "Pushed"), FormatDate(_repositoryDetails.PushedAt));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Archived", "Archived"), FormatBoolean(_repositoryDetails.IsArchived));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Fork", "Fork"), FormatBoolean(_repositoryDetails.IsFork));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.HomepageUrl", "Homepage"), DisplayValue(_repositoryDetails.HomepageUrl));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.HttpsUrl", "HTTPS URL"), DisplayValue(_repositoryDetails.HttpsUrl));
                AddOverviewItem(UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.SshUrl", "SSH URL"), DisplayValue(_repositoryDetails.SshUrl));
            }
            finally
            {
                _overviewListBox.EndUpdate();
            }

            if (_overviewListBox.Items.Count > 0)
            {
                _overviewListBox.SelectedIndex = 0;
            }
        }

        private static bool HasRepositoryOverviewFallback(GitHubRepositoryEntry repository)
        {
            return repository != null && !string.IsNullOrWhiteSpace(repository.NameWithOwner);
        }

        private async Task LoadBranchesAsync(CancellationToken cancellationToken)
        {
            var entries = await _gitHubCliService.ListBranchesAsync(_repository, DefaultListLimit, cancellationToken).ConfigureAwait(true);
            await RefreshMatchingLocalRemoteAsync(cancellationToken).ConfigureAwait(true);
            _branchesListView.BeginUpdate();
            try
            {
                _branchesListView.Items.Clear();
                foreach (var entry in entries ?? new GitHubBranchEntry[0])
                {
                    var item = new ListViewItem(DisplayValue(entry.Name));
                    item.SubItems.Add(ShortSha(entry.CommitSha));
                    item.SubItems.Add(FormatBoolean(entry.IsProtected));
                    item.Tag = entry;
                    _branchesListView.Items.Add(item);
                }
            }
            finally
            {
                _branchesListView.EndUpdate();
            }

            SelectFirstItemOrShowEmpty(
                _branchesListView,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Empty.Branches", "No branches found."));
        }

        private async Task RefreshMatchingLocalRemoteAsync(CancellationToken cancellationToken)
        {
            _matchingLocalRemoteName = null;
            _currentLocalBranch = null;
            if (_gitRepositoryService == null || string.IsNullOrWhiteSpace(_currentWorkspacePath))
            {
                return;
            }

            var remotes = await _gitRepositoryService.ReadRemotesAsync(_currentWorkspacePath, cancellationToken).ConfigureAwait(true);
            foreach (var remote in remotes ?? new GitRemoteEntry[0])
            {
                if (remote == null || string.IsNullOrWhiteSpace(remote.Name))
                {
                    continue;
                }

                if (RepositoryMatchesRemote(remote.FetchUrl) || RepositoryMatchesRemote(remote.PushUrl))
                {
                    _matchingLocalRemoteName = remote.Name.Trim();
                    break;
                }
            }

            var status = await _gitRepositoryService.ReadStatusAsync(_currentWorkspacePath, cancellationToken).ConfigureAwait(true);
            _currentLocalBranch = status == null || string.IsNullOrWhiteSpace(status.Branch)
                ? null
                : status.Branch.Trim();
        }

        private async Task LoadTagsAsync(CancellationToken cancellationToken)
        {
            var entries = await _gitHubCliService.ListTagsAsync(_repository, DefaultListLimit, cancellationToken).ConfigureAwait(true);
            _tagsListView.BeginUpdate();
            try
            {
                _tagsListView.Items.Clear();
                foreach (var entry in entries ?? new GitHubTagEntry[0])
                {
                    var item = new ListViewItem(DisplayValue(entry.Name));
                    item.SubItems.Add(ShortSha(entry.CommitSha));
                    item.Tag = entry;
                    _tagsListView.Items.Add(item);
                }
            }
            finally
            {
                _tagsListView.EndUpdate();
            }

            SelectFirstItemOrShowEmpty(
                _tagsListView,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Empty.Tags", "No tags found."));
        }

        private async Task LoadReleasesAsync(CancellationToken cancellationToken)
        {
            var entries = await _gitHubCliService.ListReleasesAsync(_repository, DefaultListLimit, cancellationToken).ConfigureAwait(true);
            _releasesListView.BeginUpdate();
            try
            {
                _releasesListView.Items.Clear();
                foreach (var entry in entries ?? new GitHubReleaseEntry[0])
                {
                    var item = new ListViewItem(DisplayValue(entry.Name, entry.TagName));
                    item.SubItems.Add(DisplayValue(entry.TagName));
                    item.SubItems.Add(BuildReleaseState(entry));
                    item.SubItems.Add(FormatDate(entry.PublishedAt ?? entry.CreatedAt));
                    item.Tag = entry;
                    _releasesListView.Items.Add(item);
                }
            }
            finally
            {
                _releasesListView.EndUpdate();
            }

            SelectFirstItemOrShowEmpty(
                _releasesListView,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Empty.Releases", "No releases found."));
        }

        private async Task LoadIssuesAsync(CancellationToken cancellationToken)
        {
            var entries = await _gitHubCliService.ListIssuesAsync(
                _repository,
                DefaultListLimit,
                _issueStateFilter,
                _issueScopeFilter,
                cancellationToken).ConfigureAwait(true);
            _issuesListView.BeginUpdate();
            try
            {
                _issuesListView.Items.Clear();
                foreach (var entry in entries ?? new GitHubIssueEntry[0])
                {
                    var item = new ListViewItem(BuildNumberTitle(entry.Number, entry.Title));
                    item.SubItems.Add(DisplayValue(entry.State));
                    item.SubItems.Add(DisplayValue(entry.Author));
                    item.SubItems.Add(entry.Comments.ToString());
                    item.SubItems.Add(FormatDate(entry.UpdatedAt ?? entry.CreatedAt));
                    item.Tag = entry;
                    _issuesListView.Items.Add(item);
                }
            }
            finally
            {
                _issuesListView.EndUpdate();
            }

            SelectFirstItemOrShowEmpty(
                _issuesListView,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Empty.Issues", "No issues found."));
        }

        private async Task LoadPullRequestsAsync(CancellationToken cancellationToken)
        {
            await RefreshMatchingLocalRemoteAsync(cancellationToken).ConfigureAwait(true);
            var entries = await _gitHubCliService.ListPullRequestsAsync(
                _repository,
                DefaultListLimit,
                _pullRequestStateFilter,
                _pullRequestScopeFilter,
                cancellationToken).ConfigureAwait(true);
            _pullRequestsListView.BeginUpdate();
            try
            {
                _pullRequestsListView.Items.Clear();
                foreach (var entry in entries ?? new GitHubPullRequestEntry[0])
                {
                    var item = new ListViewItem(BuildNumberTitle(entry.Number, entry.Title));
                    item.SubItems.Add(BuildPullRequestState(entry));
                    item.SubItems.Add(DisplayValue(entry.Author));
                    item.SubItems.Add(DisplayValue(entry.ReviewDecision));
                    item.SubItems.Add(FormatDate(entry.UpdatedAt ?? entry.CreatedAt));
                    item.Tag = entry;
                    _pullRequestsListView.Items.Add(item);
                }
            }
            finally
            {
                _pullRequestsListView.EndUpdate();
            }

            SelectFirstItemOrShowEmpty(
                _pullRequestsListView,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Empty.PullRequests", "No pull requests found."));
        }

        private async Task LoadContributorsAsync(CancellationToken cancellationToken)
        {
            var entries = await _gitHubCliService.ListContributorsAsync(_repository, DefaultListLimit, cancellationToken).ConfigureAwait(true);
            _contributorsListView.BeginUpdate();
            try
            {
                _contributorsListView.Items.Clear();
                foreach (var entry in entries ?? new GitHubContributorEntry[0])
                {
                    var item = new ListViewItem(DisplayValue(entry.Login));
                    item.SubItems.Add(entry.Contributions.ToString());
                    item.SubItems.Add(DisplayValue(entry.Type));
                    item.Tag = entry;
                    _contributorsListView.Items.Add(item);
                }
            }
            finally
            {
                _contributorsListView.EndUpdate();
            }

            SelectFirstItemOrShowEmpty(
                _contributorsListView,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Empty.Contributors", "No contributors found."));
        }

        private void AddOverviewItem(string name, string value)
        {
            _overviewListBox.Items.Add(string.Format(
                UiLocalization.Text("GitHubRepositoryDetailsForm.Overview.Template", "{0}: {1}"),
                name ?? string.Empty,
                value ?? string.Empty));
        }

        private void UpdateOverviewDetails()
        {
            var text = _overviewListBox.SelectedItem == null ? BuildRepositoryDetails() : _overviewListBox.SelectedItem.ToString();
            TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, text);
        }

        private void UpdateBranchDetails()
        {
            var entry = GetSelectedTag<GitHubBranchEntry>(_branchesListView);
            if (entry == null)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, string.Empty);
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Name.Template", "Name: {0}"), DisplayValue(entry.Name)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.CommitSha.Template", "Commit SHA: {0}"), DisplayValue(entry.CommitSha)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Protected.Template", "Protected: {0}"), FormatBoolean(entry.IsProtected)));
            TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, builder.ToString().TrimEnd());
        }

        private void UpdateTagDetails()
        {
            var entry = GetSelectedTag<GitHubTagEntry>(_tagsListView);
            if (entry == null)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, string.Empty);
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Name.Template", "Name: {0}"), DisplayValue(entry.Name)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.CommitSha.Template", "Commit SHA: {0}"), DisplayValue(entry.CommitSha)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.ZipballUrl.Template", "ZIP URL: {0}"), DisplayValue(entry.ZipballUrl)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.TarballUrl.Template", "TAR URL: {0}"), DisplayValue(entry.TarballUrl)));
            TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, builder.ToString().TrimEnd());
        }

        private void UpdateReleaseDetails()
        {
            var entry = GetSelectedTag<GitHubReleaseEntry>(_releasesListView);
            if (entry == null)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, string.Empty);
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.ReleaseName.Template", "Name: {0}"), DisplayValue(entry.Name)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Tag.Template", "Tag: {0}"), DisplayValue(entry.TagName)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.State.Template", "State: {0}"), BuildReleaseState(entry)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.PublishedAt.Template", "Published: {0}"), FormatDate(entry.PublishedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.CreatedAt.Template", "Created: {0}"), FormatDate(entry.CreatedAt)));
            TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, builder.ToString().TrimEnd());
        }

        private void UpdateIssueDetails()
        {
            var entry = GetSelectedTag<GitHubIssueEntry>(_issuesListView);
            if (entry == null)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, string.Empty);
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Number.Template", "Number: {0}"), entry.Number));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Title.Template", "Title: {0}"), DisplayValue(entry.Title)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.State.Template", "State: {0}"), DisplayValue(entry.State)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Author.Template", "Author: {0}"), DisplayValue(entry.Author)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Comments.Template", "Comments: {0}"), entry.Comments));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.UpdatedAt.Template", "Updated: {0}"), FormatDate(entry.UpdatedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Url.Template", "URL: {0}"), DisplayValue(entry.Url)));
            TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, builder.ToString().TrimEnd());
        }

        private void UpdatePullRequestDetails()
        {
            var entry = GetSelectedTag<GitHubPullRequestEntry>(_pullRequestsListView);
            if (entry == null)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, string.Empty);
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Number.Template", "Number: {0}"), entry.Number));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Title.Template", "Title: {0}"), DisplayValue(entry.Title)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.State.Template", "State: {0}"), BuildPullRequestState(entry)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Author.Template", "Author: {0}"), DisplayValue(entry.Author)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.BaseBranch.Template", "Base: {0}"), DisplayValue(entry.BaseBranch)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.HeadBranch.Template", "Head: {0}"), DisplayValue(entry.HeadBranch)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.ReviewDecision.Template", "Review: {0}"), DisplayValue(entry.ReviewDecision)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Comments.Template", "Comments: {0}"), entry.Comments));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.MergedAt.Template", "Merged: {0}"), FormatDate(entry.MergedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.UpdatedAt.Template", "Updated: {0}"), FormatDate(entry.UpdatedAt)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Url.Template", "URL: {0}"), DisplayValue(entry.Url)));
            TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, builder.ToString().TrimEnd());
        }

        private async Task ApplyPullRequestFiltersAsync()
        {
            if (_loadCancellation != null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            _pullRequestStateFilter = GetSelectedFilterValue(_pullRequestStateFilterComboBox, "all");
            _pullRequestScopeFilter = GetSelectedFilterValue(_pullRequestScopeFilterComboBox, "all");
            _loadedTabs.Remove(_pullRequestsTabPage);
            await LoadCurrentTabAsync(true, true).ConfigureAwait(true);
        }

        private async Task ApplyIssueFiltersAsync()
        {
            if (_loadCancellation != null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            _issueStateFilter = GetSelectedFilterValue(_issueStateFilterComboBox, "all");
            _issueScopeFilter = GetSelectedFilterValue(_issueScopeFilterComboBox, "all");
            _loadedTabs.Remove(_issuesTabPage);
            await LoadCurrentTabAsync(true, true).ConfigureAwait(true);
        }

        private void ShowReleaseDetails()
        {
            var entry = GetSelectedTag<GitHubReleaseEntry>(_releasesListView);
            if (entry == null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            var form = new GitHubReleaseDetailsForm(_gitHubCliService, _repository, entry.TagName, _screenReaderService);
            form.FormClosed += (sender, e) => FocusCurrentTabPrimaryControl();
            form.Show(this);
        }

        private void ShowIssueDiscussion()
        {
            var entry = GetSelectedTag<GitHubIssueEntry>(_issuesListView);
            if (entry == null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            ShowDiscussion(entry.Number, false);
        }

        private void OpenSelectedIssue()
        {
            var entry = GetSelectedTag<GitHubIssueEntry>(_issuesListView);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Url))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            OpenUrl(entry.Url, UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OpenedIssue", "Issue page opened."));
        }

        private void CopySelectedIssueUrl()
        {
            var entry = GetSelectedTag<GitHubIssueEntry>(_issuesListView);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Url))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            Clipboard.SetText(entry.Url.Trim());
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CopiedIssueUrl", "Copied issue URL."), true);
            FocusCurrentTabPrimaryControl();
        }

        private async Task CreateIssueAsync()
        {
            if (_loadCancellation != null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            using (var form = new GitHubIssueCreateForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    FocusCurrentTabPrimaryControl();
                    return;
                }

                _loadCancellation = new CancellationTokenSource();
                SetBusy(true);
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CreatingIssue", "Creating issue..."), true);
                try
                {
                    var url = await _gitHubCliService.CreateIssueAsync(
                        _repository,
                        form.IssueTitle,
                        form.Body,
                        _loadCancellation.Token).ConfigureAwait(true);
                    _loadedTabs.Remove(_issuesTabPage);
                    SetStatus(
                        string.IsNullOrWhiteSpace(url)
                            ? UiLocalization.Text("GitHubRepositoryDetailsForm.Status.IssueCreated", "Issue created.")
                            : string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.IssueCreatedWithUrl.Template", "Issue created: {0}"), url),
                        true);
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                    SetBusy(false);
                    await LoadCurrentTabAsync(true, true).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
                }
                catch (Exception ex)
                {
                    SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed."), true);
                    MessageBox.Show(this, FormatRepositoryOperationError(ex), UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (_loadCancellation != null)
                    {
                        _loadCancellation.Dispose();
                        _loadCancellation = null;
                    }

                    SetBusy(false);
                    FocusCurrentTabPrimaryControl();
                }
            }
        }

        private async Task AddCommentToSelectedIssueAsync()
        {
            var entry = GetSelectedTag<GitHubIssueEntry>(_issuesListView);
            if (entry == null || _loadCancellation != null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            using (var form = new GitHubCommentForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    FocusCurrentTabPrimaryControl();
                    return;
                }

                await AddIssueCommentAsync(entry.Number, form.Body, true).ConfigureAwait(true);
            }
        }

        private async Task CloseSelectedIssueAsync()
        {
            await ChangeSelectedIssueStateAsync(
                true,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Confirm.CloseIssue", "Close selected issue?"),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.ClosingIssue", "Closing issue..."),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.IssueClosed", "Issue closed.")).ConfigureAwait(true);
        }

        private async Task ReopenSelectedIssueAsync()
        {
            await ChangeSelectedIssueStateAsync(
                false,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Confirm.ReopenIssue", "Reopen selected issue?"),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.ReopeningIssue", "Reopening issue..."),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.IssueReopened", "Issue reopened.")).ConfigureAwait(true);
        }

        private async Task ChangeSelectedIssueStateAsync(bool close, string confirmation, string busyStatus, string successStatus)
        {
            var entry = GetSelectedTag<GitHubIssueEntry>(_issuesListView);
            if (entry == null || _loadCancellation != null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            if (MessageBox.Show(this, confirmation, UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(busyStatus, true);
            try
            {
                if (close)
                {
                    await _gitHubCliService.CloseIssueAsync(_repository, entry.Number, _loadCancellation.Token).ConfigureAwait(true);
                }
                else
                {
                    await _gitHubCliService.ReopenIssueAsync(_repository, entry.Number, _loadCancellation.Token).ConfigureAwait(true);
                }

                _loadedTabs.Remove(_issuesTabPage);
                _loadCancellation.Dispose();
                _loadCancellation = null;
                SetBusy(false);
                await LoadCurrentTabAsync(true, true).ConfigureAwait(true);
                SetStatus(successStatus, true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_loadCancellation != null)
                {
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                }

                SetBusy(false);
                FocusCurrentTabPrimaryControl();
            }
        }

        private async Task AddIssueCommentAsync(int number, string body, bool refreshList)
        {
            if (_loadCancellation != null || string.IsNullOrWhiteSpace(body))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CommentingIssue", "Adding issue comment..."), true);
            try
            {
                await _gitHubCliService.AddIssueCommentAsync(_repository, number, body, _loadCancellation.Token).ConfigureAwait(true);
                if (refreshList)
                {
                    _loadedTabs.Remove(_issuesTabPage);
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                    SetBusy(false);
                    await LoadCurrentTabAsync(true, true).ConfigureAwait(true);
                }

                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.IssueCommented", "Issue comment added."), true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_loadCancellation != null)
                {
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                }

                SetBusy(false);
                FocusCurrentTabPrimaryControl();
            }
        }

        private void ShowPullRequestDiscussion()
        {
            var entry = GetSelectedTag<GitHubPullRequestEntry>(_pullRequestsListView);
            if (entry == null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            ShowDiscussion(entry.Number, true);
        }

        private void OpenSelectedPullRequest()
        {
            var entry = GetSelectedTag<GitHubPullRequestEntry>(_pullRequestsListView);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Url))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            OpenUrl(entry.Url, UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OpenedPullRequest", "Pull request page opened."));
        }

        private void CopySelectedPullRequestUrl()
        {
            var entry = GetSelectedTag<GitHubPullRequestEntry>(_pullRequestsListView);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Url))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            Clipboard.SetText(entry.Url.Trim());
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CopiedPullRequestUrl", "Copied pull request URL."), true);
            FocusCurrentTabPrimaryControl();
        }

        private async Task CreatePullRequestFromCurrentBranchAsync()
        {
            if (_loadCancellation != null || string.IsNullOrWhiteSpace(_currentLocalBranch) || string.IsNullOrWhiteSpace(_matchingLocalRemoteName))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            using (var form = new GitHubPullRequestCreateForm(_currentLocalBranch, GetCurrentDefaultBranch()))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    FocusCurrentTabPrimaryControl();
                    return;
                }

                _loadCancellation = new CancellationTokenSource();
                SetBusy(true);
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CreatingPullRequest", "Creating pull request..."), true);
                try
                {
                    var url = await _gitHubCliService.CreatePullRequestAsync(
                        _repository,
                        form.BaseBranch,
                        _currentLocalBranch,
                        form.PullRequestTitle,
                        form.Body,
                        form.Draft,
                        _loadCancellation.Token).ConfigureAwait(true);
                    _loadedTabs.Remove(_pullRequestsTabPage);
                    SetStatus(
                        string.IsNullOrWhiteSpace(url)
                            ? UiLocalization.Text("GitHubRepositoryDetailsForm.Status.PullRequestCreated", "Pull request created.")
                            : string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.PullRequestCreatedWithUrl.Template", "Pull request created: {0}"), url),
                        true);
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                    SetBusy(false);
                    await LoadCurrentTabAsync(true, true).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
                }
                catch (Exception ex)
                {
                    SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed."), true);
                    MessageBox.Show(this, FormatRepositoryOperationError(ex), UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    if (_loadCancellation != null)
                    {
                        _loadCancellation.Dispose();
                        _loadCancellation = null;
                    }

                    SetBusy(false);
                    FocusCurrentTabPrimaryControl();
                }
            }
        }

        private async Task AddCommentToSelectedPullRequestAsync()
        {
            var entry = GetSelectedTag<GitHubPullRequestEntry>(_pullRequestsListView);
            if (entry == null || _loadCancellation != null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            using (var form = new GitHubCommentForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    FocusCurrentTabPrimaryControl();
                    return;
                }

                await AddPullRequestCommentAsync(entry.Number, form.Body, true).ConfigureAwait(true);
            }
        }

        private async Task CheckoutSelectedPullRequestAsync()
        {
            var entry = GetSelectedTag<GitHubPullRequestEntry>(_pullRequestsListView);
            if (entry == null || _loadCancellation != null || _gitRepositoryService == null ||
                string.IsNullOrWhiteSpace(_currentWorkspacePath) || string.IsNullOrWhiteSpace(_matchingLocalRemoteName))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            if (!await ConfirmCheckoutWithLocalChangesAsync("PR #" + entry.Number).ConfigureAwait(true))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            var localBranchName = "pr-" + entry.Number;
            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CheckingOutPullRequest", "Checking out pull request..."), true);
            try
            {
                var result = await _gitRepositoryService.CheckoutPullRequestAsync(
                    _currentWorkspacePath,
                    _matchingLocalRemoteName,
                    entry.Number,
                    localBranchName,
                    _loadCancellation.Token).ConfigureAwait(true);
                if (result == null || !result.Success)
                {
                    ShowGitOperationFailure(result);
                    return;
                }

                _currentLocalBranch = localBranchName;
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CheckedOutPullRequest", "Pull request is now checked out in the current workspace."), true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, ex.Message, UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_loadCancellation != null)
                {
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                }

                SetBusy(false);
                FocusCurrentTabPrimaryControl();
            }
        }

        private async Task CloseSelectedPullRequestAsync()
        {
            await ChangeSelectedPullRequestStateAsync(
                true,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Confirm.ClosePullRequest", "Close selected pull request?"),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.ClosingPullRequest", "Closing pull request..."),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.PullRequestClosed", "Pull request closed.")).ConfigureAwait(true);
        }

        private async Task ReopenSelectedPullRequestAsync()
        {
            await ChangeSelectedPullRequestStateAsync(
                false,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Confirm.ReopenPullRequest", "Reopen selected pull request?"),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.ReopeningPullRequest", "Reopening pull request..."),
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.PullRequestReopened", "Pull request reopened.")).ConfigureAwait(true);
        }

        private async Task ChangeSelectedPullRequestStateAsync(bool close, string confirmation, string busyStatus, string successStatus)
        {
            var entry = GetSelectedTag<GitHubPullRequestEntry>(_pullRequestsListView);
            if (entry == null || _loadCancellation != null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            if (MessageBox.Show(this, confirmation, UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(busyStatus, true);
            try
            {
                if (close)
                {
                    await _gitHubCliService.ClosePullRequestAsync(_repository, entry.Number, _loadCancellation.Token).ConfigureAwait(true);
                }
                else
                {
                    await _gitHubCliService.ReopenPullRequestAsync(_repository, entry.Number, _loadCancellation.Token).ConfigureAwait(true);
                }

                _loadedTabs.Remove(_pullRequestsTabPage);
                _loadCancellation.Dispose();
                _loadCancellation = null;
                SetBusy(false);
                await LoadCurrentTabAsync(true, true).ConfigureAwait(true);
                SetStatus(successStatus, true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_loadCancellation != null)
                {
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                }

                SetBusy(false);
                FocusCurrentTabPrimaryControl();
            }
        }

        private async Task AddPullRequestCommentAsync(int number, string body, bool refreshList)
        {
            if (_loadCancellation != null || string.IsNullOrWhiteSpace(body))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CommentingPullRequest", "Adding pull request comment..."), true);
            try
            {
                await _gitHubCliService.AddPullRequestCommentAsync(_repository, number, body, _loadCancellation.Token).ConfigureAwait(true);
                if (refreshList)
                {
                    _loadedTabs.Remove(_pullRequestsTabPage);
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                    SetBusy(false);
                    await LoadCurrentTabAsync(true, true).ConfigureAwait(true);
                }

                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.PullRequestCommented", "Pull request comment added."), true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_loadCancellation != null)
                {
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                }

                SetBusy(false);
                FocusCurrentTabPrimaryControl();
            }
        }

        private void ShowDiscussion(int number, bool pullRequest)
        {
            var form = new GitHubDiscussionForm(_gitHubCliService, _repository, number, pullRequest, _screenReaderService);
            form.FormClosed += (sender, e) => FocusCurrentTabPrimaryControl();
            form.Show(this);
        }

        private void CopySelectedBranchName()
        {
            CopySelectedBranchValue(
                entry => entry.Name,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CopiedBranchName", "Copied branch name."));
        }

        private void CopySelectedBranchCommitSha()
        {
            CopySelectedBranchValue(
                entry => entry.CommitSha,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CopiedCommitSha", "Copied commit SHA."));
        }

        private void CopySelectedBranchValue(Func<GitHubBranchEntry, string> valueSelector, string status)
        {
            var entry = GetSelectedTag<GitHubBranchEntry>(_branchesListView);
            var value = entry == null || valueSelector == null ? null : valueSelector(entry);
            if (string.IsNullOrWhiteSpace(value))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            Clipboard.SetText(value.Trim());
            SetStatus(status, true);
            FocusCurrentTabPrimaryControl();
        }

        private void OpenSelectedBranch()
        {
            var entry = GetSelectedTag<GitHubBranchEntry>(_branchesListView);
            var url = BuildRepositoryWebUrl(_repository, "tree/" + EscapeBranchPath(entry == null ? null : entry.Name));
            if (string.IsNullOrWhiteSpace(url))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            OpenUrl(url, UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OpenedBranch", "Branch page opened."));
        }

        private void CopySelectedTagName()
        {
            CopySelectedTagValue(
                entry => entry.Name,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CopiedTagName", "Copied tag name."));
        }

        private void CopySelectedTagCommitSha()
        {
            CopySelectedTagValue(
                entry => entry.CommitSha,
                UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CopiedTagCommitSha", "Copied tag commit SHA."));
        }

        private void CopySelectedTagValue(Func<GitHubTagEntry, string> valueSelector, string status)
        {
            var entry = GetSelectedTag<GitHubTagEntry>(_tagsListView);
            var value = entry == null || valueSelector == null ? null : valueSelector(entry);
            if (string.IsNullOrWhiteSpace(value))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            Clipboard.SetText(value.Trim());
            SetStatus(status, true);
            FocusCurrentTabPrimaryControl();
        }

        private void OpenSelectedTag()
        {
            var entry = GetSelectedTag<GitHubTagEntry>(_tagsListView);
            var url = BuildRepositoryWebUrl(_repository, "tree/" + EscapeBranchPath(entry == null ? null : entry.Name));
            if (string.IsNullOrWhiteSpace(url))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            OpenUrl(url, UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OpenedTag", "Tag page opened."));
        }

        private void OpenSelectedTagArchive(bool zip)
        {
            var entry = GetSelectedTag<GitHubTagEntry>(_tagsListView);
            var url = entry == null ? null : (zip ? entry.ZipballUrl : entry.TarballUrl);
            if (string.IsNullOrWhiteSpace(url))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            OpenUrl(
                url.Trim(),
                zip
                    ? UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OpenedTagZipball", "ZIP archive opened.")
                    : UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OpenedTagTarball", "TAR archive opened."));
        }

        private void OpenUrl(string url, string successStatus)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                SetStatus(successStatus, true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                MessageBox.Show(this, ex.Message, UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                FocusCurrentTabPrimaryControl();
            }
        }

        private async Task SetSelectedBranchAsDefaultAsync()
        {
            var entry = GetSelectedTag<GitHubBranchEntry>(_branchesListView);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || _loadCancellation != null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            var currentDefault = GetCurrentDefaultBranch();
            if (string.Equals(entry.Name, currentDefault, StringComparison.Ordinal))
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.AlreadyDefaultBranch", "Selected branch is already the default branch."), true);
                FocusCurrentTabPrimaryControl();
                return;
            }

            var confirmation = string.Format(
                UiLocalization.Text("GitHubRepositoryDetailsForm.Confirm.SetDefaultBranch.Template", "Set {0} as the default branch?"),
                entry.Name);
            if (MessageBox.Show(this, confirmation, UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.SettingDefaultBranch", "Setting default branch..."), true);
            try
            {
                await _gitHubCliService.SetDefaultBranchAsync(_repository, entry.Name, _loadCancellation.Token).ConfigureAwait(true);
                _repository.DefaultBranch = entry.Name;
                if (_repositoryDetails != null)
                {
                    _repositoryDetails.DefaultBranch = entry.Name;
                }

                _loadedTabs.Remove(_overviewTabPage);
                _loadedTabs.Remove(_branchesTabPage);
                _loadCancellation.Dispose();
                _loadCancellation = null;
                SetBusy(false);
                await LoadCurrentTabAsync(true, true).ConfigureAwait(true);
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.DefaultBranchSet", "Default branch updated."), true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                FocusCurrentTabPrimaryControl();
            }
            finally
            {
                if (_loadCancellation != null)
                {
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                }

                SetBusy(false);
            }
        }

        private async Task DeleteSelectedBranchAsync()
        {
            var entry = GetSelectedTag<GitHubBranchEntry>(_branchesListView);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || _loadCancellation != null)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            if (string.Equals(entry.Name, GetCurrentDefaultBranch(), StringComparison.Ordinal))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitHubRepositoryDetailsForm.Message.CannotDeleteDefaultBranch", "The default branch cannot be deleted. Set another default branch first."),
                    UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                FocusCurrentTabPrimaryControl();
                return;
            }

            var confirmation = string.Format(
                UiLocalization.Text("GitHubRepositoryDetailsForm.Confirm.DeleteBranch.Template", "Delete remote branch {0}?\r\n\r\nThis removes the branch from GitHub and can affect collaborators."),
                entry.Name);
            if (MessageBox.Show(this, confirmation, UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.DeletingBranch", "Deleting remote branch..."), true);
            try
            {
                await _gitHubCliService.DeleteRepositoryBranchAsync(_repository, entry.Name, _loadCancellation.Token).ConfigureAwait(true);
                _loadedTabs.Remove(_branchesTabPage);
                _loadCancellation.Dispose();
                _loadCancellation = null;
                SetBusy(false);
                await LoadCurrentTabAsync(true, true).ConfigureAwait(true);
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.BranchDeleted", "Remote branch deleted."), true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, FormatRepositoryOperationError(ex), UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                FocusCurrentTabPrimaryControl();
            }
            finally
            {
                if (_loadCancellation != null)
                {
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                }

                SetBusy(false);
            }
        }

        private async Task CheckoutSelectedBranchInWorkspaceAsync()
        {
            var entry = GetSelectedTag<GitHubBranchEntry>(_branchesListView);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || _loadCancellation != null ||
                _gitRepositoryService == null || string.IsNullOrWhiteSpace(_currentWorkspacePath) ||
                string.IsNullOrWhiteSpace(_matchingLocalRemoteName))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            if (string.Equals(entry.Name, _currentLocalBranch, StringComparison.Ordinal))
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.AlreadyCurrentBranch", "Selected branch is already checked out in the current workspace."), true);
                FocusCurrentTabPrimaryControl();
                return;
            }

            if (!await ConfirmCheckoutWithLocalChangesAsync(entry.Name).ConfigureAwait(true))
            {
                FocusCurrentTabPrimaryControl();
                return;
            }

            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.FetchingBeforeCheckout", "Fetching remote branch before switch..."), true);
            try
            {
                var fetchResult = await _gitRepositoryService.FetchAsync(_currentWorkspacePath, _loadCancellation.Token).ConfigureAwait(true);
                if (fetchResult == null || !fetchResult.Success)
                {
                    ShowGitOperationFailure(fetchResult);
                    return;
                }

                var localBranches = await _gitRepositoryService.ReadBranchesAsync(_currentWorkspacePath, _loadCancellation.Token).ConfigureAwait(true);
                var localBranchExists = (localBranches ?? new GitBranchEntry[0]).Any(branch =>
                    branch != null &&
                    !branch.IsRemote &&
                    string.Equals(branch.Name, entry.Name, StringComparison.Ordinal));
                var result = localBranchExists
                    ? await _gitRepositoryService.CheckoutBranchAsync(_currentWorkspacePath, entry.Name, _loadCancellation.Token).ConfigureAwait(true)
                    : await _gitRepositoryService.CheckoutRemoteBranchAsync(_currentWorkspacePath, _matchingLocalRemoteName + "/" + entry.Name, _loadCancellation.Token).ConfigureAwait(true);
                if (result == null || !result.Success)
                {
                    ShowGitOperationFailure(result);
                    return;
                }

                _currentLocalBranch = entry.Name;
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CheckedOutBranch", "Remote branch is now checked out in the current workspace."), true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed."), true);
                MessageBox.Show(this, ex.Message, UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_loadCancellation != null)
                {
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                }

                SetBusy(false);
                FocusCurrentTabPrimaryControl();
            }
        }

        private async Task<bool> ConfirmCheckoutWithLocalChangesAsync(string branchName)
        {
            _loadCancellation = new CancellationTokenSource();
            SetBusy(true);
            SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.CheckingWorkingTree", "Checking local changes..."), true);
            try
            {
                var status = await _gitRepositoryService.ReadStatusAsync(_currentWorkspacePath, _loadCancellation.Token).ConfigureAwait(true);
                var preflight = GitCheckoutPreflightPlanner.Evaluate(status);
                if (preflight == GitCheckoutPreflightDecision.Proceed)
                {
                    return true;
                }

                if (preflight == GitCheckoutPreflightDecision.BlockConflicts)
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitHubRepositoryDetailsForm.Message.BlockedByConflicts", "Resolve conflicts before switching branches."),
                        UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return false;
                }

                if (preflight == GitCheckoutPreflightDecision.BlockActiveGitOperation)
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("GitHubRepositoryDetailsForm.Message.BlockedByGitOperation", "Finish or abort the current Git operation before switching branches."),
                        UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"),
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return false;
                }

                var result = MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text(
                            "GitHubRepositoryDetailsForm.Confirm.CheckoutDirtyWithStash.Template",
                            "Current workspace has {0} uncommitted change(s). Switch to {1}?\r\n\r\nYes: stash changes first, then switch.\r\nNo: switch directly.\r\nCancel: do not switch."),
                        status.Files.Count,
                        branchName),
                    UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"),
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button3);
                if (result == DialogResult.Cancel)
                {
                    return false;
                }

                if (result == DialogResult.Yes)
                {
                    SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.StashingBeforeCheckout", "Saving local changes to stash..."), true);
                    var stashResult = await _gitRepositoryService.StashPushAsync(
                        _currentWorkspacePath,
                        UiLocalization.Text("GitHubRepositoryDetailsForm.Value.AutoStashMessage", "MyCodex auto stash before remote branch switch"),
                        true,
                        _loadCancellation.Token).ConfigureAwait(true);
                    if (stashResult == null || !stashResult.Success)
                    {
                        ShowGitOperationFailure(stashResult);
                        return false;
                    }
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubRepositoryDetailsForm.Status.Canceled", "Repository section loading canceled."), false);
                return false;
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                MessageBox.Show(this, ex.Message, UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                if (_loadCancellation != null)
                {
                    _loadCancellation.Dispose();
                    _loadCancellation = null;
                }

                SetBusy(false);
            }
        }

        private void ShowGitOperationFailure(GitOperationResult result)
        {
            var message = result == null || string.IsNullOrWhiteSpace(result.DisplayMessage)
                ? UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed.")
                : result.DisplayMessage;
            SetStatus(message, true);
            MessageBox.Show(this, message, UiLocalization.Text("GitHubRepositoryDetailsForm.Error.Title", "GitHub Repository"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void RefreshContextMenus()
        {
            var loading = _loadCancellation != null;
            RefreshBranchesContextMenu(loading);
            RefreshTagsContextMenu(loading);
            RefreshIssuesContextMenu(loading);
            RefreshPullRequestsContextMenu(loading);
            _viewReleaseDetailsMenuItem.Enabled = !loading && GetSelectedTag<GitHubReleaseEntry>(_releasesListView) != null;
        }

        private void RefreshBranchesContextMenu(bool loading)
        {
            _branchesContextMenu.MenuItems.Clear();
            var entry = GetSelectedTag<GitHubBranchEntry>(_branchesListView);
            if (loading || entry == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(entry.Name))
            {
                _branchesContextMenu.MenuItems.Add(_copyBranchNameMenuItem);
                _branchesContextMenu.MenuItems.Add(_openBranchMenuItem);
            }

            if (!string.IsNullOrWhiteSpace(entry.CommitSha))
            {
                _branchesContextMenu.MenuItems.Add(_copyBranchCommitShaMenuItem);
            }

            if (!string.IsNullOrWhiteSpace(entry.Name) &&
                IsManageMode &&
                !string.IsNullOrWhiteSpace(_matchingLocalRemoteName) &&
                !string.Equals(entry.Name, _currentLocalBranch, StringComparison.Ordinal))
            {
                AddContextSeparator(_branchesContextMenu);
                _branchesContextMenu.MenuItems.Add(_checkoutBranchInWorkspaceMenuItem);
            }

            if (IsManageMode &&
                !string.IsNullOrWhiteSpace(entry.Name) &&
                !string.Equals(entry.Name, GetCurrentDefaultBranch(), StringComparison.Ordinal))
            {
                AddContextSeparator(_branchesContextMenu);
                _branchesContextMenu.MenuItems.Add(_setDefaultBranchMenuItem);
                _branchesContextMenu.MenuItems.Add(_deleteBranchMenuItem);
            }
        }

        private void RefreshTagsContextMenu(bool loading)
        {
            _tagsContextMenu.MenuItems.Clear();
            var entry = GetSelectedTag<GitHubTagEntry>(_tagsListView);
            if (loading || entry == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(entry.Name))
            {
                _tagsContextMenu.MenuItems.Add(_copyTagNameMenuItem);
                _tagsContextMenu.MenuItems.Add(_openTagMenuItem);
            }

            if (!string.IsNullOrWhiteSpace(entry.CommitSha))
            {
                _tagsContextMenu.MenuItems.Add(_copyTagCommitShaMenuItem);
            }

            if (!string.IsNullOrWhiteSpace(entry.ZipballUrl))
            {
                AddContextSeparator(_tagsContextMenu);
                _tagsContextMenu.MenuItems.Add(_openTagZipballMenuItem);
            }

            if (!string.IsNullOrWhiteSpace(entry.TarballUrl))
            {
                AddContextSeparator(_tagsContextMenu);
                _tagsContextMenu.MenuItems.Add(_openTagTarballMenuItem);
            }
        }

        private void RefreshIssuesContextMenu(bool loading)
        {
            _issuesContextMenu.MenuItems.Clear();
            if (loading)
            {
                return;
            }

            _issuesContextMenu.MenuItems.Add(_createIssueMenuItem);

            var entry = GetSelectedTag<GitHubIssueEntry>(_issuesListView);
            if (entry == null)
            {
                return;
            }

            AddContextSeparator(_issuesContextMenu);
            if (!string.IsNullOrWhiteSpace(entry.Url))
            {
                _issuesContextMenu.MenuItems.Add(_openIssueMenuItem);
                _issuesContextMenu.MenuItems.Add(_copyIssueUrlMenuItem);
            }

            _issuesContextMenu.MenuItems.Add(_viewIssueDiscussionMenuItem);
            _issuesContextMenu.MenuItems.Add(_commentIssueMenuItem);

            if (IsManageMode && IsOpenIssue(entry))
            {
                AddContextSeparator(_issuesContextMenu);
                _issuesContextMenu.MenuItems.Add(_closeIssueMenuItem);
            }
            else if (IsManageMode && IsClosedIssue(entry))
            {
                AddContextSeparator(_issuesContextMenu);
                _issuesContextMenu.MenuItems.Add(_reopenIssueMenuItem);
            }
        }

        private void RefreshPullRequestsContextMenu(bool loading)
        {
            _pullRequestsContextMenu.MenuItems.Clear();
            if (loading)
            {
                return;
            }

            if (CanCreatePullRequestFromCurrentBranch())
            {
                _pullRequestsContextMenu.MenuItems.Add(_createPullRequestMenuItem);
            }

            var entry = GetSelectedTag<GitHubPullRequestEntry>(_pullRequestsListView);
            if (entry == null)
            {
                return;
            }

            AddContextSeparator(_pullRequestsContextMenu);
            if (!string.IsNullOrWhiteSpace(entry.Url))
            {
                _pullRequestsContextMenu.MenuItems.Add(_openPullRequestMenuItem);
                _pullRequestsContextMenu.MenuItems.Add(_copyPullRequestUrlMenuItem);
            }

            _pullRequestsContextMenu.MenuItems.Add(_viewPullRequestDiscussionMenuItem);
            _pullRequestsContextMenu.MenuItems.Add(_commentPullRequestMenuItem);

            if (!string.IsNullOrWhiteSpace(_matchingLocalRemoteName) && _gitRepositoryService != null && !string.IsNullOrWhiteSpace(_currentWorkspacePath))
            {
                AddContextSeparator(_pullRequestsContextMenu);
                _pullRequestsContextMenu.MenuItems.Add(_checkoutPullRequestMenuItem);
            }

            if (IsManageMode && IsOpenPullRequest(entry))
            {
                AddContextSeparator(_pullRequestsContextMenu);
                _pullRequestsContextMenu.MenuItems.Add(_closePullRequestMenuItem);
            }
            else if (IsManageMode && IsClosedPullRequest(entry) && !IsMergedPullRequest(entry))
            {
                AddContextSeparator(_pullRequestsContextMenu);
                _pullRequestsContextMenu.MenuItems.Add(_reopenPullRequestMenuItem);
            }
        }

        private bool IsManageMode
        {
            get { return _mode == GitHubRepositoryDetailsMode.Manage; }
        }

        private void HandleListContextMenuKeyDown(ListView listView, ContextMenu contextMenu, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Apps && !(e.Shift && e.KeyCode == Keys.F10))
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            ShowContextMenuForKeyboard(listView, contextMenu);
        }

        private static void ShowContextMenuForKeyboard(ListView listView, ContextMenu contextMenu)
        {
            if (listView.Items.Count == 0)
            {
                contextMenu.Show(listView, new Point(8, 8));
                return;
            }

            var item = listView.SelectedItems.Count == 0
                ? listView.Items[0]
                : listView.SelectedItems[0];
            if (listView.SelectedItems.Count == 0)
            {
                item.Selected = true;
            }

            item.Focused = true;
            item.EnsureVisible();
            contextMenu.Show(listView, new Point(Math.Max(8, item.Bounds.Left), item.Bounds.Bottom));
        }

        private static void AddContextSeparator(ContextMenu contextMenu)
        {
            if (contextMenu == null || contextMenu.MenuItems.Count == 0)
            {
                return;
            }

            var last = contextMenu.MenuItems[contextMenu.MenuItems.Count - 1];
            if (last.Text != "-")
            {
                contextMenu.MenuItems.Add("-");
            }
        }

        private void UpdateContributorDetails()
        {
            var entry = GetSelectedTag<GitHubContributorEntry>(_contributorsListView);
            if (entry == null)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, string.Empty);
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Login.Template", "Login: {0}"), DisplayValue(entry.Login)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Contributions.Template", "Contributions: {0}"), entry.Contributions));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Type.Template", "Type: {0}"), DisplayValue(entry.Type)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Url.Template", "URL: {0}"), DisplayValue(entry.Url)));
            TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, builder.ToString().TrimEnd());
        }

        private string BuildRepositoryDetails()
        {
            var builder = new StringBuilder();
            builder.AppendLine(DisplayValue(_repositoryDetails.NameWithOwner));
            builder.AppendLine(DisplayValue(_repositoryDetails.Description));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.OpenIssues.Template", "Open issues: {0}"), FormatNumber(_repositoryDetails.OpenIssues)));
            builder.AppendLine(string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Details.Topics.Template", "Topics: {0}"), DisplayValue(_repositoryDetails.Topics)));
            builder.AppendLine(DisplayValue(_repositoryDetails.HttpsUrl));
            return builder.ToString().TrimEnd();
        }

        private void SetBusy(bool busy)
        {
            _refreshButton.Enabled = !busy;
            RefreshContextMenus();
        }

        private void SetStatus(string text, bool speak)
        {
            _statusLabel.Text = text ?? string.Empty;
            if (speak && _screenReaderService != null)
            {
                _screenReaderService.Speak(_statusLabel.Text, true, false, true);
            }
        }

        private void FocusCurrentTabPrimaryControl()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                var control = GetCurrentPrimaryControl();
                var listView = control as ListView;
                if (listView != null)
                {
                    if (listView.Items.Count > 0 && listView.SelectedItems.Count == 0)
                    {
                        listView.Items[0].Selected = true;
                        listView.Items[0].Focused = true;
                    }

                    if (listView.SelectedItems.Count > 0)
                    {
                        listView.SelectedItems[0].Focused = true;
                        listView.SelectedItems[0].EnsureVisible();
                    }
                }

                var listBox = control as ListBox;
                if (listBox != null && listBox.Items.Count > 0 && listBox.SelectedIndex < 0)
                {
                    listBox.SelectedIndex = 0;
                }

                if (control != null && control.CanFocus)
                {
                    control.Focus();
                }
            });
        }

        private Control GetCurrentPrimaryControl()
        {
            if (ReferenceEquals(_tabs.SelectedTab, _overviewTabPage))
            {
                return _overviewListBox;
            }

            if (ReferenceEquals(_tabs.SelectedTab, _branchesTabPage))
            {
                return _branchesListView;
            }

            if (ReferenceEquals(_tabs.SelectedTab, _tagsTabPage))
            {
                return _tagsListView;
            }

            if (ReferenceEquals(_tabs.SelectedTab, _releasesTabPage))
            {
                return _releasesListView;
            }

            if (ReferenceEquals(_tabs.SelectedTab, _issuesTabPage))
            {
                return _issuesListView;
            }

            if (ReferenceEquals(_tabs.SelectedTab, _pullRequestsTabPage))
            {
                return _pullRequestsListView;
            }

            return _contributorsListView;
        }

        private void BuildLayout()
        {
            _overviewTabPage.Controls.Add(_overviewListBox);
            _branchesTabPage.Controls.Add(_branchesListView);
            _tagsTabPage.Controls.Add(_tagsListView);
            _releasesTabPage.Controls.Add(_releasesListView);
            _issuesTabPage.Controls.Add(BuildIssuesTabContent());
            _pullRequestsTabPage.Controls.Add(BuildPullRequestsTabContent());
            _contributorsTabPage.Controls.Add(_contributorsListView);
            _tabs.TabPages.Add(_overviewTabPage);
            _tabs.TabPages.Add(_branchesTabPage);
            _tabs.TabPages.Add(_tagsTabPage);
            _tabs.TabPages.Add(_releasesTabPage);
            _tabs.TabPages.Add(_issuesTabPage);
            _tabs.TabPages.Add(_pullRequestsTabPage);
            _tabs.TabPages.Add(_contributorsTabPage);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var heading = new Label
            {
                Dock = DockStyle.Fill,
                Text = DisplayValue(_repository.NameWithOwner),
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_refreshButton);

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(_tabs, 0, 1);
            root.Controls.Add(_detailsTextBox, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);
            root.Controls.Add(footer, 0, 4);
            Controls.Add(root);
        }

        private static void ConfigureHiddenCloseButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.Visible = false;
            button.TabStop = false;
            button.Width = 1;
            button.Height = 1;
            button.Margin = Padding.Empty;
        }
        private static ListBox CreateListBox(string accessibleName)
        {
            var listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                TabIndex = 0
            };
            UiTheme.ApplyListBox(listBox);
            return listBox;
        }

        private static ListView CreateBranchesListView()
        {
            var listView = CreateListView(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.Branches", "Branches"));
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Name", "Name"), 300);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.CommitSha", "Commit SHA"), 180);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Protected", "Protected"), 120);
            return listView;
        }

        private static ListView CreateTagsListView()
        {
            var listView = CreateListView(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.Tags", "Tags"));
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Name", "Name"), 300);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.CommitSha", "Commit SHA"), 180);
            return listView;
        }

        private static ListView CreateReleasesListView()
        {
            var listView = CreateListView(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.Releases", "Releases"));
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Name", "Name"), 260);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Tag", "Tag"), 160);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.State", "State"), 130);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.UpdatedAt", "Updated"), 150);
            return listView;
        }

        private static ListView CreateIssuesListView()
        {
            var listView = CreateListView(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.Issues", "Issues"));
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Title", "Title"), 360);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.State", "State"), 100);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Author", "Author"), 150);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Comments", "Comments"), 90);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.UpdatedAt", "Updated"), 150);
            return listView;
        }

        private static ListView CreatePullRequestsListView()
        {
            var listView = CreateListView(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.PullRequests", "Pull requests"));
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Title", "Title"), 360);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.State", "State"), 120);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Author", "Author"), 150);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Review", "Review"), 130);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.UpdatedAt", "Updated"), 150);
            return listView;
        }

        private static ListView CreateContributorsListView()
        {
            var listView = CreateListView(UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.Contributors", "Contributors"));
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Login", "Login"), 260);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Contributions", "Contributions"), 120);
            listView.Columns.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Column.Type", "Type"), 120);
            return listView;
        }

        private static ListView CreateListView(string accessibleName)
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                TabIndex = 0
            };
            UiTheme.ApplyListView(listView);
            return listView;
        }

        private Control BuildIssuesTabContent()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            filters.Controls.Add(CreateFilterLabel(UiLocalization.Text("GitHubRepositoryDetailsForm.Label.IssueStateFilter", "State(&S)")));
            filters.Controls.Add(_issueStateFilterComboBox);
            filters.Controls.Add(CreateFilterLabel(UiLocalization.Text("GitHubRepositoryDetailsForm.Label.IssueScopeFilter", "Scope(&P)")));
            filters.Controls.Add(_issueScopeFilterComboBox);
            filters.Controls.Add(_issueApplyFilterButton);

            root.Controls.Add(filters, 0, 0);
            root.Controls.Add(_issuesListView, 0, 1);
            return root;
        }

        private Control BuildPullRequestsTabContent()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var filters = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            filters.Controls.Add(CreateFilterLabel(UiLocalization.Text("GitHubRepositoryDetailsForm.Label.PullRequestStateFilter", "State(&S)")));
            filters.Controls.Add(_pullRequestStateFilterComboBox);
            filters.Controls.Add(CreateFilterLabel(UiLocalization.Text("GitHubRepositoryDetailsForm.Label.PullRequestScopeFilter", "Scope(&P)")));
            filters.Controls.Add(_pullRequestScopeFilterComboBox);
            filters.Controls.Add(_pullRequestApplyFilterButton);

            root.Controls.Add(filters, 0, 0);
            root.Controls.Add(_pullRequestsListView, 0, 1);
            return root;
        }

        private static Label CreateFilterLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 8, 4, 0),
                TabStop = false,
                UseMnemonic = true
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private static ComboBox CreateFilterComboBox(string accessibleName)
        {
            var comboBox = new ComboBox
            {
                AccessibleName = accessibleName,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 150,
                Margin = new Padding(0, 4, 12, 0),
                TabIndex = 0
            };
            UiTheme.ApplyComboBox(comboBox);
            return comboBox;
        }

        private static void AddFilterOption(ComboBox comboBox, string text, string value)
        {
            if (comboBox == null)
            {
                return;
            }

            comboBox.Items.Add(new FilterOption(text, value));
        }

        private static string GetSelectedFilterValue(ComboBox comboBox, string fallback)
        {
            var option = comboBox == null ? null : comboBox.SelectedItem as FilterOption;
            return option == null || string.IsNullOrWhiteSpace(option.Value)
                ? fallback
                : option.Value;
        }

        private static TextBox CreateDetailsTextBox()
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitHubRepositoryDetailsForm.Accessible.Details", "Details"),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(textBox, true, true);
            return textBox;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 112,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }

        private void SelectFirstItemOrShowEmpty(ListView listView, string emptyText)
        {
            if (listView.Items.Count == 0)
            {
                TextBoxCaretHelper.SetTextAndMoveToStart(_detailsTextBox, emptyText ?? string.Empty);
                return;
            }

            listView.Items[0].Selected = true;
            listView.Items[0].Focused = true;
        }

        private static T GetSelectedTag<T>(ListView listView) where T : class
        {
            if (listView.SelectedItems.Count > 0)
            {
                return listView.SelectedItems[0].Tag as T;
            }

            if (listView.FocusedItem != null)
            {
                return listView.FocusedItem.Tag as T;
            }

            return listView.Items.Count == 1 ? listView.Items[0].Tag as T : null;
        }

        private static string BuildNumberTitle(int number, string title)
        {
            return "#" + number + " " + DisplayValue(title);
        }

        private static string ShortSha(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DisplayValue(value);
            }

            var trimmed = value.Trim();
            return trimmed.Length <= 12 ? trimmed : trimmed.Substring(0, 12);
        }

        private static string BuildReleaseState(GitHubReleaseEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (entry.IsLatest)
            {
                parts.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Value.Latest", "Latest"));
            }

            if (entry.IsDraft)
            {
                parts.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Value.Draft", "Draft"));
            }

            if (entry.IsPrerelease)
            {
                parts.Add(UiLocalization.Text("GitHubRepositoryDetailsForm.Value.Prerelease", "Prerelease"));
            }

            return parts.Count == 0
                ? UiLocalization.Text("GitHubRepositoryDetailsForm.Value.Release", "Release")
                : string.Join(", ", parts.ToArray());
        }

        private static string BuildPullRequestState(GitHubPullRequestEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            return entry.IsDraft
                ? DisplayValue(entry.State) + ", " + UiLocalization.Text("GitHubRepositoryDetailsForm.Value.Draft", "Draft")
                : DisplayValue(entry.State);
        }

        private bool CanCreatePullRequestFromCurrentBranch()
        {
            return !string.IsNullOrWhiteSpace(_matchingLocalRemoteName) &&
                   !string.IsNullOrWhiteSpace(_currentLocalBranch) &&
                   !string.Equals(_currentLocalBranch, GetCurrentDefaultBranch(), StringComparison.Ordinal);
        }

        private static bool IsOpenPullRequest(GitHubPullRequestEntry entry)
        {
            return entry != null && string.Equals(entry.State, "open", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsClosedPullRequest(GitHubPullRequestEntry entry)
        {
            return entry != null && string.Equals(entry.State, "closed", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMergedPullRequest(GitHubPullRequestEntry entry)
        {
            return entry != null &&
                   (entry.MergedAt.HasValue || string.Equals(entry.State, "merged", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsOpenIssue(GitHubIssueEntry entry)
        {
            return entry != null && string.Equals(entry.State, "open", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsClosedIssue(GitHubIssueEntry entry)
        {
            return entry != null && string.Equals(entry.State, "closed", StringComparison.OrdinalIgnoreCase);
        }

        private string GetCurrentDefaultBranch()
        {
            if (_repositoryDetails != null && !string.IsNullOrWhiteSpace(_repositoryDetails.DefaultBranch))
            {
                return _repositoryDetails.DefaultBranch.Trim();
            }

            return _repository == null || string.IsNullOrWhiteSpace(_repository.DefaultBranch)
                ? string.Empty
                : _repository.DefaultBranch.Trim();
        }

        private static string BuildRepositoryWebUrl(GitHubRepositoryEntry repository, string relativePath)
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

            return string.IsNullOrWhiteSpace(relativePath)
                ? baseUrl
                : baseUrl.TrimEnd('/') + "/" + relativePath.TrimStart('/');
        }

        private bool RepositoryMatchesRemote(string remoteUrl)
        {
            var repositoryFullName = NormalizeRepositoryFullName(_repository == null ? null : _repository.NameWithOwner);
            var remoteFullName = ExtractGitHubRepositoryFullName(remoteUrl);
            return !string.IsNullOrWhiteSpace(repositoryFullName) &&
                   string.Equals(repositoryFullName, remoteFullName, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRepositoryFullName(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().Trim('/').ToLowerInvariant();
        }

        private static string ExtractGitHubRepositoryFullName(string remoteUrl)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                return string.Empty;
            }

            var value = remoteUrl.Trim();
            const string httpsPrefix = "https://github.com/";
            if (value.StartsWith(httpsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeRepositoryFullName(RemoveGitSuffix(value.Substring(httpsPrefix.Length)));
            }

            const string sshPrefix = "git@github.com:";
            if (value.StartsWith(sshPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeRepositoryFullName(RemoveGitSuffix(value.Substring(sshPrefix.Length)));
            }

            const string sshUrlPrefix = "ssh://git@github.com/";
            if (value.StartsWith(sshUrlPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeRepositoryFullName(RemoveGitSuffix(value.Substring(sshUrlPrefix.Length)));
            }

            return string.Empty;
        }

        private static string RemoveGitSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim().Trim('/');
            return trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? trimmed.Substring(0, trimmed.Length - 4)
                : trimmed;
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

        private static string EscapeBranchPath(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                return string.Empty;
            }

            var parts = branchName.Trim().Split('/');
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = Uri.EscapeDataString(parts[i]);
            }

            return string.Join("/", parts);
        }

        private static string FormatRepositoryOperationError(Exception ex)
        {
            var message = ex == null ? string.Empty : ex.Message ?? string.Empty;
            var result = message;
            if (message.IndexOf("Must have admin rights", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("admin rights to Repository", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                result += Environment.NewLine + Environment.NewLine +
                    UiLocalization.Text("GitHubRepositoryDetailsForm.Message.AdminRightsRequired", "This operation requires repository administrator permission.");
            }

            return string.IsNullOrWhiteSpace(result)
                ? UiLocalization.Text("GitHubRepositoryDetailsForm.Status.OperationFailed", "GitHub repository operation failed.")
                : result.Trim();
        }

        private static string FormatDate(DateTimeOffset? value)
        {
            return value.HasValue
                ? value.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : UiLocalization.Text("GitHubRepositoryDetailsForm.Value.NotAvailable", "Not available");
        }

        private static string FormatNumber(int? value)
        {
            return value.HasValue
                ? value.Value.ToString()
                : UiLocalization.Text("GitHubRepositoryDetailsForm.Value.NotAvailable", "Not available");
        }

        private static string FormatDiskUsage(long? value)
        {
            return value.HasValue
                ? string.Format(UiLocalization.Text("GitHubRepositoryDetailsForm.Value.Kilobytes.Template", "{0} KB"), value.Value)
                : UiLocalization.Text("GitHubRepositoryDetailsForm.Value.NotAvailable", "Not available");
        }

        private static string FormatBoolean(bool value)
        {
            return value
                ? UiLocalization.Text("GitHubRepositoryDetailsForm.Value.Yes", "Yes")
                : UiLocalization.Text("GitHubRepositoryDetailsForm.Value.No", "No");
        }

        private static string DisplayValue(string value)
        {
            return DisplayValue(value, null);
        }

        private static string DisplayValue(string value, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback.Trim();
            }

            return UiLocalization.Text("GitHubRepositoryDetailsForm.Value.NotAvailable", "Not available");
        }

        private sealed class FilterOption
        {
            public FilterOption(string text, string value)
            {
                Text = text ?? string.Empty;
                Value = value ?? string.Empty;
            }

            public string Text { get; private set; }

            public string Value { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }

        private sealed class GitHubIssueCreateForm : Form
        {
            private readonly TextBox _titleTextBox;
            private readonly TextBox _bodyTextBox;
            private readonly Button _okButton;
            private readonly Button _cancelButton;

            public GitHubIssueCreateForm()
            {
                Text = UiLocalization.Text("GitHubIssueCreateForm.Title", "Create Issue");
                StartPosition = FormStartPosition.CenterParent;
                MinimumSize = new Size(540, 360);
                Size = new Size(660, 460);
                ShowInTaskbar = false;
                KeyPreview = true;
                UiTheme.ApplyForm(this);

                _titleTextBox = CreateDialogTextBox(UiLocalization.Text("GitHubIssueCreateForm.Accessible.Title", "Title"), 0);
                _bodyTextBox = CreateDialogTextBox(UiLocalization.Text("GitHubIssueCreateForm.Accessible.Body", "Body"), 1);
                _bodyTextBox.Multiline = true;
                TextBoxLineEndingNormalizer.NormalizeTextBox(_bodyTextBox);
                _bodyTextBox.ScrollBars = ScrollBars.Vertical;
                _bodyTextBox.Height = 180;
                _okButton = CreateDialogButton(
                    UiLocalization.Text("GitHubIssueCreateForm.Button.OK", "Create(&C)"),
                    UiLocalization.Text("GitHubIssueCreateForm.Accessible.OK", "Create"),
                    DialogResult.OK,
                    2);
                _cancelButton = CreateDialogButton(
                    UiLocalization.Text("GitHubIssueCreateForm.Button.Cancel", "Cancel(&N)"),
                    UiLocalization.Text("GitHubIssueCreateForm.Accessible.Cancel", "Cancel"),
                    DialogResult.Cancel,
                    3);

                _okButton.Click += (sender, e) =>
                {
                    if (string.IsNullOrWhiteSpace(IssueTitle))
                    {
                        DialogResult = DialogResult.None;
                        MessageBox.Show(this, UiLocalization.Text("GitHubIssueCreateForm.Message.Required", "Title is required."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                };

                BuildLayout();
                AcceptButton = _okButton;
                CancelButton = _cancelButton;
            }

            public string IssueTitle
            {
                get { return _titleTextBox.Text == null ? string.Empty : _titleTextBox.Text.Trim(); }
            }

            public string Body
            {
                get { return _bodyTextBox.Text == null ? string.Empty : _bodyTextBox.Text.Trim(); }
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _titleTextBox.Focus();
            }

            private void BuildLayout()
            {
                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 6,
                    Padding = new Padding(UiTheme.OuterPadding),
                    BackColor = UiTheme.WindowBackColor,
                    TabStop = false
                };
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 8F));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                root.Controls.Add(CreateDialogLabel(UiLocalization.Text("GitHubIssueCreateForm.Label.Title", "Title(&T)")), 0, 0);
                root.Controls.Add(_titleTextBox, 0, 1);
                root.Controls.Add(CreateDialogLabel(UiLocalization.Text("GitHubIssueCreateForm.Label.Body", "Body(&B)")), 0, 2);
                root.Controls.Add(_bodyTextBox, 0, 3);
                root.Controls.Add(CreateDialogFooter(_okButton, _cancelButton), 0, 5);
                Controls.Add(root);
            }
        }

        private sealed class GitHubPullRequestCreateForm : Form
        {
            private readonly TextBox _baseBranchTextBox;
            private readonly TextBox _titleTextBox;
            private readonly TextBox _bodyTextBox;
            private readonly CheckBox _draftCheckBox;
            private readonly Button _okButton;
            private readonly Button _cancelButton;

            public GitHubPullRequestCreateForm(string headBranch, string defaultBaseBranch)
            {
                Text = UiLocalization.Text("GitHubPullRequestCreateForm.Title", "Create Pull Request");
                StartPosition = FormStartPosition.CenterParent;
                MinimumSize = new Size(560, 420);
                Size = new Size(680, 520);
                ShowInTaskbar = false;
                KeyPreview = true;
                UiTheme.ApplyForm(this);

                _baseBranchTextBox = CreateDialogTextBox(UiLocalization.Text("GitHubPullRequestCreateForm.Accessible.BaseBranch", "Base branch"), 0);
                _baseBranchTextBox.Text = string.IsNullOrWhiteSpace(defaultBaseBranch) ? "main" : defaultBaseBranch.Trim();
                _titleTextBox = CreateDialogTextBox(UiLocalization.Text("GitHubPullRequestCreateForm.Accessible.Title", "Title"), 1);
                _titleTextBox.Text = string.Format(UiLocalization.Text("GitHubPullRequestCreateForm.DefaultTitle.Template", "Merge {0}"), string.IsNullOrWhiteSpace(headBranch) ? string.Empty : headBranch.Trim());
                _bodyTextBox = CreateDialogTextBox(UiLocalization.Text("GitHubPullRequestCreateForm.Accessible.Body", "Body"), 2);
                _bodyTextBox.Multiline = true;
                TextBoxLineEndingNormalizer.NormalizeTextBox(_bodyTextBox);
                _bodyTextBox.ScrollBars = ScrollBars.Vertical;
                _bodyTextBox.Height = 160;
                _draftCheckBox = new CheckBox
                {
                    Text = UiLocalization.Text("GitHubPullRequestCreateForm.CheckBox.Draft", "Draft(&D)"),
                    AccessibleName = UiLocalization.Text("GitHubPullRequestCreateForm.Accessible.Draft", "Draft"),
                    AutoSize = true,
                    UseMnemonic = true,
                    TabIndex = 3
                };
                UiTheme.ApplyCheckBox(_draftCheckBox);
                _okButton = CreateDialogButton(
                    UiLocalization.Text("GitHubPullRequestCreateForm.Button.OK", "Create(&C)"),
                    UiLocalization.Text("GitHubPullRequestCreateForm.Accessible.OK", "Create"),
                    DialogResult.OK,
                    4);
                _cancelButton = CreateDialogButton(
                    UiLocalization.Text("GitHubPullRequestCreateForm.Button.Cancel", "Cancel(&N)"),
                    UiLocalization.Text("GitHubPullRequestCreateForm.Accessible.Cancel", "Cancel"),
                    DialogResult.Cancel,
                    5);

                _okButton.Click += (sender, e) =>
                {
                    if (string.IsNullOrWhiteSpace(BaseBranch) || string.IsNullOrWhiteSpace(PullRequestTitle))
                    {
                        DialogResult = DialogResult.None;
                        MessageBox.Show(this, UiLocalization.Text("GitHubPullRequestCreateForm.Message.Required", "Base branch and title are required."), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                };

                BuildLayout();
                AcceptButton = _okButton;
                CancelButton = _cancelButton;
            }

            public string BaseBranch
            {
                get { return _baseBranchTextBox.Text == null ? string.Empty : _baseBranchTextBox.Text.Trim(); }
            }

            public string PullRequestTitle
            {
                get { return _titleTextBox.Text == null ? string.Empty : _titleTextBox.Text.Trim(); }
            }

            public string Body
            {
                get { return _bodyTextBox.Text == null ? string.Empty : _bodyTextBox.Text.Trim(); }
            }

            public bool Draft
            {
                get { return _draftCheckBox.Checked; }
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                _titleTextBox.Focus();
            }

            private void BuildLayout()
            {
                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 8,
                    Padding = new Padding(UiTheme.OuterPadding),
                    BackColor = UiTheme.WindowBackColor,
                    TabStop = false
                };
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                root.Controls.Add(CreateDialogLabel(UiLocalization.Text("GitHubPullRequestCreateForm.Label.BaseBranch", "Base branch(&B)")), 0, 0);
                root.Controls.Add(_baseBranchTextBox, 0, 1);
                root.Controls.Add(CreateDialogLabel(UiLocalization.Text("GitHubPullRequestCreateForm.Label.Title", "Title(&T)")), 0, 2);
                root.Controls.Add(_titleTextBox, 0, 3);
                root.Controls.Add(CreateDialogLabel(UiLocalization.Text("GitHubPullRequestCreateForm.Label.Body", "Body(&Y)")), 0, 4);
                root.Controls.Add(_bodyTextBox, 0, 5);
                root.Controls.Add(_draftCheckBox, 0, 6);
                root.Controls.Add(CreateDialogFooter(_okButton, _cancelButton), 0, 7);
                Controls.Add(root);
            }
        }

        private static TextBox CreateDialogTextBox(string accessibleName, int tabIndex)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, false, true);
            return textBox;
        }

        private static Label CreateDialogLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false,
                UseMnemonic = true
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        private static Button CreateDialogButton(string text, string accessibleName, DialogResult dialogResult, int tabIndex)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                DialogResult = dialogResult,
                Width = 112,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
            UiTheme.ApplyButton(button, dialogResult == DialogResult.OK ? UiGlyph.Ok : UiGlyph.Cancel);
            return button;
        }

        private static Control CreateDialogFooter(params Button[] buttons)
        {
            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            foreach (var button in buttons ?? new Button[0])
            {
                if (button != null)
                {
                    footer.Controls.Add(button);
                }
            }

            return footer;
        }
    }
}
