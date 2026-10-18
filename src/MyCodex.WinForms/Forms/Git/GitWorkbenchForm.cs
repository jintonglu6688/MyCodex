using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
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
    internal sealed class GitWorkbenchForm : Form
    {
        private const int FilesPanelMinWidth = 320;
        private const int DiffPanelMinWidth = 420;

        private string _workspacePath;
        private string _initialBranchLabel;
        private readonly IGitRepositoryService _gitService;
        private readonly IGitDiffReader _gitDiffReader;
        private readonly IGitHubCliService _gitHubCliService;
        private readonly IScreenReaderService _screenReaderService;
        private readonly Func<string, GitWorktreeSwitchMode, Task> _openWorkspaceAsync;
        private readonly Func<TextFileOpenPreference> _textFileOpenPreferenceProvider;
        private readonly TextFileOpenService _textFileOpenService;

        private readonly Label _summaryLabel;
        private readonly ComboBox _fileFilterComboBox;
        private readonly ComboBox _diffKindComboBox;
        private readonly TreeView _worktreeTreeView;
        private readonly ListView _fileListView;
        private readonly TextBox _diffTextBox;
        private readonly TextBox _commitSubjectTextBox;
        private readonly TextBox _commitBodyTextBox;
        private readonly TextBox _operationLogTextBox;
        private readonly Button _initializeButton;
        private readonly Button _refreshButton;
        private readonly Button _commitButton;
        private readonly Button _amendButton;
        private readonly Button _fetchButton;
        private readonly Button _pullButton;
        private readonly Button _continueSequencerButton;
        private readonly Button _abortSequencerButton;
        private readonly Button _skipSequencerButton;
        private readonly Button _pushButton;
        private readonly Button _publishButton;
        private readonly Button _remotesButton;
        private readonly Button _branchesAndCommitsButton;
        private readonly Button _stashButton;
        private readonly Button _diagnosticsButton;
        private readonly Button _closeButton;
        private readonly ContextMenu _fileContextMenu;
        private readonly MenuItem _stageMenuItem;
        private readonly MenuItem _stageAllMenuItem;
        private readonly MenuItem _unstageMenuItem;
        private readonly MenuItem _unstageAllMenuItem;
        private readonly MenuItem _discardMenuItem;
        private readonly MenuItem _deleteUntrackedMenuItem;
        private readonly MenuItem _stashSelectedMenuItem;
        private readonly MenuItem _viewConflictMenuItem;
        private readonly MenuItem _markConflictResolvedMenuItem;
        private readonly MenuItem _loadDiffMenuItem;
        private readonly MenuItem _openFileMenuItem;
        private readonly MenuItem _copyDiffMenuItem;
        private readonly MenuItem _copyPathMenuItem;
        private readonly MenuItem _copySummaryMenuItem;
        private readonly MenuItem _copyOperationLogMenuItem;
        private readonly ContextMenu _worktreeContextMenu;
        private readonly MenuItem _switchWorktreeMenuItem;
        private readonly MenuItem _integrateWorktreeBranchMenuItem;
        private readonly MenuItem _createWorktreeMenuItem;
        private readonly MenuItem _removeWorktreeMenuItem;
        private readonly MenuItem _pruneWorktreesMenuItem;
        private readonly MenuItem _copyWorktreePathMenuItem;
        private readonly MenuItem _refreshWorktreesMenuItem;

        private SplitContainer _contentSplitContainer;
        private SplitContainer _leftSplitContainer;
        private bool _isBusy;
        private bool _splitterInitialized;
        private GitRepositoryStatusSnapshot _currentStatus;
        private FileListFocusRequest _pendingFileListFocusRequest;
        private CancellationTokenSource _operationCancellationSource;
        private bool _hasReportedRepositoryMetadata;
        private bool _reportedIsGitRepository;
        private string _reportedBranch;

        internal bool IsBusy
        {
            get { return _isBusy; }
        }

        public GitWorkbenchForm(
            string workspacePath,
            string branchLabel,
            IGitRepositoryService gitService,
            IGitHubCliService gitHubCliService,
            IScreenReaderService screenReaderService,
            Func<string, GitWorktreeSwitchMode, Task> openWorkspaceAsync = null,
            IGitDiffReader gitDiffReader = null,
            Func<TextFileOpenPreference> textFileOpenPreferenceProvider = null,
            TextFileOpenService textFileOpenService = null)
        {
            _workspacePath = workspacePath;
            _initialBranchLabel = branchLabel;
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            _gitDiffReader = gitDiffReader ?? _gitService;
            _gitHubCliService = gitHubCliService ?? throw new ArgumentNullException(nameof(gitHubCliService));
            _screenReaderService = screenReaderService;
            _openWorkspaceAsync = openWorkspaceAsync;
            _textFileOpenPreferenceProvider = textFileOpenPreferenceProvider ?? (() => new TextFileOpenPreference { Mode = TextFileOpenMode.Internal });
            _textFileOpenService = textFileOpenService ?? new TextFileOpenService(null, OpenLocalTextFileInternally);

            Text = BuildWindowTitle(null);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1120, 760);
            Size = new Size(1320, 860);
            ShowInTaskbar = true;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _summaryLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                TabStop = false
            };

            _fileFilterComboBox = CreateComboBox(UiLocalization.Text("GitWorkbenchForm.Accessible.Filter", "Filter"), 0);
            _diffKindComboBox = CreateComboBox(UiLocalization.Text("GitWorkbenchForm.Accessible.DiffKind", "Diff range"), 1);
            PopulateFileFilterComboBox();
            PopulateDiffKindComboBox();

            _worktreeTreeView = CreateWorktreeTreeView();
            _fileListView = CreateFileListView();
            _diffTextBox = CreateTextBox(UiLocalization.Text("GitWorkbenchForm.Accessible.Diff", "Diff"), true, true, 1);
            _commitSubjectTextBox = CreateSingleLineTextBox(UiLocalization.Text("GitWorkbenchForm.Accessible.CommitSubject", "Commit subject"), 2);
            _commitBodyTextBox = CreateTextBox(UiLocalization.Text("GitWorkbenchForm.Accessible.CommitBody", "Commit body"), false, false, 3);
            _operationLogTextBox = CreateTextBox(UiLocalization.Text("GitWorkbenchForm.Accessible.OperationLog", "Operation log"), true, true, 4);
            _operationLogTextBox.Height = 72;
            _operationLogTextBox.Width = 300;
            _operationLogTextBox.TabStop = false;

            _commitButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Commit", "Commit(&C)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Commit", "Commit"), 0);
            _amendButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Amend", "Amend(&A)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Amend", "Amend"), 1);
            _pushButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Push", "Push(&P)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Push", "Push"), 2);
            _publishButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Publish", "Publish(&N)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Publish", "Publish"), 3);
            _remotesButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Remotes", "Remotes(&M)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Remotes", "Remotes"), 4);
            _fetchButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Fetch", "Fetch(&F)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Fetch", "Fetch"), 5);
            _pullButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Pull", "Pull(&U)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Pull", "Pull"), 6);
            _continueSequencerButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.ContinueSequencer", "Continue(&T)"), UiLocalization.Text("GitWorkbenchForm.Accessible.ContinueSequencer", "Continue current operation"), 7);
            _abortSequencerButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.AbortSequencer", "Abort(&O)"), UiLocalization.Text("GitWorkbenchForm.Accessible.AbortSequencer", "Abort current operation"), 8);
            _skipSequencerButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.SkipSequencer", "Skip(&K)"), UiLocalization.Text("GitWorkbenchForm.Accessible.SkipSequencer", "Skip current operation"), 9);
            _branchesAndCommitsButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.BranchesAndCommits", "Branches and Commits(&H)"), UiLocalization.Text("GitWorkbenchForm.Accessible.BranchesAndCommits", "Branches and commits"), 10);
            _stashButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Stashes", "Stashes(&S)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Stashes", "Stashes"), 11);
            _diagnosticsButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Diagnostics", "Diagnostics and Maintenance(&G)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Diagnostics", "Diagnostics and maintenance"), 12);
            _refreshButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Refresh", "Refresh(&R)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Refresh", "Refresh"), 13);
            _initializeButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Initialize", "Initialize(&I)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Initialize", "Initialize"), 14);
            _closeButton = CreateButton(UiLocalization.Text("GitWorkbenchForm.Button.Close", "Close(&E)"), UiLocalization.Text("GitWorkbenchForm.Accessible.Close", "Close"), 15);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);

            UiTheme.ApplyButton(_initializeButton, UiGlyph.NewSession);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_fetchButton, UiGlyph.Download);
            UiTheme.ApplyButton(_pullButton, UiGlyph.Download);
            UiTheme.ApplyButton(_continueSequencerButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_abortSequencerButton, UiGlyph.Error);
            UiTheme.ApplyButton(_skipSequencerButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_commitButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_amendButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_pushButton, UiGlyph.Send);
            UiTheme.ApplyButton(_publishButton, UiGlyph.NewSession);
            UiTheme.ApplyButton(_remotesButton, UiGlyph.Info);
            UiTheme.ApplyButton(_branchesAndCommitsButton, UiGlyph.Info);
            UiTheme.ApplyButton(_stashButton, UiGlyph.Download);
            UiTheme.ApplyButton(_diagnosticsButton, UiGlyph.Info);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _fileContextMenu = new ContextMenu();
            _stageMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.Stage", "Stage(&S)"));
            _stageAllMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.StageAll", "Stage All(&A)"));
            _unstageMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.Unstage", "Unstage(&U)"));
            _unstageAllMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.UnstageAll", "Unstage All(&L)"));
            _discardMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.Discard", "Discard Unstaged Changes(&D)"));
            _deleteUntrackedMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.DeleteUntracked", "Delete Untracked File(&X)"));
            _stashSelectedMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.StashSelected", "Stash Selected File(&H)"));
            _viewConflictMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.ViewConflict", "View Conflict(&C)"));
            _markConflictResolvedMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.MarkConflictResolved", "Mark Resolved and Stage(&M)"));
            _loadDiffMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.LoadDiff", "Load Diff(&V)"));
            _openFileMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.OpenFile", "Open File(&O)"));
            _copyDiffMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.CopyDiff", "Copy Diff(&F)"));
            _copyPathMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.CopyPath", "Copy Path(&Y)"));
            _copySummaryMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.CopySummary", "Copy Repository Summary(&R)"));
            _copyOperationLogMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.CopyOperationLog", "Copy Last Output(&L)"));
            _worktreeContextMenu = new ContextMenu();
            _switchWorktreeMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.SwitchWorktree", "Switch to Workspace(&S)"));
            _integrateWorktreeBranchMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.IntegrateWorktreeBranch", "Integrate Branch with Current...(&I)"));
            _createWorktreeMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.CreateWorktree", "Create(&C)"));
            _removeWorktreeMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.RemoveWorktree", "Remove(&R)"));
            _pruneWorktreesMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.PruneWorktrees", "Prune(&P)"));
            _copyWorktreePathMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.CopyWorktreePath", "Copy Path(&C)"));
            _refreshWorktreesMenuItem = new MenuItem(UiLocalization.Text("GitWorkbenchForm.Menu.RefreshWorktrees", "Refresh(&F)"));
            _worktreeContextMenu.MenuItems.Add(_switchWorktreeMenuItem);
            _worktreeContextMenu.MenuItems.Add(_integrateWorktreeBranchMenuItem);
            _worktreeContextMenu.MenuItems.Add(_createWorktreeMenuItem);
            _worktreeContextMenu.MenuItems.Add(_removeWorktreeMenuItem);
            _worktreeContextMenu.MenuItems.Add(_pruneWorktreesMenuItem);
            _worktreeContextMenu.MenuItems.Add("-");
            _worktreeContextMenu.MenuItems.Add(_copyWorktreePathMenuItem);
            _worktreeContextMenu.MenuItems.Add(_refreshWorktreesMenuItem);
            _worktreeTreeView.ContextMenu = _worktreeContextMenu;
            _fileContextMenu.MenuItems.Add(_stageMenuItem);
            _fileContextMenu.MenuItems.Add(_stageAllMenuItem);
            _fileContextMenu.MenuItems.Add(_unstageMenuItem);
            _fileContextMenu.MenuItems.Add(_unstageAllMenuItem);
            _fileContextMenu.MenuItems.Add(_discardMenuItem);
            _fileContextMenu.MenuItems.Add(_deleteUntrackedMenuItem);
            _fileContextMenu.MenuItems.Add(_stashSelectedMenuItem);
            _fileContextMenu.MenuItems.Add(_viewConflictMenuItem);
            _fileContextMenu.MenuItems.Add(_markConflictResolvedMenuItem);
            _fileContextMenu.MenuItems.Add("-");
            _fileContextMenu.MenuItems.Add(_loadDiffMenuItem);
            _fileContextMenu.MenuItems.Add(_openFileMenuItem);
            _fileContextMenu.MenuItems.Add(_copyDiffMenuItem);
            _fileContextMenu.MenuItems.Add(_copyPathMenuItem);
            _fileContextMenu.MenuItems.Add("-");
            _fileContextMenu.MenuItems.Add(_copySummaryMenuItem);
            _fileContextMenu.MenuItems.Add(_copyOperationLogMenuItem);
            _fileListView.ContextMenu = _fileContextMenu;

            _initializeButton.Click += async (sender, e) => await InitializeRepositoryAsync();
            _refreshButton.Click += async (sender, e) => await LoadStatusAsync(false, true);
            _fetchButton.Click += async (sender, e) => await FetchAsync();
            _pullButton.Click += async (sender, e) => await PullAsync();
            _continueSequencerButton.Click += async (sender, e) => await ContinueSequencerAsync();
            _abortSequencerButton.Click += async (sender, e) => await AbortSequencerAsync();
            _skipSequencerButton.Click += async (sender, e) => await SkipSequencerAsync();
            _commitButton.Click += async (sender, e) => await CommitAsync();
            _amendButton.Click += async (sender, e) => await AmendAsync();
            _pushButton.Click += async (sender, e) => await PushAsync();
            _publishButton.Click += async (sender, e) => await PublishCurrentRepositoryAsync();
            _remotesButton.Click += async (sender, e) => await OpenRemoteManagementAsync();
            _branchesAndCommitsButton.Click += (sender, e) => OpenBranchesAndCommits();
            _stashButton.Click += (sender, e) => OpenStashManagement();
            _diagnosticsButton.Click += (sender, e) => OpenRepositoryDiagnostics();
            _closeButton.Click += (sender, e) => Close();
            _stageMenuItem.Click += async (sender, e) => await StageSelectedAsync();
            _stageAllMenuItem.Click += async (sender, e) => await RunGitOperationAsync(() => _gitService.StageAllAsync(_workspacePath), false);
            _unstageMenuItem.Click += async (sender, e) => await UnstageSelectedAsync();
            _unstageAllMenuItem.Click += async (sender, e) => await RunGitOperationAsync(() => _gitService.UnstageAllAsync(_workspacePath), false);
            _discardMenuItem.Click += async (sender, e) => await DiscardSelectedAsync();
            _deleteUntrackedMenuItem.Click += async (sender, e) => await DeleteUntrackedSelectedAsync();
            _stashSelectedMenuItem.Click += async (sender, e) => await StashSelectedAsync();
            _viewConflictMenuItem.Click += (sender, e) => OpenConflictDetails();
            _markConflictResolvedMenuItem.Click += async (sender, e) => await MarkSelectedConflictResolvedAsync();
            _loadDiffMenuItem.Click += async (sender, e) => await LoadSelectedDiffAsync();
            _openFileMenuItem.Click += (sender, e) => OpenSelectedFile();
            _copyDiffMenuItem.Click += async (sender, e) => await CopySelectedDiffAsync();
            _copyPathMenuItem.Click += (sender, e) => CopySelectedPath();
            _copySummaryMenuItem.Click += (sender, e) => CopyRepositorySummary();
            _copyOperationLogMenuItem.Click += (sender, e) => CopyOperationLog();
            _switchWorktreeMenuItem.Click += async (sender, e) => await SwitchSelectedWorktreeAsync();
            _integrateWorktreeBranchMenuItem.Click += async (sender, e) => await IntegrateSelectedWorktreeBranchAsync();
            _createWorktreeMenuItem.Click += async (sender, e) => await CreateWorktreeFromTreeAsync();
            _removeWorktreeMenuItem.Click += async (sender, e) => await RemoveSelectedWorktreeFromTreeAsync();
            _pruneWorktreesMenuItem.Click += async (sender, e) => await PruneWorktreesFromTreeAsync();
            _copyWorktreePathMenuItem.Click += (sender, e) => CopySelectedWorktreePath();
            _refreshWorktreesMenuItem.Click += async (sender, e) => await RefreshWorktreeTreeAsync(true);
            _worktreeTreeView.MouseDown += WorktreeTreeView_MouseDown;
            _worktreeTreeView.KeyDown += WorktreeTreeView_KeyDown;
            _worktreeContextMenu.Popup += (sender, e) => RefreshWorktreeContextMenu();
            _fileListView.SelectedIndexChanged += (sender, e) =>
            {
                UpdateDiffText();
                RefreshActionButtons();
            };
            _fileListView.ItemActivate += (sender, e) => OpenSelectedFile();
            _fileListView.MouseDown += FileListView_MouseDown;
            _fileListView.KeyDown += FileListView_KeyDown;
            _fileContextMenu.Popup += (sender, e) => RefreshContextMenu();
            _commitSubjectTextBox.TextChanged += (sender, e) => RefreshActionButtons();
            _commitBodyTextBox.TextChanged += (sender, e) => RefreshActionButtons();
            _fileFilterComboBox.SelectedIndexChanged += (sender, e) =>
            {
                BindStatus();
                RefreshActionButtons();
            };
            _diffKindComboBox.SelectedIndexChanged += (sender, e) =>
            {
                UpdateDiffText();
                RefreshActionButtons();
            };
            KeyDown += GitWorkbenchForm_KeyDown;
            Shown += OnShown;

            BuildLayout();
            CancelButton = _closeButton;
            SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.Preparing", "Preparing Git workbench..."));
            RefreshActionButtons();
        }

        public event EventHandler WorkspacePathChanged;

        public event EventHandler RepositoryMetadataChanged;

        internal string WorkspacePath
        {
            get { return _workspacePath; }
        }

        private async void OnShown(object sender, EventArgs e)
        {
            Activate();
            EnsureInitialSplitterDistance();
            await LoadStatusAsync(false, true);
        }

        private async void GitWorkbenchForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                await LoadStatusAsync(false, true);
            }
        }

        internal void ActivateAndFocusFiles()
        {
            Activate();
            RestoreFileListFocus();
        }

        private async Task LoadStatusAsync(bool preserveOperationLog = false, bool restoreFileListFocus = false)
        {
            if (_isBusy)
            {
                return;
            }

            _isBusy = true;
            var cancellationSource = BeginOperationCancellation();
            try
            {
                if (!preserveOperationLog)
                {
                    SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.Loading", "Loading Git status..."));
                }

                RefreshActionButtons();
                _currentStatus = await _gitService.ReadStatusAsync(_workspacePath, cancellationSource.Token).ConfigureAwait(true);
                BindStatus();
                NotifyRepositoryMetadataChangedIfNeeded(_currentStatus);
                await RefreshWorktreeTreeAsync(false, cancellationSource.Token).ConfigureAwait(true);
                if (!preserveOperationLog)
                {
                    SetOperationLog(BuildLoadedStatusText(_currentStatus));
                }
            }
            catch (OperationCanceledException)
            {
                if (!preserveOperationLog)
                {
                    SetOperationLog(string.Empty);
                }
            }
            catch (Exception ex)
            {
                _currentStatus = new GitRepositoryStatusSnapshot
                {
                    WorkspacePath = _workspacePath
                };
                BindStatus();
                SetOperationLog(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ClearOperationCancellation(cancellationSource);
                _isBusy = false;
                RefreshActionButtons();
                if (restoreFileListFocus)
                {
                    RestoreFileListFocus();
                }
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

        private void BindStatus()
        {
            var status = _currentStatus ?? new GitRepositoryStatusSnapshot { WorkspacePath = _workspacePath };
            var existingOrder = CaptureFileListOrder();
            Text = BuildWindowTitle(status);
            _summaryLabel.Text = BuildSummaryText(status);
            _fileListView.BeginUpdate();
            try
            {
                _fileListView.Items.Clear();
                foreach (var entry in GetFilteredFiles(status).OrderBy(item => GetStableFileOrder(item, existingOrder)).ThenBy(GetGroupOrder).ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase))
                {
                    var item = new ListViewItem(GetStatusGroupText(entry));
                    item.SubItems.Add(GetDisplayFilePath(entry));
                    item.SubItems.Add(GetChangeKindText(entry.ChangeKind));
                    item.SubItems.Add(BuildAddedLineText(entry));
                    item.SubItems.Add(BuildDeletedLineText(entry));
                    item.Tag = entry;
                    _fileListView.Items.Add(item);
                }
            }
            finally
            {
                _fileListView.EndUpdate();
            }

            if (_fileListView.Items.Count > 0)
            {
                _fileListView.Items[0].Selected = true;
                _fileListView.Items[0].Focused = true;
                _fileListView.EnsureVisible(0);
            }

            UpdateDiffText();
        }

        private async Task RefreshWorktreeTreeAsync(bool announceStatus, CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                if (_currentStatus != null && !string.IsNullOrWhiteSpace(_currentStatus.StatusErrorMessage))
                {
                    BindWorktreeStatusError(_currentStatus.StatusErrorMessage);
                    if (announceStatus)
                    {
                        SetOperationLog(GitWorkbenchStatusErrorFormatter.BuildDisplayMessage(_workspacePath, _currentStatus.StatusErrorMessage));
                        RestoreWorktreeTreeFocus();
                    }

                    return;
                }

                if (announceStatus)
                {
                    SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.LoadingWorktrees", "Loading worktrees..."));
                }

                var entries = await _gitService.ReadWorktreesAsync(_workspacePath, cancellationToken).ConfigureAwait(true)
                    ?? new GitWorktreeEntry[0];
                BindWorktreeTree(entries);
                if (announceStatus)
                {
                    SetOperationLog(string.Format(
                        UiLocalization.Text("GitWorkbenchForm.Status.WorktreesLoaded.Template", "Loaded {0} worktree(s)."),
                        entries.Count));
                    RestoreWorktreeTreeFocus();
                }
            }
            catch (OperationCanceledException)
            {
                if (announceStatus)
                {
                    SetOperationLog(string.Empty);
                    RestoreWorktreeTreeFocus();
                }
            }
            catch (Exception ex)
            {
                BindWorktreeTree(new GitWorktreeEntry[0]);
                if (announceStatus)
                {
                    SetOperationLog(ex.Message);
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    RestoreWorktreeTreeFocus();
                }
            }
        }

        private void BindWorktreeTree(IReadOnlyList<GitWorktreeEntry> entries)
        {
            _worktreeTreeView.BeginUpdate();
            try
            {
                _worktreeTreeView.Nodes.Clear();
                var root = new TreeNode(UiLocalization.Text("GitWorkbenchForm.Value.WorktreeRoot", "Worktrees"));
                _worktreeTreeView.Nodes.Add(root);
                TreeNode currentNode = null;
                foreach (var entry in entries ?? new GitWorktreeEntry[0])
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    var node = new TreeNode(BuildWorktreeNodeText(entry));
                    node.Tag = entry;
                    root.Nodes.Add(node);
                    if (IsCurrentWorkspaceEntry(entry))
                    {
                        currentNode = node;
                    }
                }

                root.Expand();
                _worktreeTreeView.SelectedNode = currentNode ?? (root.Nodes.Count > 0 ? root.Nodes[0] : root);
            }
            finally
            {
                _worktreeTreeView.EndUpdate();
            }

            RefreshWorktreeContextMenu();
        }

        private void BindWorktreeStatusError(string statusErrorMessage)
        {
            _worktreeTreeView.BeginUpdate();
            try
            {
                _worktreeTreeView.Nodes.Clear();
                var root = new TreeNode(UiLocalization.Text("GitWorkbenchForm.Value.WorktreeRoot", "Worktrees"));
                var detailNode = new TreeNode(GitWorkbenchStatusErrorFormatter.BuildWorktreePlaceholderText(_workspacePath, statusErrorMessage));
                root.Nodes.Add(detailNode);
                root.Expand();
                _worktreeTreeView.Nodes.Add(root);
                _worktreeTreeView.SelectedNode = detailNode;
            }
            finally
            {
                _worktreeTreeView.EndUpdate();
            }

            RefreshWorktreeContextMenu();
        }

        private string BuildWorktreeNodeText(GitWorktreeEntry entry)
        {
            var name = GetWorkspaceNameForPath(entry == null ? null : entry.Path);
            var branch = GetWorktreeBranchText(entry);
            return IsCurrentWorkspaceEntry(entry)
                ? string.Format(UiLocalization.Text("GitWorkbenchForm.Value.WorktreeCurrent.Template", "{0}    {1}    current"), name, branch)
                : string.Format(UiLocalization.Text("GitWorkbenchForm.Value.Worktree.Template", "{0}    {1}"), name, branch);
        }

        private string GetWorktreeBranchText(GitWorktreeEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(entry.Branch))
            {
                return entry.Branch;
            }

            return entry.IsDetached
                ? UiLocalization.Text("GitWorkbenchForm.Value.Detached", "Detached")
                : UiLocalization.Text("GitWorkbenchForm.Value.UnknownBranch", "(Unknown branch)");
        }

        private string BuildWindowTitle(GitRepositoryStatusSnapshot status)
        {
            var baseTitle = UiLocalization.Text("GitWorkbenchForm.Title", "Git Workbench");
            var workspaceName = GetWorkspaceNameForTitle(status);
            var branch = GetBranchNameForTitle(status);
            return string.Format(
                UiLocalization.Text("GitWorkbenchForm.Title.Template", "{0} - {1} - {2}"),
                baseTitle,
                workspaceName,
                branch);
        }

        private string GetWorkspaceNameForTitle(GitRepositoryStatusSnapshot status)
        {
            var path = status != null && !string.IsNullOrWhiteSpace(status.WorkspacePath)
                ? status.WorkspacePath
                : _workspacePath;
            return GetWorkspaceNameForPath(path);
        }

        private string GetWorkspaceNameForPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return UiLocalization.Text("GitWorkbenchForm.Value.UnknownWorkspace", "(Unknown workspace)");
            }

            var trimmedPath = path.Trim().TrimEnd('\\', '/');
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                return path.Trim();
            }

            try
            {
                return Path.GetFileName(trimmedPath);
            }
            catch
            {
                var lastSlash = Math.Max(trimmedPath.LastIndexOf('/'), trimmedPath.LastIndexOf('\\'));
                return lastSlash >= 0 && lastSlash < trimmedPath.Length - 1
                    ? trimmedPath.Substring(lastSlash + 1)
                    : trimmedPath;
            }
        }

        private string GetBranchNameForTitle(GitRepositoryStatusSnapshot status)
        {
            if (status != null && !string.IsNullOrWhiteSpace(status.Branch))
            {
                return status.Branch;
            }

            return TextValueHelper.FirstNonEmptyTrimmed(_initialBranchLabel, UiLocalization.Text("GitWorkbenchForm.Value.UnknownBranch", "(Unknown branch)"));
        }

        private static int GetGroupOrder(GitChangedFileEntry entry)
        {
            if (entry == null)
            {
                return 99;
            }

            if (entry.IsConflicted)
            {
                return 0;
            }

            if (entry.IsStaged)
            {
                return 1;
            }

            if (entry.IsUnstaged)
            {
                return 2;
            }

            if (entry.IsUntracked)
            {
                return 3;
            }

            return 4;
        }

        private string BuildSummaryText(GitRepositoryStatusSnapshot status)
        {
            status = status ?? new GitRepositoryStatusSnapshot { WorkspacePath = _workspacePath };
            var workspaceText = string.IsNullOrWhiteSpace(status.WorkspacePath) ? _workspacePath : status.WorkspacePath;
            if (!string.IsNullOrWhiteSpace(status.StatusErrorMessage))
            {
                return string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Summary.StatusError.Template", "Workspace: {0}    Git: status unavailable"),
                    workspaceText);
            }

            if (!status.IsGitRepository)
            {
                return string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Summary.NotRepository.Template", "Workspace: {0}    Git: not initialized"),
                    workspaceText);
            }

            var branch = TextValueHelper.FirstNonEmptyTrimmed(status.Branch, _initialBranchLabel, UiLocalization.Text("GitWorkbenchForm.Value.UnknownBranch", "(Unknown branch)"));
            var upstream = string.IsNullOrWhiteSpace(status.Upstream)
                ? UiLocalization.Text("GitWorkbenchForm.Value.NoUpstream", "(No upstream)")
                : status.Upstream;
            var sync = status.AheadCount.HasValue && status.BehindCount.HasValue
                ? string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Summary.AheadBehind.Template", "ahead {0}, behind {1}"),
                    status.AheadCount.Value,
                    status.BehindCount.Value)
                : UiLocalization.Text("GitWorkbenchForm.Value.SyncUnknown", "sync unknown");
            return string.Format(
                UiLocalization.Text("GitWorkbenchForm.Summary.Template", "Workspace: {0}    Branch: {1}    Upstream: {2}    {3}    Head: {4}    Files: {5} (S:{6} U:{7} N:{8} C:{9})    Stash: {10}"),
                workspaceText,
                branch,
                upstream,
                sync,
                string.IsNullOrWhiteSpace(status.HeadSha) ? UiLocalization.Text("GitWorkbenchForm.Value.NoHead", "(No HEAD)") : status.HeadSha,
                status.Files.Count,
                status.StagedCount,
                status.UnstagedCount,
                status.UntrackedCount,
                status.ConflictedCount,
                status.StashCount);
        }

        private string BuildLoadedStatusText(GitRepositoryStatusSnapshot status)
        {
            if (status != null && !string.IsNullOrWhiteSpace(status.StatusErrorMessage))
            {
                return GitWorkbenchStatusErrorFormatter.BuildDisplayMessage(_workspacePath, status.StatusErrorMessage);
            }

            if (status == null || !status.IsGitRepository)
            {
                return UiLocalization.Text("GitWorkbenchForm.Status.NotRepository", "Current workspace is not a Git repository.");
            }

            if (status.HasConflicts)
            {
                if (status.HasMergeOrRebaseOperation)
                {
                    return UiLocalization.Text("GitWorkbenchForm.Status.LoadedWithMergeOrRebaseConflicts", "Merge or rebase is paused with conflicts. Resolve conflicts in the workbench, then continue or abort from Git.");
                }

                return status.HasSequencerOperation
                    ? UiLocalization.Text("GitWorkbenchForm.Status.LoadedWithSequencerConflicts", "Git operation paused. Resolve conflicts, then continue, abort, or skip.")
                    : UiLocalization.Text("GitWorkbenchForm.Status.LoadedWithConflicts", "Git status loaded. Resolve conflicts before committing or pushing.");
            }

            if (status.HasSequencerOperation)
            {
                return UiLocalization.Text("GitWorkbenchForm.Status.LoadedWithSequencer", "Git operation is waiting. Continue, abort, or skip the current step.");
            }

            if (status.HasMergeOrRebaseOperation)
            {
                return UiLocalization.Text("GitWorkbenchForm.Status.LoadedWithMergeOrRebase", "Merge or rebase is in progress. Resolve it with Git before starting another branch operation.");
            }

            if (status.Files.Count == 0)
            {
                return UiLocalization.Text("GitWorkbenchForm.Status.LoadedClean", "Git status loaded. No local changes.");
            }

            if (IsDetachedHead(status))
            {
                return UiLocalization.Text("GitWorkbenchForm.Status.LoadedDetached", "Git status loaded. Detached HEAD; push is unavailable.");
            }

            if (string.IsNullOrWhiteSpace(status.Upstream))
            {
                return UiLocalization.Text("GitWorkbenchForm.Status.LoadedNoUpstream", "Git status loaded. No upstream is configured; push is unavailable.");
            }

            return UiLocalization.Text("GitWorkbenchForm.Status.Loaded", "Git status loaded.");
        }

        private static bool IsDetachedHead(GitRepositoryStatusSnapshot status)
        {
            return status != null &&
                   !string.IsNullOrWhiteSpace(status.Branch) &&
                   status.Branch.StartsWith("detached", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCurrentBranchName(string branch)
        {
            if (string.IsNullOrWhiteSpace(branch))
            {
                return null;
            }

            var normalized = branch.Trim();
            return normalized.StartsWith("detached", StringComparison.OrdinalIgnoreCase)
                ? null
                : normalized;
        }

        private void UpdateDiffText()
        {
            var entry = GetSelectedEntry();
            string text;
            if (entry == null)
            {
                text = _currentStatus != null && !string.IsNullOrWhiteSpace(_currentStatus.StatusErrorMessage)
                    ? GitWorkbenchStatusErrorFormatter.BuildDisplayMessage(_workspacePath, _currentStatus.StatusErrorMessage)
                    : _currentStatus != null && !_currentStatus.IsGitRepository
                        ? UiLocalization.Text("GitWorkbenchForm.Value.NotRepository", "Current workspace is not a Git repository. Initialize it to start using Git operations.")
                        : UiLocalization.Text("GitWorkbenchForm.Value.SelectFile", "Select a changed file to view its diff.");
            }
            else
            {
                GitDiffKind diffKind;
                if (!TryResolveSelectedDiffKind(entry, out diffKind))
                {
                    text = UiLocalization.Text("GitWorkbenchForm.Value.DiffKindUnavailable", "The selected diff range is not available for this file.");
                }
                else
                {
                    var diffText = GetDiffText(entry, diffKind);
                    text = !string.IsNullOrWhiteSpace(diffText)
                        ? diffText
                        : UiLocalization.Text("GitWorkbenchForm.Value.DiffNotLoaded", "Diff is not loaded.");
                }
            }

            TextBoxCaretHelper.SetTextAndMoveToStart(_diffTextBox, text);
        }

        private async Task InitializeRepositoryAsync()
        {
            if (_isBusy)
            {
                return;
            }

            if (_currentStatus != null && _currentStatus.IsGitRepository)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Confirm.Initialize.Template", "Initialize a Git repository in this workspace?\r\n\r\n{0}"),
                    _workspacePath),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                return;
            }

            await RunGitOperationAsync(() => _gitService.InitializeRepositoryAsync(_workspacePath));
        }

        private async Task PublishCurrentRepositoryAsync()
        {
            if (_isBusy || !CanConfigureRemote(_currentStatus))
            {
                return;
            }

            if (await TryOpenRemoteManagementWhenOriginExistsAsync().ConfigureAwait(true))
            {
                return;
            }

            var defaultName = GetDefaultRepositoryName(_workspacePath);
            var repositories = await LoadEmptyGitHubRepositoriesForPublishAsync();
            var allowGitHubCreate = true;
            using (var form = new GitRepositoryPublishForm(repositories, defaultName, allowGitHubCreate))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreFileListFocus();
                    return;
                }

                var confirmText = BuildPublishConfirmText(form);
                var confirm = MessageBox.Show(
                    this,
                    confirmText,
                    AppConstants.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (confirm != DialogResult.Yes)
                {
                    RestoreFileListFocus();
                    return;
                }

                await RunGitOperationAsync(async () =>
                {
                    return await RunPublishActionAsync(form).ConfigureAwait(true);
                });
            }
        }

        private string BuildPublishConfirmText(GitRepositoryPublishForm form)
        {
            if (form == null)
            {
                return string.Empty;
            }

            if (form.SelectedAction == GitRepositoryPublishAction.CreateGitHubRepository)
            {
                return string.Format(
                    UiLocalization.Text(
                        "GitWorkbenchForm.Confirm.PublishCreate.Template",
                        "Create GitHub repository '{0}' and bind this workspace as origin?\r\n\r\nThis will initialize Git first if needed. Local commits are not pushed automatically."),
                    form.CreateRequest.Name);
            }

            return string.Format(
                UiLocalization.Text(
                    "GitWorkbenchForm.Confirm.PublishBind.Template",
                    "Bind this workspace to the remote as origin?\r\n\r\n{0}\r\n\r\nThis will initialize Git first if needed. Local commits are not pushed automatically."),
                form.RemoteUrl);
        }

        private async Task<GitOperationResult> RunPublishActionAsync(GitRepositoryPublishForm form)
        {
            var initResult = await EnsureRepositoryInitializedAsync().ConfigureAwait(true);
            if (initResult != null && !initResult.Success)
            {
                return initResult;
            }

            if (form.SelectedAction == GitRepositoryPublishAction.CreateGitHubRepository)
            {
                var request = form.CreateRequest;
                request.SourceDirectory = _workspacePath;
                request.RemoteName = "origin";
                var createResult = await _gitHubCliService.CreateRepositoryAsync(request, System.Threading.CancellationToken.None).ConfigureAwait(true);
                return new GitOperationResult
                {
                    Success = createResult != null && createResult.Success,
                    ExitCode = createResult == null ? -1 : createResult.ExitCode,
                    StandardOutput = createResult == null ? string.Empty : createResult.Output,
                    StandardError = createResult != null && createResult.Success ? string.Empty : createResult == null ? string.Empty : createResult.Output,
                    DisplayMessage = createResult != null && createResult.Success
                        ? UiLocalization.Text("GitWorkbenchForm.Status.Published", "Created remote repository and bound origin.")
                        : UiLocalization.Text("GitWorkbenchForm.Status.PublishFailed", "Failed to publish repository.")
                };
            }

            return await _gitService.AddRemoteAsync(_workspacePath, "origin", form.RemoteUrl).ConfigureAwait(true);
        }

        private async Task<GitOperationResult> EnsureRepositoryInitializedAsync()
        {
            if (_currentStatus != null && _currentStatus.IsGitRepository)
            {
                return null;
            }

            return await _gitService.InitializeRepositoryAsync(_workspacePath).ConfigureAwait(true);
        }

        private async Task<System.Collections.Generic.IReadOnlyList<GitHubRepositoryEntry>> LoadEmptyGitHubRepositoriesForPublishAsync()
        {
            _isBusy = true;
            try
            {
                RefreshActionButtons();
                SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.LoadingEmptyGitHubRepositories", "Loading empty GitHub repositories..."));
                var repositories = await _gitHubCliService.ListEmptyRepositoriesAsync(null, 100, System.Threading.CancellationToken.None).ConfigureAwait(true);
                SetOperationLog(string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Status.EmptyGitHubRepositoriesLoaded.Template", "Loaded {0} empty GitHub repositories."),
                    repositories == null ? 0 : repositories.Count));
                return repositories ?? new GitHubRepositoryEntry[0];
            }
            catch (Exception ex)
            {
                SetOperationLog(ex.Message);
                return new GitHubRepositoryEntry[0];
            }
            finally
            {
                _isBusy = false;
                RefreshActionButtons();
            }
        }

        private async Task StageSelectedAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || !entry.CanStage)
            {
                return;
            }

            QueueFileListFocusAfterOperation(entry);
            await RunGitOperationAsync(() => _gitService.StageAsync(_workspacePath, new[] { entry.FilePath }), false);
        }

        private async Task UnstageSelectedAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || !entry.CanUnstage)
            {
                return;
            }

            QueueFileListFocusAfterOperation(entry);
            await RunGitOperationAsync(() => _gitService.UnstageAsync(_workspacePath, new[] { entry.FilePath }), false);
        }

        private async Task ToggleSelectedStagingAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || entry.IsConflicted)
            {
                return;
            }

            if (entry.CanStage || entry.HasUnstagedChanges)
            {
                QueueFileListFocusAfterOperation(entry);
                await RunGitOperationAsync(() => _gitService.StageAsync(_workspacePath, new[] { entry.FilePath }), false).ConfigureAwait(true);
                return;
            }

            if (entry.CanUnstage)
            {
                QueueFileListFocusAfterOperation(entry);
                await RunGitOperationAsync(() => _gitService.UnstageAsync(_workspacePath, new[] { entry.FilePath }), false).ConfigureAwait(true);
            }
        }

        private Dictionary<string, int> CaptureFileListOrder()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < _fileListView.Items.Count; i++)
            {
                var entry = _fileListView.Items[i].Tag as GitChangedFileEntry;
                if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath) || result.ContainsKey(entry.FilePath))
                {
                    continue;
                }

                result[entry.FilePath] = i;
            }

            return result;
        }

        private static int GetStableFileOrder(GitChangedFileEntry entry, Dictionary<string, int> existingOrder)
        {
            if (entry == null || existingOrder == null || string.IsNullOrWhiteSpace(entry.FilePath))
            {
                return int.MaxValue;
            }

            return existingOrder.TryGetValue(entry.FilePath, out var index)
                ? index
                : int.MaxValue;
        }

        private async Task MarkSelectedConflictResolvedAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || !entry.IsConflicted)
            {
                return;
            }

            await RunGitOperationAsync(() => _gitService.MarkConflictsResolvedAsync(_workspacePath, new[] { entry.FilePath }));
        }

        private async Task DiscardSelectedAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || !entry.CanDiscard)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Confirm.Discard.Template", "Discard unstaged changes in this file?\r\n\r\n{0}\r\n\r\nThis cannot be undone."),
                    entry.FilePath),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                return;
            }

            await RunGitOperationAsync(() => _gitService.DiscardUnstagedAsync(_workspacePath, new[] { entry.FilePath }));
        }

        private async Task DeleteUntrackedSelectedAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || !entry.IsUntracked)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Confirm.DeleteUntracked.Template", "Delete this untracked file?\r\n\r\n{0}\r\n\r\nThis cannot be undone."),
                    entry.FilePath),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                return;
            }

            await RunGitOperationAsync(() => _gitService.DeleteUntrackedAsync(_workspacePath, new[] { entry.FilePath }));
        }

        private async Task StashSelectedAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath) || entry.IsConflicted)
            {
                return;
            }

            using (var form = new GitStashCreateForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreFileListFocus();
                    return;
                }

                var includeUntracked = form.IncludeUntracked || entry.IsUntracked;
                await RunGitOperationAsync(() => _gitService.StashPushPathsAsync(
                    _workspacePath,
                    form.StashMessage,
                    includeUntracked,
                    new[] { entry.FilePath }));
            }
        }

        private async Task CommitAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var subject = GetCommitSubject();
            var body = GetCommitBody();
            if (string.IsNullOrWhiteSpace(subject))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitWorkbenchForm.Message.CommitMessageRequired", "Enter a commit subject first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _commitSubjectTextBox.Focus();
                return;
            }

            if (!ValidateCommitPreconditions(false))
            {
                return;
            }

            var confirm = MessageBox.Show(
                this,
                BuildCommitConfirmText(subject, body),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                RestoreFileListFocus();
                return;
            }

            var success = await RunGitOperationAsync(() => _gitService.CommitAsync(_workspacePath, subject, body));
            if (success)
            {
                ClearCommitInputs();
            }
        }

        private async Task AmendAsync()
        {
            if (_isBusy)
            {
                return;
            }

            if (_currentStatus == null || !_currentStatus.IsGitRepository || _currentStatus.HasConflicts)
            {
                if (!ValidateCommitPreconditions(true))
                {
                    return;
                }
            }

            if (!ValidateCommitPreconditions(true))
            {
                return;
            }

            var lastCommit = await ReadLastCommitAsync().ConfigureAwait(true);
            var subject = GetCommitSubject();
            var body = GetCommitBody();
            var confirm = MessageBox.Show(
                this,
                BuildAmendConfirmText(lastCommit, subject, body),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                RestoreFileListFocus();
                return;
            }

            var success = await RunGitOperationAsync(() => _gitService.CommitAmendAsync(_workspacePath, subject, body));
            if (success)
            {
                ClearCommitInputs();
            }
        }

        private bool ValidateCommitPreconditions(bool amend)
        {
            var status = _currentStatus;
            string message = null;
            var hasStaged = status != null && status.Files.Any(item => item.IsStaged);
            if (status == null || !status.IsGitRepository)
            {
                message = UiLocalization.Text("GitWorkbenchForm.Message.CommitNotRepository", "Current workspace is not a Git repository.");
            }
            else if (status.HasConflicts)
            {
                message = UiLocalization.Text("GitWorkbenchForm.Message.CommitHasConflicts", "There are unresolved conflicts. Resolve them before committing.");
            }
            else if (status.HasSequencerOperation)
            {
                message = UiLocalization.Text("GitWorkbenchForm.Message.CommitHasSequencerOperation", "A Git operation is in progress. Use Continue, Abort, or Skip before committing manually.");
            }
            else if (status.HasMergeOrRebaseOperation)
            {
                message = UiLocalization.Text("GitWorkbenchForm.Message.CommitHasMergeOrRebaseOperation", "A merge or rebase is in progress. Resolve it before committing manually.");
            }
            else if (!hasStaged)
            {
                message = UiLocalization.Text("GitWorkbenchForm.Message.CommitNoStagedChanges", "There are no staged changes to commit.");
            }
            else if (amend && IsDetachedHead(status))
            {
                message = UiLocalization.Text("GitWorkbenchForm.Message.AmendDetached", "Amend is unavailable in detached HEAD.");
            }
            else if (amend && string.IsNullOrWhiteSpace(status.HeadSha))
            {
                message = UiLocalization.Text("GitWorkbenchForm.Message.AmendNoHead", "No previous commit was found to amend.");
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return true;
            }

            SetOperationLog(message);
            MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            RestoreFileListFocus();
            return false;
        }

        private async Task PushAsync()
        {
            if (_isBusy)
            {
                return;
            }

            if (_currentStatus == null || !_currentStatus.IsGitRepository || _currentStatus.HasConflicts || IsDetachedHead(_currentStatus))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentStatus.Upstream))
            {
                var branchName = NormalizeCurrentBranchName(_currentStatus.Branch);
                if (string.IsNullOrWhiteSpace(branchName))
                {
                    return;
                }

                var result = MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text(
                            "GitWorkbenchForm.Confirm.PushSetUpstream.Template",
                            "Set upstream to origin/{0} and push the current branch?\r\n\r\nThis runs: git push -u origin {0}"),
                        branchName),
                    AppConstants.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (result != DialogResult.Yes)
                {
                    return;
                }

                await RunGitOperationAsync(cancellationToken => _gitService.PushSetUpstreamAsync(_workspacePath, branchName, cancellationToken));
                return;
            }

            var pushConfirm = MessageBox.Show(
                this,
                BuildPushConfirmText(_currentStatus),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                _currentStatus.AheadCount.GetValueOrDefault() == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (pushConfirm != DialogResult.Yes)
            {
                RestoreFileListFocus();
                return;
            }

            await RunGitOperationAsync(cancellationToken => _gitService.PushAsync(_workspacePath, cancellationToken));
        }

        private async Task FetchAsync()
        {
            if (_isBusy)
            {
                return;
            }

            if (_currentStatus == null || !_currentStatus.IsGitRepository)
            {
                return;
            }

            await RunGitOperationAsync(cancellationToken => _gitService.FetchAsync(_workspacePath, cancellationToken));
        }

        private async Task PullAsync()
        {
            if (_isBusy)
            {
                return;
            }

            if (_currentStatus == null ||
                !_currentStatus.IsGitRepository ||
                _currentStatus.HasConflicts ||
                IsDetachedHead(_currentStatus) ||
                string.IsNullOrWhiteSpace(_currentStatus.Upstream))
            {
                return;
            }

            var confirm = MessageBox.Show(
                this,
                BuildPullConfirmText(_currentStatus),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                _currentStatus.Files.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                RestoreFileListFocus();
                return;
            }

            await RunGitOperationAsync(cancellationToken => _gitService.PullAsync(_workspacePath, cancellationToken));
        }

        private async Task ContinueSequencerAsync()
        {
            if (_isBusy || _currentStatus == null || !HasPendingGitOperation(_currentStatus) || _currentStatus.HasConflicts)
            {
                return;
            }

            await RunGitOperationAsync(() => _gitService.ContinueSequencerAsync(_workspacePath));
        }

        private async Task AbortSequencerAsync()
        {
            if (_isBusy || _currentStatus == null || !HasPendingGitOperation(_currentStatus))
            {
                return;
            }

            var confirm = MessageBox.Show(
                this,
                UiLocalization.Text("GitWorkbenchForm.Confirm.AbortSequencer", "Abort the current Git operation?\r\n\r\nThis stops the operation and restores Git to the state before it started."),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                RestoreFileListFocus();
                return;
            }

            await RunGitOperationAsync(() => _gitService.AbortSequencerAsync(_workspacePath));
        }

        private async Task SkipSequencerAsync()
        {
            if (_isBusy || _currentStatus == null || !CanSkipPendingGitOperation(_currentStatus))
            {
                return;
            }

            var confirm = MessageBox.Show(
                this,
                UiLocalization.Text("GitWorkbenchForm.Confirm.SkipSequencer", "Skip the current Git operation step?\r\n\r\nGit will omit the current step and continue the operation when skip is supported."),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                RestoreFileListFocus();
                return;
            }

            await RunGitOperationAsync(() => _gitService.SkipSequencerAsync(_workspacePath));
        }

        private string BuildCommitConfirmText(string subject, string body)
        {
            var summary = BuildStagedFilesSummaryText(_currentStatus);
            return string.Format(
                UiLocalization.Text(
                    "GitWorkbenchForm.Confirm.Commit.Template",
                    "Commit staged changes?\r\n\r\nSubject:\r\n{0}\r\n\r\nDetails:\r\n{1}\r\n\r\nStaged files:\r\n{2}\r\n\r\nThis runs: git commit -m <subject> [-m <details>]"),
                subject,
                FormatOptionalCommitBody(body),
                summary);
        }

        private async Task<GitCommitHistoryEntry> ReadLastCommitForAmendAsync()
        {
            _isBusy = true;
            try
            {
                RefreshActionButtons();
                SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.LoadingLastCommit", "Loading last commit..."));
                var history = await _gitService.ReadCommitHistoryAsync(_workspacePath, 1).ConfigureAwait(true);
                return history == null ? null : history.FirstOrDefault();
            }
            finally
            {
                _isBusy = false;
                RefreshActionButtons();
            }
        }

        private async Task<GitCommitHistoryEntry> ReadLastCommitAsync()
        {
            try
            {
                return await ReadLastCommitForAmendAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SetOperationLog(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private string BuildAmendConfirmText(GitCommitHistoryEntry lastCommit, string subject, string body)
        {
            var commitText = lastCommit == null
                ? UiLocalization.Text("GitWorkbenchForm.Value.UnknownCommit", "(Unknown commit)")
                : string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Value.CommitSummary.Template", "{0} {1}"),
                    TextValueHelper.FirstNonEmptyTrimmed(lastCommit.ShortSha, lastCommit.FullSha),
                    lastCommit.Subject);
            var messageMode = string.IsNullOrWhiteSpace(subject) && string.IsNullOrWhiteSpace(body)
                ? UiLocalization.Text("GitWorkbenchForm.Value.AmendReuseMessage", "Reuse previous commit message")
                : string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Value.AmendReplaceMessage.Template", "Replace message with subject: {0}"),
                    subject);
            var publishedWarning = _currentStatus != null &&
                                   !string.IsNullOrWhiteSpace(_currentStatus.Upstream) &&
                                   _currentStatus.AheadCount.GetValueOrDefault() == 0
                ? UiLocalization.Text("GitWorkbenchForm.Value.AmendPublishedWarning", "The branch does not appear to be ahead of upstream. If the previous commit is already pushed, you may need force-with-lease afterward.")
                : UiLocalization.Text("GitWorkbenchForm.Value.NoAdditionalWarning", "No additional warning.");
            return string.Format(
                UiLocalization.Text(
                    "GitWorkbenchForm.Confirm.Amend.Template",
                    "Amend the previous commit?\r\n\r\nPrevious commit:\r\n{0}\r\n\r\nMessage mode:\r\n{1}\r\n\r\nDetails:\r\n{2}\r\n\r\nStaged files:\r\n{3}\r\n\r\n{4}\r\n\r\nThis runs: git commit --amend"),
                commitText,
                messageMode,
                FormatOptionalCommitBody(body),
                BuildStagedFilesSummaryText(_currentStatus),
                publishedWarning);
        }

        private string BuildPushConfirmText(GitRepositoryStatusSnapshot status)
        {
            status = status ?? new GitRepositoryStatusSnapshot();
            var branch = TextValueHelper.FirstNonEmptyTrimmed(status.Branch, UiLocalization.Text("GitWorkbenchForm.Value.UnknownBranch", "(Unknown branch)"));
            var upstream = TextValueHelper.FirstNonEmptyTrimmed(status.Upstream, UiLocalization.Text("GitWorkbenchForm.Value.NoUpstream", "(No upstream)"));
            var noAheadText = status.AheadCount.GetValueOrDefault() == 0
                ? UiLocalization.Text("GitWorkbenchForm.Value.PushNoAhead", "No local commits are currently detected ahead of upstream.")
                : string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Value.PushAhead.Template", "Local branch is ahead by {0} commit(s)."),
                    status.AheadCount.GetValueOrDefault());
            return string.Format(
                UiLocalization.Text(
                    "GitWorkbenchForm.Confirm.Push.Template",
                    "Push current branch?\r\n\r\nLocal branch: {0}\r\nRemote target: {1}\r\nAhead: {2}, behind: {3}\r\n\r\n{4}\r\n\r\nThis runs: git push"),
                branch,
                upstream,
                status.AheadCount.GetValueOrDefault(),
                status.BehindCount.GetValueOrDefault(),
                noAheadText);
        }

        private string BuildPullConfirmText(GitRepositoryStatusSnapshot status)
        {
            status = status ?? new GitRepositoryStatusSnapshot();
            var branch = TextValueHelper.FirstNonEmptyTrimmed(status.Branch, UiLocalization.Text("GitWorkbenchForm.Value.UnknownBranch", "(Unknown branch)"));
            var upstream = TextValueHelper.FirstNonEmptyTrimmed(status.Upstream, UiLocalization.Text("GitWorkbenchForm.Value.NoUpstream", "(No upstream)"));
            var localChangesText = status.Files.Count > 0
                ? string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Value.PullLocalChanges.Template", "Local changes: {0}. Pull may fail or create conflicts."),
                    status.Files.Count)
                : UiLocalization.Text("GitWorkbenchForm.Value.PullClean", "Local working tree has no detected changes.");
            var behindText = status.BehindCount.GetValueOrDefault() == 0
                ? UiLocalization.Text("GitWorkbenchForm.Value.PullNoBehind", "No remote commits are currently detected behind upstream.")
                : string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Value.PullBehind.Template", "Remote branch is ahead by {0} commit(s)."),
                    status.BehindCount.GetValueOrDefault());
            return string.Format(
                UiLocalization.Text(
                    "GitWorkbenchForm.Confirm.Pull.Template",
                    "Pull remote updates?\r\n\r\nLocal branch: {0}\r\nRemote source: {1}\r\nAhead: {2}, behind: {3}\r\n\r\n{4}\r\n{5}\r\n\r\nThis runs: git pull"),
                branch,
                upstream,
                status.AheadCount.GetValueOrDefault(),
                status.BehindCount.GetValueOrDefault(),
                localChangesText,
                behindText);
        }

        private string BuildStagedFilesSummaryText(GitRepositoryStatusSnapshot status)
        {
            var stagedFiles = status == null || status.Files == null
                ? new GitChangedFileEntry[0]
                : status.Files.Where(item => item.IsStaged).ToArray();
            if (stagedFiles.Length == 0)
            {
                return UiLocalization.Text("GitWorkbenchForm.Value.NoStagedFiles", "(No staged files)");
            }

            var maxItems = 12;
            var lines = stagedFiles
                .Take(maxItems)
                .Select(item => string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Value.StagedFileSummary.Template", "- {0}    {1}    {2}"),
                    GetDisplayFilePath(item),
                    GetChangeKindText(item.ChangeKind),
                    BuildLineSummaryText(item)))
                .ToList();
            if (stagedFiles.Length > maxItems)
            {
                lines.Add(string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Value.MoreFiles.Template", "... and {0} more file(s)"),
                    stagedFiles.Length - maxItems));
            }

            return string.Join(Environment.NewLine, lines.ToArray());
        }

        private string GetCommitSubject()
        {
            return (_commitSubjectTextBox.Text ?? string.Empty).Trim();
        }

        private string GetCommitBody()
        {
            return (_commitBodyTextBox.Text ?? string.Empty).Trim();
        }

        private void ClearCommitInputs()
        {
            _commitSubjectTextBox.Clear();
            _commitBodyTextBox.Clear();
        }

        private static string FormatOptionalCommitBody(string body)
        {
            return string.IsNullOrWhiteSpace(body)
                ? UiLocalization.Text("GitWorkbenchForm.Value.NoCommitBody", "(No details)")
                : body.Trim();
        }

        private void OpenBranchesAndCommits()
        {
            if (_isBusy || _currentStatus == null || !_currentStatus.IsGitRepository)
            {
                return;
            }

            var form = new GitCommitHistoryForm(_workspacePath, _gitService, _screenReaderService);
            form.RepositoryChanged += async (sender, e) =>
            {
                if (!IsDisposed && !Disposing)
                {
                    await LoadStatusAsync(false, false);
                }
            };
            form.Show(this);
        }

        private void OpenStashManagement()
        {
            if (_isBusy || _currentStatus == null || !_currentStatus.IsGitRepository)
            {
                return;
            }

            var form = new GitStashManagementForm(_workspacePath, _gitService, _screenReaderService);
            form.RepositoryChanged += async (sender, e) =>
            {
                if (!IsDisposed && !Disposing)
                {
                    await LoadStatusAsync(false, false);
                }
            };
            form.FormClosed += async (sender, e) =>
            {
                if (!IsDisposed && !Disposing)
                {
                    await LoadStatusAsync(false, true);
                }
            };
            form.Show(this);
        }

        private void OpenRepositoryDiagnostics()
        {
            if (_isBusy || _currentStatus == null || !_currentStatus.IsGitRepository)
            {
                return;
            }

            var form = new GitRepositoryDiagnosticsForm(_workspacePath, _gitService);
            form.Show(this);
        }

        private void OpenConflictDetails()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || !entry.IsConflicted)
            {
                return;
            }

            var form = new GitConflictDetailsForm(_workspacePath, _gitService, entry);
            form.Show(this);
        }

        private Task<bool> RunGitOperationAsync(Func<Task<GitOperationResult>> operation, bool speakSuccess = true)
        {
            return operation == null
                ? Task.FromResult(false)
                : RunGitOperationAsync(_ => operation(), speakSuccess);
        }

        private async Task<bool> RunGitOperationAsync(Func<CancellationToken, Task<GitOperationResult>> operation, bool speakSuccess = true)
        {
            if (_isBusy || operation == null)
            {
                return false;
            }

            _isBusy = true;
            var cancellationSource = BeginOperationCancellation();
            var success = false;
            var canceled = false;
            string operationLog = null;
            string speechText = null;
            try
            {
                RefreshActionButtons();
                SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.RunningOperation", "Running Git operation..."));
                if (_pendingFileListFocusRequest == null)
                {
                    RestoreFileListFocus();
                }
                var result = await operation(cancellationSource.Token).ConfigureAwait(true);
                success = result != null && result.Success;
                speechText = result != null && result.Success ? result.DisplayMessage : null;
                operationLog = BuildOperationLog(result);
                SetOperationLog(operationLog);
                if (result != null && !result.Success)
                {
                    MessageBox.Show(
                        this,
                        result.DisplayMessage,
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                operationLog = string.Empty;
                SetOperationLog(operationLog);
            }
            catch (Exception ex)
            {
                operationLog = ex.Message;
                SetOperationLog(operationLog);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                ClearOperationCancellation(cancellationSource);
                _isBusy = false;
                if (!canceled && !IsDisposed)
                {
                    await LoadStatusAsync(true, true);
                }

                if (!string.IsNullOrWhiteSpace(operationLog))
                {
                    SetOperationLog(operationLog);
                    if (success && speakSuccess)
                    {
                        SpeakOperationStatus(speechText);
                    }
                }

                RestoreFileListFocus();
            }

            return success;
        }

        private static string BuildOperationLog(GitOperationResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            var output = TextValueHelper.FirstNonEmptyTrimmed(result.StandardOutput, result.StandardError);
            return string.IsNullOrWhiteSpace(output)
                ? result.DisplayMessage
                : result.DisplayMessage + Environment.NewLine + output;
        }

        private void OpenSelectedFile()
        {
            if (WorkspacePathUtilities.LooksLikeUnixAbsolutePath(_workspacePath))
            {
                SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.TextOpenUnsupportedRemote", "Opening files from this workspace path is not supported by the selected opener."));
                RestoreFileListFocus();
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
            {
                return;
            }

            var path = ResolveAbsoluteFilePath(entry.FilePath);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("GitWorkbenchForm.Message.FileNotFound", "The selected file is not available on disk."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                RestoreFileListFocus();
                return;
            }

            var openedInternally = false;
            try
            {
                var result = _textFileOpenService.Open(
                    _textFileOpenPreferenceProvider(),
                    new TextFileOpenRequest
                    {
                        FilePath = path,
                        IsLocalWindowsFile = !WorkspacePathUtilities.LooksLikeUnixAbsolutePath(_workspacePath)
                    });
                openedInternally = result.OpenedInternally;

                if (!result.Handled)
                {
                    ShowOpenFileFailed(path, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                ShowOpenFileFailed(path, ex.Message);
            }
            finally
            {
                if (!openedInternally)
                {
                    RestoreFileListFocus();
                }
            }
        }

        private void OpenLocalTextFileInternally(TextFileOpenRequest request)
        {
            var path = request == null ? null : request.FilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(UiLocalization.Text("GitWorkbenchForm.Message.FileNotFound", "The selected file is not available on disk."));
            }

            var form = new AppServerFilePreviewForm(
                path,
                () => Task.FromResult(File.ReadAllText(path, Encoding.UTF8)),
                data =>
                {
                    File.WriteAllBytes(path, data ?? new byte[0]);
                    return Task.CompletedTask;
                });
            form.Shown += async (sender, e) =>
            {
                await form.LoadAsync().ConfigureAwait(true);
            };
            form.Show(this);
            form.Activate();
        }

        private void ShowOpenFileFailed(string path, string errorMessage)
        {
            MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Message.FileOpenFailed.Template", "Could not open the selected file.\r\n\r\n{0}\r\n\r\n{1}"),
                    path,
                    errorMessage),
                AppConstants.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }

        private async Task LoadSelectedDiffAsync()
        {
            await LoadSelectedDiffAsync(true, true);
        }

        private async Task LoadSelectedDiffAsync(bool restoreFileListFocus, bool showErrorDialog)
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null)
            {
                return;
            }

            GitDiffKind diffKind;
            if (!TryResolveSelectedDiffKind(entry, out diffKind))
            {
                SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Value.DiffKindUnavailable", "The selected diff range is not available for this file."));
                UpdateDiffText();
                return;
            }

            _isBusy = true;
            try
            {
                RefreshActionButtons();
                SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.LoadingDiff", "Loading selected diff..."));
                if (restoreFileListFocus)
                {
                    RestoreFileListFocus();
                }
                var result = await _gitDiffReader.ReadDiffAsync(_workspacePath, entry, diffKind).ConfigureAwait(true);
                if (result == null || !result.Success)
                {
                    var message = result == null
                        ? UiLocalization.Text("GitWorkbenchForm.Value.NoDiff", "No diff available for the selected file.")
                        : result.DisplayMessage;
                    SetOperationLog(message);
                    if (showErrorDialog)
                    {
                        MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return;
                }

                SetDiffText(entry, diffKind, result.StandardOutput ?? string.Empty);
                GitRepositoryService.PopulateDiffLineCounts(entry);
                UpdateSelectedListViewLineStats(entry);
                UpdateDiffText();
                SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.DiffLoaded", "Selected diff loaded."));
            }
            catch (Exception ex)
            {
                SetOperationLog(ex.Message);
                if (showErrorDialog)
                {
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                _isBusy = false;
                RefreshActionButtons();
                if (restoreFileListFocus)
                {
                    RestoreFileListFocus();
                }
            }
        }

        private async Task CopySelectedDiffAsync()
        {
            var entry = GetSelectedEntry();
            if (entry == null)
            {
                return;
            }

            GitDiffKind diffKind;
            if (!TryResolveSelectedDiffKind(entry, out diffKind))
            {
                SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Value.DiffKindUnavailable", "The selected diff range is not available for this file."));
                RestoreFileListFocus();
                return;
            }

            if (string.IsNullOrWhiteSpace(GetDiffText(entry, diffKind)))
            {
                await LoadSelectedDiffAsync();
                entry = GetSelectedEntry();
                if (entry == null ||
                    !TryResolveSelectedDiffKind(entry, out diffKind) ||
                    string.IsNullOrWhiteSpace(GetDiffText(entry, diffKind)))
                {
                    return;
                }
            }

            var diffText = GetDiffText(entry, diffKind);
            Clipboard.SetText(string.IsNullOrWhiteSpace(diffText)
                ? UiLocalization.Text("GitWorkbenchForm.Value.NoDiff", "No diff available for the selected file.")
                : diffText);
            SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.CopiedDiff", "Copied selected diff."));
            RestoreFileListFocus();
        }

        private void CopySelectedPath()
        {
            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
            {
                return;
            }

            Clipboard.SetText(entry.FilePath);
            SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.CopiedPath", "Copied selected path."));
            RestoreFileListFocus();
        }

        private void CopyRepositorySummary()
        {
            var summary = _summaryLabel.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            Clipboard.SetText(summary);
            SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.CopiedSummary", "Copied repository summary."));
            RestoreFileListFocus();
        }

        private void CopyOperationLog()
        {
            var operationLog = _operationLogTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(operationLog))
            {
                return;
            }

            Clipboard.SetText(operationLog);
            SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.CopiedOperationLog", "Copied last Git output."));
            RestoreFileListFocus();
        }

        private async Task SwitchSelectedWorktreeAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedWorktreeEntry();
            if (!CanSwitchToWorktree(entry))
            {
                RestoreWorktreeTreeFocus();
                return;
            }

            GitWorktreeSwitchMode switchMode;
            using (var form = new GitWorktreeSwitchForm(entry.Path, GetWorktreeBranchText(entry)))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreWorktreeTreeFocus();
                    return;
                }

                switchMode = form.Mode;
            }

            if (switchMode == GitWorktreeSwitchMode.SwitchOnly && !CanSwitchToWorktree(entry))
            {
                RestoreWorktreeTreeFocus();
                return;
            }

            _isBusy = true;
            try
            {
                RefreshActionButtons();
                SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.SwitchingWorktree", "Switching workspace..."));
                if (_openWorkspaceAsync != null)
                {
                    await _openWorkspaceAsync(entry.Path, switchMode).ConfigureAwait(true);
                }

                _workspacePath = entry.Path;
                _initialBranchLabel = entry.Branch;
                OnWorkspacePathChanged();
                ClearCommitInputs();
                _isBusy = false;
                await LoadStatusAsync(false, false).ConfigureAwait(true);
                RestoreWorktreeTreeFocus();
            }
            catch (Exception ex)
            {
                SetOperationLog(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                RestoreWorktreeTreeFocus();
            }
            finally
            {
                _isBusy = false;
                RefreshActionButtons();
            }
        }

        private async Task CreateWorktreeFromTreeAsync()
        {
            if (_isBusy)
            {
                return;
            }

            using (var form = new GitWorktreeCreateForm(_workspacePath))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreWorktreeTreeFocus();
                    return;
                }

                await RunGitOperationAsync(() => _gitService.CreateWorktreeAsync(_workspacePath, form.TargetPath, form.BranchName, form.StartPoint));
                await RefreshWorktreeTreeAsync(true).ConfigureAwait(true);
            }
        }

        private async Task IntegrateSelectedWorktreeBranchAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedWorktreeEntry();
            var validationMessage = ValidateWorktreeBranchIntegration(_currentStatus, entry);
            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                SetOperationLog(validationMessage);
                MessageBox.Show(this, validationMessage, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                RestoreWorktreeTreeFocus();
                return;
            }

            GitBranchComparison comparison;
            try
            {
                SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.ComparingWorktreeBranch", "Comparing worktree branch with current branch..."));
                comparison = await _gitService.CompareBranchesAsync(_workspacePath, "HEAD", entry.Branch).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                SetOperationLog(string.Empty);
                RestoreWorktreeTreeFocus();
                return;
            }
            catch (Exception ex)
            {
                SetOperationLog(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                RestoreWorktreeTreeFocus();
                return;
            }

            using (var form = new GitBranchIntegrateForm(_currentStatus.Branch, entry.Branch, comparison))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreWorktreeTreeFocus();
                    return;
                }

                await RunGitOperationAsync(async () =>
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
                            return await _gitService.MergeBranchIntoCurrentAsync(_workspacePath, entry.Branch, true).ConfigureAwait(true);
                        case GitBranchIntegrationMode.Rebase:
                            return await _gitService.RebaseCurrentBranchOntoAsync(_workspacePath, entry.Branch).ConfigureAwait(true);
                        default:
                            return await _gitService.MergeBranchIntoCurrentAsync(_workspacePath, entry.Branch, false).ConfigureAwait(true);
                    }
                }).ConfigureAwait(true);
            }

            RestoreWorktreeTreeFocus();
        }

        private async Task RemoveSelectedWorktreeFromTreeAsync()
        {
            var entry = GetSelectedWorktreeEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.Path) || IsCurrentWorkspaceEntry(entry))
            {
                RestoreWorktreeTreeFocus();
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text("GitWorkbenchForm.Confirm.RemoveWorktree.Template", "Remove this worktree?\r\n\r\n{0}\r\n\r\nGit will refuse if the worktree has local changes unless force is used from command line."),
                    entry.Path),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreWorktreeTreeFocus();
                return;
            }

            await RunGitOperationAsync(() => _gitService.RemoveWorktreeAsync(_workspacePath, entry.Path, false));
            await RefreshWorktreeTreeAsync(true).ConfigureAwait(true);
        }

        private async Task PruneWorktreesFromTreeAsync()
        {
            var result = MessageBox.Show(
                this,
                UiLocalization.Text("GitWorkbenchForm.Confirm.PruneWorktrees", "Prune stale worktree records?\r\n\r\nThis only removes Git metadata for missing worktree directories."),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreWorktreeTreeFocus();
                return;
            }

            await RunGitOperationAsync(() => _gitService.PruneWorktreesAsync(_workspacePath));
            await RefreshWorktreeTreeAsync(true).ConfigureAwait(true);
        }

        private void CopySelectedWorktreePath()
        {
            var entry = GetSelectedWorktreeEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.Path))
            {
                return;
            }

            Clipboard.SetText(entry.Path);
            SetOperationLog(UiLocalization.Text("GitWorkbenchForm.Status.CopiedWorktreePath", "Copied worktree path."));
            RestoreWorktreeTreeFocus();
        }

        private GitWorktreeEntry GetSelectedWorktreeEntry()
        {
            return _worktreeTreeView.SelectedNode == null
                ? null
                : _worktreeTreeView.SelectedNode.Tag as GitWorktreeEntry;
        }

        private void RefreshWorktreeContextMenu()
        {
            var entry = GetSelectedWorktreeEntry();
            var hasEntry = entry != null;
            var canUseRepository = _currentStatus != null &&
                                   _currentStatus.IsGitRepository &&
                                   string.IsNullOrWhiteSpace(_currentStatus.StatusErrorMessage) &&
                                   !_isBusy;
            _switchWorktreeMenuItem.Enabled = canUseRepository && CanSwitchToWorktree(entry);
            _integrateWorktreeBranchMenuItem.Enabled = canUseRepository && CanIntegrateWorktreeBranch(entry, _currentStatus);
            _createWorktreeMenuItem.Enabled = canUseRepository;
            _removeWorktreeMenuItem.Enabled = canUseRepository && hasEntry && !IsCurrentWorkspaceEntry(entry);
            _pruneWorktreesMenuItem.Enabled = canUseRepository;
            _copyWorktreePathMenuItem.Enabled = hasEntry;
            _refreshWorktreesMenuItem.Enabled = !_isBusy;
        }

        private bool CanSwitchToWorktree(GitWorktreeEntry entry)
        {
            return _openWorkspaceAsync != null &&
                   entry != null &&
                   !string.IsNullOrWhiteSpace(entry.Path) &&
                   !IsCurrentWorkspaceEntry(entry);
        }

        private bool CanIntegrateWorktreeBranch(GitWorktreeEntry entry, GitRepositoryStatusSnapshot status)
        {
            return string.IsNullOrWhiteSpace(ValidateWorktreeBranchIntegration(status, entry));
        }

        private string ValidateWorktreeBranchIntegration(GitRepositoryStatusSnapshot status, GitWorktreeEntry entry)
        {
            if (status == null || !status.IsGitRepository || !string.IsNullOrWhiteSpace(status.StatusErrorMessage))
            {
                return UiLocalization.Text("GitWorkbenchForm.Message.IntegrationNotRepository", "Current workspace is not a usable Git repository.");
            }

            if (entry == null || string.IsNullOrWhiteSpace(entry.Branch) || entry.IsDetached)
            {
                return UiLocalization.Text("GitWorkbenchForm.Message.IntegrationSelectWorktreeBranch", "Select a worktree with an available branch first.");
            }

            if (IsCurrentWorkspaceEntry(entry) || string.Equals(entry.Branch, status.Branch, StringComparison.OrdinalIgnoreCase))
            {
                return UiLocalization.Text("GitWorkbenchForm.Message.IntegrationSelectAnotherBranch", "Select a different worktree branch first.");
            }

            if (status.HasConflicts)
            {
                return UiLocalization.Text("GitWorkbenchForm.Message.IntegrationBlockedByConflicts", "Resolve conflicts before integrating branches.");
            }

            if (HasPendingGitOperation(status))
            {
                return UiLocalization.Text("GitWorkbenchForm.Message.IntegrationBlockedByOperation", "Finish or abort the current Git operation before integrating branches.");
            }

            if (IsDetachedHead(status))
            {
                return UiLocalization.Text("GitWorkbenchForm.Message.IntegrationBlockedByDetachedHead", "Branch integration is unavailable in detached HEAD.");
            }

            var fileCount = status.Files == null ? 0 : status.Files.Count;
            return fileCount > 0
                ? string.Format(
                    UiLocalization.Text(
                        "GitWorkbenchForm.Message.IntegrationRequiresCleanTree.Template",
                        "Current branch has {0} uncommitted change(s). Commit, stash, or discard them before integrating branches."),
                    fileCount)
                : null;
        }

        private bool IsCurrentWorkspaceEntry(GitWorktreeEntry entry)
        {
            return entry != null && string.Equals(
                NormalizePathForComparison(entry.Path),
                NormalizePathForComparison(_workspacePath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePathForComparison(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var text = path.Trim();
            try
            {
                if (WorkspacePathUtilities.LooksLikeUnixAbsolutePath(text))
                {
                    return WorkspacePathUtilities.NormalizeUnixAbsolutePath(text).TrimEnd('/');
                }

                text = Path.GetFullPath(text);
            }
            catch
            {
            }

            return text
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
        }

        private string ResolveAbsoluteFilePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }

            return Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.GetFullPath(Path.Combine(_workspacePath, relativePath));
        }

        private GitChangedFileEntry GetSelectedEntry()
        {
            return _fileListView.SelectedItems.Count == 0
                ? null
                : _fileListView.SelectedItems[0].Tag as GitChangedFileEntry;
        }

        private void RefreshActionButtons()
        {
            var status = _currentStatus;
            var isRepository = status != null && status.IsGitRepository;
            var hasStatusError = status != null && !string.IsNullOrWhiteSpace(status.StatusErrorMessage);
            var canUseRepository = isRepository && !hasStatusError;
            var entry = GetSelectedEntry();
            var hasStaged = canUseRepository && status.Files.Any(item => item.IsStaged);
            var hasPendingOperation = HasPendingGitOperation(status);
            var canCommit = hasStaged &&
                            !_isBusy &&
                            !status.HasConflicts &&
                            !hasPendingOperation &&
                            !string.IsNullOrWhiteSpace(_commitSubjectTextBox.Text);
            var canAmend = hasStaged &&
                           !_isBusy &&
                           !status.HasConflicts &&
                           !hasPendingOperation &&
                           !IsDetachedHead(status) &&
                           !string.IsNullOrWhiteSpace(status.HeadSha);
            var canPushExistingUpstream = canUseRepository &&
                                          !_isBusy &&
                                          !status.HasConflicts &&
                                          !hasPendingOperation &&
                                          !IsDetachedHead(status) &&
                                          !string.IsNullOrWhiteSpace(status.Upstream);
            var canPushSetUpstream = canUseRepository &&
                                     !_isBusy &&
                                     !status.HasConflicts &&
                                     !hasPendingOperation &&
                                     !IsDetachedHead(status) &&
                                     string.IsNullOrWhiteSpace(status.Upstream) &&
                                     !string.IsNullOrWhiteSpace(status.OriginUrl) &&
                                     !string.IsNullOrWhiteSpace(NormalizeCurrentBranchName(status.Branch));
            var canPull = canUseRepository &&
                          !_isBusy &&
                          !status.HasConflicts &&
                          !hasPendingOperation &&
                          !IsDetachedHead(status) &&
                          !string.IsNullOrWhiteSpace(status.Upstream);
            var canUseSequencer = canUseRepository && !_isBusy && hasPendingOperation;

            _fileListView.Enabled = true;
            _diffTextBox.Enabled = true;
            _commitSubjectTextBox.Enabled = canUseRepository;
            _commitBodyTextBox.Enabled = canUseRepository;
            _initializeButton.Enabled = status != null && !status.IsGitRepository && status.CanInitializeRepository;
            _refreshButton.Enabled = true;
            _fetchButton.Enabled = canUseRepository;
            _pullButton.Enabled = canPull;
            _continueSequencerButton.Enabled = canUseSequencer && !status.HasConflicts;
            _abortSequencerButton.Enabled = canUseSequencer;
            _skipSequencerButton.Enabled = canUseSequencer && CanSkipPendingGitOperation(status);
            _commitButton.Enabled = canCommit;
            _amendButton.Enabled = canAmend;
            _pushButton.Enabled = canPushExistingUpstream || canPushSetUpstream;
            _publishButton.Enabled = CanConfigureRemote(status);
            _remotesButton.Enabled = canUseRepository && !_isBusy;
            _branchesAndCommitsButton.Enabled = canUseRepository && !_isBusy;
            _stashButton.Enabled = canUseRepository && !_isBusy;
            _diagnosticsButton.Enabled = canUseRepository && !_isBusy;
            _pushButton.Text = canPushSetUpstream
                ? UiLocalization.Text("GitWorkbenchForm.Button.PushSetUpstream", "Set Upstream and Push(&P)")
                : UiLocalization.Text("GitWorkbenchForm.Button.Push", "Push(&P)");
            _pushButton.AccessibleName = canPushSetUpstream
                ? UiLocalization.Text("GitWorkbenchForm.Accessible.PushSetUpstream", "Set upstream and push")
                : UiLocalization.Text("GitWorkbenchForm.Accessible.Push", "Push");
            RefreshWorktreeContextMenu();
        }

        private bool CanConfigureRemote(GitRepositoryStatusSnapshot status)
        {
            return CanCreateGitHubRemote(status) || CanBindExistingRemote(status) || CanReviewExistingOrigin(status);
        }

        private bool CanCreateGitHubRemote(GitRepositoryStatusSnapshot status)
        {
            if (_isBusy || string.IsNullOrWhiteSpace(_workspacePath))
            {
                return false;
            }

            return CanBindRemoteToStatus(status);
        }

        private bool CanBindExistingRemote(GitRepositoryStatusSnapshot status)
        {
            if (_isBusy || string.IsNullOrWhiteSpace(_workspacePath))
            {
                return false;
            }

            return CanBindRemoteToStatus(status);
        }

        private static bool CanBindRemoteToStatus(GitRepositoryStatusSnapshot status)
        {
            if (status == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(status.StatusErrorMessage))
            {
                return false;
            }

            if (!status.IsGitRepository)
            {
                return status.CanInitializeRepository;
            }

            return string.IsNullOrWhiteSpace(status.OriginUrl);
        }

        private bool CanReviewExistingOrigin(GitRepositoryStatusSnapshot status)
        {
            return !_isBusy &&
                   status != null &&
                   status.IsGitRepository &&
                   string.IsNullOrWhiteSpace(status.StatusErrorMessage) &&
                   !string.IsNullOrWhiteSpace(status.OriginUrl);
        }

        private async Task<bool> TryOpenRemoteManagementWhenOriginExistsAsync()
        {
            IReadOnlyList<GitRemoteEntry> remotes;
            try
            {
                remotes = await _gitService.ReadRemotesAsync(_workspacePath).ConfigureAwait(true);
            }
            catch
            {
                remotes = new GitRemoteEntry[0];
            }

            var hasOrigin = remotes.Any(item =>
                item != null &&
                string.Equals(item.Name, "origin", StringComparison.OrdinalIgnoreCase));
            if (!hasOrigin && (_currentStatus == null || string.IsNullOrWhiteSpace(_currentStatus.OriginUrl)))
            {
                return false;
            }

            var confirm = MessageBox.Show(
                this,
                UiLocalization.Text("GitWorkbenchForm.Message.OriginExistsOpenRemotes", "The origin remote already exists. Open remote management to review or edit it?"),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information,
                MessageBoxDefaultButton.Button1);
            if (confirm == DialogResult.Yes)
            {
                await OpenRemoteManagementAsync().ConfigureAwait(true);
            }
            else
            {
                RestoreFileListFocus();
            }

            return true;
        }

        private async Task OpenRemoteManagementAsync()
        {
            if (_isBusy || _currentStatus == null || !_currentStatus.IsGitRepository || !string.IsNullOrWhiteSpace(_currentStatus.StatusErrorMessage))
            {
                return;
            }

            using (var form = new GitRemoteManagementForm(_workspacePath, _gitService))
            {
                form.ShowDialog(this);
                if (form.Changed)
                {
                    await LoadStatusAsync(false, true).ConfigureAwait(true);
                    return;
                }
            }

            RestoreFileListFocus();
        }

        private static string GetDefaultRepositoryName(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return string.Empty;
            }

            try
            {
                var name = new DirectoryInfo(workspacePath).Name;
                return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void RefreshContextMenu()
        {
            var status = _currentStatus;
            var entry = GetSelectedEntry();
            var canRunGitOperation = !_isBusy;
            var canUseRepository = status != null && status.IsGitRepository && string.IsNullOrWhiteSpace(status.StatusErrorMessage);
            _stageMenuItem.Enabled = canRunGitOperation && entry != null && entry.CanStage;
            _stageAllMenuItem.Enabled = canRunGitOperation && canUseRepository && status.Files.Any(item => item.CanStage);
            _unstageMenuItem.Enabled = canRunGitOperation && entry != null && entry.CanUnstage;
            _unstageAllMenuItem.Enabled = canRunGitOperation && canUseRepository && status.Files.Any(item => item.IsStaged);
            _discardMenuItem.Enabled = canRunGitOperation && entry != null && entry.CanDiscard;
            _deleteUntrackedMenuItem.Enabled = canRunGitOperation && entry != null && entry.IsUntracked;
            _stashSelectedMenuItem.Enabled = canRunGitOperation &&
                                             canUseRepository &&
                                             entry != null &&
                                             !entry.IsConflicted &&
                                             !status.HasConflicts &&
                                             !HasPendingGitOperation(status);
            _viewConflictMenuItem.Enabled = canRunGitOperation && entry != null && entry.IsConflicted;
            _markConflictResolvedMenuItem.Enabled = canRunGitOperation && entry != null && entry.IsConflicted;
            _loadDiffMenuItem.Enabled = canRunGitOperation && entry != null;
            _openFileMenuItem.Enabled = entry != null &&
                                        !entry.IsDeletedChange() &&
                                        !WorkspacePathUtilities.LooksLikeUnixAbsolutePath(_workspacePath) &&
                                        File.Exists(ResolveAbsoluteFilePath(entry.FilePath));
            _copyDiffMenuItem.Enabled = entry != null;
            _copyPathMenuItem.Enabled = entry != null;
            _copySummaryMenuItem.Enabled = !string.IsNullOrWhiteSpace(_summaryLabel.Text);
            _copyOperationLogMenuItem.Enabled = !string.IsNullOrWhiteSpace(_operationLogTextBox.Text);
        }

        private static bool HasPendingGitOperation(GitRepositoryStatusSnapshot status)
        {
            return status != null && (status.HasSequencerOperation || status.HasMergeOrRebaseOperation);
        }

        private static bool CanSkipPendingGitOperation(GitRepositoryStatusSnapshot status)
        {
            return status != null && status.HasSequencerOperation;
        }

        private void UpdateSelectedListViewLineStats(GitChangedFileEntry entry)
        {
            if (entry == null || _fileListView.SelectedItems.Count == 0)
            {
                return;
            }

            var item = _fileListView.SelectedItems[0];
            if (!ReferenceEquals(item.Tag, entry) || item.SubItems.Count < 5)
            {
                return;
            }

            item.SubItems[3].Text = BuildAddedLineText(entry);
            item.SubItems[4].Text = BuildDeletedLineText(entry);
        }

        private void FileListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            _fileListView.Focus();
            var hit = _fileListView.HitTest(e.Location);
            if (hit.Item != null)
            {
                hit.Item.Selected = true;
                hit.Item.Focused = true;
            }
        }

        private async void FileListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                OpenSelectedFile();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Space)
            {
                await ToggleSelectedStagingAsync().ConfigureAwait(true);
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            {
                ShowFileContextMenuForKeyboard();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void WorktreeTreeView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            _worktreeTreeView.Focus();
            var node = _worktreeTreeView.GetNodeAt(e.Location);
            if (node != null)
            {
                _worktreeTreeView.SelectedNode = node;
                RefreshWorktreeContextMenu();
            }
        }

        private async void WorktreeTreeView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                await SwitchSelectedWorktreeAsync();
                return;
            }

            if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            {
                ShowWorktreeContextMenuForKeyboard();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void ShowFileContextMenuForKeyboard()
        {
            if (_fileListView.SelectedItems.Count == 0)
            {
                RestoreFileSelection();
                _fileContextMenu.Show(_fileListView, new Point(8, 8));
                return;
            }

            var item = _fileListView.SelectedItems[0];
            _fileContextMenu.Show(_fileListView, new Point(Math.Max(8, item.Bounds.Left), item.Bounds.Bottom));
        }

        private void ShowWorktreeContextMenuForKeyboard()
        {
            if (_worktreeTreeView.SelectedNode == null && _worktreeTreeView.Nodes.Count > 0)
            {
                _worktreeTreeView.SelectedNode = _worktreeTreeView.Nodes[0];
            }

            var node = _worktreeTreeView.SelectedNode;
            var point = node == null
                ? new Point(8, 8)
                : new Point(Math.Max(8, node.Bounds.Left), node.Bounds.Bottom);
            _worktreeContextMenu.Show(_worktreeTreeView, point);
        }

        private void SetOperationLog(string text)
        {
            TextBoxCaretHelper.SetTextAndMoveToStart(_operationLogTextBox, text ?? string.Empty);
        }

        private void SpeakOperationStatus(string text)
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

        private void RestoreFileListFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                RestoreFileSelection();
                Activate();
                if (_fileListView.Enabled && _fileListView.CanFocus)
                {
                    _fileListView.Focus();
                    return;
                }

                if (_initializeButton.Enabled && _initializeButton.CanFocus)
                {
                    _initializeButton.Focus();
                    return;
                }

                if (_refreshButton.Enabled && _refreshButton.CanFocus)
                {
                    _refreshButton.Focus();
                }
            });
        }

        private void RestoreWorktreeTreeFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                Activate();
                if (_worktreeTreeView.CanFocus)
                {
                    _worktreeTreeView.Focus();
                }
            });
        }

        private void OnWorkspacePathChanged()
        {
            var handler = WorkspacePathChanged;
            handler?.Invoke(this, EventArgs.Empty);
        }

        private void NotifyRepositoryMetadataChangedIfNeeded(GitRepositoryStatusSnapshot status)
        {
            var isGitRepository = status != null && status.IsGitRepository;
            var branch = isGitRepository ? status.Branch : null;
            if (_hasReportedRepositoryMetadata &&
                _reportedIsGitRepository == isGitRepository &&
                string.Equals(_reportedBranch, branch, StringComparison.Ordinal))
            {
                return;
            }

            _hasReportedRepositoryMetadata = true;
            _reportedIsGitRepository = isGitRepository;
            _reportedBranch = branch;
            var handler = RepositoryMetadataChanged;
            handler?.Invoke(this, EventArgs.Empty);
        }

        private void RestoreFileSelection()
        {
            if (_fileListView.Items.Count == 0)
            {
                _pendingFileListFocusRequest = null;
                return;
            }

            if (TryRestorePendingFileSelection())
            {
                return;
            }

            if (_fileListView.SelectedItems.Count == 0)
            {
                _fileListView.Items[0].Selected = true;
            }

            var selectedItem = _fileListView.SelectedItems.Count > 0
                ? _fileListView.SelectedItems[0]
                : _fileListView.Items[0];
            selectedItem.Focused = true;
            selectedItem.EnsureVisible();
        }

        private void QueueFileListFocusAfterOperation(GitChangedFileEntry entry)
        {
            var selectedIndex = _fileListView.SelectedItems.Count == 0
                ? 0
                : _fileListView.SelectedItems[0].Index;
            _pendingFileListFocusRequest = new FileListFocusRequest
            {
                StartIndex = Math.Max(0, selectedIndex),
                OriginalFilePath = entry == null ? null : entry.FilePath
            };
        }

        private bool TryRestorePendingFileSelection()
        {
            var request = _pendingFileListFocusRequest;
            if (request == null)
            {
                return false;
            }

            _pendingFileListFocusRequest = null;
            if (!string.IsNullOrWhiteSpace(request.OriginalFilePath) &&
                TrySelectFileListItem(0, entry => string.Equals(entry.FilePath, request.OriginalFilePath, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var fallbackIndex = Math.Max(0, Math.Min(request.StartIndex, _fileListView.Items.Count - 1));
            SelectFileListItemAt(fallbackIndex);
            return true;
        }

        private bool TrySelectFileListItem(int startIndex, Predicate<GitChangedFileEntry> predicate)
        {
            if (predicate == null || _fileListView.Items.Count == 0)
            {
                return false;
            }

            var boundedStartIndex = Math.Max(0, Math.Min(startIndex, _fileListView.Items.Count - 1));
            for (var i = boundedStartIndex; i < _fileListView.Items.Count; i++)
            {
                if (TrySelectFileListItemAt(i, predicate))
                {
                    return true;
                }
            }

            for (var i = 0; i < boundedStartIndex; i++)
            {
                if (TrySelectFileListItemAt(i, predicate))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TrySelectFileListItemAt(int index, Predicate<GitChangedFileEntry> predicate)
        {
            if (index < 0 || index >= _fileListView.Items.Count)
            {
                return false;
            }

            var item = _fileListView.Items[index];
            var entry = item.Tag as GitChangedFileEntry;
            if (!predicate(entry))
            {
                return false;
            }

            foreach (ListViewItem selectedItem in _fileListView.SelectedItems)
            {
                selectedItem.Selected = false;
            }

            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
            return true;
        }

        private void SelectFileListItemAt(int index)
        {
            if (index < 0 || index >= _fileListView.Items.Count)
            {
                return;
            }

            foreach (ListViewItem selectedItem in _fileListView.SelectedItems)
            {
                selectedItem.Selected = false;
            }

            var item = _fileListView.Items[index];
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
        }

        private void BuildLayout()
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
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var heading = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitWorkbenchForm.Heading", "Git Workbench"),
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 0
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 158F));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 172F));
            topPanel.Controls.Add(_summaryLabel, 0, 0);
            topPanel.Controls.Add(_fileFilterComboBox, 1, 0);
            topPanel.Controls.Add(_diffKindComboBox, 2, 0);

            _contentSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel1,
                TabStop = false,
                TabIndex = 1
            };

            _contentSplitContainer.Panel1.TabStop = false;
            _contentSplitContainer.Panel2.TabStop = false;
            _leftSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 190,
                SplitterWidth = 6,
                FixedPanel = FixedPanel.Panel1,
                TabStop = false
            };
            _leftSplitContainer.Panel1.TabStop = false;
            _leftSplitContainer.Panel2.TabStop = false;
            _leftSplitContainer.Panel1.Controls.Add(_worktreeTreeView);
            _leftSplitContainer.Panel2.Controls.Add(_fileListView);
            _contentSplitContainer.Panel1.Controls.Add(_leftSplitContainer);
            _contentSplitContainer.Panel2.Controls.Add(_diffTextBox);

            var commitPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 2
            };
            commitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            commitPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300F));
            commitPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            commitPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            commitPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            commitPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var subjectLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitWorkbenchForm.Label.CommitSubject", "Commit subject"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(subjectLabel, true);
            var bodyLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitWorkbenchForm.Label.CommitBody", "Details"),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(bodyLabel, true);
            commitPanel.Controls.Add(subjectLabel, 0, 0);
            commitPanel.Controls.Add(_operationLogTextBox, 1, 0);
            commitPanel.SetRowSpan(_operationLogTextBox, 4);
            commitPanel.Controls.Add(_commitSubjectTextBox, 0, 1);
            commitPanel.Controls.Add(bodyLabel, 0, 2);
            commitPanel.Controls.Add(_commitBodyTextBox, 0, 3);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 3
            };
            footer.Controls.Add(_commitButton);
            footer.Controls.Add(_amendButton);
            footer.Controls.Add(_pushButton);
            footer.Controls.Add(_publishButton);
            footer.Controls.Add(_remotesButton);
            footer.Controls.Add(_fetchButton);
            footer.Controls.Add(_pullButton);
            footer.Controls.Add(_continueSequencerButton);
            footer.Controls.Add(_abortSequencerButton);
            footer.Controls.Add(_skipSequencerButton);
            footer.Controls.Add(_branchesAndCommitsButton);
            footer.Controls.Add(_stashButton);
            footer.Controls.Add(_diagnosticsButton);
            footer.Controls.Add(_refreshButton);
            footer.Controls.Add(_initializeButton);
            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(topPanel, 0, 1);
            root.Controls.Add(_contentSplitContainer, 0, 2);
            root.Controls.Add(commitPanel, 0, 3);
            root.Controls.Add(footer, 0, 4);
            Controls.Add(root);
        }

        private void EnsureInitialSplitterDistance()
        {
            if (_splitterInitialized || _contentSplitContainer == null)
            {
                return;
            }

            var availableWidth = _contentSplitContainer.ClientSize.Width;
            if (availableWidth <= 0)
            {
                return;
            }

            var maxDistance = availableWidth - UiTheme.Scale(DiffPanelMinWidth);
            if (maxDistance < UiTheme.Scale(FilesPanelMinWidth))
            {
                return;
            }

            var preferredDistance = Math.Max(UiTheme.Scale(FilesPanelMinWidth), Math.Min(UiTheme.Scale(430), maxDistance));
            _contentSplitContainer.Panel1MinSize = 0;
            _contentSplitContainer.Panel2MinSize = 0;
            _contentSplitContainer.SplitterDistance = preferredDistance;
            _contentSplitContainer.Panel1MinSize = UiTheme.Scale(FilesPanelMinWidth);
            _contentSplitContainer.Panel2MinSize = UiTheme.Scale(DiffPanelMinWidth);
            _splitterInitialized = true;
        }

        private static ListView CreateFileListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                AccessibleName = UiLocalization.Text("GitWorkbenchForm.Accessible.Files", "Files"),
                TabIndex = 1
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(UiLocalization.Text("GitWorkbenchForm.Column.Group", "Group"), 95);
            listView.Columns.Add(UiLocalization.Text("GitWorkbenchForm.Column.Path", "Path"), 250);
            listView.Columns.Add(UiLocalization.Text("GitWorkbenchForm.Column.Type", "Type"), 100);
            listView.Columns.Add(UiLocalization.Text("GitWorkbenchForm.Column.AddedLines", "Added"), 70);
            listView.Columns.Add(UiLocalization.Text("GitWorkbenchForm.Column.DeletedLines", "Deleted"), 70);
            return listView;
        }

        private static TreeView CreateWorktreeTreeView()
        {
            var treeView = new TreeView
            {
                AccessibleName = UiLocalization.Text("GitWorkbenchForm.Accessible.WorktreeTree", "Worktrees"),
                Dock = DockStyle.Fill,
                HideSelection = false,
                LabelEdit = false,
                PathSeparator = " / ",
                TabIndex = 0
            };
            treeView.Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular);
            treeView.BackColor = UiTheme.WindowBackColor;
            treeView.ForeColor = UiTheme.TextColor;
            return treeView;
        }

        private static ComboBox CreateComboBox(string accessibleName, int tabIndex)
        {
            var comboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                DropDownStyle = ComboBoxStyle.DropDownList,
                TabIndex = tabIndex,
                Margin = new Padding(8, 2, 0, 2)
            };
            UiTheme.ApplyComboBox(comboBox);
            return comboBox;
        }

        private void PopulateFileFilterComboBox()
        {
            _fileFilterComboBox.Items.Add(new ComboBoxOption(
                UiLocalization.Text("GitWorkbenchForm.Filter.All", "All"),
                (int)GitWorkbenchFileFilter.All));
            _fileFilterComboBox.Items.Add(new ComboBoxOption(
                UiLocalization.Text("GitWorkbenchForm.Filter.Staged", "Staged"),
                (int)GitWorkbenchFileFilter.Staged));
            _fileFilterComboBox.Items.Add(new ComboBoxOption(
                UiLocalization.Text("GitWorkbenchForm.Filter.Unstaged", "Unstaged"),
                (int)GitWorkbenchFileFilter.Unstaged));
            _fileFilterComboBox.Items.Add(new ComboBoxOption(
                UiLocalization.Text("GitWorkbenchForm.Filter.Untracked", "Untracked"),
                (int)GitWorkbenchFileFilter.Untracked));
            _fileFilterComboBox.Items.Add(new ComboBoxOption(
                UiLocalization.Text("GitWorkbenchForm.Filter.Conflicts", "Conflicts"),
                (int)GitWorkbenchFileFilter.Conflicts));
            _fileFilterComboBox.SelectedIndex = 0;
        }

        private void PopulateDiffKindComboBox()
        {
            _diffKindComboBox.Items.Add(new ComboBoxOption(
                UiLocalization.Text("GitWorkbenchForm.DiffKind.Auto", "Auto"),
                (int)GitDiffKind.Auto));
            _diffKindComboBox.Items.Add(new ComboBoxOption(
                UiLocalization.Text("GitWorkbenchForm.DiffKind.Staged", "Staged"),
                (int)GitDiffKind.Staged));
            _diffKindComboBox.Items.Add(new ComboBoxOption(
                UiLocalization.Text("GitWorkbenchForm.DiffKind.Unstaged", "Unstaged"),
                (int)GitDiffKind.Unstaged));
            _diffKindComboBox.SelectedIndex = 0;
        }

        private System.Collections.Generic.IEnumerable<GitChangedFileEntry> GetFilteredFiles(GitRepositoryStatusSnapshot status)
        {
            if (status == null || status.Files == null)
            {
                return new GitChangedFileEntry[0];
            }

            var filter = GetSelectedFileFilter();
            return status.Files.Where(entry => MatchesFileFilter(entry, filter));
        }

        private static bool MatchesFileFilter(GitChangedFileEntry entry, GitWorkbenchFileFilter filter)
        {
            if (entry == null)
            {
                return false;
            }

            switch (filter)
            {
                case GitWorkbenchFileFilter.Staged:
                    return entry.IsStaged;
                case GitWorkbenchFileFilter.Unstaged:
                    return entry.IsUnstaged;
                case GitWorkbenchFileFilter.Untracked:
                    return entry.IsUntracked;
                case GitWorkbenchFileFilter.Conflicts:
                    return entry.IsConflicted;
                case GitWorkbenchFileFilter.All:
                default:
                    return true;
            }
        }

        private GitWorkbenchFileFilter GetSelectedFileFilter()
        {
            var option = _fileFilterComboBox.SelectedItem as ComboBoxOption;
            return option == null
                ? GitWorkbenchFileFilter.All
                : (GitWorkbenchFileFilter)option.Value;
        }

        private GitDiffKind GetSelectedDiffKind()
        {
            var option = _diffKindComboBox.SelectedItem as ComboBoxOption;
            return option == null
                ? GitDiffKind.Auto
                : (GitDiffKind)option.Value;
        }

        private bool TryResolveSelectedDiffKind(GitChangedFileEntry entry, out GitDiffKind diffKind)
        {
            diffKind = GitDiffKind.Unstaged;
            if (entry == null)
            {
                return false;
            }

            var selectedKind = GetSelectedDiffKind();
            if (selectedKind == GitDiffKind.Auto)
            {
                diffKind = entry.IsStaged && !entry.IsConflicted
                    ? GitDiffKind.Staged
                    : GitDiffKind.Unstaged;
                return true;
            }

            if (selectedKind == GitDiffKind.Staged)
            {
                diffKind = GitDiffKind.Staged;
                return entry.HasStagedChanges || entry.IsStaged;
            }

            diffKind = GitDiffKind.Unstaged;
            return entry.HasUnstagedChanges || entry.IsUnstaged || entry.IsUntracked || entry.IsConflicted;
        }

        private static string GetDiffText(GitChangedFileEntry entry, GitDiffKind diffKind)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            switch (diffKind)
            {
                case GitDiffKind.Staged:
                    return entry.StagedDiffText;
                case GitDiffKind.Unstaged:
                    return entry.UnstagedDiffText;
                case GitDiffKind.Auto:
                default:
                    return entry.DiffText;
            }
        }

        private static void SetDiffText(GitChangedFileEntry entry, GitDiffKind diffKind, string diffText)
        {
            if (entry == null)
            {
                return;
            }

            entry.DiffText = diffText ?? string.Empty;
            if (diffKind == GitDiffKind.Staged)
            {
                entry.StagedDiffText = entry.DiffText;
            }
            else if (diffKind == GitDiffKind.Unstaged)
            {
                entry.UnstagedDiffText = entry.DiffText;
            }
        }

        private static TextBox CreateTextBox(string accessibleName, bool codeFont, bool readOnly, int tabIndex)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                Multiline = true,
                ReadOnly = readOnly,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, codeFont, readOnly);
            return textBox;
        }

        private static TextBox CreateSingleLineTextBox(string accessibleName, int tabIndex)
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName,
                Multiline = false,
                ReadOnly = false,
                TabIndex = tabIndex
            };
            UiTheme.ApplyTextBox(textBox, false, false);
            return textBox;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 118,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }

        private static string GetStatusGroupText(GitChangedFileEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            if (entry.IsConflicted)
            {
                return UiLocalization.Text("GitWorkbenchForm.Group.Conflict", "Conflict");
            }

            if (entry.IsStaged)
            {
                return UiLocalization.Text("GitWorkbenchForm.Group.Staged", "Staged");
            }

            if (entry.IsUnstaged)
            {
                return UiLocalization.Text("GitWorkbenchForm.Group.Unstaged", "Unstaged");
            }

            if (entry.IsUntracked)
            {
                return UiLocalization.Text("GitWorkbenchForm.Group.Untracked", "Untracked");
            }

            return UiLocalization.Text("GitWorkbenchForm.Group.Other", "Other");
        }

        private static string GetChangeKindText(string changeKind)
        {
            switch ((changeKind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "added":
                    return UiLocalization.Text("GitWorkbenchForm.ChangeKind.Added", "Added");
                case "deleted":
                    return UiLocalization.Text("GitWorkbenchForm.ChangeKind.Deleted", "Deleted");
                case "untracked":
                    return UiLocalization.Text("GitWorkbenchForm.ChangeKind.Untracked", "Untracked");
                case "type_changed":
                    return UiLocalization.Text("GitWorkbenchForm.ChangeKind.TypeChanged", "Type changed");
                case "unmerged":
                    return UiLocalization.Text("GitWorkbenchForm.ChangeKind.Unmerged", "Unmerged");
                case "renamed":
                    return UiLocalization.Text("GitWorkbenchForm.ChangeKind.Renamed", "Renamed");
                default:
                    return UiLocalization.Text("GitWorkbenchForm.ChangeKind.Modified", "Modified");
            }
        }

        private static string GetDisplayFilePath(GitChangedFileEntry entry)
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

        private static string BuildLineSummaryText(GitChangedFileEntry entry)
        {
            if (entry == null || !entry.HasLineStats)
            {
                return UiLocalization.Text("GitWorkbenchForm.Value.NoLineStats", "No line stats");
            }

            return string.Format(
                UiLocalization.Text("GitWorkbenchForm.Value.LineStats.Template", "+{0} / -{1}"),
                entry.AddedLineCount,
                entry.DeletedLineCount);
        }

        private static string BuildAddedLineText(GitChangedFileEntry entry)
        {
            return entry == null || !entry.HasLineStats
                ? "-"
                : entry.AddedLineCount.ToString();
        }

        private static string BuildDeletedLineText(GitChangedFileEntry entry)
        {
            return entry == null || !entry.HasLineStats
                ? "-"
                : entry.DeletedLineCount.ToString();
        }

        private enum GitWorkbenchFileFilter
        {
            All = 0,
            Staged = 1,
            Unstaged = 2,
            Untracked = 3,
            Conflicts = 4
        }

        private sealed class FileListFocusRequest
        {
            public int StartIndex { get; set; }

            public string OriginalFilePath { get; set; }
        }

        private sealed class ComboBoxOption
        {
            public ComboBoxOption(string text, int value)
            {
                Text = text ?? string.Empty;
                Value = value;
            }

            public string Text { get; private set; }

            public int Value { get; private set; }

            public override string ToString()
            {
                return Text;
            }
        }
    }

    internal static class GitChangedFileEntryExtensions
    {
        public static bool IsDeletedChange(this GitChangedFileEntry entry)
        {
            return entry != null && string.Equals(entry.ChangeKind, "deleted", StringComparison.OrdinalIgnoreCase);
        }
    }
}
