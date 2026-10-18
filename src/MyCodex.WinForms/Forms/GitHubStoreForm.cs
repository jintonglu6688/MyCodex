using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitHubStoreForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _searchCancellation != null || _cloneCancellation != null;

        private sealed class SearchSortItem
        {
            public SearchSortItem(string text, string value)
            {
                Text = text ?? string.Empty;
                Value = value;
            }

            public string Text { get; }

            public string Value { get; }

            public override string ToString()
            {
                return Text;
            }
        }

        private readonly IScreenReaderService _screenReaderService;
        private readonly IGitHubCliService _gitHubCliService;
        private readonly TextBox _keywordTextBox;
        private readonly TextBox _languageTextBox;
        private readonly TextBox _topicTextBox;
        private readonly TextBox _licenseTextBox;
        private readonly TextBox _minStarsTextBox;
        private readonly TextBox _maxStarsTextBox;
        private readonly TextBox _minForksTextBox;
        private readonly TextBox _maxForksTextBox;
        private readonly TextBox _minOpenIssuesTextBox;
        private readonly TextBox _maxOpenIssuesTextBox;
        private readonly ComboBox _sortComboBox;
        private readonly ComboBox _storePresetComboBox;
        private readonly ComboBox _limitComboBox;
        private readonly TextBox _rankingLanguageTextBox;
        private readonly ComboBox _rankingLimitComboBox;
        private readonly CheckBox _publicOnlyCheckBox;
        private readonly CheckBox _showAdvancedSearchCheckBox;
        private readonly Button _searchButton;
        private readonly Button _rankingRefreshButton;
        private readonly Button _closeButton;
        private readonly Label _statusLabel;
        private readonly ListView _repositoriesListView;
        private readonly TabControl _modeTabControl;
        private readonly TabPage _searchTabPage;
        private readonly TabPage _rankingTabPage;
        private readonly ContextMenu _repositoryContextMenu;
        private readonly MenuItem _viewDetailsMenuItem;
        private readonly MenuItem _openRepositoryPageMenuItem;
        private readonly MenuItem _copyHttpsUrlMenuItem;
        private readonly MenuItem _copyFullNameMenuItem;
        private readonly MenuItem _cloneRepositoryMenuItem;
        private readonly MenuItem _forkAndCloneRepositoryMenuItem;
        private readonly MenuItem _fetchRepositoryMenuItem;
        private readonly MenuItem _pullRepositoryMenuItem;
        private readonly IGitRepositoryService _gitRepositoryService;
        private readonly string _defaultCloneParentDirectory;
        private readonly string _currentWorkspacePath;
        private GitHubRepositorySearchResult _lastSearchResult;
        private IReadOnlyList<GitHubRepositoryEntry> _repositories = new List<GitHubRepositoryEntry>();
        private CancellationTokenSource _searchCancellation;
        private CancellationTokenSource _cloneCancellation;

        public GitHubStoreForm(
            IGitHubCliService gitHubCliService,
            IScreenReaderService screenReaderService,
            string defaultCloneParentDirectory,
            string currentWorkspacePath = null,
            IGitRepositoryService gitRepositoryService = null)
        {
            _gitHubCliService = gitHubCliService ?? throw new ArgumentNullException(nameof(gitHubCliService));
            _screenReaderService = screenReaderService;
            _defaultCloneParentDirectory = string.IsNullOrWhiteSpace(defaultCloneParentDirectory)
                ? Environment.CurrentDirectory
                : defaultCloneParentDirectory;
            _currentWorkspacePath = string.IsNullOrWhiteSpace(currentWorkspacePath) ? null : currentWorkspacePath.Trim();
            _gitRepositoryService = gitRepositoryService ??
                new GitRepositoryService(new MyCodex.Infrastructure.Git.GitCommandRunner());

            Text = UiLocalization.Text("GitHubStoreForm.Title", "GitHub Store");
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 620);
            Size = new Size(1120, 700);
            ShowInTaskbar = true;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _keywordTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.Keyword", "Keyword"), 0);
            _languageTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.Language", "Language"), 1);
            _topicTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.Topic", "Topic"), 2);
            _licenseTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.License", "License"), 7);
            _sortComboBox = CreateComboBox(UiLocalization.Text("GitHubStoreForm.Accessible.Sort", "Sort"));
            _sortComboBox.TabIndex = 3;
            _sortComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Sort.BestMatch", "Best Match"), null));
            _sortComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Sort.Created", "Created"), "created"));
            _sortComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Sort.Stars", "Stars"), "stars"));
            _sortComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Sort.Forks", "Forks"), "forks"));
            _sortComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Sort.Updated", "Updated"), "updated"));
            _sortComboBox.SelectedIndex = 0;

            _storePresetComboBox = CreateComboBox(UiLocalization.Text("GitHubStoreForm.Accessible.Preset", "Preset"));
            _storePresetComboBox.TabIndex = 0;
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.Custom", "Custom"), "custom"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.TrendingStars", "Trending Stars"), "trending-stars"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.TrendingToday", "Trending Today"), "trending-today"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.TrendingWeek", "Trending Week"), "trending-week"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.TrendingMonth", "Trending Month"), "trending-month"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.NewRepos", "New Repositories"), "new-repos"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.NewStars", "New Stars"), "new-stars"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.NewForks", "New Forks"), "new-forks"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.HighActivity", "High Activity"), "high-activity"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.RecentUpdated", "Recent Updated"), "recent-updated"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.TopForks", "Top Forks"), "top-forks"));
            _storePresetComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Preset.RecentCreated", "Recent Created"), "recent-created"));
            _storePresetComboBox.SelectedIndex = 0;

            _minStarsTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.MinStars", "Min stars"), 8);
            _maxStarsTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.MaxStars", "Max stars"), 9);
            _minForksTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.MinForks", "Min forks"), 10);
            _maxForksTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.MaxForks", "Max forks"), 11);
            _minOpenIssuesTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.MinOpenIssues", "Min open issues"), 12);
            _maxOpenIssuesTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.MaxOpenIssues", "Max open issues"), 13);
            _limitComboBox = CreateComboBox(UiLocalization.Text("GitHubStoreForm.Accessible.Limit", "Limit"));
            _limitComboBox.TabIndex = 3;
            _limitComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Limit.10", "10"), "10"));
            _limitComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Limit.20", "20"), "20"));
            _limitComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Limit.30", "30"), "30"));
            _limitComboBox.SelectedIndex = 1;
            _rankingLanguageTextBox = UiControlFactory.CreateFillTextBox(UiLocalization.Text("GitHubStoreForm.Accessible.RankingLanguage", "Ranking language"), 1);
            _rankingLimitComboBox = CreateComboBox(UiLocalization.Text("GitHubStoreForm.Accessible.RankingLimit", "Ranking limit"));
            _rankingLimitComboBox.TabIndex = 2;
            _rankingLimitComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Limit.10", "10"), "10"));
            _rankingLimitComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Limit.20", "20"), "20"));
            _rankingLimitComboBox.Items.Add(new SearchSortItem(UiLocalization.Text("GitHubStoreForm.Limit.30", "30"), "30"));
            _rankingLimitComboBox.SelectedIndex = 1;
            _publicOnlyCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("GitHubStoreForm.Check.PublicOnly", "Public repositories(&U)"),
                AccessibleName = UiLocalization.Text("GitHubStoreForm.Accessible.PublicOnly", "Public repositories"),
                Checked = true,
                TabIndex = 6,
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0)
            };
            UiTheme.ApplyCheckBox(_publicOnlyCheckBox);
            _showAdvancedSearchCheckBox = new CheckBox
            {
                Text = UiLocalization.Text("GitHubStoreForm.Check.ShowAdvanced", "Show advanced filters(&A)"),
                AccessibleName = UiLocalization.Text("GitHubStoreForm.Accessible.ShowAdvanced", "Show advanced filters"),
                Checked = false,
                TabIndex = 4,
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 6)
            };
            UiTheme.ApplyCheckBox(_showAdvancedSearchCheckBox);

            _searchButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitHubStoreForm.Button.Search", "Search(&S)"),
                UiLocalization.Text("GitHubStoreForm.Accessible.Search", "Search"),
                14,
                106);
            _rankingRefreshButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitHubStoreForm.Button.RefreshRanking", "Load Rankings(&R)"),
                UiLocalization.Text("GitHubStoreForm.Accessible.RefreshRanking", "Load rankings"),
                3,
                132);
            _closeButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitHubStoreForm.Button.Close", "Close"),
                UiLocalization.Text("GitHubStoreForm.Accessible.Close", "Close"),
                15,
                112);
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitHubStoreForm.Status.Ready", "Ready to search GitHub repositories."),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, true);

            _repositoriesListView = CreateRepositoryListView();
            _modeTabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                AccessibleName = string.Empty,
                TabIndex = 0
            };
            _searchTabPage = new TabPage(UiLocalization.Text("GitHubStoreForm.Tab.Search", "Search"));
            _rankingTabPage = new TabPage(UiLocalization.Text("GitHubStoreForm.Tab.Rankings", "Rankings"));
            _repositoryContextMenu = new ContextMenu();
            _viewDetailsMenuItem = new MenuItem(UiLocalization.Text("GitHubStoreForm.Menu.ViewDetails", "View Details(&D)"));
            _openRepositoryPageMenuItem = new MenuItem(UiLocalization.Text("GitHubStoreForm.Menu.OpenRepository", "Open Repository(&O)"));
            _copyHttpsUrlMenuItem = new MenuItem(UiLocalization.Text("GitHubStoreForm.Menu.CopyHttpsUrl", "Copy HTTPS URL(&H)"));
            _copyFullNameMenuItem = new MenuItem(UiLocalization.Text("GitHubStoreForm.Menu.CopyFullName", "Copy Full Name(&N)"));
            _cloneRepositoryMenuItem = new MenuItem(UiLocalization.Text("GitHubStoreForm.Menu.Clone", "Clone(&L)"));
            _forkAndCloneRepositoryMenuItem = new MenuItem(UiLocalization.Text("GitHubStoreForm.Menu.ForkAndClone", "Fork and Clone(&F)"));
            _fetchRepositoryMenuItem = new MenuItem(UiLocalization.Text("GitHubStoreForm.Menu.Fetch", "Fetch(&F)"));
            _pullRepositoryMenuItem = new MenuItem(UiLocalization.Text("GitHubStoreForm.Menu.Pull", "Pull(&P)"));
            _repositoriesListView.ContextMenu = _repositoryContextMenu;
            _repositoriesListView.DoubleClick += (sender, e) => ShowSelectedRepositoryDetails();
            _repositoriesListView.KeyDown += RepositoryListView_KeyDown;
            _repositoryContextMenu.Popup += (sender, e) => RefreshRepositoryContextMenu();

            _keywordTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _languageTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _topicTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _rankingLanguageTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _licenseTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _minStarsTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _maxStarsTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _minForksTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _maxForksTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _minOpenIssuesTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _maxOpenIssuesTextBox.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    _ = SearchRepositoriesAsync();
                }
            };
            _searchButton.Click += async (sender, e) => await SearchRepositoriesAsync();
            _showAdvancedSearchCheckBox.CheckedChanged += (sender, e) => UpdateAdvancedSearchVisibility();
            _rankingRefreshButton.Click += async (sender, e) => await SearchRepositoriesAsync();
            _viewDetailsMenuItem.Click += (sender, e) => ShowSelectedRepositoryDetails();
            _openRepositoryPageMenuItem.Click += (sender, e) => OpenSelectedRepository();
            _copyHttpsUrlMenuItem.Click += (sender, e) => CopySelectedRepositoryValue(
                repository => repository.HttpsUrl,
                UiLocalization.Text("GitHubStoreForm.Status.CopiedHttpsUrl", "Copied HTTPS URL."));
            _copyFullNameMenuItem.Click += (sender, e) => CopySelectedRepositoryValue(
                repository => repository.NameWithOwner,
                UiLocalization.Text("GitHubStoreForm.Status.CopiedFullName", "Copied repository full name."));
            _cloneRepositoryMenuItem.Click += (sender, e) => _ = CloneSelectedRepositoryAsync();
            _forkAndCloneRepositoryMenuItem.Click += (sender, e) => _ = CloneSelectedRepositoryAsync(true);
            _fetchRepositoryMenuItem.Click += (sender, e) => _ = FetchSelectedRepositoryAsync();
            _pullRepositoryMenuItem.Click += (sender, e) => _ = PullSelectedRepositoryAsync();
            _closeButton.Click += (sender, e) => Close();
            FormClosing += GitHubStoreForm_FormClosing;
            KeyDown += GitHubStoreForm_KeyDown;
            Shown += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(_keywordTextBox.Text) ||
                    !string.IsNullOrWhiteSpace(_languageTextBox.Text) ||
                    !string.IsNullOrWhiteSpace(_topicTextBox.Text))
                {
                    _ = SearchRepositoriesAsync();
                    return;
                }

                _keywordTextBox.Focus();
            };

            UiTheme.ApplyButton(_searchButton, UiGlyph.Search);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            FormInteractionConventions.ConfigureRefreshButton(_searchButton);

            BuildLayout();
            AcceptButton = _searchButton;
            CancelButton = _closeButton;
        }

        private async void GitHubStoreForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.F5 || _searchCancellation != null)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            FormInteractionConventions.SpeakRefresh(_screenReaderService);
            await SearchRepositoriesAsync();
        }

        private void GitHubStoreForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_searchCancellation != null)
            {
                _searchCancellation.Cancel();
            }

            if (_cloneCancellation != null)
            {
                _cloneCancellation.Cancel();
            }
        }

        private async Task SearchRepositoriesAsync()
        {
            if (_searchCancellation != null)
            {
                return;
            }

            _searchCancellation = new CancellationTokenSource();
            SetLoading(true);
            if (_repositoriesListView.CanFocus)
            {
                _repositoriesListView.Focus();
            }

            RestoreRepositoryFocus();
            SetStatus(
                UiLocalization.Text("GitHubStoreForm.Status.Searching", "Searching GitHub repositories..."),
                false);
            var shouldFocusResults = false;
            try
            {
                var result = await _gitHubCliService.SearchRepositoriesAsync(
                    BuildCurrentRequest(),
                    _searchCancellation.Token).ConfigureAwait(true);
                _lastSearchResult = result;
                _repositories = result?.Items ?? new List<GitHubRepositoryEntry>();
                PopulateRepositories(_repositories);
                SetStatus(BuildLoadedStatusText(_lastSearchResult), false);
                shouldFocusResults = true;
            }
            catch (OperationCanceledException)
            {
                SetStatus(
                    UiLocalization.Text("GitHubStoreForm.Status.Canceled", "Repository search canceled."),
                    false);
            }
            catch (Exception ex)
            {
                _repositories = new List<GitHubRepositoryEntry>();
                _lastSearchResult = null;
                _repositoriesListView.Items.Clear();
                SetStatus(
                    UiLocalization.Text("GitHubStoreForm.Status.Failed", "Failed to load GitHub repositories."),
                    true);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (_searchCancellation != null)
                {
                    _searchCancellation.Dispose();
                    _searchCancellation = null;
                }

                SetLoading(false);
                if (shouldFocusResults)
                {
                    RestoreRepositoryFocus();
                }
            }
        }

        private GitHubRepositorySearchRequest BuildCurrentRequest()
        {
            return _modeTabControl != null && ReferenceEquals(_modeTabControl.SelectedTab, _rankingTabPage)
                ? BuildRankingRequest()
                : BuildSearchRequest();
        }

        private GitHubRepositorySearchRequest BuildSearchRequest()
        {
            var request = new GitHubRepositorySearchRequest
            {
                Keyword = _keywordTextBox.Text,
                Language = _languageTextBox.Text,
                Topic = _topicTextBox.Text,
                License = NormalizeTextValue(_licenseTextBox.Text),
                MinStars = ParseFilterIntValue(_minStarsTextBox),
                MaxStars = ParseFilterIntValue(_maxStarsTextBox),
                MinForks = ParseFilterIntValue(_minForksTextBox),
                MaxForks = ParseFilterIntValue(_maxForksTextBox),
                MinOpenIssues = ParseFilterIntValue(_minOpenIssuesTextBox),
                MaxOpenIssues = ParseFilterIntValue(_maxOpenIssuesTextBox),
                PublicOnly = _publicOnlyCheckBox.Checked,
                Sort = GetComboValue(_sortComboBox),
                Order = "desc",
                Limit = GetComboValueAsInt(_limitComboBox, 30)
            };
            return request;
        }

        private GitHubRepositorySearchRequest BuildRankingRequest()
        {
            var request = new GitHubRepositorySearchRequest
            {
                Language = _rankingLanguageTextBox.Text,
                PublicOnly = true,
                Sort = "stars",
                Order = "desc",
                Limit = GetComboValueAsInt(_rankingLimitComboBox, 30)
            };
            ApplyMarketPreset(request);
            return request;
        }

        private void ApplyMarketPreset(GitHubRepositorySearchRequest request)
        {
            if (request == null)
            {
                return;
            }

            var preset = GetComboValue(_storePresetComboBox);
            if (string.IsNullOrWhiteSpace(preset) || preset == "custom")
            {
                return;
            }

            if (preset == "trending-stars")
            {
                request.Sort = "stars";
                request.Order = "desc";
                request.MinStars = Math.Max(request.MinStars.GetValueOrDefault(0), 1000);
                request.Keyword = AppendSearchTerm(request.Keyword, "archived:false");
                return;
            }

            if (preset == "recent-updated")
            {
                request.Sort = "updated";
                request.Order = "desc";
                request.Keyword = AppendSearchTerm(request.Keyword, "pushed:>=" + GetDateDaysAgo(30));
                return;
            }

            if (preset == "trending-today")
            {
                request.Sort = "stars";
                request.Order = "desc";
                request.Keyword = AppendSearchTerm(request.Keyword, "pushed:>=" + DateTime.UtcNow.Date.ToString("yyyy-MM-dd"));
                return;
            }

            if (preset == "trending-week")
            {
                request.Sort = "stars";
                request.Order = "desc";
                request.Keyword = AppendSearchTerm(request.Keyword, "pushed:>=" + GetDateDaysAgo(7));
                return;
            }

            if (preset == "trending-month")
            {
                request.Sort = "stars";
                request.Order = "desc";
                request.Keyword = AppendSearchTerm(request.Keyword, "pushed:>=" + GetDateDaysAgo(30));
                return;
            }

            if (preset == "new-repos")
            {
                request.Sort = "created";
                request.Order = "desc";
                request.Keyword = AppendSearchTerm(request.Keyword, "created:>=" + GetDateDaysAgo(30));
                return;
            }

            if (preset == "new-stars")
            {
                request.Sort = "stars";
                request.Order = "desc";
                request.Keyword = AppendSearchTerm(request.Keyword, "pushed:>=" + GetDateDaysAgo(7));
                return;
            }

            if (preset == "new-forks")
            {
                request.Sort = "forks";
                request.Order = "desc";
                request.Keyword = AppendSearchTerm(request.Keyword, "pushed:>=" + GetDateDaysAgo(30));
                request.MinForks = Math.Max(request.MinForks.GetValueOrDefault(0), 20);
                return;
            }

            if (preset == "high-activity")
            {
                request.Sort = "updated";
                request.Order = "desc";
                request.Keyword = AppendSearchTerm(request.Keyword, "pushed:>=" + GetDateDaysAgo(14));
                request.MinStars = Math.Max(request.MinStars.GetValueOrDefault(0), 50);
                return;
            }

            if (preset == "top-forks")
            {
                request.Sort = "forks";
                request.Order = "desc";
                request.MinForks = Math.Max(request.MinForks.GetValueOrDefault(0), 50);
                request.Keyword = AppendSearchTerm(request.Keyword, "archived:false");
                return;
            }

            if (preset == "recent-created")
            {
                request.Sort = "created";
                request.Order = "desc";
                request.Keyword = AppendSearchTerm(request.Keyword, "created:>=" + GetDateDaysAgo(30));
            }
        }

        private static string GetDateDaysAgo(int days)
        {
            return DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
        }

        private static string AppendSearchTerm(string keyword, string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return keyword;
            }

            var normalizedKeyword = string.IsNullOrWhiteSpace(keyword)
                ? string.Empty
                : keyword.Trim();
            return string.IsNullOrWhiteSpace(normalizedKeyword)
                ? term
                : string.Format("{0} {1}", normalizedKeyword, term);
        }

        private static string BuildLoadedStatusText(GitHubRepositorySearchResult result)
        {
            if (result == null || result.Items == null || result.Items.Count == 0)
            {
                return UiLocalization.Text(
                    "GitHubStoreForm.Status.Empty",
                    "No repositories matched the current filters.");
            }

            var loadedCount = result?.Items == null ? 0 : result.Items.Count;
            var text = result != null && result.TotalCount.HasValue
                ? string.Format(
                    UiLocalization.Text("GitHubStoreForm.Status.LoadedWithTotal.Template", "Loaded {0}/{1} repositories."),
                    loadedCount,
                    result.TotalCount.Value)
                : string.Format(
                    UiLocalization.Text("GitHubStoreForm.Status.Loaded.Template", "Loaded {0} repositories."),
                    loadedCount);

            if (result != null && result.IncompleteResults)
            {
                text = string.Format(
                    "{0} {1}",
                    text,
                    UiLocalization.Text("GitHubStoreForm.Status.IncompleteSuffix", "Some results may be incomplete."));
            }

            return text;
        }

        private void PopulateRepositories(IReadOnlyList<GitHubRepositoryEntry> repositories)
        {
            _repositoriesListView.BeginUpdate();
            try
            {
                _repositoriesListView.Items.Clear();
                foreach (var repository in repositories ?? new List<GitHubRepositoryEntry>())
                {
                    _repositoriesListView.Items.Add(CreateRepositoryItem(repository));
                }
            }
            finally
            {
                _repositoriesListView.EndUpdate();
            }
        }

        private void RestoreRepositoryFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                if (_repositoriesListView.Items.Count > 0)
                {
                    if (_repositoriesListView.SelectedItems.Count == 0)
                    {
                        _repositoriesListView.Items[0].Selected = true;
                    }

                    _repositoriesListView.SelectedItems[0].Focused = true;
                    _repositoriesListView.SelectedItems[0].EnsureVisible();
                    _repositoriesListView.Focus();
                    return;
                }

                if (_keywordTextBox.CanFocus)
                {
                    _keywordTextBox.Focus();
                }
            });
        }

        private void SetLoading(bool loading)
        {
            _searchButton.Enabled = !loading;
            _keywordTextBox.Enabled = true;
            _languageTextBox.Enabled = true;
            _topicTextBox.Enabled = true;
            _licenseTextBox.Enabled = true;
            _minStarsTextBox.Enabled = true;
            _maxStarsTextBox.Enabled = true;
            _minForksTextBox.Enabled = true;
            _maxForksTextBox.Enabled = true;
            _minOpenIssuesTextBox.Enabled = true;
            _maxOpenIssuesTextBox.Enabled = true;
            _sortComboBox.Enabled = !loading;
            _storePresetComboBox.Enabled = !loading;
            _limitComboBox.Enabled = !loading;
            _rankingLanguageTextBox.Enabled = !loading;
            _rankingLimitComboBox.Enabled = !loading;
            _showAdvancedSearchCheckBox.Enabled = !loading;
            _rankingRefreshButton.Enabled = !loading;
            _publicOnlyCheckBox.Enabled = !loading;
            UpdateAdvancedSearchVisibility();
            _repositoriesListView.Enabled = !loading;
        }

        private void SetCloneLoading(bool loading)
        {
            _repositoriesListView.Enabled = !loading;
            _searchButton.Enabled = !loading;
        }

        private async Task CloneSelectedRepositoryAsync(bool forkBeforeClone = false)
        {
            if (_cloneCancellation != null)
            {
                return;
            }

            var repository = GetSelectedRepository();
            if (repository == null || string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                SetStatus(UiLocalization.Text("GitHubStoreForm.Status.CannotClone", "Cannot determine clone target."), true);
                RestoreRepositoryFocus();
                return;
            }

            var folderName = ResolveCloneTargetFolderName(repository);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                SetStatus(UiLocalization.Text("GitHubStoreForm.Status.InvalidCloneFolder", "Cannot infer target folder name."), true);
                RestoreRepositoryFocus();
                return;
            }

            SetCloneLoading(true);
            _cloneCancellation = new CancellationTokenSource();
            var targetRepository = repository;
            try
            {
                if (forkBeforeClone)
                {
                    SetStatus(UiLocalization.Text("GitHubStoreForm.Status.Forking", "Forking repository..."), true);
                    targetRepository = await _gitHubCliService.ForkRepositoryAsync(repository, _cloneCancellation.Token)
                        .ConfigureAwait(true);
                    if (targetRepository == null || string.IsNullOrWhiteSpace(targetRepository.NameWithOwner))
                    {
                        throw new InvalidOperationException(UiLocalization.Text("GitHubStoreForm.Status.InvalidForkResult", "Fork result is invalid."));
                    }
                }

                SetStatus(UiLocalization.Text("GitHubStoreForm.Status.Cloning", "Cloning repository..."), true);
                var result = await _gitHubCliService.CloneRepositoryAsync(
                    targetRepository,
                    _defaultCloneParentDirectory,
                    folderName,
                    false,
                    false,
                    null,
                    _cloneCancellation.Token).ConfigureAwait(true);
                if (result.Success)
                {
                    SetStatus(
                        string.Format(UiLocalization.Text("GitHubStoreForm.Status.CloneSucceeded.Template", "Clone completed: {0}"), result.TargetPath),
                        true);
                    return;
                }

                SetStatus(
                    string.Format(UiLocalization.Text("GitHubStoreForm.Status.CloneFailed.Template", "Clone failed: {0}"), result.ExitCode),
                    true);
                MessageBox.Show(
                    this,
                    string.Format(UiLocalization.Text("GitHubStoreForm.Status.CloneFailed.Template", "Clone failed: {0}"), result.ExitCode),
                    UiLocalization.Text("GitHubStoreForm.DialogTitle.CloneFailed", "Clone failed"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubStoreForm.Status.CloneCanceled", "Clone canceled."), true);
            }
            catch (Exception ex)
            {
                SetStatus(BuildCloneFailureStatusMessage(ex), true);
                MessageBox.Show(
                    this,
                    BuildCloneFailureStatusMessage(ex),
                    UiLocalization.Text("GitHubStoreForm.DialogTitle.CloneFailed", "Clone failed"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                if (_cloneCancellation != null)
                {
                    _cloneCancellation.Dispose();
                    _cloneCancellation = null;
                }

                SetCloneLoading(false);
                RestoreRepositoryFocus();
            }
        }

        private async Task FetchSelectedRepositoryAsync()
        {
            if (_cloneCancellation != null)
            {
                return;
            }

            var repository = GetSelectedRepository();
            if (repository == null || string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                SetStatus(UiLocalization.Text("GitHubStoreForm.Status.CannotFetch", "Cannot determine fetch target."), true);
                RestoreRepositoryFocus();
                return;
            }

            var localRepositoryPath = ResolveLocalRepositoryPath(repository);
            if (string.IsNullOrWhiteSpace(localRepositoryPath))
            {
                SetStatus(
                    UiLocalization.Text(
                        "GitHubStoreForm.Status.FetchRequiresClone",
                        "This repository is not cloned locally. Clone it before fetching."),
                    true);
                RestoreRepositoryFocus();
                return;
            }

            SetCloneLoading(true);
            _cloneCancellation = new CancellationTokenSource();
            try
            {
                SetStatus(
                    UiLocalization.Text("GitHubStoreForm.Status.Fetching", "Fetching updates from remote repository..."),
                    true);
                var result = await _gitRepositoryService.FetchAsync(localRepositoryPath, _cloneCancellation.Token)
                    .ConfigureAwait(true);
                if (result == null || !result.Success)
                {
                    throw new InvalidOperationException(BuildFetchFailureStatusMessage(result));
                }

                SetStatus(
                    string.Format(
                        UiLocalization.Text("GitHubStoreForm.Status.FetchSucceeded.Template", "Fetch succeeded: {0}"),
                        repository.NameWithOwner),
                    true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubStoreForm.Status.FetchCanceled", "Fetch canceled."), true);
            }
            catch (Exception ex)
            {
                var message = BuildFetchFailureStatusMessage(ex);
                SetStatus(message, true);
                MessageBox.Show(
                    this,
                    message,
                    UiLocalization.Text("GitHubStoreForm.DialogTitle.FetchFailed", "Fetch failed"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                if (_cloneCancellation != null)
                {
                    _cloneCancellation.Dispose();
                    _cloneCancellation = null;
                }

                SetCloneLoading(false);
                RestoreRepositoryFocus();
            }
        }

        private async Task PullSelectedRepositoryAsync()
        {
            if (_cloneCancellation != null)
            {
                return;
            }

            var repository = GetSelectedRepository();
            if (repository == null || string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                SetStatus(UiLocalization.Text("GitHubStoreForm.Status.CannotPull", "Cannot determine pull target."), true);
                RestoreRepositoryFocus();
                return;
            }

            var localRepositoryPath = ResolveLocalRepositoryPath(repository);
            if (string.IsNullOrWhiteSpace(localRepositoryPath))
            {
                SetStatus(
                    UiLocalization.Text(
                        "GitHubStoreForm.Status.PullRequiresClone",
                        "This repository is not cloned locally. Clone it before pulling."),
                    true);
                RestoreRepositoryFocus();
                return;
            }

            SetCloneLoading(true);
            _cloneCancellation = new CancellationTokenSource();
            try
            {
                SetStatus(
                    UiLocalization.Text("GitHubStoreForm.Status.Pulling", "Pulling updates from remote repository..."),
                    true);
                var result = await _gitRepositoryService.PullAsync(localRepositoryPath, _cloneCancellation.Token)
                    .ConfigureAwait(true);
                if (result == null || !result.Success)
                {
                    throw new InvalidOperationException(BuildPullFailureStatusMessage(result));
                }

                SetStatus(
                    string.Format(
                        UiLocalization.Text("GitHubStoreForm.Status.PullSucceeded.Template", "Pull succeeded: {0}"),
                        repository.NameWithOwner),
                    true);
            }
            catch (OperationCanceledException)
            {
                SetStatus(UiLocalization.Text("GitHubStoreForm.Status.PullCanceled", "Pull canceled."), true);
            }
            catch (Exception ex)
            {
                var message = BuildPullFailureStatusMessage(ex);
                SetStatus(message, true);
                MessageBox.Show(
                    this,
                    message,
                    UiLocalization.Text("GitHubStoreForm.DialogTitle.PullFailed", "Pull failed"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                if (_cloneCancellation != null)
                {
                    _cloneCancellation.Dispose();
                    _cloneCancellation = null;
                }

                SetCloneLoading(false);
                RestoreRepositoryFocus();
            }
        }

        private static string ResolveCloneTargetFolderName(GitHubRepositoryEntry repository)
        {
            if (repository == null)
            {
                return string.Empty;
            }

            return GitClonePathPlanner.InferTargetFolderName(
                string.IsNullOrWhiteSpace(repository.HttpsUrl) ? repository.NameWithOwner : repository.HttpsUrl);
        }

        private string ResolveLocalRepositoryPath(GitHubRepositoryEntry repository)
        {
            if (repository == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(_defaultCloneParentDirectory))
            {
                return null;
            }

            var folderName = ResolveCloneTargetFolderName(repository);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return null;
            }

            var localPath = Path.Combine(_defaultCloneParentDirectory, folderName);
            return Directory.Exists(localPath) && Directory.Exists(Path.Combine(localPath, ".git"))
                ? localPath
                : null;
        }

        private void ShowSelectedRepositoryDetails()
        {
            var repository = GetSelectedRepository();
            if (repository == null || string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                RestoreRepositoryFocus();
                return;
            }

            var form = new GitHubRepositoryDetailsForm(
                _gitHubCliService,
                repository,
                _screenReaderService,
                _currentWorkspacePath,
                _gitRepositoryService,
                GitHubRepositoryDetailsMode.PublicBrowse);
            form.FormClosed += (sender, e) => RestoreRepositoryFocus();
            form.Show(this);
        }

        private void OpenSelectedRepository()
        {
            var repository = GetSelectedRepository();
            var url = BuildRepositoryWebUrl(repository);
            if (string.IsNullOrWhiteSpace(url))
            {
                RestoreRepositoryFocus();
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                SetStatus(UiLocalization.Text("GitHubStoreForm.Status.OpenedRepository", "Repository page opened."), true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CopySelectedRepositoryValue(
            Func<GitHubRepositoryEntry, string> valueSelector,
            string status)
        {
            var repository = GetSelectedRepository();
            var value = repository == null || valueSelector == null ? null : valueSelector(repository);
            if (string.IsNullOrWhiteSpace(value))
            {
                RestoreRepositoryFocus();
                return;
            }

            Clipboard.SetText(value.Trim());
            SetStatus(status, true);
            RestoreRepositoryFocus();
        }

        private GitHubRepositoryEntry GetSelectedRepository()
        {
            var index = _repositoriesListView.SelectedIndices.Count > 0
                ? _repositoriesListView.SelectedIndices[0]
                : -1;

            if (_repositories == null || index < 0 || index >= _repositories.Count)
            {
                if (_repositories == null || _repositories.Count == 0)
                {
                    return null;
                }

                if (_repositoriesListView.Items.Count == 0)
                {
                    return null;
                }

                if (_repositoriesListView.SelectedIndices.Count == 0)
                {
                    return _repositories[0];
                }

                return null;
            }

            return _repositories[index];
        }

        private static string BuildRepositoryWebUrl(GitHubRepositoryEntry repository)
        {
            if (repository == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(repository.HttpsUrl))
            {
                return repository.HttpsUrl.Trim();
            }

            if (!string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                return string.Format(
                    UiLocalization.Text("GitHubStoreForm.Value.RepositoryWebUrl", "https://github.com/{0}"),
                    repository.NameWithOwner.Trim());
            }

            return string.Empty;
        }

        private void RefreshRepositoryContextMenu()
        {
            var repository = GetSelectedRepository();
            _repositoryContextMenu.MenuItems.Clear();
            if (repository == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                _repositoryContextMenu.MenuItems.Add(_viewDetailsMenuItem);
            }

            if (!string.IsNullOrWhiteSpace(BuildRepositoryWebUrl(repository)))
            {
                _repositoryContextMenu.MenuItems.Add(_openRepositoryPageMenuItem);
                _repositoryContextMenu.MenuItems.Add(_copyHttpsUrlMenuItem);
            }

            if (!string.IsNullOrWhiteSpace(repository.NameWithOwner))
            {
                if (_repositoryContextMenu.MenuItems.Count > 0)
                {
                    _repositoryContextMenu.MenuItems.Add("-");
                }

                _repositoryContextMenu.MenuItems.Add(_copyFullNameMenuItem);
                _repositoryContextMenu.MenuItems.Add("-");
                _repositoryContextMenu.MenuItems.Add(_cloneRepositoryMenuItem);
                _repositoryContextMenu.MenuItems.Add(_forkAndCloneRepositoryMenuItem);
                if (!string.IsNullOrWhiteSpace(ResolveLocalRepositoryPath(repository)))
                {
                    _repositoryContextMenu.MenuItems.Add("-");
                    _repositoryContextMenu.MenuItems.Add(_fetchRepositoryMenuItem);
                    _repositoryContextMenu.MenuItems.Add(_pullRepositoryMenuItem);
                }
            }
        }

        private void RepositoryListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ShowSelectedRepositoryDetails();
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
            if (_repositoriesListView.Items.Count == 0)
            {
                _repositoryContextMenu.Show(_repositoriesListView, new Point(8, 8));
                return;
            }

            var item = _repositoriesListView.SelectedItems.Count == 0
                ? _repositoriesListView.Items[0]
                : _repositoriesListView.SelectedItems[0];
            if (_repositoriesListView.SelectedItems.Count == 0)
            {
                item.Selected = true;
            }

            item.Focused = true;
            item.EnsureVisible();
            _repositoryContextMenu.Show(
                _repositoriesListView,
                new Point(Math.Max(8, item.Bounds.Left), item.Bounds.Bottom));
        }

        private void SetStatus(string text, bool speak)
        {
            _statusLabel.Text = string.IsNullOrWhiteSpace(text)
                ? UiLocalization.Text("GitHubStoreForm.Status.Ready", "Ready to search GitHub repositories.")
                : text;

            if (speak && _screenReaderService != null)
            {
                _screenReaderService.Speak(_statusLabel.Text, true, false, true);
            }
        }

        private static string BuildCloneFailureStatusMessage(Exception ex)
        {
            var errorMessage = ex == null || string.IsNullOrWhiteSpace(ex.Message)
                ? UiLocalization.Text("GitHubStoreForm.Status.Failed", "Failed to perform operation.")
                : ex.Message.Trim();

            if (IsCloneAuthenticationFailure(errorMessage))
            {
                return string.Format(
                    UiLocalization.Text(
                        "GitHubStoreForm.Status.CloneFailedWithRetryHint.Template",
                        "Clone failed: {0}. You can retry, and if this is an authentication issue, run `gh auth refresh -s repo` and retry."),
                    errorMessage);
            }

            return string.Format(
                UiLocalization.Text("GitHubStoreForm.Status.CloneFailed.Template", "Clone failed: {0}"),
                errorMessage);
        }

        private static string BuildPullFailureStatusMessage(GitOperationResult result)
        {
            var errorMessage = GetFetchFailureMessage(result);
            if (IsCloneAuthenticationFailure(errorMessage))
            {
                return string.Format(
                    UiLocalization.Text(
                        "GitHubStoreForm.Status.PullFailedWithRetryHint.Template",
                        "Pull failed: {0}. You can retry, and if this is an authentication issue, run `gh auth refresh -s repo` and retry."),
                    errorMessage);
            }

            return string.Format(
                UiLocalization.Text("GitHubStoreForm.Status.PullFailed.Template", "Pull failed: {0}"),
                errorMessage);
        }

        private static string BuildPullFailureStatusMessage(Exception ex)
        {
            var message = ex == null || string.IsNullOrWhiteSpace(ex.Message)
                ? UiLocalization.Text("GitHubStoreForm.Status.Failed", "Failed to perform operation.")
                : ex.Message.Trim();

            return BuildPullFailureStatusMessage(new GitOperationResult
            {
                DisplayMessage = message
            });
        }

        private static string BuildFetchFailureStatusMessage(GitOperationResult result)
        {
            var errorMessage = GetFetchFailureMessage(result);
            if (IsCloneAuthenticationFailure(errorMessage))
            {
                return string.Format(
                    UiLocalization.Text(
                        "GitHubStoreForm.Status.FetchFailedWithRetryHint.Template",
                        "Fetch failed: {0}. You can retry, and if this is an authentication issue, run `gh auth refresh -s repo` and retry."),
                    errorMessage);
            }

            return string.Format(
                UiLocalization.Text("GitHubStoreForm.Status.FetchFailed.Template", "Fetch failed: {0}"),
                errorMessage);
        }

        private static string BuildFetchFailureStatusMessage(Exception ex)
        {
            var message = ex == null || string.IsNullOrWhiteSpace(ex.Message)
                ? UiLocalization.Text("GitHubStoreForm.Status.Failed", "Failed to perform operation.")
                : ex.Message.Trim();

            var result = new GitOperationResult
            {
                DisplayMessage = message
            };

            return BuildFetchFailureStatusMessage(result);
        }

        private static string GetFetchFailureMessage(GitOperationResult result)
        {
            if (result == null)
            {
                return UiLocalization.Text("GitHubStoreForm.Status.Failed", "Failed to perform operation.");
            }

            var value = string.IsNullOrWhiteSpace(result.DisplayMessage) ? result.StandardError : result.DisplayMessage;
            if (string.IsNullOrWhiteSpace(value))
            {
                value = result.StandardOutput;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            return result.ExitCode.ToString();
        }

        private static bool IsCloneAuthenticationFailure(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            var lower = message.ToLowerInvariant();
            return lower.Contains("authentication")
                || lower.Contains("permission")
                || lower.Contains("forbidden")
                || lower.Contains("unauthorized")
                || lower.Contains("not authorized")
                || lower.Contains("denied")
                || lower.Contains("401")
                || lower.Contains("403");
        }

        private static ComboBox CreateComboBox(string accessibleName)
        {
            var comboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 120,
                AccessibleName = accessibleName
            };
            UiTheme.ApplyComboBox(comboBox);
            return comboBox;
        }

        private static ListView CreateRepositoryListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitHubStoreForm.Accessible.Repositories", "Repositories"),
                TabIndex = 1,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            listView.Columns.Add(
                UiLocalization.Text("GitHubStoreForm.Column.Repository", "Repository"), 230);
            listView.Columns.Add(
                UiLocalization.Text("GitHubStoreForm.Column.Description", "Description"), 330);
            listView.Columns.Add(
                UiLocalization.Text("GitHubStoreForm.Column.Language", "Language"), 130);
            listView.Columns.Add(
                UiLocalization.Text("GitHubStoreForm.Column.Stars", "Stars"), 75);
            listView.Columns.Add(
                UiLocalization.Text("GitHubStoreForm.Column.Forks", "Forks"), 75);
            listView.Columns.Add(
                UiLocalization.Text("GitHubStoreForm.Column.OpenIssues", "Open Issues"), 95);
            listView.Columns.Add(
                UiLocalization.Text("GitHubStoreForm.Column.License", "License"), 95);
            listView.Columns.Add(
                UiLocalization.Text("GitHubStoreForm.Column.PushedAt", "Pushed"), 150);
            listView.Columns.Add(
                UiLocalization.Text("GitHubStoreForm.Column.UpdatedAt", "Updated"), 150);
            listView.Columns.Add(
                UiLocalization.Text("GitHubStoreForm.Column.Topics", "Topics"), 260);
            UiTheme.ApplyListView(listView);
            return listView;
        }

        private static ListViewItem CreateRepositoryItem(GitHubRepositoryEntry repository)
        {
            var item = new ListViewItem(repository == null ? string.Empty : repository.NameWithOwner);
            item.SubItems.Add(DisplayValue(repository == null ? null : repository.Description));
            item.SubItems.Add(DisplayValue(repository == null ? null : repository.PrimaryLanguage));
            item.SubItems.Add(DisplayNumeric(repository == null ? null : repository.Stars));
            item.SubItems.Add(DisplayNumeric(repository == null ? null : repository.Forks));
            item.SubItems.Add(DisplayNumeric(repository == null ? null : repository.OpenIssues));
            item.SubItems.Add(DisplayValue(repository == null ? null : repository.License));
            item.SubItems.Add(DisplayDateValue(repository == null ? null : repository.PushedAt));
            item.SubItems.Add(DisplayDateValue(repository == null ? null : repository.UpdatedAt));
            item.SubItems.Add(DisplayValue(repository == null ? null : repository.Topics));
            item.Tag = repository;
            return item;
        }

        private static string DisplayValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? UiLocalization.Text("GitHubStoreForm.Value.NotAvailable", "—")
                : value;
        }

        private static string DisplayNumeric(int? value)
        {
            return value?.ToString() ?? UiLocalization.Text("GitHubStoreForm.Value.NotAvailable", "—");
        }

        private static string DisplayDateValue(DateTimeOffset? value)
        {
            return value.HasValue
                ? value.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : UiLocalization.Text("GitHubStoreForm.Value.NotAvailable", "—");
        }

        private static int GetComboValueAsInt(ComboBox comboBox, int defaultValue)
        {
            var item = comboBox?.SelectedItem as SearchSortItem;
            if (item == null || string.IsNullOrWhiteSpace(item.Value))
            {
                return defaultValue;
            }

            return int.TryParse(item.Value, out var limit) ? limit : defaultValue;
        }

        private static string GetComboValue(ComboBox comboBox)
        {
            return (comboBox?.SelectedItem as SearchSortItem)?.Value;
        }

        private static string NormalizeTextValue(string value)
        {
            var normalized = value?.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }

        private static int? ParseFilterIntValue(TextBox textBox)
        {
            if (textBox == null)
            {
                return null;
            }

            var text = textBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (!int.TryParse(text, out var value) || value < 0)
            {
                return null;
            }

            return value;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter && ReferenceEquals(ActiveControl, _repositoriesListView))
            {
                ShowSelectedRepositoryDetails();
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
                RowCount = 4,
                Padding = new Padding(12),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var heading = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitHubStoreForm.Heading", "GitHub Store"),
                AutoSize = true,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = ForeColor,
                BackColor = UiTheme.WindowBackColor,
                Margin = new Padding(0, 0, 0, 8),
                TabStop = false
            };

            _modeTabControl.TabPages.Clear();
            _searchTabPage.Controls.Add(CreateSearchTabLayout());
            _rankingTabPage.Controls.Add(CreateRankingTabLayout());
            _modeTabControl.TabPages.Add(_searchTabPage);
            _modeTabControl.TabPages.Add(_rankingTabPage);

            var statusPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor
            };
            statusPanel.Controls.Add(_statusLabel);

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
            footer.Controls.Add(statusPanel, 0, 0);
            footer.Controls.Add(_closeButton, 1, 0);

            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(_modeTabControl, 0, 1);
            root.Controls.Add(_repositoriesListView, 0, 2);
            root.Controls.Add(footer, 0, 3);

            Controls.Add(root);
        }

        private Control CreateSearchTabLayout()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            ConfigureFreeControl(_keywordTextBox, 80, 10, 190, 24, 0);
            ConfigureFreeControl(_languageTextBox, 340, 10, 150, 24, 1);
            ConfigureFreeControl(_topicTextBox, 560, 10, 150, 24, 2);
            ConfigureFreeControl(_limitComboBox, 80, 44, 110, 24, 3);
            ConfigureFreeControl(_showAdvancedSearchCheckBox, 0, 78, 220, 28, 4);
            ConfigureFreeControl(_licenseTextBox, 80, 112, 150, 24, 5);
            ConfigureFreeControl(_minStarsTextBox, 340, 112, 120, 24, 6);
            ConfigureFreeControl(_maxStarsTextBox, 560, 112, 120, 24, 7);
            ConfigureFreeControl(_minForksTextBox, 80, 146, 120, 24, 8);
            ConfigureFreeControl(_maxForksTextBox, 340, 146, 120, 24, 9);
            ConfigureFreeControl(_minOpenIssuesTextBox, 560, 146, 120, 24, 10);
            ConfigureFreeControl(_maxOpenIssuesTextBox, 80, 180, 120, 24, 11);
            ConfigureFreeControl(_sortComboBox, 340, 180, 150, 24, 12);
            ConfigureFreeControl(_publicOnlyCheckBox, 560, 180, 180, 28, 13);
            ConfigureFreeControl(_searchButton, 0, 218, 106, UiTheme.ButtonHeight, 14);

            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.Keyword", "Keyword(&K)"), 0, 12, 72, 22);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.Language", "Language(&L)"), 280, 12, 56, 22);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.Topic", "Topic(&T)"), 500, 12, 56, 22);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.Limit", "Limit(&I)"), 0, 46, 72, 22);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.License", "License(&A)"), 0, 114, 72, 22, true);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.MinStars", "Min Stars(&N)"), 250, 114, 84, 22, true);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.MaxStars", "Max Stars(&M)"), 470, 114, 84, 22, true);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.MinForks", "Min Forks(&F)"), 0, 148, 72, 22, true);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.MaxForks", "Max Forks(&R)"), 250, 148, 84, 22, true);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.MinOpenIssues", "Min Issues(&E)"), 470, 148, 84, 22, true);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.MaxOpenIssues", "Max Issues(&J)"), 0, 182, 72, 22, true);
            AddFreeLabel(panel, UiLocalization.Text("GitHubStoreForm.Label.Sort", "Sort(&S)"), 250, 182, 84, 22, true);

            panel.Controls.Add(_keywordTextBox);
            panel.Controls.Add(_languageTextBox);
            panel.Controls.Add(_topicTextBox);
            panel.Controls.Add(_limitComboBox);
            panel.Controls.Add(_showAdvancedSearchCheckBox);
            panel.Controls.Add(_licenseTextBox);
            panel.Controls.Add(_minStarsTextBox);
            panel.Controls.Add(_maxStarsTextBox);
            panel.Controls.Add(_minForksTextBox);
            panel.Controls.Add(_maxForksTextBox);
            panel.Controls.Add(_minOpenIssuesTextBox);
            panel.Controls.Add(_maxOpenIssuesTextBox);
            panel.Controls.Add(_sortComboBox);
            panel.Controls.Add(_publicOnlyCheckBox);
            panel.Controls.Add(_searchButton);

            panel.Height = 260;
            UpdateAdvancedSearchVisibility();
            return panel;
        }

        private Control CreateRankingTabLayout()
        {
            var panel = CreateFilterPanel(6);
            panel.Controls.Add(CreateSimpleLabel(UiLocalization.Text("GitHubStoreForm.Label.Ranking", "Ranking(&R)")), 0, 0);
            panel.Controls.Add(_storePresetComboBox, 1, 0);
            panel.Controls.Add(CreateSimpleLabel(UiLocalization.Text("GitHubStoreForm.Label.RankingLanguage", "Language(&G)")), 2, 0);
            panel.Controls.Add(_rankingLanguageTextBox, 3, 0);
            panel.Controls.Add(CreateSimpleLabel(UiLocalization.Text("GitHubStoreForm.Label.RankingLimit", "Limit(&I)")), 0, 1);
            panel.Controls.Add(_rankingLimitComboBox, 1, 1);
            panel.Controls.Add(_rankingRefreshButton, 5, 0);
            return panel;
        }

        private static void ConfigureFreeControl(Control control, int x, int y, int width, int height, int tabIndex)
        {
            if (control == null)
            {
                return;
            }

            control.Dock = DockStyle.None;
            control.Location = new Point(x, y);
            control.Size = new Size(width, height);
            control.TabIndex = tabIndex;
            control.TabStop = true;
            control.Margin = Padding.Empty;
        }

        private static void AddFreeLabel(
            Control parent,
            string text,
            int x,
            int y,
            int width,
            int height,
            bool advanced = false)
        {
            var label = new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                Name = advanced ? "AdvancedSearchLabel" : string.Empty
            };
            UiTheme.ApplyLabel(label, true);
            parent.Controls.Add(label);
        }

        private void UpdateAdvancedSearchVisibility()
        {
            var visible = _showAdvancedSearchCheckBox != null && _showAdvancedSearchCheckBox.Checked;
            SetAdvancedLabelsVisible(this, visible);

            SetAdvancedControlFocusState(_licenseTextBox, visible, 5);
            SetAdvancedControlFocusState(_minStarsTextBox, visible, 6);
            SetAdvancedControlFocusState(_maxStarsTextBox, visible, 7);
            SetAdvancedControlFocusState(_minForksTextBox, visible, 8);
            SetAdvancedControlFocusState(_maxForksTextBox, visible, 9);
            SetAdvancedControlFocusState(_minOpenIssuesTextBox, visible, 10);
            SetAdvancedControlFocusState(_maxOpenIssuesTextBox, visible, 11);
            SetAdvancedControlFocusState(_sortComboBox, visible, 12);
            SetAdvancedControlFocusState(_publicOnlyCheckBox, visible, 13);
        }

        private static void SetAdvancedLabelsVisible(Control root, bool visible)
        {
            if (root == null)
            {
                return;
            }

            foreach (Control child in root.Controls)
            {
                if (string.Equals(child.Name, "AdvancedSearchLabel", StringComparison.Ordinal))
                {
                    child.Visible = visible;
                }

                SetAdvancedLabelsVisible(child, visible);
            }
        }

        private static void SetAdvancedControlFocusState(Control control, bool visible, int tabIndex)
        {
            if (control == null)
            {
                return;
            }

            control.Visible = visible;
            control.TabStop = visible;
            control.TabIndex = tabIndex;
        }

        private static Control FindControlByName(Control root, string name)
        {
            if (root == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (Control child in root.Controls)
            {
                if (string.Equals(child.Name, name, StringComparison.Ordinal))
                {
                    return child;
                }

                var nested = FindControlByName(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static TableLayoutPanel CreateFilterPanel(int columnCount, int rowCount = 2)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columnCount,
                RowCount = rowCount,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 8),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            for (var index = 0; index < columnCount; index++)
            {
                panel.ColumnStyles.Add(index % 2 == 0
                    ? new ColumnStyle(SizeType.AutoSize)
                    : new ColumnStyle(SizeType.Percent, 25F));
            }

            for (var row = 0; row < rowCount; row++)
            {
                panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            return panel;
        }

        private static Label CreateSimpleLabel(string text)
        {
            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false,
                Margin = new Padding(0, 4, 0, 0)
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }
    }
}
