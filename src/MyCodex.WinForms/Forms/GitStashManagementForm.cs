using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GitStashManagementForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _isLoading;

        private readonly string _workspacePath;
        private readonly IGitStashService _gitService;
        private readonly IScreenReaderService _screenReaderService;
        private readonly Label _statusLabel;
        private readonly ListView _stashListView;
        private readonly ListView _fileListView;
        private readonly TextBox _diffTextBox;
        private readonly Button _saveButton;
        private readonly Button _loadDetailsButton;
        private readonly Button _branchButton;
        private readonly Button _applyButton;
        private readonly Button _popButton;
        private readonly Button _dropButton;
        private readonly Button _refreshButton;
        private readonly Button _closeButton;
        private readonly ContextMenu _stashContextMenu;
        private readonly MenuItem _loadDetailsMenuItem;
        private readonly MenuItem _branchMenuItem;
        private readonly MenuItem _applyMenuItem;
        private readonly MenuItem _popMenuItem;
        private readonly MenuItem _dropMenuItem;
        private readonly ContextMenu _fileContextMenu;
        private readonly MenuItem _loadFileDiffMenuItem;
        private readonly MenuItem _copyDiffMenuItem;

        private bool _isLoading;
        private string _loadedStashRef;

        public GitStashManagementForm(string workspacePath, IGitStashService gitService, IScreenReaderService screenReaderService)
        {
            _workspacePath = workspacePath;
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            _screenReaderService = screenReaderService;

            Text = UiLocalization.Text("GitStashManagementForm.Title", "Stashes");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 460);
            Size = new Size(860, 540);
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

            _stashListView = CreateStashListView();
            _fileListView = CreateStashFileListView();
            _diffTextBox = CreateDiffTextBox();
            _stashContextMenu = new ContextMenu();
            _loadDetailsMenuItem = new MenuItem(UiLocalization.Text("GitStashManagementForm.Menu.LoadDetails", "Load Details(&L)"));
            _branchMenuItem = new MenuItem(UiLocalization.Text("GitStashManagementForm.Menu.CreateBranch", "Create Branch(&B)"));
            _applyMenuItem = new MenuItem(UiLocalization.Text("GitStashManagementForm.Menu.Apply", "Apply(&A)"));
            _popMenuItem = new MenuItem(UiLocalization.Text("GitStashManagementForm.Menu.Pop", "Pop(&P)"));
            _dropMenuItem = new MenuItem(UiLocalization.Text("GitStashManagementForm.Menu.Drop", "Drop(&D)"));
            _stashContextMenu.MenuItems.Add(_loadDetailsMenuItem);
            _stashContextMenu.MenuItems.Add(_branchMenuItem);
            _stashContextMenu.MenuItems.Add("-");
            _stashContextMenu.MenuItems.Add(_applyMenuItem);
            _stashContextMenu.MenuItems.Add(_popMenuItem);
            _stashContextMenu.MenuItems.Add("-");
            _stashContextMenu.MenuItems.Add(_dropMenuItem);
            _stashListView.ContextMenu = _stashContextMenu;
            _fileContextMenu = new ContextMenu();
            _loadFileDiffMenuItem = new MenuItem(UiLocalization.Text("GitStashManagementForm.Menu.LoadFileDiff", "Load File Diff(&F)"));
            _copyDiffMenuItem = new MenuItem(UiLocalization.Text("GitStashManagementForm.Menu.CopyDiff", "Copy Diff(&C)"));
            _fileContextMenu.MenuItems.Add(_loadFileDiffMenuItem);
            _fileContextMenu.MenuItems.Add(_copyDiffMenuItem);
            _fileListView.ContextMenu = _fileContextMenu;

            _saveButton = UiControlFactory.CreateActionButton(UiLocalization.Text("GitStashManagementForm.Button.Save", "Save(&S)"), UiLocalization.Text("GitStashManagementForm.Accessible.Save", "Save"), 1, 118);
            _loadDetailsButton = UiControlFactory.CreateActionButton(UiLocalization.Text("GitStashManagementForm.Button.LoadDetails", "Load Details(&L)"), UiLocalization.Text("GitStashManagementForm.Accessible.LoadDetails", "Load details"), 2, 118);
            _branchButton = UiControlFactory.CreateActionButton(UiLocalization.Text("GitStashManagementForm.Button.CreateBranch", "Create Branch(&B)"), UiLocalization.Text("GitStashManagementForm.Accessible.CreateBranch", "Create branch"), 3, 118);
            _applyButton = UiControlFactory.CreateActionButton(UiLocalization.Text("GitStashManagementForm.Button.Apply", "Apply(&A)"), UiLocalization.Text("GitStashManagementForm.Accessible.Apply", "Apply"), 4, 118);
            _popButton = UiControlFactory.CreateActionButton(UiLocalization.Text("GitStashManagementForm.Button.Pop", "Pop(&P)"), UiLocalization.Text("GitStashManagementForm.Accessible.Pop", "Pop"), 5, 118);
            _dropButton = UiControlFactory.CreateActionButton(UiLocalization.Text("GitStashManagementForm.Button.Drop", "Drop(&D)"), UiLocalization.Text("GitStashManagementForm.Accessible.Drop", "Drop"), 6, 118);
            _refreshButton = UiControlFactory.CreateActionButton(UiLocalization.Text("GitStashManagementForm.Button.Refresh", "Refresh(&R)"), UiLocalization.Text("GitStashManagementForm.Accessible.Refresh", "Refresh"), 7, 118);
            _closeButton = UiControlFactory.CreateActionButton(UiLocalization.Text("GitStashManagementForm.Button.Close", "Close(&E)"), UiLocalization.Text("GitStashManagementForm.Accessible.Close", "Close"), 8, 118);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_saveButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_loadDetailsButton, UiGlyph.Info);
            UiTheme.ApplyButton(_branchButton, UiGlyph.NewSession);
            UiTheme.ApplyButton(_applyButton, UiGlyph.Download);
            UiTheme.ApplyButton(_popButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_dropButton, UiGlyph.Error);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _saveButton.Click += async (sender, e) => await SaveStashAsync();
            _loadDetailsButton.Click += async (sender, e) => await LoadSelectedStashDetailsAsync();
            _branchButton.Click += async (sender, e) => await CreateBranchFromSelectedStashAsync();
            _applyButton.Click += async (sender, e) => await ApplySelectedStashAsync();
            _popButton.Click += async (sender, e) => await PopSelectedStashAsync();
            _dropButton.Click += async (sender, e) => await DropSelectedStashAsync();
            _refreshButton.Click += async (sender, e) => await LoadStashesAsync();
            _closeButton.Click += (sender, e) => Close();
            _loadDetailsMenuItem.Click += async (sender, e) => await LoadSelectedStashDetailsAsync();
            _branchMenuItem.Click += async (sender, e) => await CreateBranchFromSelectedStashAsync();
            _applyMenuItem.Click += async (sender, e) => await ApplySelectedStashAsync();
            _popMenuItem.Click += async (sender, e) => await PopSelectedStashAsync();
            _dropMenuItem.Click += async (sender, e) => await DropSelectedStashAsync();
            _stashContextMenu.Popup += (sender, e) => RefreshActionButtons();
            _loadFileDiffMenuItem.Click += async (sender, e) => await LoadSelectedStashFileDiffAsync();
            _copyDiffMenuItem.Click += (sender, e) => CopyCurrentDiff();
            _fileContextMenu.Popup += (sender, e) => RefreshActionButtons();
            _stashListView.SelectedIndexChanged += (sender, e) =>
            {
                ClearStashDetails();
                RefreshActionButtons();
            };
            _stashListView.ItemActivate += async (sender, e) => await LoadSelectedStashDetailsAsync();
            _stashListView.MouseDown += StashListView_MouseDown;
            _stashListView.KeyDown += StashListView_KeyDown;
            _fileListView.ItemActivate += async (sender, e) => await LoadSelectedStashFileDiffAsync();
            _fileListView.MouseDown += StashFileListView_MouseDown;
            _fileListView.KeyDown += StashFileListView_KeyDown;
            KeyDown += GitStashManagementForm_KeyDown;
            Shown += async (sender, e) =>
            {
                Activate();
                await LoadStashesAsync();
            };

            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(UiLocalization.Text("GitStashManagementForm.Status.Preparing", "Preparing stashes..."));
            RefreshActionButtons();
        }

        public event EventHandler RepositoryChanged;

        private async void GitStashManagementForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isLoading)
            {
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                await LoadStashesAsync();
            }
        }

        private async Task LoadStashesAsync()
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            try
            {
                RefreshActionButtons();
                SetStatus(UiLocalization.Text("GitStashManagementForm.Status.Loading", "Loading stashes..."));
                var entries = await _gitService.ReadStashesAsync(_workspacePath).ConfigureAwait(true)
                    ?? new GitStashEntry[0];
                BindStashes(entries);
                SetStatus(entries.Count == 0
                    ? UiLocalization.Text("GitStashManagementForm.Status.Empty", "No stashes were found.")
                    : string.Format(UiLocalization.Text("GitStashManagementForm.Status.Loaded.Template", "Loaded {0} stash(es)."), entries.Count));
            }
            catch (Exception ex)
            {
                BindStashes(new GitStashEntry[0]);
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                RefreshActionButtons();
                RestoreFocus();
            }
        }

        private async Task SaveStashAsync()
        {
            if (_isLoading)
            {
                return;
            }

            using (var form = new GitStashCreateForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreFocus();
                    return;
                }

                await RunStashOperationAsync(() => _gitService.StashPushAsync(_workspacePath, form.StashMessage, form.IncludeUntracked));
            }
        }

        private async Task ApplySelectedStashAsync()
        {
            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.RefName))
            {
                return;
            }

            if (!ConfirmSelectedOperation(
                    "GitStashManagementForm.Confirm.Apply.Template",
                    "Apply this stash?\r\n\r\n{0}\r\n{1}\r\n\r\nThe stash will remain in the list."))
            {
                return;
            }

            await RunStashOperationAsync(() => _gitService.StashApplyAsync(_workspacePath, entry.RefName));
        }

        private async Task PopSelectedStashAsync()
        {
            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.RefName))
            {
                return;
            }

            if (!ConfirmSelectedOperation(
                    "GitStashManagementForm.Confirm.Pop.Template",
                    "Pop this stash?\r\n\r\n{0}\r\n{1}\r\n\r\nIf Git applies it successfully, the stash will be removed from the list."))
            {
                return;
            }

            await RunStashOperationAsync(() => _gitService.StashPopAsync(_workspacePath, entry.RefName));
        }

        private async Task DropSelectedStashAsync()
        {
            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.RefName))
            {
                return;
            }

            if (!ConfirmSelectedOperation(
                    "GitStashManagementForm.Confirm.Drop.Template",
                    "Drop this stash?\r\n\r\n{0}\r\n{1}\r\n\r\nThis cannot be undone."))
            {
                return;
            }

            await RunStashOperationAsync(() => _gitService.StashDropAsync(_workspacePath, entry.RefName));
        }

        private async Task CreateBranchFromSelectedStashAsync()
        {
            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.RefName))
            {
                return;
            }

            using (var form = new GitStashBranchCreateForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreFocus();
                    return;
                }

                await RunStashOperationAsync(() => _gitService.StashBranchAsync(_workspacePath, entry.RefName, form.BranchName));
            }
        }

        private async Task LoadSelectedStashDetailsAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var entry = GetSelectedEntry();
            if (entry == null || string.IsNullOrWhiteSpace(entry.RefName))
            {
                return;
            }

            _isLoading = true;
            try
            {
                RefreshActionButtons();
                SetStatus(UiLocalization.Text("GitStashManagementForm.Status.LoadingDetails", "Loading stash details..."));
                ClearStashDetails();
                _loadedStashRef = entry.RefName;
                var files = await _gitService.ReadStashFilesAsync(_workspacePath, entry.RefName).ConfigureAwait(true)
                    ?? new GitCommitFileEntry[0];
                var diff = await _gitService.ReadStashDiffAsync(_workspacePath, entry.RefName).ConfigureAwait(true);
                BindStashFiles(files);
                TextBoxCaretHelper.SetTextAndMoveToStart(
                    _diffTextBox,
                    diff == null || !diff.Success
                        ? (diff == null ? string.Empty : diff.DisplayMessage)
                        : string.IsNullOrWhiteSpace(diff.StandardOutput)
                            ? UiLocalization.Text("GitStashManagementForm.Value.NoDiff", "No diff is available for the selected stash.")
                            : diff.StandardOutput);
                SetStatus(string.Format(
                    UiLocalization.Text("GitStashManagementForm.Status.DetailsLoaded.Template", "Loaded {0} changed file(s)."),
                    files.Count));
                RestoreDetailsFocus();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                RestoreFocus();
            }
            finally
            {
                _isLoading = false;
                RefreshActionButtons();
            }
        }

        private async Task LoadSelectedStashFileDiffAsync()
        {
            if (_isLoading)
            {
                return;
            }

            var fileEntry = GetSelectedStashFileEntry();
            if (fileEntry == null || string.IsNullOrWhiteSpace(fileEntry.FilePath) || string.IsNullOrWhiteSpace(_loadedStashRef))
            {
                return;
            }

            _isLoading = true;
            try
            {
                RefreshActionButtons();
                SetStatus(UiLocalization.Text("GitStashManagementForm.Status.LoadingFileDiff", "Loading stash file diff..."));
                var diff = await _gitService.ReadStashFileDiffAsync(_workspacePath, _loadedStashRef, fileEntry.FilePath).ConfigureAwait(true);
                TextBoxCaretHelper.SetTextAndMoveToStart(
                    _diffTextBox,
                    diff == null || !diff.Success
                        ? (diff == null ? string.Empty : diff.DisplayMessage)
                        : string.IsNullOrWhiteSpace(diff.StandardOutput)
                            ? UiLocalization.Text("GitStashManagementForm.Value.NoFileDiff", "No diff is available for the selected file.")
                            : diff.StandardOutput);
                SetStatus(UiLocalization.Text("GitStashManagementForm.Status.FileDiffLoaded", "Loaded stash file diff."));
                RestoreDiffFocus();
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                RestoreDetailsFocus();
            }
            finally
            {
                _isLoading = false;
                RefreshActionButtons();
            }
        }

        private void CopyCurrentDiff()
        {
            var text = _diffTextBox.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus(UiLocalization.Text("GitStashManagementForm.Status.NoDiffToCopy", "No diff to copy."));
                return;
            }

            Clipboard.SetText(text);
            SetStatus(UiLocalization.Text("GitStashManagementForm.Status.CopiedDiff", "Copied diff."));
        }

        private bool ConfirmSelectedOperation(string resourceKey, string fallback)
        {
            var entry = GetSelectedEntry();
            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text(resourceKey, fallback),
                    entry.RefName,
                    GetSubjectText(entry)),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                RestoreFocus();
                return false;
            }

            return true;
        }

        private async Task RunStashOperationAsync(Func<Task<GitOperationResult>> operation)
        {
            if (_isLoading || operation == null)
            {
                return;
            }

            _isLoading = true;
            string statusText = null;
            try
            {
                RefreshActionButtons();
                SetStatus(UiLocalization.Text("GitStashManagementForm.Status.Running", "Running stash operation..."));
                var result = await operation().ConfigureAwait(true);
                statusText = result == null ? string.Empty : result.DisplayMessage;
                SetStatus(statusText);
                if (result == null || !result.Success)
                {
                    MessageBox.Show(this, statusText, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                SpeakStatus(statusText);
                OnRepositoryChanged();
                _isLoading = false;
                await LoadStashesAsync().ConfigureAwait(true);
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
                RefreshActionButtons();
                RestoreFocus();
            }
        }

        private void BindStashes(IReadOnlyList<GitStashEntry> entries)
        {
            _stashListView.BeginUpdate();
            try
            {
                _stashListView.Items.Clear();
                foreach (var entry in entries ?? new GitStashEntry[0])
                {
                    var item = new ListViewItem(GetSubjectText(entry));
                    item.SubItems.Add(entry.RefName ?? string.Empty);
                    item.Tag = entry;
                    _stashListView.Items.Add(item);
                }
            }
            finally
            {
                _stashListView.EndUpdate();
            }

            if (_stashListView.Items.Count > 0)
            {
                _stashListView.Items[0].Selected = true;
                _stashListView.Items[0].Focused = true;
                _stashListView.EnsureVisible(0);
            }

            ClearStashDetails();
        }

        private void BindStashFiles(IReadOnlyList<GitCommitFileEntry> entries)
        {
            _fileListView.BeginUpdate();
            try
            {
                _fileListView.Items.Clear();
                foreach (var entry in entries ?? new GitCommitFileEntry[0])
                {
                    var item = new ListViewItem(GetStashFilePathText(entry));
                    item.SubItems.Add(GetStashFileChangeKindText(entry == null ? null : entry.ChangeKind));
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
        }

        private void ClearStashDetails()
        {
            _loadedStashRef = null;
            _fileListView.Items.Clear();
            TextBoxCaretHelper.SetTextAndMoveToStart(
                _diffTextBox,
                UiLocalization.Text("GitStashManagementForm.Value.LoadDetailsHint", "Press Enter or use Load Details to inspect the selected stash."));
        }

        private void RefreshActionButtons()
        {
            var hasEntry = GetSelectedEntry() != null;
            _saveButton.Enabled = !_isLoading;
            _refreshButton.Enabled = !_isLoading;
            _loadDetailsButton.Enabled = !_isLoading && hasEntry;
            _loadDetailsMenuItem.Enabled = !_isLoading && hasEntry;
            _branchButton.Enabled = !_isLoading && hasEntry;
            _branchMenuItem.Enabled = !_isLoading && hasEntry;
            _applyButton.Enabled = !_isLoading && hasEntry;
            _popButton.Enabled = !_isLoading && hasEntry;
            _dropButton.Enabled = !_isLoading && hasEntry;
            _applyMenuItem.Enabled = !_isLoading && hasEntry;
            _popMenuItem.Enabled = !_isLoading && hasEntry;
            _dropMenuItem.Enabled = !_isLoading && hasEntry;
            var hasFile = GetSelectedStashFileEntry() != null;
            _loadFileDiffMenuItem.Enabled = !_isLoading && hasFile && !string.IsNullOrWhiteSpace(_loadedStashRef);
            _copyDiffMenuItem.Enabled = !_isLoading && !string.IsNullOrWhiteSpace(_loadedStashRef) && !string.IsNullOrWhiteSpace(_diffTextBox.Text);
        }

        private void StashListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            _stashListView.Focus();
            var hit = _stashListView.HitTest(e.Location);
            if (hit.Item != null)
            {
                hit.Item.Selected = true;
                hit.Item.Focused = true;
            }
        }

        private void StashListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                BeginInvoke(new Action(async () => await LoadSelectedStashDetailsAsync()));
                return;
            }

            if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            {
                ShowStashContextMenuForKeyboard();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void StashFileListView_MouseDown(object sender, MouseEventArgs e)
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

        private void StashFileListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                BeginInvoke(new Action(async () => await LoadSelectedStashFileDiffAsync()));
                return;
            }

            if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            {
                ShowStashFileContextMenuForKeyboard();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void ShowStashFileContextMenuForKeyboard()
        {
            if (_fileListView.SelectedItems.Count == 0)
            {
                _fileContextMenu.Show(_fileListView, new Point(8, 8));
                return;
            }

            var item = _fileListView.SelectedItems[0];
            _fileContextMenu.Show(_fileListView, new Point(Math.Max(8, item.Bounds.Left), item.Bounds.Bottom));
        }

        private void ShowStashContextMenuForKeyboard()
        {
            if (_stashListView.SelectedItems.Count == 0)
            {
                RestoreStashSelection();
                _stashContextMenu.Show(_stashListView, new Point(8, 8));
                return;
            }

            var item = _stashListView.SelectedItems[0];
            _stashContextMenu.Show(_stashListView, new Point(Math.Max(8, item.Bounds.Left), item.Bounds.Bottom));
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
                if (_stashListView.Items.Count > 0 && _stashListView.CanFocus)
                {
                    RestoreStashSelection();
                    _stashListView.Focus();
                    return;
                }

                if (_saveButton.Enabled && _saveButton.CanFocus)
                {
                    _saveButton.Focus();
                    return;
                }

                if (_closeButton.CanFocus)
                {
                    _closeButton.Focus();
                }
            });
        }

        private void RestoreDetailsFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                Activate();
                if (_fileListView.Items.Count > 0 && _fileListView.CanFocus)
                {
                    _fileListView.Focus();
                    return;
                }

                if (_diffTextBox.CanFocus)
                {
                    _diffTextBox.Focus();
                }
            });
        }

        private void RestoreDiffFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                Activate();
                if (_diffTextBox.CanFocus)
                {
                    _diffTextBox.Focus();
                }
            });
        }

        private void RestoreStashSelection()
        {
            if (_stashListView.Items.Count == 0)
            {
                return;
            }

            if (_stashListView.SelectedItems.Count == 0)
            {
                _stashListView.Items[0].Selected = true;
            }

            var selectedItem = _stashListView.SelectedItems.Count > 0
                ? _stashListView.SelectedItems[0]
                : _stashListView.Items[0];
            selectedItem.Focused = true;
            selectedItem.EnsureVisible();
        }

        private GitStashEntry GetSelectedEntry()
        {
            return _stashListView.SelectedItems.Count == 0
                ? null
                : _stashListView.SelectedItems[0].Tag as GitStashEntry;
        }

        private GitCommitFileEntry GetSelectedStashFileEntry()
        {
            return _fileListView.SelectedItems.Count == 0
                ? null
                : _fileListView.SelectedItems[0].Tag as GitCommitFileEntry;
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text ?? string.Empty;
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

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_saveButton);
            footer.Controls.Add(_loadDetailsButton);
            footer.Controls.Add(_branchButton);
            footer.Controls.Add(_applyButton);
            footer.Controls.Add(_popButton);
            footer.Controls.Add(_dropButton);
            footer.Controls.Add(_refreshButton);

            var detailsSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                TabStop = false
            };
            detailsSplitContainer.Panel1.Controls.Add(_fileListView);
            detailsSplitContainer.Panel2.Controls.Add(_diffTextBox);

            var contentSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 190,
                SplitterWidth = 6,
                TabStop = false
            };
            contentSplitContainer.Panel1.Controls.Add(_stashListView);
            contentSplitContainer.Panel2.Controls.Add(detailsSplitContainer);

            root.Controls.Add(_statusLabel, 0, 0);
            root.Controls.Add(contentSplitContainer, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
        }

        private static ListView CreateStashListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                AccessibleName = UiLocalization.Text("GitStashManagementForm.Accessible.Stashes", "Stashes"),
                TabIndex = 0
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(string.Empty, 520);
            listView.Columns.Add(UiLocalization.Text("GitStashManagementForm.Column.Ref", "Ref"), 120);
            return listView;
        }

        private static ListView CreateStashFileListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                AccessibleName = UiLocalization.Text("GitStashManagementForm.Accessible.Files", "Files"),
                TabIndex = 1,
                UseCompatibleStateImageBehavior = false
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(UiLocalization.Text("GitStashManagementForm.Column.Path", "Path"), 360);
            listView.Columns.Add(UiLocalization.Text("GitStashManagementForm.Column.Type", "Type"), 120);
            return listView;
        }

        private static TextBox CreateDiffTextBox()
        {
            var textBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("GitStashManagementForm.Accessible.Diff", "Diff"),
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                TabIndex = 2
            };
            UiTheme.ApplyTextBox(textBox, true, true);
            return textBox;
        }

        private static string GetSubjectText(GitStashEntry entry)
        {
            return entry == null || string.IsNullOrWhiteSpace(entry.Subject)
                ? UiLocalization.Text("GitStashManagementForm.Value.NoSubject", "(No description)")
                : entry.Subject;
        }

        private static string GetStashFilePathText(GitCommitFileEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(entry.OriginalFilePath))
            {
                return string.Format(
                    UiLocalization.Text("GitStashManagementForm.Value.RenamedPath.Template", "{0} -> {1}"),
                    entry.OriginalFilePath,
                    entry.FilePath);
            }

            return entry.FilePath;
        }

        private static string GetStashFileChangeKindText(string changeKind)
        {
            if (string.IsNullOrWhiteSpace(changeKind))
            {
                return string.Empty;
            }

            switch (changeKind.Trim().ToLowerInvariant())
            {
                case "added":
                    return UiLocalization.Text("GitStashManagementForm.Value.ChangeKind.Added", "Added");
                case "modified":
                    return UiLocalization.Text("GitStashManagementForm.Value.ChangeKind.Modified", "Modified");
                case "deleted":
                    return UiLocalization.Text("GitStashManagementForm.Value.ChangeKind.Deleted", "Deleted");
                case "renamed":
                    return UiLocalization.Text("GitStashManagementForm.Value.ChangeKind.Renamed", "Renamed");
                case "copied":
                    return UiLocalization.Text("GitStashManagementForm.Value.ChangeKind.Copied", "Copied");
                default:
                    return changeKind;
            }
        }
    }
}
