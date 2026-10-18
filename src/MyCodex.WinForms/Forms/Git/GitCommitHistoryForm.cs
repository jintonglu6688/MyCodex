using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitCommitHistoryForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _isLoading || _operationCancellationSource != null;

        private const int MaxCommitCount = 100;

        private readonly string _workspacePath;
        private readonly IGitRepositoryService _gitService;
        private readonly IScreenReaderService _screenReaderService;
        private readonly Label _statusLabel;
        private readonly Label _historyRefLabel;
        private readonly TreeView _historyTreeView;
        private readonly ListView _commitListView;
        private readonly ListView _fileListView;
        private readonly TextBox _diffTextBox;
        private readonly Button _refreshButton;
        private readonly Button _loadDetailsButton;
        private readonly Button _closeButton;
        private readonly ContextMenu _commitContextMenu;
        private readonly MenuItem _loadDetailsMenuItem;
        private readonly MenuItem _cherryPickMenuItem;
        private readonly MenuItem _revertMenuItem;
        private readonly MenuItem _amendMessageMenuItem;
        private readonly MenuItem _resetMenuItem;
        private readonly MenuItem _copyIdMenuItem;
        private readonly MenuItem _copyShortIdMenuItem;
        private readonly MenuItem _copySubjectMenuItem;
        private readonly MenuItem _copySummaryMenuItem;
        private readonly MenuItem _copyDiffMenuItem;
        private readonly ContextMenu _branchContextMenu;
        private readonly MenuItem _loadBranchHistoryMenuItem;
        private readonly MenuItem _checkoutBranchMenuItem;
        private readonly MenuItem _fetchBranchesMenuItem;
        private readonly MenuItem _pullBranchMenuItem;
        private readonly MenuItem _pushBranchMenuItem;
        private readonly MenuItem _forcePushBranchMenuItem;
        private readonly MenuItem _publishBranchMenuItem;
        private readonly MenuItem _renameCurrentBranchMenuItem;
        private readonly MenuItem _renameBranchMenuItem;
        private readonly MenuItem _compareBranchWithCurrentMenuItem;
        private readonly MenuItem _integrateBranchWithCurrentMenuItem;
        private readonly MenuItem _setBranchUpstreamMenuItem;
        private readonly MenuItem _unsetBranchUpstreamMenuItem;
        private readonly MenuItem _newBranchMenuItem;
        private readonly MenuItem _deleteBranchMenuItem;
        private readonly MenuItem _forceDeleteBranchMenuItem;
        private readonly MenuItem _deleteRemoteBranchMenuItem;
        private readonly MenuItem _refreshBranchesMenuItem;
        private readonly MenuItem _copyBranchNameMenuItem;
        private readonly MenuItem _copyBranchUpstreamMenuItem;

        private bool _isLoading;
        private string _loadedCommitSha;
        private string _loadedCommitDiff;
        private bool _loadedHistoryRefIsCurrent = true;
        private string _loadedHistoryRefDisplayText;
        private GitRepositoryStatusSnapshot _cachedRepositoryStatus;
        private CancellationTokenSource _operationCancellationSource;

        public GitCommitHistoryForm(string workspacePath, IGitRepositoryService gitService, IScreenReaderService screenReaderService)
        {
            _workspacePath = workspacePath;
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            _screenReaderService = screenReaderService;

            Text = UiLocalization.Text("GitCommitHistoryForm.Title", "Branches and Commits");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(860, 520);
            Size = new Size(1120, 660);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, true);

            _historyRefLabel = new Label
            {
                Text = UiLocalization.Text("GitCommitHistoryForm.Label.HistoryRef", "Branches(&B)"),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(_historyRefLabel, true);
            _historyTreeView = CreateHistoryTreeView();
            _commitListView = CreateCommitListView();
            _fileListView = CreateCommitFileListView();
            _diffTextBox = CreateDiffTextBox();
            _commitContextMenu = new ContextMenu();
            _loadDetailsMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.LoadDetails", "Load Details(&D)"));
            _cherryPickMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.CherryPick", "Cherry-pick(&P)"));
            _revertMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.Revert", "Revert(&R)"));
            _amendMessageMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.AmendMessage", "Edit Latest Message(&E)"));
            _resetMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.Reset", "Roll Back to Here...(&T)"));
            _copyIdMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.CopyId", "Copy ID(&I)"));
            _copyShortIdMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.CopyShortId", "Copy Short ID(&S)"));
            _copySubjectMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.CopySubject", "Copy Subject(&C)"));
            _copySummaryMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.CopySummary", "Copy Summary(&M)"));
            _copyDiffMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.CopyDiff", "Copy Diff(&F)"));
            _commitContextMenu.MenuItems.Add(_loadDetailsMenuItem);
            _commitContextMenu.MenuItems.Add("-");
            _commitContextMenu.MenuItems.Add(_cherryPickMenuItem);
            _commitContextMenu.MenuItems.Add(_revertMenuItem);
            _commitContextMenu.MenuItems.Add(_amendMessageMenuItem);
            _commitContextMenu.MenuItems.Add(_resetMenuItem);
            _commitContextMenu.MenuItems.Add("-");
            _commitContextMenu.MenuItems.Add(_copyIdMenuItem);
            _commitContextMenu.MenuItems.Add(_copyShortIdMenuItem);
            _commitContextMenu.MenuItems.Add(_copySubjectMenuItem);
            _commitContextMenu.MenuItems.Add(_copySummaryMenuItem);
            _commitContextMenu.MenuItems.Add(_copyDiffMenuItem);
            _commitListView.ContextMenu = _commitContextMenu;

            _branchContextMenu = new ContextMenu();
            _loadBranchHistoryMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.LoadBranchHistory", "Load History(&L)"));
            _checkoutBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.CheckoutBranch", "Checkout(&C)"));
            _fetchBranchesMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.Fetch", "Fetch(&F)"));
            _pullBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.PullCurrent", "Pull Current(&U)"));
            _pushBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.PushCurrent", "Push Current(&P)"));
            _forcePushBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.ForcePushCurrent", "Force Push Current(&W)"));
            _publishBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.PublishCurrent", "Publish Current(&B)"));
            _renameCurrentBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.RenameCurrentBranch", "Rename Current Branch(&M)"));
            _renameBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.RenameBranch", "Rename Branch(&M)"));
            _compareBranchWithCurrentMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.CompareWithCurrent", "Compare with Current(&V)"));
            _integrateBranchWithCurrentMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.IntegrateWithCurrent", "Integrate with Current...(&I)"));
            _setBranchUpstreamMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.SetUpstream", "Set Upstream Branch(&S)"));
            _unsetBranchUpstreamMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.UnsetUpstream", "Unset Upstream Branch(&O)"));
            _newBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.NewBranch", "New Branch(&N)"));
            _deleteBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.DeleteBranch", "Delete Local Branch(&D)"));
            _forceDeleteBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.ForceDeleteBranch", "Force Delete Local Branch(&X)"));
            _deleteRemoteBranchMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.DeleteRemoteBranch", "Delete Remote Branch(&D)"));
            _refreshBranchesMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.RefreshBranches", "Refresh Refs(&R)"));
            _copyBranchNameMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.CopyBranchName", "Copy Name(&A)"));
            _copyBranchUpstreamMenuItem = new MenuItem(UiLocalization.Text("GitCommitHistoryForm.Menu.CopyBranchUpstream", "Copy Upstream(&U)"));
            _branchContextMenu.MenuItems.Add(_loadBranchHistoryMenuItem);
            _branchContextMenu.MenuItems.Add(_checkoutBranchMenuItem);
            _branchContextMenu.MenuItems.Add("-");
            _branchContextMenu.MenuItems.Add(_fetchBranchesMenuItem);
            _branchContextMenu.MenuItems.Add(_pullBranchMenuItem);
            _branchContextMenu.MenuItems.Add(_pushBranchMenuItem);
            _branchContextMenu.MenuItems.Add(_forcePushBranchMenuItem);
            _branchContextMenu.MenuItems.Add(_publishBranchMenuItem);
            _branchContextMenu.MenuItems.Add("-");
            _branchContextMenu.MenuItems.Add(_setBranchUpstreamMenuItem);
            _branchContextMenu.MenuItems.Add(_unsetBranchUpstreamMenuItem);
            _branchContextMenu.MenuItems.Add("-");
            _branchContextMenu.MenuItems.Add(_newBranchMenuItem);
            _branchContextMenu.MenuItems.Add(_deleteBranchMenuItem);
            _branchContextMenu.MenuItems.Add(_forceDeleteBranchMenuItem);
            _branchContextMenu.MenuItems.Add(_deleteRemoteBranchMenuItem);
            _branchContextMenu.MenuItems.Add(_refreshBranchesMenuItem);
            _branchContextMenu.MenuItems.Add("-");
            _branchContextMenu.MenuItems.Add(_copyBranchNameMenuItem);
            _branchContextMenu.MenuItems.Add(_copyBranchUpstreamMenuItem);
            _historyTreeView.ContextMenu = _branchContextMenu;

            _refreshButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitCommitHistoryForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("GitCommitHistoryForm.Accessible.Refresh", "Refresh"),
                1,
                118);
            _loadDetailsButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitCommitHistoryForm.Button.LoadDetails", "Load Details(&D)"),
                UiLocalization.Text("GitCommitHistoryForm.Accessible.LoadDetails", "Load details"),
                2,
                118);
            _closeButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("GitCommitHistoryForm.Button.Close", "Close(&E)"),
                UiLocalization.Text("GitCommitHistoryForm.Accessible.Close", "Close"),
                3,
                118);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_loadDetailsButton, UiGlyph.Info);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _refreshButton.Click += async (sender, e) => await RefreshAllAsync();
            _loadDetailsButton.Click += async (sender, e) => await LoadSelectedCommitDetailsAsync();
            _closeButton.Click += (sender, e) => Close();
            _loadDetailsMenuItem.Click += async (sender, e) => await LoadSelectedCommitDetailsAsync();
            _cherryPickMenuItem.Click += async (sender, e) => await CherryPickSelectedCommitAsync();
            _revertMenuItem.Click += async (sender, e) => await RevertSelectedCommitAsync();
            _amendMessageMenuItem.Click += async (sender, e) => await AmendSelectedLatestCommitMessageAsync();
            _resetMenuItem.Click += async (sender, e) => await ResetSelectedCommitAsync();
            _copyIdMenuItem.Click += (sender, e) => CopySelectedCommitValue(entry => entry.FullSha, UiLocalization.Text("GitCommitHistoryForm.Status.CopiedId", "Copied commit ID."));
            _copyShortIdMenuItem.Click += (sender, e) => CopySelectedCommitValue(entry => entry.ShortSha, UiLocalization.Text("GitCommitHistoryForm.Status.CopiedShortId", "Copied short commit ID."));
            _copySubjectMenuItem.Click += (sender, e) => CopySelectedCommitValue(entry => entry.Subject, UiLocalization.Text("GitCommitHistoryForm.Status.CopiedSubject", "Copied commit subject."));
            _copySummaryMenuItem.Click += (sender, e) => CopySelectedCommitValue(BuildCommitSummaryText, UiLocalization.Text("GitCommitHistoryForm.Status.CopiedSummary", "Copied commit summary."));
            _copyDiffMenuItem.Click += (sender, e) => CopyLoadedCommitDiff();
            _commitContextMenu.Popup += (sender, e) => RefreshContextMenu();
            _loadBranchHistoryMenuItem.Click += async (sender, e) => await LoadHistoryAsync();
            _checkoutBranchMenuItem.Click += async (sender, e) => await CheckoutSelectedBranchAsync();
            _fetchBranchesMenuItem.Click += async (sender, e) => await FetchBranchesAsync();
            _pullBranchMenuItem.Click += async (sender, e) => await PullCurrentBranchAsync();
            _pushBranchMenuItem.Click += async (sender, e) => await PushCurrentBranchAsync();
            _forcePushBranchMenuItem.Click += async (sender, e) => await ForcePushCurrentBranchAsync();
            _publishBranchMenuItem.Click += async (sender, e) => await PublishCurrentBranchAsync();
            _renameCurrentBranchMenuItem.Click += async (sender, e) => await RenameCurrentBranchAsync();
            _renameBranchMenuItem.Click += async (sender, e) => await RenameSelectedBranchAsync();
            _compareBranchWithCurrentMenuItem.Click += async (sender, e) => await CompareSelectedBranchWithCurrentAsync();
            _integrateBranchWithCurrentMenuItem.Click += async (sender, e) => await IntegrateSelectedBranchWithCurrentAsync();
            _setBranchUpstreamMenuItem.Click += async (sender, e) => await SetSelectedBranchUpstreamAsync();
            _unsetBranchUpstreamMenuItem.Click += async (sender, e) => await UnsetSelectedBranchUpstreamAsync();
            _newBranchMenuItem.Click += async (sender, e) => await CreateBranchAsync();
            _deleteBranchMenuItem.Click += async (sender, e) => await DeleteSelectedBranchAsync();
            _forceDeleteBranchMenuItem.Click += async (sender, e) => await ForceDeleteSelectedBranchAsync();
            _deleteRemoteBranchMenuItem.Click += async (sender, e) => await DeleteSelectedRemoteBranchAsync();
            _refreshBranchesMenuItem.Click += async (sender, e) => await RefreshAllAsync();
            _copyBranchNameMenuItem.Click += (sender, e) => CopySelectedBranchValue(item => item.Name, UiLocalization.Text("GitCommitHistoryForm.Status.CopiedBranchName", "Copied branch name."));
            _copyBranchUpstreamMenuItem.Click += (sender, e) => CopySelectedBranchValue(item => item.Upstream, UiLocalization.Text("GitCommitHistoryForm.Status.CopiedBranchUpstream", "Copied upstream."));
            _branchContextMenu.Popup += (sender, e) => RefreshBranchContextMenu();
            _historyTreeView.AfterSelect += (sender, e) => RefreshButtons();
            _historyTreeView.NodeMouseDoubleClick += async (sender, e) => await LoadHistoryAsync();
            _historyTreeView.MouseDown += HistoryTreeView_MouseDown;
            _historyTreeView.KeyDown += HistoryTreeView_KeyDown;
            _commitListView.SelectedIndexChanged += (sender, e) => ClearCommitDetailsForSelection();
            _commitListView.ItemActivate += async (sender, e) => await LoadSelectedCommitDetailsAsync();
            _commitListView.MouseDown += CommitListView_MouseDown;
            _commitListView.KeyDown += CommitListView_KeyDown;
            KeyDown += GitCommitHistoryForm_KeyDown;
            Shown += async (sender, e) =>
            {
                Activate();
                await LoadHistoryRefsAsync();
                await LoadHistoryAsync();
            };

            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.Preparing", "Preparing commit history..."));
        }

        public event EventHandler RepositoryChanged;

        private async void GitCommitHistoryForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                await RefreshAllAsync();
            }
        }

        private async Task RefreshAllAsync()
        {
            if (_isLoading)
            {
                return;
            }

            await RefreshRepositoryStatusCacheAsync().ConfigureAwait(true);
            await LoadHistoryRefsAsync().ConfigureAwait(true);
            await LoadHistoryAsync().ConfigureAwait(true);
        }

        private async Task LoadHistoryAsync()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            var cancellationSource = BeginOperationCancellation();
            try
            {
                RefreshButtons();
                var historyRef = GetSelectedHistoryRef();
                var displayText = historyRef == null
                    ? UiLocalization.Text("GitCommitHistoryForm.Value.CurrentBranch", "Current branch")
                    : historyRef.DisplayText;
                SetWindowTitle(displayText);
                BindHistory(new GitCommitHistoryEntry[0]);
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.Loading", "Loading commit history..."));
                var entries = historyRef == null || string.IsNullOrWhiteSpace(historyRef.Revision)
                    ? await _gitService.ReadCommitHistoryAsync(_workspacePath, MaxCommitCount, cancellationSource.Token).ConfigureAwait(true)
                    : await _gitService.ReadCommitHistoryAsync(_workspacePath, historyRef.Revision, MaxCommitCount, cancellationSource.Token).ConfigureAwait(true);
                entries = entries
                    ?? new GitCommitHistoryEntry[0];
                _loadedHistoryRefIsCurrent = historyRef == null || historyRef.IsCurrent;
                _loadedHistoryRefDisplayText = displayText;
                SetWindowTitle(_loadedHistoryRefDisplayText);
                BindHistory(entries);
                SetStatus(entries.Count == 0
                    ? string.Format(
                        UiLocalization.Text("GitCommitHistoryForm.Status.EmptyFor.Template", "No commits were found for {0}."),
                        _loadedHistoryRefDisplayText)
                    : string.Format(
                        UiLocalization.Text("GitCommitHistoryForm.Status.LoadedFor.Template", "Loaded {0} commit(s) for {1}."),
                        entries.Count,
                        _loadedHistoryRefDisplayText));
            }
            catch (OperationCanceledException)
            {
                SetStatus(string.Empty);
            }
            catch (Exception ex)
            {
                BindHistory(new GitCommitHistoryEntry[0]);
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ClearOperationCancellation(cancellationSource);
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CancelCurrentOperation();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CancelCurrentOperation();
                if (_operationCancellationSource != null)
                {
                    _operationCancellationSource.Dispose();
                    _operationCancellationSource = null;
                }
            }

            base.Dispose(disposing);
        }

        private CancellationTokenSource BeginOperationCancellation()
        {
            CancelCurrentOperation();
            if (_operationCancellationSource != null)
            {
                _operationCancellationSource.Dispose();
            }

            _operationCancellationSource = new CancellationTokenSource();
            return _operationCancellationSource;
        }

        private void CancelCurrentOperation()
        {
            try
            {
                if (_operationCancellationSource != null)
                {
                    _operationCancellationSource.Cancel();
                }
            }
            catch
            {
            }
        }

        private void ClearOperationCancellation(CancellationTokenSource cancellationSource)
        {
            if (!object.ReferenceEquals(_operationCancellationSource, cancellationSource))
            {
                return;
            }

            _operationCancellationSource.Dispose();
            _operationCancellationSource = null;
        }

        private void BindHistory(IReadOnlyList<GitCommitHistoryEntry> entries)
        {
            _commitListView.BeginUpdate();
            try
            {
                _commitListView.Items.Clear();
                foreach (var entry in entries ?? new GitCommitHistoryEntry[0])
                {
                    var item = new ListViewItem(GetSubjectText(entry));
                    item.SubItems.Add(entry.ShortSha ?? string.Empty);
                    item.SubItems.Add(entry.AuthorName ?? string.Empty);
                    item.SubItems.Add(entry.AuthorDateText ?? string.Empty);
                    item.Tag = entry;
                    _commitListView.Items.Add(item);
                }
            }
            finally
            {
                _commitListView.EndUpdate();
            }

            if (_commitListView.Items.Count > 0)
            {
                _commitListView.Items[0].Selected = true;
                _commitListView.Items[0].Focused = true;
                _commitListView.EnsureVisible(0);
            }

            ClearCommitDetails();
        }

        private void BindCommitFiles(IReadOnlyList<GitCommitFileEntry> entries)
        {
            _fileListView.BeginUpdate();
            try
            {
                _fileListView.Items.Clear();
                foreach (var entry in entries ?? new GitCommitFileEntry[0])
                {
                    var item = new ListViewItem(GetCommitFilePathText(entry));
                    item.SubItems.Add(GetCommitFileChangeKindText(entry == null ? null : entry.ChangeKind));
                    item.Tag = entry;
                    _fileListView.Items.Add(item);
                }
            }
            finally
            {
                _fileListView.EndUpdate();
            }
        }

        private void RefreshButtons()
        {
            _refreshButton.Enabled = !_isLoading;
            _historyTreeView.Enabled = !_isLoading && _historyTreeView.Nodes.Count > 0;
            _loadDetailsButton.Enabled = !_isLoading && GetSelectedEntry() != null;
        }

        private void RefreshContextMenu()
        {
            var entries = GetSelectedEntries();
            var entry = entries.Length == 1 ? entries[0] : null;
            var hasEntry = entries.Length > 0;
            var hasSingleEntry = entry != null;
            var allHaveCommitIds = entries.All(item => item != null && !string.IsNullOrWhiteSpace(item.FullSha));
            _loadDetailsMenuItem.Enabled = !_isLoading && hasSingleEntry && !string.IsNullOrWhiteSpace(entry.FullSha);
            _cherryPickMenuItem.Enabled = !_isLoading && hasSingleEntry && !string.IsNullOrWhiteSpace(entry.FullSha) && !_loadedHistoryRefIsCurrent;
            _revertMenuItem.Enabled = !_isLoading && hasEntry && allHaveCommitIds && _loadedHistoryRefIsCurrent;
            _amendMessageMenuItem.Enabled = !_isLoading && hasSingleEntry && !string.IsNullOrWhiteSpace(entry.FullSha) && IsSelectedLatestCurrentCommit();
            _resetMenuItem.Enabled = !_isLoading && hasSingleEntry && !string.IsNullOrWhiteSpace(entry.FullSha) && _loadedHistoryRefIsCurrent;
            _copyIdMenuItem.Enabled = hasSingleEntry && !string.IsNullOrWhiteSpace(entry.FullSha);
            _copyShortIdMenuItem.Enabled = hasSingleEntry && !string.IsNullOrWhiteSpace(entry.ShortSha);
            _copySubjectMenuItem.Enabled = hasSingleEntry && !string.IsNullOrWhiteSpace(entry.Subject);
            _copySummaryMenuItem.Enabled = hasSingleEntry;
            _copyDiffMenuItem.Enabled = hasSingleEntry &&
                                        string.Equals(_loadedCommitSha, entry.FullSha, StringComparison.OrdinalIgnoreCase) &&
                                        !string.IsNullOrWhiteSpace(_loadedCommitDiff);
        }

        private void RefreshBranchContextMenu()
        {
            var historyRef = GetSelectedHistoryRef();
            var branch = historyRef == null ? null : historyRef.Branch;
            var canRun = !_isLoading;
            var isCurrentLocalBranch = branch != null && branch.IsCurrent && !branch.IsRemote;
            var isLocalBranch = branch != null && !branch.IsRemote;
            var isRemoteBranch = branch != null && branch.IsRemote;
            var canOfferIntegration = canRun && CanOfferBranchIntegrationMenu(branch, _cachedRepositoryStatus);
            _loadBranchHistoryMenuItem.Enabled = canRun && historyRef != null;
            _checkoutBranchMenuItem.Enabled = canRun && branch != null && !branch.IsCurrent;
            _fetchBranchesMenuItem.Enabled = canRun;
            _pullBranchMenuItem.Enabled = canRun && isCurrentLocalBranch && !string.IsNullOrWhiteSpace(branch.Upstream);
            _pushBranchMenuItem.Enabled = canRun && isCurrentLocalBranch && !string.IsNullOrWhiteSpace(branch.Upstream);
            _forcePushBranchMenuItem.Enabled = canRun && isCurrentLocalBranch && !string.IsNullOrWhiteSpace(branch.Upstream);
            _publishBranchMenuItem.Enabled = canRun && isCurrentLocalBranch && string.IsNullOrWhiteSpace(branch.Upstream);
            _renameCurrentBranchMenuItem.Enabled = canRun && isCurrentLocalBranch;
            _renameBranchMenuItem.Enabled = canRun && isLocalBranch && !branch.IsCurrent;
            _compareBranchWithCurrentMenuItem.Enabled = canRun && branch != null && !branch.IsCurrent;
            _integrateBranchWithCurrentMenuItem.Enabled = canOfferIntegration;
            _setBranchUpstreamMenuItem.Enabled = canRun && isLocalBranch;
            _unsetBranchUpstreamMenuItem.Enabled = canRun && isLocalBranch && !string.IsNullOrWhiteSpace(branch.Upstream);
            _newBranchMenuItem.Enabled = canRun;
            _deleteBranchMenuItem.Enabled = canRun && isLocalBranch && !branch.IsCurrent;
            _forceDeleteBranchMenuItem.Enabled = canRun && isLocalBranch && !branch.IsCurrent;
            _deleteRemoteBranchMenuItem.Enabled = canRun && isRemoteBranch;
            _refreshBranchesMenuItem.Enabled = canRun;
            _copyBranchNameMenuItem.Enabled = branch != null && !string.IsNullOrWhiteSpace(branch.Name);
            _copyBranchUpstreamMenuItem.Enabled = branch != null && !string.IsNullOrWhiteSpace(branch.Upstream);
            RebuildBranchContextMenu(historyRef, branch, isCurrentLocalBranch, isLocalBranch, isRemoteBranch, canOfferIntegration);
        }

        private void RebuildBranchContextMenu(HistoryRefItem historyRef, GitBranchEntry branch, bool isCurrentLocalBranch, bool isLocalBranch, bool isRemoteBranch, bool canOfferIntegration)
        {
            _branchContextMenu.MenuItems.Clear();
            if (historyRef != null)
            {
                _branchContextMenu.MenuItems.Add(_loadBranchHistoryMenuItem);
            }

            if (branch != null && !branch.IsCurrent)
            {
                _branchContextMenu.MenuItems.Add(_checkoutBranchMenuItem);
            }
            if (branch != null && !branch.IsCurrent)
            {
                _branchContextMenu.MenuItems.Add(_compareBranchWithCurrentMenuItem);
                if (canOfferIntegration)
                {
                    _branchContextMenu.MenuItems.Add(_integrateBranchWithCurrentMenuItem);
                }
            }

            if (isCurrentLocalBranch)
            {
                AddBranchMenuSeparatorIfNeeded();
                if (!string.IsNullOrWhiteSpace(branch.Upstream))
                {
                    _branchContextMenu.MenuItems.Add(_pullBranchMenuItem);
                    _branchContextMenu.MenuItems.Add(_pushBranchMenuItem);
                    _branchContextMenu.MenuItems.Add(_forcePushBranchMenuItem);
                }
                else
                {
                    _branchContextMenu.MenuItems.Add(_publishBranchMenuItem);
                }
                _branchContextMenu.MenuItems.Add(_renameCurrentBranchMenuItem);
            }

            if (isLocalBranch)
            {
                AddBranchMenuSeparatorIfNeeded();
                _branchContextMenu.MenuItems.Add(_setBranchUpstreamMenuItem);
                if (!string.IsNullOrWhiteSpace(branch.Upstream))
                {
                    _branchContextMenu.MenuItems.Add(_unsetBranchUpstreamMenuItem);
                }

                if (!branch.IsCurrent)
                {
                    _branchContextMenu.MenuItems.Add(_renameBranchMenuItem);
                    _branchContextMenu.MenuItems.Add(_deleteBranchMenuItem);
                    _branchContextMenu.MenuItems.Add(_forceDeleteBranchMenuItem);
                }
            }
            else if (isRemoteBranch)
            {
                AddBranchMenuSeparatorIfNeeded();
                _branchContextMenu.MenuItems.Add(_deleteRemoteBranchMenuItem);
            }

            AddBranchMenuSeparatorIfNeeded();
            _branchContextMenu.MenuItems.Add(_fetchBranchesMenuItem);
            _branchContextMenu.MenuItems.Add(_newBranchMenuItem);
            _branchContextMenu.MenuItems.Add(_refreshBranchesMenuItem);

            if (branch != null)
            {
                AddBranchMenuSeparatorIfNeeded();
                _branchContextMenu.MenuItems.Add(_copyBranchNameMenuItem);
                if (!string.IsNullOrWhiteSpace(branch.Upstream))
                {
                    _branchContextMenu.MenuItems.Add(_copyBranchUpstreamMenuItem);
                }
            }
        }

        private void AddBranchMenuSeparatorIfNeeded()
        {
            var count = _branchContextMenu.MenuItems.Count;
            if (count > 0 && _branchContextMenu.MenuItems[count - 1].Text != "-")
            {
                _branchContextMenu.MenuItems.Add("-");
            }
        }

        private async Task LoadSelectedCommitDetailsAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.FullSha))
            {
                return;
            }

            _isLoading = true;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.LoadingDetails", "Loading commit details..."));
                var files = await _gitService.ReadCommitFilesAsync(_workspacePath, entry.FullSha).ConfigureAwait(true)
                    ?? new GitCommitFileEntry[0];
                var diff = await _gitService.ReadCommitDiffAsync(_workspacePath, entry.FullSha).ConfigureAwait(true);
                BindCommitFiles(files);
                _loadedCommitSha = entry.FullSha;
                _loadedCommitDiff = diff != null && diff.Success ? diff.StandardOutput ?? string.Empty : string.Empty;
                TextBoxCaretHelper.SetTextAndMoveToStart(
                    _diffTextBox,
                    string.IsNullOrWhiteSpace(_loadedCommitDiff)
                        ? UiLocalization.Text("GitCommitHistoryForm.Value.NoDiff", "No diff is available for the selected commit.")
                        : _loadedCommitDiff);
                if (diff == null || !diff.Success)
                {
                    var message = diff == null ? string.Empty : diff.DisplayMessage;
                    SetStatus(message);
                    MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                SetStatus(string.Format(
                    UiLocalization.Text("GitCommitHistoryForm.Status.DetailsLoaded.Template", "Loaded {0} changed file(s)."),
                    files.Count));
            }
            catch (Exception ex)
            {
                ClearCommitDetails();
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        private void ClearCommitDetailsForSelection()
        {
            _loadedCommitSha = null;
            _loadedCommitDiff = null;
            ClearCommitDetails();
            RefreshButtons();
        }

        private void ClearCommitDetails()
        {
            BindCommitFiles(new GitCommitFileEntry[0]);
            TextBoxCaretHelper.SetTextAndMoveToStart(
                _diffTextBox,
                UiLocalization.Text("GitCommitHistoryForm.Value.LoadDetailsHint", "Press Enter or use Load Details to inspect the selected commit."));
        }

        private async Task CherryPickSelectedCommitAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.FullSha))
            {
                return;
            }

            if (_loadedHistoryRefIsCurrent)
            {
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.CherryPickCurrentDisabled", "The selected commit is already in the current branch history."));
                RestoreFocus();
                return;
            }

            var status = await ReadStatusForOperationAsync().ConfigureAwait(true);
            if (status == null || !status.IsGitRepository)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                BuildCherryPickConfirmText(entry, status),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                status.Files.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreFocus();
                return;
            }

            await RunCherryPickOperationAsync(entry.FullSha).ConfigureAwait(true);
        }

        private async Task CreateBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            using (var form = new GitBranchCreateForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreTreeFocus();
                    return;
                }

                await RunBranchOperationAsync(cancellationToken => _gitService.CreateBranchAsync(_workspacePath, form.BranchName, cancellationToken)).ConfigureAwait(true);
            }
        }

        private async Task CheckoutSelectedBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var historyRef = GetSelectedHistoryRef();
            var branch = historyRef == null ? null : historyRef.Branch;
            if (branch == null || branch.IsCurrent)
            {
                return;
            }

            if (!await ConfirmCheckoutWithLocalChangesAsync(branch).ConfigureAwait(true))
            {
                return;
            }

            await RunBranchOperationAsync(cancellationToken => branch.IsRemote
                ? _gitService.CheckoutRemoteBranchAsync(_workspacePath, branch.Name, cancellationToken)
                : _gitService.CheckoutBranchAsync(_workspacePath, branch.Name, cancellationToken)).ConfigureAwait(true);
        }

        private async Task FetchBranchesAsync()
        {
            if (_isLoading)
            {
                return;
            }

            await RunBranchOperationAsync(cancellationToken => _gitService.FetchAsync(_workspacePath, cancellationToken)).ConfigureAwait(true);
        }

        private async Task PullCurrentBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var status = await ReadStatusForOperationAsync().ConfigureAwait(true);
            if (!ValidateCurrentBranchSyncStatus(status, true, false))
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(
                        "GitCommitHistoryForm.Confirm.PullCurrentBranch.Template",
                        "Pull remote updates into {0} from {1}?\r\n\r\nLocal changes: {2}. This runs: git pull"),
                    status.Branch,
                    status.Upstream,
                    status.Files.Count),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                status.Files.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreTreeFocus();
                return;
            }

            await RunBranchOperationAsync(cancellationToken => _gitService.PullAsync(_workspacePath, cancellationToken)).ConfigureAwait(true);
        }

        private async Task PushCurrentBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var status = await ReadStatusForOperationAsync().ConfigureAwait(true);
            if (!ValidateCurrentBranchSyncStatus(status, true, false))
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(
                        "GitCommitHistoryForm.Confirm.PushCurrentBranch.Template",
                        "Push current branch {0} to {1}?\r\n\r\nAhead: {2}, behind: {3}. This runs: git push"),
                    status.Branch,
                    status.Upstream,
                    status.AheadCount.GetValueOrDefault(),
                    status.BehindCount.GetValueOrDefault()),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreTreeFocus();
                return;
            }

            await RunBranchOperationAsync(cancellationToken => _gitService.PushAsync(_workspacePath, cancellationToken)).ConfigureAwait(true);
        }

        private async Task ForcePushCurrentBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var status = await ReadStatusForOperationAsync().ConfigureAwait(true);
            if (!ValidateCurrentBranchSyncStatus(status, true, false))
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(
                        "GitCommitHistoryForm.Confirm.ForcePushCurrentBranch.Template",
                        "Force-push current branch {0} to {1}?\r\n\r\nThis runs: git push --force-with-lease\r\n\r\nThis can rewrite remote history."),
                    status.Branch,
                    status.Upstream),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreTreeFocus();
                return;
            }

            await RunBranchOperationAsync(cancellationToken => _gitService.PushForceWithLeaseAsync(_workspacePath, cancellationToken)).ConfigureAwait(true);
        }

        private async Task PublishCurrentBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var status = await ReadStatusForOperationAsync().ConfigureAwait(true);
            if (!ValidateCurrentBranchSyncStatus(status, false, true))
            {
                return;
            }

            var branchName = status.Branch.Trim();
            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(
                        "GitCommitHistoryForm.Confirm.PublishCurrentBranch.Template",
                        "Publish current branch {0} to origin/{0} and set upstream?\r\n\r\nThis runs: git push -u origin {0}"),
                    branchName),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreTreeFocus();
                return;
            }

            await RunBranchOperationAsync(cancellationToken => _gitService.PushSetUpstreamAsync(_workspacePath, branchName, cancellationToken)).ConfigureAwait(true);
        }

        private async Task RenameCurrentBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var historyRef = GetSelectedHistoryRef();
            var branch = historyRef == null ? null : historyRef.Branch;
            if (branch == null || branch.IsRemote || !branch.IsCurrent)
            {
                return;
            }

            using (var form = new GitBranchCreateForm(
                UiLocalization.Text("GitBranchCreateForm.Title.RenameCurrent", "Rename Current Branch"),
                UiLocalization.Text("GitBranchCreateForm.Label.NewName", "New branch name"),
                UiLocalization.Text("GitBranchCreateForm.Button.Rename", "Rename(&R)"),
                UiLocalization.Text("GitBranchCreateForm.Accessible.Rename", "Rename"),
                branch.Name))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreFocus();
                    return;
                }

                if (string.Equals(form.BranchName, branch.Name, StringComparison.Ordinal))
                {
                    RestoreFocus();
                    return;
                }

                await RunBranchOperationAsync(cancellationToken => _gitService.RenameCurrentBranchAsync(_workspacePath, form.BranchName, cancellationToken)).ConfigureAwait(true);
            }
        }

        private async Task RenameSelectedBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var branch = GetSelectedBranch();
            if (branch == null || branch.IsRemote || branch.IsCurrent || string.IsNullOrWhiteSpace(branch.Name))
            {
                return;
            }

            if (!await ConfirmBranchOperationWithLocalChangesAsync(branch.Name).ConfigureAwait(true))
            {
                return;
            }

            using (var form = new GitBranchCreateForm(
                UiLocalization.Text("GitBranchCreateForm.Title.RenameBranch", "Rename Branch"),
                UiLocalization.Text("GitBranchCreateForm.Label.NewName", "New branch name"),
                UiLocalization.Text("GitBranchCreateForm.Button.Rename", "Rename(&R)"),
                UiLocalization.Text("GitBranchCreateForm.Accessible.Rename", "Rename"),
                branch.Name))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreTreeFocus();
                    return;
                }

                if (string.Equals(form.BranchName, branch.Name, StringComparison.Ordinal))
                {
                    RestoreTreeFocus();
                    return;
                }

                await RunBranchOperationAsync(cancellationToken => _gitService.RenameBranchAsync(_workspacePath, branch.Name, form.BranchName, cancellationToken)).ConfigureAwait(true);
            }
        }

        private async Task CompareSelectedBranchWithCurrentAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var historyRef = GetSelectedHistoryRef();
            var branch = historyRef == null ? null : historyRef.Branch;
            if (branch == null || branch.IsCurrent || string.IsNullOrWhiteSpace(branch.Name))
            {
                return;
            }

            _isLoading = true;
            try
            {
                RefreshButtons();
                var targetName = branch.Name.Trim();
                var displayText = string.Format(
                    UiLocalization.Text("GitCommitHistoryForm.Value.CompareWithCurrent.Template", "Current vs {0}"),
                    targetName);
                SetWindowTitle(displayText);
                BindHistory(new GitCommitHistoryEntry[0]);
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.ComparingBranches", "Comparing branches..."));
                var comparison = await _gitService.CompareBranchesAsync(_workspacePath, "HEAD", targetName).ConfigureAwait(true);
                var entries = await _gitService.ReadCommitHistoryAsync(_workspacePath, "HEAD.." + targetName, MaxCommitCount).ConfigureAwait(true)
                    ?? new GitCommitHistoryEntry[0];
                _loadedHistoryRefIsCurrent = false;
                _loadedHistoryRefDisplayText = displayText;
                SetWindowTitle(_loadedHistoryRefDisplayText);
                BindHistory(entries);
                SetStatus(string.Format(
                    UiLocalization.Text("GitCommitHistoryForm.Status.ComparedBranches.Template", "Compared current branch with {0}. Current-only: {1}; target-only: {2}. Loaded {3} target-only commit(s)."),
                    targetName,
                    comparison == null ? 0 : comparison.CurrentOnlyCount,
                    comparison == null ? 0 : comparison.TargetOnlyCount,
                    entries.Count));
            }
            catch (Exception ex)
            {
                BindHistory(new GitCommitHistoryEntry[0]);
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        private async Task IntegrateSelectedBranchWithCurrentAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var branch = GetSelectedBranch();
            if (branch == null || branch.IsCurrent || string.IsNullOrWhiteSpace(branch.Name))
            {
                return;
            }

            var status = await ReadStatusForOperationAsync().ConfigureAwait(true);
            if (!ValidateCurrentBranchIntegrationStatus(status, branch))
            {
                return;
            }

            GitBranchComparison comparison;
            try
            {
                comparison = await _gitService.CompareBranchesAsync(_workspacePath, "HEAD", branch.Name).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                RestoreFocus();
                return;
            }

            using (var form = new GitBranchIntegrateForm(status.Branch, branch.Name, comparison))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreFocus();
                    return;
                }

                await RunBranchIntegrationOperationAsync(async () =>
                {
                    if (form.FetchFirst)
                    {
                        var fetchResult = await _gitService.FetchAsync(_workspacePath).ConfigureAwait(true);
                        if (fetchResult == null || !fetchResult.Success)
                        {
                            return fetchResult;
                        }
                    }

                    switch (form.SelectedMode)
                    {
                        case GitBranchIntegrationMode.SquashMerge:
                            return await _gitService.MergeBranchIntoCurrentAsync(_workspacePath, branch.Name, true).ConfigureAwait(true);
                        case GitBranchIntegrationMode.Rebase:
                            return await _gitService.RebaseCurrentBranchOntoAsync(_workspacePath, branch.Name).ConfigureAwait(true);
                        default:
                            return await _gitService.MergeBranchIntoCurrentAsync(_workspacePath, branch.Name, false).ConfigureAwait(true);
                    }
                }).ConfigureAwait(true);
            }
        }

        private async Task SetSelectedBranchUpstreamAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var branch = GetSelectedBranch();
            if (branch == null || branch.IsRemote || string.IsNullOrWhiteSpace(branch.Name))
            {
                return;
            }

            var remoteBranches = await LoadRemoteBranchesForUpstreamAsync().ConfigureAwait(true);
            if (remoteBranches.Count == 0)
            {
                var message = UiLocalization.Text("GitCommitHistoryForm.Message.NoRemoteBranchesForUpstream", "No remote branches are available. Fetch remote branches first, or publish the current branch.");
                SetStatus(message);
                MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                RestoreTreeFocus();
                return;
            }

            using (var form = new GitUpstreamEditForm(branch.Name, branch.Upstream, remoteBranches))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreTreeFocus();
                    return;
                }

                await RunBranchOperationAsync(cancellationToken => _gitService.SetUpstreamAsync(_workspacePath, branch.Name, form.Upstream, cancellationToken)).ConfigureAwait(true);
            }
        }

        private async Task UnsetSelectedBranchUpstreamAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var branch = GetSelectedBranch();
            if (branch == null || branch.IsRemote || string.IsNullOrWhiteSpace(branch.Name) || string.IsNullOrWhiteSpace(branch.Upstream))
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text("GitCommitHistoryForm.Confirm.UnsetUpstream.Template", "Unset upstream branch for {0}?\r\n\r\nCurrent upstream branch: {1}"),
                    branch.Name,
                    branch.Upstream),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreTreeFocus();
                return;
            }

            await RunBranchOperationAsync(cancellationToken => _gitService.UnsetUpstreamAsync(_workspacePath, branch.Name, cancellationToken)).ConfigureAwait(true);
        }

        private async Task DeleteSelectedBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var branch = GetSelectedBranch();
            if (branch == null || branch.IsRemote || branch.IsCurrent || string.IsNullOrWhiteSpace(branch.Name))
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(
                        "GitCommitHistoryForm.Confirm.DeleteBranch.Template",
                        "Delete local branch {0}?\r\n\r\nThis uses safe delete and will fail if Git considers the branch not fully merged."),
                    branch.Name),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreTreeFocus();
                return;
            }

            await RunBranchOperationAsync(cancellationToken => _gitService.DeleteBranchAsync(_workspacePath, branch.Name, cancellationToken)).ConfigureAwait(true);
        }

        private async Task<IReadOnlyList<GitBranchEntry>> LoadRemoteBranchesForUpstreamAsync()
        {
            _isLoading = true;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.LoadingRemoteBranches", "Loading remote branches..."));
                var branches = await _gitService.ReadBranchesAsync(_workspacePath).ConfigureAwait(true);
                return (branches ?? new GitBranchEntry[0])
                    .Where(branch => branch != null && branch.IsRemote && !string.IsNullOrWhiteSpace(branch.Name))
                    .ToArray();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new GitBranchEntry[0];
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
            }
        }

        private bool ValidateCurrentBranchSyncStatus(GitRepositoryStatusSnapshot status, bool requireUpstream, bool requirePublishable)
        {
            string message = null;
            if (status == null || !status.IsGitRepository)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.NotRepository", "Current workspace is not a Git repository.");
            }
            else if (status.HasConflicts)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.BlockedByConflicts", "Resolve conflicts before synchronizing branches.");
            }
            else if (status.HasSequencerOperation)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.BlockedBySequencer", "Finish or abort the current Git operation before synchronizing branches.");
            }
            else if (status.HasMergeOrRebaseOperation)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.BlockedByMergeOrRebase", "Finish or abort the current merge or rebase before synchronizing branches.");
            }
            else if (status.IsDetachedHead || string.IsNullOrWhiteSpace(status.Branch) || status.Branch.StartsWith("detached", StringComparison.OrdinalIgnoreCase))
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.DetachedBranchSync", "Branch synchronization is unavailable in detached HEAD.");
            }
            else if (requireUpstream && string.IsNullOrWhiteSpace(status.Upstream))
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.NoUpstream", "Current branch has no upstream. Use Publish Current first.");
            }
            else if (requirePublishable && !string.IsNullOrWhiteSpace(status.Upstream))
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.UpstreamAlreadyConfigured", "Current branch already has an upstream. Use Push Current instead.");
            }
            else if (requirePublishable && string.IsNullOrWhiteSpace(status.OriginUrl))
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.NoOrigin", "No origin remote is configured for publishing.");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return true;
            }

            SetStatus(message);
            MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            RestoreTreeFocus();
            return false;
        }

        private bool ValidateCurrentBranchIntegrationStatus(GitRepositoryStatusSnapshot status, GitBranchEntry branch)
        {
            string message = null;
            if (status == null || !status.IsGitRepository)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.NotRepository", "Current workspace is not a Git repository.");
            }
            else if (branch == null || string.IsNullOrWhiteSpace(branch.Name) || branch.IsCurrent)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.SelectAnotherBranch", "Select a different branch first.");
            }
            else if (status.HasConflicts)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.BlockedByConflicts", "Resolve conflicts before synchronizing branches.");
            }
            else if (status.HasSequencerOperation)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.BlockedBySequencer", "Finish or abort the current Git operation before synchronizing branches.");
            }
            else if (status.HasMergeOrRebaseOperation)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.BlockedByMergeOrRebase", "Finish or abort the current merge or rebase before synchronizing branches.");
            }
            else if (status.IsDetachedHead || string.IsNullOrWhiteSpace(status.Branch) || status.Branch.StartsWith("detached", StringComparison.OrdinalIgnoreCase))
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.DetachedBranchSync", "Branch synchronization is unavailable in detached HEAD.");
            }
            else if (status.Files != null && status.Files.Count > 0)
            {
                message = string.Format(
                    UiLocalization.Text(
                        "GitCommitHistoryForm.Message.IntegrationRequiresCleanTree.Template",
                        "Current branch has {0} uncommitted change(s). Commit, stash, or discard them before integrating branches."),
                    status.Files.Count);
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return true;
            }

            SetStatus(message);
            MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            RestoreTreeFocus();
            return false;
        }

        private bool CanOfferBranchIntegrationMenu(GitBranchEntry branch, GitRepositoryStatusSnapshot status)
        {
            if (branch == null || branch.IsCurrent || string.IsNullOrWhiteSpace(branch.Name))
            {
                return false;
            }

            if (status == null || !status.IsGitRepository)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(status.StatusErrorMessage) ||
                status.HasConflicts ||
                status.HasSequencerOperation ||
                status.HasMergeOrRebaseOperation ||
                status.IsDetachedHead ||
                string.IsNullOrWhiteSpace(status.Branch) ||
                status.Branch.StartsWith("detached", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return status.Files == null || status.Files.Count == 0;
        }

        private async Task<bool> ConfirmBranchOperationWithLocalChangesAsync(string branchName)
        {
            _isLoading = true;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.CheckingWorkingTree", "Checking local changes..."));
                var status = await _gitService.ReadStatusAsync(_workspacePath).ConfigureAwait(true);
                if (status == null || !status.IsGitRepository || status.Files.Count == 0)
                {
                    return true;
                }

                var result = MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text("GitCommitHistoryForm.Confirm.BranchOperationDirty.Template", "Current branch has {0} uncommitted change(s). Continue operating on branch {1}?"),
                        status.Files.Count,
                        branchName ?? string.Empty),
                    AppConstants.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                return result == DialogResult.Yes;
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
            }
        }

        private async Task<bool> ConfirmCheckoutWithLocalChangesAsync(GitBranchEntry branch)
        {
            _isLoading = true;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.CheckingWorkingTree", "Checking local changes..."));
                var status = await _gitService.ReadStatusAsync(_workspacePath).ConfigureAwait(true);
                var preflight = GitCheckoutPreflightPlanner.Evaluate(status);
                if (preflight == GitCheckoutPreflightDecision.Proceed)
                {
                    return true;
                }

                if (preflight == GitCheckoutPreflightDecision.BlockConflicts)
                {
                    var conflictMessage = UiLocalization.Text("GitCommitHistoryForm.Message.BlockedByConflicts", "Resolve conflicts before synchronizing branches.");
                    SetStatus(conflictMessage);
                    MessageBox.Show(this, conflictMessage, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                if (preflight == GitCheckoutPreflightDecision.BlockActiveGitOperation)
                {
                    var operationMessage = UiLocalization.Text("GitCommitHistoryForm.Message.BlockedBySequencer", "Finish or abort the current Git operation before synchronizing branches.");
                    SetStatus(operationMessage);
                    MessageBox.Show(this, operationMessage, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                var result = MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text(
                            "GitCommitHistoryForm.Confirm.CheckoutDirtyWithStash.Template",
                            "Current branch has {0} uncommitted change(s). Switch to {1}?\r\n\r\nYes: stash changes first, then switch.\r\nNo: switch directly.\r\nCancel: do not switch."),
                        status.Files.Count,
                        branch == null ? string.Empty : branch.Name),
                    AppConstants.AppName,
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button3);
                if (result == DialogResult.Cancel)
                {
                    return false;
                }

                if (result == DialogResult.Yes)
                {
                    SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.StashingBeforeCheckout", "Saving local changes to stash..."));
                    var stashResult = await _gitService.StashPushAsync(
                        _workspacePath,
                        UiLocalization.Text("GitCommitHistoryForm.Value.AutoStashMessage", "MyCodex auto stash before branch switch"),
                        true).ConfigureAwait(true);
                    if (stashResult == null || !stashResult.Success)
                    {
                        var message = stashResult == null ? string.Empty : stashResult.DisplayMessage;
                        SetStatus(message);
                        MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreTreeFocus();
            }
        }

        private async Task RunBranchOperationAsync(Func<CancellationToken, Task<GitOperationResult>> operation)
        {
            if (operation == null)
            {
                return;
            }

            _isLoading = true;
            var cancellationSource = BeginOperationCancellation();
            string statusText = null;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.RunningOperation", "Running Git operation..."));
                var result = await operation(cancellationSource.Token).ConfigureAwait(true);
                statusText = BuildGitOperationMessage(result);
                SetStatus(statusText);
                if (result == null || !result.Success)
                {
                    MessageBox.Show(this, statusText, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                SpeakStatus(statusText);
                OnRepositoryChanged();
                _isLoading = false;
                await RefreshAllAsync().ConfigureAwait(true);
                SetStatus(statusText);
            }
            catch (OperationCanceledException)
            {
                SetStatus(string.Empty);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ClearOperationCancellation(cancellationSource);
                _isLoading = false;
                RefreshButtons();
                RestoreTreeFocus();
            }
        }

        private static string BuildGitOperationMessage(GitOperationResult result)
        {
            var message = result == null ? string.Empty : result.DisplayMessage ?? string.Empty;
            if (result != null &&
                !result.Success &&
                message.IndexOf("requested upstream branch", StringComparison.OrdinalIgnoreCase) >= 0 &&
                message.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return UiLocalization.Text(
                    "GitCommitHistoryForm.Message.UpstreamBranchMissing",
                    "The selected upstream branch does not exist locally. Fetch remote branches first, or publish the current branch if this is a new remote branch.");
            }

            return message;
        }

        private async Task RevertSelectedCommitAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var entries = GetSelectedEntries();
            if (entries.Length == 0 || entries.Any(entry => entry == null || string.IsNullOrWhiteSpace(entry.FullSha)))
            {
                return;
            }

            if (!await ConfirmCleanWorkingTreeForRevertAsync().ConfigureAwait(true))
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                BuildRevertConfirmText(entries),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreFocus();
                return;
            }

            await RunRevertOperationAsync(entries.Select(entry => entry.FullSha).ToArray()).ConfigureAwait(true);
        }

        private async Task AmendSelectedLatestCommitMessageAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.FullSha) || !IsSelectedLatestCurrentCommit())
            {
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Message.AmendOnlyLatest", "Only the latest commit on the current branch can be edited here."));
                RestoreFocus();
                return;
            }

            var status = await ReadStatusForOperationAsync().ConfigureAwait(true);
            if (!ValidateAmendMessageStatus(status))
            {
                return;
            }

            var mayRequireForcePush = !string.IsNullOrWhiteSpace(status.Upstream) &&
                                      status.AheadCount.GetValueOrDefault() == 0;
            GitCommitMessageEntry message;
            try
            {
                _isLoading = true;
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.LoadingCommitMessage", "Loading commit message..."));
                message = await _gitService.ReadCommitMessageAsync(_workspacePath, entry.FullSha).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
            }

            using (var form = new GitCommitMessageEditForm(
                message == null ? entry.Subject : message.Subject,
                message == null ? string.Empty : message.Body,
                mayRequireForcePush))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreFocus();
                    return;
                }

                var confirm = MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text(
                            "GitCommitHistoryForm.Confirm.AmendMessage.Template",
                            "Amend the latest commit message?\r\n\r\nCommit: {0}\r\nNew subject: {1}\r\n\r\nThis rewrites the latest commit."),
                        TextValueHelper.FirstNonEmptyTrimmed(entry.ShortSha, entry.FullSha),
                        form.CommitSubject),
                    AppConstants.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirm != DialogResult.Yes)
                {
                    RestoreFocus();
                    return;
                }

                if (!await RunAmendMessageOperationAsync(form.CommitSubject, form.CommitBody).ConfigureAwait(true))
                {
                    return;
                }
            }

            if (mayRequireForcePush)
            {
                await PromptForcePushAfterAmendAsync(status).ConfigureAwait(true);
            }
        }

        private bool ValidateAmendMessageStatus(GitRepositoryStatusSnapshot status)
        {
            string message = null;
            if (status == null || !status.IsGitRepository)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.NotRepository", "Current workspace is not a Git repository.");
            }
            else if (status.HasConflicts)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.BlockedByConflicts", "Resolve conflicts before synchronizing branches.");
            }
            else if (status.HasSequencerOperation)
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.BlockedBySequencer", "Finish or abort the current Git operation before synchronizing branches.");
            }
            else if (status.IsDetachedHead || string.IsNullOrWhiteSpace(status.HeadSha))
            {
                message = UiLocalization.Text("GitCommitHistoryForm.Message.DetachedBranchSync", "Branch synchronization is unavailable in detached HEAD.");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return true;
            }

            SetStatus(message);
            MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            RestoreFocus();
            return false;
        }

        private async Task<bool> RunAmendMessageOperationAsync(string subject, string body)
        {
            _isLoading = true;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.AmendingMessage", "Amending latest commit message..."));
                var result = await _gitService.CommitAmendAsync(_workspacePath, subject, body).ConfigureAwait(true);
                var statusText = result == null ? string.Empty : result.DisplayMessage;
                SetStatus(statusText);
                if (result == null || !result.Success)
                {
                    MessageBox.Show(this, statusText, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                SpeakStatus(statusText);
                OnRepositoryChanged();
                _isLoading = false;
                await LoadHistoryAsync().ConfigureAwait(true);
                SetStatus(statusText);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        private async Task RunBranchIntegrationOperationAsync(Func<Task<GitOperationResult>> operation)
        {
            if (operation == null)
            {
                return;
            }

            _isLoading = true;
            string statusText = null;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.RunningOperation", "Running Git operation..."));
                var result = await operation().ConfigureAwait(true);
                statusText = BuildGitOperationMessage(result);
                SetStatus(statusText);
                if (result == null || !result.Success)
                {
                    var repositoryStatus = await TryReadRepositoryStatusAfterIntegrationFailureAsync().ConfigureAwait(true);
                    if (repositoryStatus != null && repositoryStatus.HasMergeOrRebaseOperation)
                    {
                        OnRepositoryChanged();
                        _isLoading = false;
                        await RefreshAllAsync().ConfigureAwait(true);
                        SetStatus(statusText);
                        MessageBox.Show(this, statusText, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    MessageBox.Show(this, statusText, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                SpeakStatus(statusText);
                OnRepositoryChanged();
                _isLoading = false;
                await RefreshAllAsync().ConfigureAwait(true);
                SetStatus(statusText);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        private async Task<GitRepositoryStatusSnapshot> TryReadRepositoryStatusAfterIntegrationFailureAsync()
        {
            try
            {
                return await _gitService.ReadStatusAsync(_workspacePath).ConfigureAwait(true);
            }
            catch
            {
                return null;
            }
        }

        private async Task PromptForcePushAfterAmendAsync(GitRepositoryStatusSnapshot previousStatus)
        {
            if (previousStatus == null || string.IsNullOrWhiteSpace(previousStatus.Upstream))
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(
                        "GitCommitHistoryForm.Confirm.ForcePushAfterAmend.Template",
                        "The amended commit may already have been pushed to {0}.\r\n\r\nUse force-with-lease to update the remote branch now?"),
                    previousStatus.Upstream),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreFocus();
                return;
            }

            await RunBranchOperationAsync(cancellationToken => _gitService.PushForceWithLeaseAsync(_workspacePath, cancellationToken)).ConfigureAwait(true);
        }

        private async Task ResetSelectedCommitAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.FullSha))
            {
                return;
            }

            if (!await ConfirmCleanWorkingTreeForResetAsync().ConfigureAwait(true))
            {
                return;
            }

            using (var form = new GitResetCommitForm(string.Format("{0}\r\n{1}", entry.ShortSha, GetSubjectText(entry))))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreFocus();
                    return;
                }

                await RunResetOperationAsync(entry.FullSha, form.SelectedMode).ConfigureAwait(true);
            }
        }

        private async Task<bool> ConfirmCleanWorkingTreeForRevertAsync()
        {
            var status = await ReadStatusForOperationAsync().ConfigureAwait(true);
            if (status == null || !status.IsGitRepository || status.Files.Count == 0)
            {
                return true;
            }

            MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(
                        "GitCommitHistoryForm.Message.RevertRequiresCleanTree.Template",
                        "Current branch has {0} uncommitted change(s). Commit, stash, or discard them before reverting a commit."),
                    status.Files.Count),
                AppConstants.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        private async Task<bool> ConfirmCleanWorkingTreeForResetAsync()
        {
            var status = await ReadStatusForOperationAsync().ConfigureAwait(true);
            if (status == null || !status.IsGitRepository || status.Files.Count == 0)
            {
                return true;
            }

            MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(
                        "GitCommitHistoryForm.Message.ResetRequiresCleanTree.Template",
                        "Current branch has {0} uncommitted change(s). Commit, stash, or discard them before rolling back the branch."),
                    status.Files.Count),
                AppConstants.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        private async Task<GitRepositoryStatusSnapshot> ReadStatusForOperationAsync()
        {
            _isLoading = true;
            var cancellationSource = BeginOperationCancellation();
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.CheckingWorkingTree", "Checking local changes..."));
                _cachedRepositoryStatus = await _gitService.ReadStatusAsync(_workspacePath, cancellationSource.Token).ConfigureAwait(true);
                return _cachedRepositoryStatus;
            }
            catch (OperationCanceledException)
            {
                _cachedRepositoryStatus = null;
                SetStatus(string.Empty);
                return null;
            }
            catch (Exception ex)
            {
                _cachedRepositoryStatus = null;
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        private async Task RunRevertOperationAsync(IReadOnlyList<string> commitRefs)
        {
            _isLoading = true;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.Reverting", "Reverting selected commit..."));
                var refs = commitRefs == null
                    ? new string[0]
                    : commitRefs.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray();
                var result = refs.Length == 1
                    ? await _gitService.RevertCommitAsync(_workspacePath, refs[0]).ConfigureAwait(true)
                    : await _gitService.RevertCommitsAsync(_workspacePath, refs).ConfigureAwait(true);
                SetStatus(result == null ? string.Empty : result.DisplayMessage);
                if (result == null || !result.Success)
                {
                    MessageBox.Show(
                        this,
                        result == null ? string.Empty : result.DisplayMessage,
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                SpeakStatus(result.DisplayMessage);
                OnRepositoryChanged();
                _isLoading = false;
                await LoadHistoryAsync().ConfigureAwait(true);
                SetStatus(result.DisplayMessage);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        private async Task RunCherryPickOperationAsync(string commitRef)
        {
            _isLoading = true;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.CherryPicking", "Cherry-picking selected commit..."));
                var result = await _gitService.CherryPickAsync(_workspacePath, commitRef).ConfigureAwait(true);
                SetStatus(result == null ? string.Empty : result.DisplayMessage);
                if (result == null || !result.Success)
                {
                    MessageBox.Show(
                        this,
                        result == null ? string.Empty : result.DisplayMessage,
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                SpeakStatus(result.DisplayMessage);
                OnRepositoryChanged();
                _isLoading = false;
                await LoadHistoryAsync().ConfigureAwait(true);
                SetStatus(result.DisplayMessage);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        private async Task RunResetOperationAsync(string commitRef, GitResetMode mode)
        {
            _isLoading = true;
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.Resetting", "Rolling back current branch..."));
                var result = await _gitService.ResetCommitAsync(_workspacePath, commitRef, mode).ConfigureAwait(true);
                SetStatus(result == null ? string.Empty : result.DisplayMessage);
                if (result == null || !result.Success)
                {
                    MessageBox.Show(
                        this,
                        result == null ? string.Empty : result.DisplayMessage,
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                SpeakStatus(result.DisplayMessage);
                OnRepositoryChanged();
                _isLoading = false;
                await LoadHistoryAsync().ConfigureAwait(true);
                SetStatus(result.DisplayMessage);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                RefreshButtons();
                RestoreFocus();
            }
        }

        private void CommitListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            _commitListView.Focus();
            var hit = _commitListView.HitTest(e.Location);
            if (hit.Item != null)
            {
                hit.Item.Selected = true;
                hit.Item.Focused = true;
            }
        }

        private void CommitListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                BeginInvoke(new Action(async () => await LoadSelectedCommitDetailsAsync()));
                return;
            }

            if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            {
                ShowCommitContextMenuForKeyboard();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void HistoryTreeView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            _historyTreeView.Focus();
            var node = _historyTreeView.GetNodeAt(e.Location);
            if (node != null)
            {
                _historyTreeView.SelectedNode = node;
            }
        }

        private void HistoryTreeView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (_isLoading || GetSelectedHistoryRef() == null)
                {
                    return;
                }

                e.SuppressKeyPress = true;
                e.Handled = true;
                BeginInvoke(new Action(async () => await LoadHistoryAsync()));
                return;
            }

            if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            {
                ShowBranchContextMenuForKeyboard();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void ShowBranchContextMenuForKeyboard()
        {
            if (_historyTreeView.SelectedNode == null)
            {
                RestoreTreeSelection();
                _branchContextMenu.Show(_historyTreeView, new Point(8, 8));
                return;
            }

            var node = _historyTreeView.SelectedNode;
            _branchContextMenu.Show(_historyTreeView, new Point(Math.Max(8, node.Bounds.Left), node.Bounds.Bottom));
        }

        private void ShowCommitContextMenuForKeyboard()
        {
            if (_commitListView.SelectedItems.Count == 0)
            {
                RestoreCommitSelection();
                _commitContextMenu.Show(_commitListView, new Point(8, 8));
                return;
            }

            var item = _commitListView.SelectedItems[0];
            _commitContextMenu.Show(_commitListView, new Point(Math.Max(8, item.Bounds.Left), item.Bounds.Bottom));
        }

        private void CopySelectedCommitValue(Func<GitCommitHistoryEntry, string> valueSelector, string statusText)
        {
            var entry = GetSelectedEntry();
            if (entry == null || valueSelector == null)
            {
                return;
            }

            var value = valueSelector(entry);
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            Clipboard.SetText(value.Trim());
            SetStatus(statusText);
            RestoreFocus();
        }

        private void CopyLoadedCommitDiff()
        {
            var entry = GetSelectedEntry();
            if (entry == null ||
                !string.Equals(_loadedCommitSha, entry.FullSha, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(_loadedCommitDiff))
            {
                return;
            }

            Clipboard.SetText(_loadedCommitDiff);
            SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.CopiedDiff", "Copied commit diff."));
            RestoreFocus();
        }

        private void CopySelectedBranchValue(Func<GitBranchEntry, string> valueSelector, string statusText)
        {
            var branch = GetSelectedBranch();
            if (branch == null || valueSelector == null)
            {
                return;
            }

            var value = valueSelector(branch);
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            Clipboard.SetText(value.Trim());
            SetStatus(statusText);
            RestoreTreeFocus();
        }

        private GitBranchEntry GetSelectedBranch()
        {
            var historyRef = GetSelectedHistoryRef();
            return historyRef == null ? null : historyRef.Branch;
        }

        private void RestoreFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                Activate();
                if (_commitListView.Items.Count > 0 && _commitListView.CanFocus)
                {
                    RestoreCommitSelection();
                    _commitListView.Focus();
                    return;
                }

                if (_refreshButton.Enabled && _refreshButton.CanFocus)
                {
                    _refreshButton.Focus();
                    return;
                }

                if (_closeButton.CanFocus)
                {
                    _closeButton.Focus();
                }
            });
        }

        private void RestoreTreeFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                Activate();
                if (_historyTreeView.Nodes.Count > 0 && _historyTreeView.CanFocus)
                {
                    RestoreTreeSelection();
                    _historyTreeView.Focus();
                    return;
                }

                if (_commitListView.Items.Count > 0 && _commitListView.CanFocus)
                {
                    RestoreCommitSelection();
                    _commitListView.Focus();
                }
            });
        }

        private void RestoreTreeSelection()
        {
            if (_historyTreeView.Nodes.Count == 0)
            {
                return;
            }

            if (_historyTreeView.SelectedNode == null)
            {
                _historyTreeView.SelectedNode = FindFirstHistoryRefNode(_historyTreeView.Nodes);
            }

            if (_historyTreeView.SelectedNode != null)
            {
                _historyTreeView.SelectedNode.EnsureVisible();
            }
        }

        private void RestoreCommitSelection()
        {
            if (_commitListView.Items.Count == 0)
            {
                return;
            }

            if (_commitListView.SelectedItems.Count == 0)
            {
                _commitListView.Items[0].Selected = true;
            }

            var selectedItem = _commitListView.SelectedItems.Count > 0
                ? _commitListView.SelectedItems[0]
                : _commitListView.Items[0];
            selectedItem.Focused = true;
            selectedItem.EnsureVisible();
        }

        private GitCommitHistoryEntry GetSelectedEntry()
        {
            return _commitListView.SelectedItems.Count == 0
                ? null
                : _commitListView.SelectedItems[0].Tag as GitCommitHistoryEntry;
        }

        private GitCommitHistoryEntry[] GetSelectedEntries()
        {
            return _commitListView.SelectedIndices
                .Cast<int>()
                .OrderBy(index => index)
                .Select(index => index >= 0 && index < _commitListView.Items.Count ? _commitListView.Items[index].Tag as GitCommitHistoryEntry : null)
                .Where(entry => entry != null)
                .ToArray();
        }

        private bool IsSelectedLatestCurrentCommit()
        {
            return _loadedHistoryRefIsCurrent &&
                   _commitListView.SelectedIndices.Count > 0 &&
                   _commitListView.SelectedIndices[0] == 0;
        }

        private static TreeNode FindFirstHistoryRefNode(TreeNodeCollection nodes)
        {
            if (nodes == null)
            {
                return null;
            }

            foreach (TreeNode node in nodes)
            {
                if (node != null && node.Tag is HistoryRefItem)
                {
                    return node;
                }

                var child = FindFirstHistoryRefNode(node == null ? null : node.Nodes);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text ?? string.Empty;
        }

        private void SetWindowTitle(string historyRefDisplayText)
        {
            var displayText = string.IsNullOrWhiteSpace(historyRefDisplayText)
                ? UiLocalization.Text("GitCommitHistoryForm.Value.CurrentBranch", "Current branch")
                : historyRefDisplayText.Trim();
            Text = string.Format(
                UiLocalization.Text("GitCommitHistoryForm.Title.Template", "Branches and Commits - {0}"),
                displayText);
        }

        private void SpeakStatus(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || _screenReaderService == null)
            {
                return;
            }

            try
            {
                if (!_screenReaderService.IsAvailable || !_screenReaderService.EnsureInitialized())
                {
                    return;
                }

                _screenReaderService.Stop(true);
                _screenReaderService.Speak(text.Trim(), true, false, true);
            }
            catch
            {
            }
        }

        private void OnRepositoryChanged()
        {
            var handler = RepositoryChanged;
            handler?.Invoke(this, EventArgs.Empty);
        }

        private async Task RefreshRepositoryStatusCacheAsync()
        {
            try
            {
                _cachedRepositoryStatus = await _gitService.ReadStatusAsync(_workspacePath).ConfigureAwait(true);
            }
            catch
            {
                _cachedRepositoryStatus = null;
            }
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel1,
                TabStop = false
            };
            var contentSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.None,
                TabStop = false
            };
            var detailsSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel1,
                TabStop = false
            };
            var refsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            refsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            refsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            refsPanel.Controls.Add(_historyRefLabel, 0, 0);
            refsPanel.Controls.Add(_historyTreeView, 0, 1);
            contentSplit.Panel1.Controls.Add(_commitListView);
            detailsSplit.Panel1.Controls.Add(_fileListView);
            detailsSplit.Panel2.Controls.Add(_diffTextBox);
            contentSplit.Panel2.Controls.Add(detailsSplit);
            mainSplit.Panel1.Controls.Add(refsPanel);
            mainSplit.Panel2.Controls.Add(contentSplit);
            mainSplit.SplitterDistance = 260;

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_refreshButton);
            footer.Controls.Add(_loadDetailsButton);

            root.Controls.Add(_statusLabel, 0, 0);
            root.Controls.Add(mainSplit, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }

        private async Task LoadHistoryRefsAsync()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            var cancellationSource = BeginOperationCancellation();
            try
            {
                RefreshButtons();
                SetStatus(UiLocalization.Text("GitCommitHistoryForm.Status.LoadingRefs", "Loading branches..."));
                var branches = await _gitService.ReadBranchesAsync(_workspacePath, cancellationSource.Token).ConfigureAwait(true)
                    ?? new GitBranchEntry[0];
                BindHistoryRefs(branches);
            }
            catch (OperationCanceledException)
            {
                SetStatus(string.Empty);
            }
            catch
            {
                BindHistoryRefs(new GitBranchEntry[0]);
            }
            finally
            {
                ClearOperationCancellation(cancellationSource);
                _isLoading = false;
                RefreshButtons();
            }
        }

        private void BindHistoryRefs(IReadOnlyList<GitBranchEntry> branches)
        {
            var selectedName = GetSelectedHistoryRef() == null ? null : GetSelectedHistoryRef().Revision;
            _historyTreeView.BeginUpdate();
            try
            {
                _historyTreeView.Nodes.Clear();
                var currentGroup = new TreeNode(UiLocalization.Text("GitCommitHistoryForm.Tree.Current", "Current branch"));
                var localGroup = new TreeNode(UiLocalization.Text("GitCommitHistoryForm.Tree.Local", "Local branches"));
                var remoteGroup = new TreeNode(UiLocalization.Text("GitCommitHistoryForm.Tree.Remote", "Remote branches"));
                var hasCurrent = false;
                foreach (var branch in branches ?? new GitBranchEntry[0])
                {
                    if (branch == null || string.IsNullOrWhiteSpace(branch.Name))
                    {
                        continue;
                    }

                    if (branch.IsCurrent)
                    {
                        hasCurrent = true;
                        currentGroup.Nodes.Add(CreateHistoryRefNode(new HistoryRefItem(
                            string.Format(UiLocalization.Text("GitCommitHistoryForm.Value.CurrentBranch.Template", "Current: {0}"), BuildBranchDisplayText(branch)),
                            null,
                            true,
                            branch)));
                    }
                    else
                    {
                        var targetGroup = branch.IsRemote ? remoteGroup : localGroup;
                        targetGroup.Nodes.Add(CreateHistoryRefNode(new HistoryRefItem(BuildBranchDisplayText(branch), branch.Name, false, branch)));
                    }
                }

                if (!hasCurrent)
                {
                    currentGroup.Nodes.Add(CreateHistoryRefNode(new HistoryRefItem(
                        UiLocalization.Text("GitCommitHistoryForm.Value.CurrentBranch", "Current branch"),
                        null,
                        true,
                        null)));
                }

                _historyTreeView.Nodes.Add(currentGroup);
                _historyTreeView.Nodes.Add(localGroup);
                _historyTreeView.Nodes.Add(remoteGroup);
                currentGroup.Expand();
                localGroup.Expand();
                remoteGroup.Expand();
                SelectHistoryRefNode(selectedName);
            }
            finally
            {
                _historyTreeView.EndUpdate();
            }
        }

        private static ListView CreateCommitListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
                View = View.Details,
                AccessibleName = UiLocalization.Text("GitCommitHistoryForm.Accessible.Commits", "Commits"),
                TabIndex = 2,
                UseCompatibleStateImageBehavior = false
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(string.Empty, 430);
            listView.Columns.Add(UiLocalization.Text("GitCommitHistoryForm.Column.ShortSha", "ID"), 110);
            listView.Columns.Add(UiLocalization.Text("GitCommitHistoryForm.Column.Author", "Author"), 160);
            listView.Columns.Add(UiLocalization.Text("GitCommitHistoryForm.Column.Date", "Date"), 210);
            return listView;
        }

        private static ListView CreateCommitFileListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = true,
                View = View.Details,
                AccessibleName = UiLocalization.Text("GitCommitHistoryForm.Accessible.Files", "Changed files"),
                TabIndex = 3
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(UiLocalization.Text("GitCommitHistoryForm.Column.Path", "Path"), 310);
            listView.Columns.Add(UiLocalization.Text("GitCommitHistoryForm.Column.Type", "Type"), 110);
            return listView;
        }

        private static TextBox CreateDiffTextBox()
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitCommitHistoryForm.Accessible.Diff", "Commit diff"),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                TabIndex = 4
            };
            UiTheme.ApplyTextBox(textBox, true, true);
            TextBoxCaretHelper.SetTextAndMoveToStart(
                textBox,
                UiLocalization.Text("GitCommitHistoryForm.Value.LoadDetailsHint", "Press Enter or use Load Details to inspect the selected commit."));
            return textBox;
        }

        private static TreeView CreateHistoryTreeView()
        {
            return new TreeView
            {
                AccessibleName = UiLocalization.Text("GitCommitHistoryForm.Accessible.HistoryRef", "Branches"),
                Dock = DockStyle.Fill,
                HideSelection = false,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                TabIndex = 0
            };
        }

        private HistoryRefItem GetSelectedHistoryRef()
        {
            return _historyTreeView.SelectedNode == null
                ? null
                : _historyTreeView.SelectedNode.Tag as HistoryRefItem;
        }

        private static TreeNode CreateHistoryRefNode(HistoryRefItem item)
        {
            return new TreeNode(item == null ? string.Empty : item.DisplayText)
            {
                Tag = item
            };
        }

        private static string BuildBranchDisplayText(GitBranchEntry branch)
        {
            if (branch == null || string.IsNullOrWhiteSpace(branch.Name))
            {
                return string.Empty;
            }

            var statusText = BuildBranchSyncStatusText(branch);
            return string.IsNullOrWhiteSpace(statusText)
                ? branch.Name.Trim()
                : string.Format(
                    UiLocalization.Text("GitCommitHistoryForm.Value.BranchDisplay.Template", "{0} ({1})"),
                    branch.Name.Trim(),
                    statusText);
        }

        private static string BuildBranchSyncStatusText(GitBranchEntry branch)
        {
            if (branch == null || branch.IsRemote)
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(branch.Upstream))
            {
                return UiLocalization.Text("GitCommitHistoryForm.Value.NoUpstream", "no upstream");
            }

            var ahead = branch.AheadCount.GetValueOrDefault();
            var behind = branch.BehindCount.GetValueOrDefault();
            if (ahead > 0 && behind > 0)
            {
                return string.Format(
                    UiLocalization.Text("GitCommitHistoryForm.Value.AheadBehind.Template", "ahead {0}, behind {1}"),
                    ahead,
                    behind);
            }

            if (ahead > 0)
            {
                return string.Format(UiLocalization.Text("GitCommitHistoryForm.Value.Ahead.Template", "ahead {0}"), ahead);
            }

            if (behind > 0)
            {
                return string.Format(UiLocalization.Text("GitCommitHistoryForm.Value.Behind.Template", "behind {0}"), behind);
            }

            return UiLocalization.Text("GitCommitHistoryForm.Value.UpToDate", "up to date");
        }

        private void SelectHistoryRefNode(string selectedName)
        {
            TreeNode target = null;
            if (!string.IsNullOrWhiteSpace(selectedName))
            {
                target = FindHistoryRefNode(_historyTreeView.Nodes, selectedName);
            }

            if (target == null)
            {
                target = FindFirstHistoryRefNode(_historyTreeView.Nodes);
            }

            _historyTreeView.SelectedNode = target;
            if (target != null)
            {
                target.EnsureVisible();
            }
        }

        private static TreeNode FindHistoryRefNode(TreeNodeCollection nodes, string revision)
        {
            if (nodes == null || string.IsNullOrWhiteSpace(revision))
            {
                return null;
            }

            foreach (TreeNode node in nodes)
            {
                var item = node == null ? null : node.Tag as HistoryRefItem;
                if (item != null && string.Equals(item.Revision, revision, StringComparison.Ordinal))
                {
                    return node;
                }

                var child = FindHistoryRefNode(node == null ? null : node.Nodes, revision);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static string GetSubjectText(GitCommitHistoryEntry entry)
        {
            return entry == null || string.IsNullOrWhiteSpace(entry.Subject)
                ? UiLocalization.Text("GitCommitHistoryForm.Value.NoSubject", "(No subject)")
                : entry.Subject;
        }

        private static string GetCommitFilePathText(GitCommitFileEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(entry.OriginalFilePath) &&
                !string.Equals(entry.OriginalFilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                return entry.OriginalFilePath + " -> " + entry.FilePath;
            }

            return entry.FilePath ?? string.Empty;
        }

        private static string GetCommitFileChangeKindText(string changeKind)
        {
            switch ((changeKind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "added":
                    return UiLocalization.Text("GitCommitHistoryForm.ChangeKind.Added", "Added");
                case "deleted":
                    return UiLocalization.Text("GitCommitHistoryForm.ChangeKind.Deleted", "Deleted");
                case "renamed":
                    return UiLocalization.Text("GitCommitHistoryForm.ChangeKind.Renamed", "Renamed");
                case "copied":
                    return UiLocalization.Text("GitCommitHistoryForm.ChangeKind.Copied", "Copied");
                case "type_changed":
                    return UiLocalization.Text("GitCommitHistoryForm.ChangeKind.TypeChanged", "Type changed");
                case "unmerged":
                    return UiLocalization.Text("GitCommitHistoryForm.ChangeKind.Unmerged", "Unmerged");
                case "modified":
                default:
                    return UiLocalization.Text("GitCommitHistoryForm.ChangeKind.Modified", "Modified");
            }
        }

        private static string BuildCommitSummaryText(GitCommitHistoryEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            return string.Format(
                UiLocalization.Text("GitCommitHistoryForm.Value.Summary.Template", "{0}\r\n{1}\r\n{2}\r\n{3}"),
                entry.FullSha,
                entry.Subject,
                entry.AuthorName,
                entry.AuthorDateText);
        }

        private string BuildCherryPickConfirmText(GitCommitHistoryEntry entry, GitRepositoryStatusSnapshot status)
        {
            var branch = status == null || string.IsNullOrWhiteSpace(status.Branch)
                ? UiLocalization.Text("GitCommitHistoryForm.Value.UnknownBranch", "(Unknown branch)")
                : status.Branch;
            var localChanges = status != null && status.Files.Count > 0
                ? string.Format(
                    UiLocalization.Text("GitCommitHistoryForm.Value.LocalChanges.Template", "Local changes: {0}. Cherry-pick may fail or create conflicts."),
                    status.Files.Count)
                : UiLocalization.Text("GitCommitHistoryForm.Value.CleanWorkingTree", "No local changes detected.");
            return string.Format(
                UiLocalization.Text(
                    "GitCommitHistoryForm.Confirm.CherryPick.Template",
                    "Cherry-pick this commit onto the current branch?\r\n\r\nCurrent branch: {0}\r\nTarget commit: {1}\r\n{2}\r\n\r\n{3}\r\n\r\nThis runs: git cherry-pick <commit>"),
                branch,
                entry == null ? string.Empty : TextValueHelper.FirstNonEmptyTrimmed(entry.ShortSha, entry.FullSha),
                entry == null ? string.Empty : GetSubjectText(entry),
                localChanges);
        }

        private static string BuildRevertConfirmText(IReadOnlyList<GitCommitHistoryEntry> entries)
        {
            var items = entries == null
                ? new GitCommitHistoryEntry[0]
                : entries.Where(entry => entry != null).ToArray();
            if (items.Length == 1)
            {
                return string.Format(
                    UiLocalization.Text(
                        "GitCommitHistoryForm.Confirm.Revert.Template",
                        "Revert this commit?\r\n\r\n{0}\r\n{1}\r\n\r\nThis creates a new commit that reverses the selected commit."),
                    items[0].ShortSha,
                    GetSubjectText(items[0]));
            }

            var maxItems = 8;
            var lines = items
                .Take(maxItems)
                .Select(entry => string.Format(
                    UiLocalization.Text("GitCommitHistoryForm.Value.RevertCommitLine.Template", "- {0} {1}"),
                    TextValueHelper.FirstNonEmptyTrimmed(entry.ShortSha, entry.FullSha),
                    GetSubjectText(entry)))
                .ToList();
            if (items.Length > maxItems)
            {
                lines.Add(string.Format(
                    UiLocalization.Text("GitCommitHistoryForm.Value.MoreCommits.Template", "... and {0} more commit(s)"),
                    items.Length - maxItems));
            }

            return string.Format(
                UiLocalization.Text(
                    "GitCommitHistoryForm.Confirm.RevertMultiple.Template",
                    "Revert {0} selected commits?\r\n\r\nGit will process them in the order shown in the list.\r\n\r\n{1}\r\n\r\nThis creates new commit(s) that reverse the selected commits."),
                items.Length,
                string.Join(Environment.NewLine, lines.ToArray()));
        }

        private async Task ForceDeleteSelectedBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var historyRef = GetSelectedHistoryRef();
            var branch = historyRef == null ? null : historyRef.Branch;
            if (branch == null || branch.IsRemote || branch.IsCurrent)
            {
                return;
            }

            var firstConfirmText = string.Format(
                UiLocalization.Text(
                    "GitCommitHistoryForm.Confirm.ForceDeleteBranch.Template",
                    "Force delete local branch {0}?\r\n\r\nThis uses git branch -D and can delete commits that are not merged into the current branch."),
                branch.Name);
            if (MessageBox.Show(this, firstConfirmText, AppConstants.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                RestoreFocus();
                return;
            }

            var secondConfirmText = string.Format(
                UiLocalization.Text(
                    "GitCommitHistoryForm.Confirm.ForceDeleteBranchSecond.Template",
                    "Confirm again to force delete {0}.\r\n\r\nMyCodex cannot undo this operation."),
                branch.Name);
            if (MessageBox.Show(this, secondConfirmText, AppConstants.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                RestoreFocus();
                return;
            }

            await RunBranchOperationAsync(cancellationToken => _gitService.ForceDeleteBranchAsync(_workspacePath, branch.Name, cancellationToken)).ConfigureAwait(true);
        }
        private async Task DeleteSelectedRemoteBranchAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var historyRef = GetSelectedHistoryRef();
            var branch = historyRef == null ? null : historyRef.Branch;
            if (branch == null || !branch.IsRemote)
            {
                return;
            }

            var target = await ResolveRemoteBranchDeleteTargetAsync(branch.Name).ConfigureAwait(true);
            if (target == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitCommitHistoryForm.Message.RemoteBranchNameInvalid", "The selected remote branch name could not be resolved."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                RestoreFocus();
                return;
            }

            var confirmText = string.Format(
                UiLocalization.Text(
                    "GitCommitHistoryForm.Confirm.DeleteRemoteBranch.Template",
                    "Delete remote branch {0}?\r\n\r\nThis removes the branch from the remote repository and can affect other collaborators. Local remote-tracking refs will be refreshed after deletion."),
                target.DisplayName);
            if (MessageBox.Show(this, confirmText, AppConstants.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                RestoreFocus();
                return;
            }

            await RunBranchOperationAsync(async cancellationToken =>
            {
                var deleteResult = await _gitService.DeleteRemoteBranchAsync(_workspacePath, target.RemoteName, target.BranchName, cancellationToken).ConfigureAwait(true);
                if (deleteResult == null || !deleteResult.Success)
                {
                    return deleteResult;
                }

                var fetchResult = await _gitService.FetchAsync(_workspacePath, cancellationToken).ConfigureAwait(true);
                return fetchResult == null || !fetchResult.Success ? fetchResult : deleteResult;
            }).ConfigureAwait(true);
        }

        private async Task<RemoteBranchDeleteTarget> ResolveRemoteBranchDeleteTargetAsync(string branchName)
        {
            var normalizedBranchName = (branchName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedBranchName))
            {
                return null;
            }

            var remotes = await _gitService.ReadRemotesAsync(_workspacePath).ConfigureAwait(true)
                ?? new GitRemoteEntry[0];
            foreach (var remote in remotes
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .OrderByDescending(item => item.Name.Trim().Length))
            {
                var remoteName = remote.Name.Trim();
                var prefix = remoteName + "/";
                if (normalizedBranchName.StartsWith(prefix, StringComparison.Ordinal) && normalizedBranchName.Length > prefix.Length)
                {
                    return new RemoteBranchDeleteTarget(remoteName, normalizedBranchName.Substring(prefix.Length), normalizedBranchName);
                }
            }

            var separatorIndex = normalizedBranchName.IndexOf('/');
            if (separatorIndex <= 0 || separatorIndex >= normalizedBranchName.Length - 1)
            {
                return null;
            }

            return new RemoteBranchDeleteTarget(
                normalizedBranchName.Substring(0, separatorIndex),
                normalizedBranchName.Substring(separatorIndex + 1),
                normalizedBranchName);
        }

        private sealed class RemoteBranchDeleteTarget
        {
            public RemoteBranchDeleteTarget(string remoteName, string branchName, string displayName)
            {
                RemoteName = remoteName ?? string.Empty;
                BranchName = branchName ?? string.Empty;
                DisplayName = displayName ?? string.Empty;
            }

            public string RemoteName { get; private set; }

            public string BranchName { get; private set; }

            public string DisplayName { get; private set; }
        }
        private sealed class HistoryRefItem
        {
            public HistoryRefItem(string displayText, string revision, bool isCurrent, GitBranchEntry branch)
            {
                DisplayText = displayText ?? string.Empty;
                Revision = revision;
                IsCurrent = isCurrent;
                Branch = branch;
            }

            public string DisplayText { get; private set; }

            public string Revision { get; private set; }

            public bool IsCurrent { get; private set; }

            public GitBranchEntry Branch { get; private set; }

            public override string ToString()
            {
                return DisplayText;
            }
        }
    }
}


