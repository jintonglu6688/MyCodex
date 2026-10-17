using System;
using System.Collections.Generic;
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
    internal sealed class GitRemoteManagementForm : Form, IRuntimeTransitionWindow
    {
        bool IRuntimeTransitionWindow.IsBusyForRuntimeTransition => _isBusy;

        private readonly string _workspacePath;
        private readonly IGitRemoteService _gitService;
        private readonly ListView _remoteListView;
        private readonly TextBox _operationLogTextBox;
        private readonly Button _refreshButton;
        private readonly Button _addButton;
        private readonly Button _editButton;
        private readonly Button _deleteButton;
        private readonly Button _copyNameButton;
        private readonly Button _copyFetchUrlButton;
        private readonly Button _copyPushUrlButton;
        private readonly Button _closeButton;
        private readonly ContextMenu _contextMenu;
        private readonly MenuItem _addMenuItem;
        private readonly MenuItem _editMenuItem;
        private readonly MenuItem _deleteMenuItem;
        private readonly MenuItem _copyNameMenuItem;
        private readonly MenuItem _copyFetchUrlMenuItem;
        private readonly MenuItem _copyPushUrlMenuItem;
        private readonly MenuItem _refreshMenuItem;
        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private bool _isBusy;
        private bool _changed;

        public GitRemoteManagementForm(string workspacePath, IGitRemoteService gitService)
        {
            _workspacePath = workspacePath;
            _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));

            Text = UiLocalization.Text("GitRemoteManagementForm.Title", "Git Remotes");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(860, 520);
            Size = new Size(980, 620);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _remoteListView = CreateRemoteListView();
            _operationLogTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitRemoteManagementForm.Accessible.OperationLog", "Operation log"),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                TabStop = false
            };
            UiTheme.ApplyTextBox(_operationLogTextBox, false, true);

            _refreshButton = CreateButton(UiLocalization.Text("GitRemoteManagementForm.Button.Refresh", "Refresh(&R)"), UiLocalization.Text("GitRemoteManagementForm.Accessible.Refresh", "Refresh"), 0);
            _addButton = CreateButton(UiLocalization.Text("GitRemoteManagementForm.Button.Add", "Add(&A)"), UiLocalization.Text("GitRemoteManagementForm.Accessible.Add", "Add"), 1);
            _editButton = CreateButton(UiLocalization.Text("GitRemoteManagementForm.Button.Edit", "Edit(&E)"), UiLocalization.Text("GitRemoteManagementForm.Accessible.Edit", "Edit"), 2);
            _deleteButton = CreateButton(UiLocalization.Text("GitRemoteManagementForm.Button.Delete", "Delete(&D)"), UiLocalization.Text("GitRemoteManagementForm.Accessible.Delete", "Delete"), 3);
            _copyNameButton = CreateButton(UiLocalization.Text("GitRemoteManagementForm.Button.CopyName", "Copy Name(&N)"), UiLocalization.Text("GitRemoteManagementForm.Accessible.CopyName", "Copy name"), 4);
            _copyFetchUrlButton = CreateButton(UiLocalization.Text("GitRemoteManagementForm.Button.CopyFetchUrl", "Copy Fetch(&F)"), UiLocalization.Text("GitRemoteManagementForm.Accessible.CopyFetchUrl", "Copy fetch URL"), 5);
            _copyPushUrlButton = CreateButton(UiLocalization.Text("GitRemoteManagementForm.Button.CopyPushUrl", "Copy Push(&P)"), UiLocalization.Text("GitRemoteManagementForm.Accessible.CopyPushUrl", "Copy push URL"), 6);
            _closeButton = CreateButton(UiLocalization.Text("GitRemoteManagementForm.Button.Close", "&Close"), UiLocalization.Text("GitRemoteManagementForm.Accessible.Close", "Close"), 7);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_addButton, UiGlyph.NewSession);
            UiTheme.ApplyButton(_editButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_deleteButton, UiGlyph.Error);
            UiTheme.ApplyButton(_copyNameButton, UiGlyph.Info);
            UiTheme.ApplyButton(_copyFetchUrlButton, UiGlyph.Info);
            UiTheme.ApplyButton(_copyPushUrlButton, UiGlyph.Info);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _contextMenu = new ContextMenu();
            _addMenuItem = new MenuItem(UiLocalization.Text("GitRemoteManagementForm.Menu.Add", "Add(&A)"));
            _editMenuItem = new MenuItem(UiLocalization.Text("GitRemoteManagementForm.Menu.Edit", "Edit(&E)"));
            _deleteMenuItem = new MenuItem(UiLocalization.Text("GitRemoteManagementForm.Menu.Delete", "Delete(&D)"));
            _copyNameMenuItem = new MenuItem(UiLocalization.Text("GitRemoteManagementForm.Menu.CopyName", "Copy Name(&N)"));
            _copyFetchUrlMenuItem = new MenuItem(UiLocalization.Text("GitRemoteManagementForm.Menu.CopyFetchUrl", "Copy Fetch URL(&F)"));
            _copyPushUrlMenuItem = new MenuItem(UiLocalization.Text("GitRemoteManagementForm.Menu.CopyPushUrl", "Copy Push URL(&P)"));
            _refreshMenuItem = new MenuItem(UiLocalization.Text("GitRemoteManagementForm.Menu.Refresh", "Refresh(&R)"));
            _contextMenu.MenuItems.Add(_addMenuItem);
            _contextMenu.MenuItems.Add(_editMenuItem);
            _contextMenu.MenuItems.Add(_deleteMenuItem);
            _contextMenu.MenuItems.Add("-");
            _contextMenu.MenuItems.Add(_copyNameMenuItem);
            _contextMenu.MenuItems.Add(_copyFetchUrlMenuItem);
            _contextMenu.MenuItems.Add(_copyPushUrlMenuItem);
            _contextMenu.MenuItems.Add("-");
            _contextMenu.MenuItems.Add(_refreshMenuItem);
            _remoteListView.ContextMenu = _contextMenu;

            _refreshButton.Click += async (sender, e) => await LoadRemotesAsync(true);
            _addButton.Click += async (sender, e) => await AddRemoteAsync();
            _editButton.Click += async (sender, e) => await EditSelectedRemoteAsync();
            _deleteButton.Click += async (sender, e) => await DeleteSelectedRemoteAsync();
            _copyNameButton.Click += (sender, e) => CopySelectedValue(RemoteCopyKind.Name);
            _copyFetchUrlButton.Click += (sender, e) => CopySelectedValue(RemoteCopyKind.FetchUrl);
            _copyPushUrlButton.Click += (sender, e) => CopySelectedValue(RemoteCopyKind.PushUrl);
            _closeButton.Click += (sender, e) => Close();
            _addMenuItem.Click += async (sender, e) => await AddRemoteAsync();
            _editMenuItem.Click += async (sender, e) => await EditSelectedRemoteAsync();
            _deleteMenuItem.Click += async (sender, e) => await DeleteSelectedRemoteAsync();
            _copyNameMenuItem.Click += (sender, e) => CopySelectedValue(RemoteCopyKind.Name);
            _copyFetchUrlMenuItem.Click += (sender, e) => CopySelectedValue(RemoteCopyKind.FetchUrl);
            _copyPushUrlMenuItem.Click += (sender, e) => CopySelectedValue(RemoteCopyKind.PushUrl);
            _refreshMenuItem.Click += async (sender, e) => await LoadRemotesAsync(true);
            _contextMenu.Popup += (sender, e) => RefreshActionState();
            _remoteListView.SelectedIndexChanged += (sender, e) => RefreshActionState();
            _remoteListView.ItemActivate += async (sender, e) => await EditSelectedRemoteAsync();
            _remoteListView.MouseDown += RemoteListView_MouseDown;
            _remoteListView.KeyDown += RemoteListView_KeyDown;
            KeyDown += GitRemoteManagementForm_KeyDown;
            Shown += async (sender, e) => await LoadRemotesAsync(false);

            BuildLayout();
            CancelButton = _closeButton;
            SetOperationLog(UiLocalization.Text("GitRemoteManagementForm.Status.Preparing", "Preparing remotes..."));
            RefreshActionState();
        }

        public bool Changed
        {
            get { return _changed; }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _lifetimeCancellation.Cancel();
            base.OnFormClosed(e);
        }

        private async void GitRemoteManagementForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                await LoadRemotesAsync(true);
            }
        }

        private async void RemoteListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                await EditSelectedRemoteAsync();
                return;
            }

            if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                ShowContextMenuForKeyboard();
            }
        }

        private void RemoteListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            _remoteListView.Focus();
            var hit = _remoteListView.HitTest(e.Location);
            if (hit.Item != null)
            {
                hit.Item.Selected = true;
                hit.Item.Focused = true;
            }
        }

        private async Task LoadRemotesAsync(bool announceStatus)
        {
            if (_isBusy)
            {
                return;
            }

            _isBusy = true;
            try
            {
                RefreshActionState();
                SetOperationLog(UiLocalization.Text("GitRemoteManagementForm.Status.Loading", "Loading remotes..."));
                var remotes = await _gitService.ReadRemotesAsync(_workspacePath, _lifetimeCancellation.Token).ConfigureAwait(true);
                if (IsDisposed || Disposing)
                {
                    return;
                }
                BindRemotes(remotes);
                SetOperationLog(string.Format(
                    UiLocalization.Text("GitRemoteManagementForm.Status.Loaded.Template", "Loaded {0} remote(s)."),
                    remotes == null ? 0 : remotes.Count));
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }
                SetOperationLog(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    _isBusy = false;
                    RefreshActionState();
                    RestoreListFocus();
                }
            }
        }

        private async Task AddRemoteAsync()
        {
            if (_isBusy)
            {
                return;
            }

            using (var form = new GitRemoteEditForm(null))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreListFocus();
                    return;
                }

                await RunRemoteOperationAsync(async () =>
                {
                    var result = await _gitService.AddRemoteAsync(_workspacePath, form.RemoteName, form.FetchUrl, _lifetimeCancellation.Token).ConfigureAwait(true);
                    if (result == null || !result.Success || string.Equals(form.FetchUrl, form.PushUrl, StringComparison.Ordinal))
                    {
                        return result;
                    }

                    return await _gitService.SetRemotePushUrlAsync(_workspacePath, form.RemoteName, form.PushUrl, _lifetimeCancellation.Token).ConfigureAwait(true);
                });
            }
        }

        private async Task EditSelectedRemoteAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedRemote();
            if (entry == null)
            {
                return;
            }

            using (var form = new GitRemoteEditForm(entry))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreListFocus();
                    return;
                }

                await RunRemoteOperationAsync(async () =>
                {
                    var result = await _gitService.SetRemoteUrlAsync(_workspacePath, form.RemoteName, form.FetchUrl, _lifetimeCancellation.Token).ConfigureAwait(true);
                    if (result == null || !result.Success || string.Equals(form.FetchUrl, form.PushUrl, StringComparison.Ordinal))
                    {
                        return result;
                    }

                    return await _gitService.SetRemotePushUrlAsync(_workspacePath, form.RemoteName, form.PushUrl, _lifetimeCancellation.Token).ConfigureAwait(true);
                });
            }
        }

        private async Task DeleteSelectedRemoteAsync()
        {
            if (_isBusy)
            {
                return;
            }

            var entry = GetSelectedRemote();
            if (entry == null)
            {
                return;
            }

            var confirm = MessageBox.Show(
                this,
                string.Format(UiLocalization.Text("GitRemoteManagementForm.Confirm.Delete.Template", "Delete remote '{0}'?"), entry.Name),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
            {
                RestoreListFocus();
                return;
            }

            await RunRemoteOperationAsync(() => _gitService.RemoveRemoteAsync(_workspacePath, entry.Name, _lifetimeCancellation.Token));
        }

        private async Task RunRemoteOperationAsync(Func<Task<GitOperationResult>> operation)
        {
            if (operation == null || _isBusy)
            {
                return;
            }

            _isBusy = true;
            try
            {
                RefreshActionState();
                var result = await operation().ConfigureAwait(true);
                var message = result == null
                    ? UiLocalization.Text("GitRemoteManagementForm.Status.OperationFailed", "Git remote operation failed.")
                    : result.DisplayMessage;
                SetOperationLog(message);
                if (result == null || !result.Success)
                {
                    MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                _changed = true;
                await LoadRemotesAfterOperationAsync();
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }
                SetOperationLog(ex.Message);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    _isBusy = false;
                    RefreshActionState();
                    RestoreListFocus();
                }
            }
        }

        private async Task LoadRemotesAfterOperationAsync()
        {
            var remotes = await _gitService.ReadRemotesAsync(_workspacePath, _lifetimeCancellation.Token).ConfigureAwait(true);
            if (IsDisposed || Disposing)
            {
                return;
            }
            BindRemotes(remotes);
        }

        private void BindRemotes(IReadOnlyList<GitRemoteEntry> remotes)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            var selectedName = GetSelectedRemoteName();
            _remoteListView.BeginUpdate();
            try
            {
                _remoteListView.Items.Clear();
                foreach (var entry in SortRemotes(remotes))
                {
                    var item = new ListViewItem(entry.Name ?? string.Empty);
                    item.SubItems.Add(entry.FetchUrl ?? string.Empty);
                    item.SubItems.Add(entry.PushUrl ?? string.Empty);
                    item.Tag = entry;
                    _remoteListView.Items.Add(item);
                }

                RestoreSelection(selectedName);
            }
            finally
            {
                _remoteListView.EndUpdate();
            }
        }

        private IEnumerable<GitRemoteEntry> SortRemotes(IReadOnlyList<GitRemoteEntry> remotes)
        {
            return (remotes ?? new GitRemoteEntry[0])
                .Where(item => item != null)
                .OrderByDescending(item => string.Equals(item.Name, "origin", StringComparison.OrdinalIgnoreCase))
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);
        }

        private void RestoreSelection(string selectedName)
        {
            if (_remoteListView.Items.Count == 0)
            {
                return;
            }

            var selectedItem = _remoteListView.Items[0];
            if (!string.IsNullOrWhiteSpace(selectedName))
            {
                foreach (ListViewItem item in _remoteListView.Items)
                {
                    var entry = item.Tag as GitRemoteEntry;
                    if (entry != null && string.Equals(entry.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedItem = item;
                        break;
                    }
                }
            }

            selectedItem.Selected = true;
            selectedItem.Focused = true;
            selectedItem.EnsureVisible();
        }

        private void RefreshActionState()
        {
            var hasEntry = GetSelectedRemote() != null;
            _remoteListView.Enabled = true;
            _refreshButton.Enabled = !_isBusy;
            _addButton.Enabled = !_isBusy;
            _editButton.Enabled = !_isBusy && hasEntry;
            _deleteButton.Enabled = !_isBusy && hasEntry;
            _copyNameButton.Enabled = hasEntry;
            _copyFetchUrlButton.Enabled = hasEntry;
            _copyPushUrlButton.Enabled = hasEntry;
            _addMenuItem.Enabled = !_isBusy;
            _editMenuItem.Enabled = !_isBusy && hasEntry;
            _deleteMenuItem.Enabled = !_isBusy && hasEntry;
            _copyNameMenuItem.Enabled = hasEntry;
            _copyFetchUrlMenuItem.Enabled = hasEntry;
            _copyPushUrlMenuItem.Enabled = hasEntry;
            _refreshMenuItem.Enabled = !_isBusy;
        }

        private void CopySelectedValue(RemoteCopyKind kind)
        {
            var entry = GetSelectedRemote();
            if (entry == null)
            {
                return;
            }

            string value;
            string message;
            switch (kind)
            {
                case RemoteCopyKind.FetchUrl:
                    value = entry.FetchUrl ?? string.Empty;
                    message = UiLocalization.Text("GitRemoteManagementForm.Status.CopiedFetchUrl", "Copied fetch URL.");
                    break;
                case RemoteCopyKind.PushUrl:
                    value = entry.PushUrl ?? string.Empty;
                    message = UiLocalization.Text("GitRemoteManagementForm.Status.CopiedPushUrl", "Copied push URL.");
                    break;
                case RemoteCopyKind.Name:
                default:
                    value = entry.Name ?? string.Empty;
                    message = UiLocalization.Text("GitRemoteManagementForm.Status.CopiedName", "Copied remote name.");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                Clipboard.SetText(value);
                SetOperationLog(message);
            }

            RestoreListFocus();
        }

        private GitRemoteEntry GetSelectedRemote()
        {
            return _remoteListView.SelectedItems.Count == 0
                ? null
                : _remoteListView.SelectedItems[0].Tag as GitRemoteEntry;
        }

        private string GetSelectedRemoteName()
        {
            var entry = GetSelectedRemote();
            return entry == null ? string.Empty : entry.Name ?? string.Empty;
        }

        private void ShowContextMenuForKeyboard()
        {
            if (_remoteListView.SelectedItems.Count == 0)
            {
                _contextMenu.Show(_remoteListView, new Point(8, 8));
                return;
            }

            var item = _remoteListView.SelectedItems[0];
            _contextMenu.Show(_remoteListView, new Point(Math.Max(8, item.Bounds.Left), item.Bounds.Bottom));
        }

        private void RestoreListFocus()
        {
            this.BeginInvokeSafe(() =>
            {
                if (IsDisposed || Disposing || !Visible)
                {
                    return;
                }

                if (_remoteListView.CanFocus)
                {
                    _remoteListView.Focus();
                }
            });
        }

        private void SetOperationLog(string text)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            TextBoxCaretHelper.SetTextAndMoveToStart(_operationLogTextBox, text ?? string.Empty);
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var heading = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("GitRemoteManagementForm.Heading", "Remote repositories"),
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            root.Controls.Add(heading, 0, 0);
            root.Controls.Add(_remoteListView, 0, 1);
            root.Controls.Add(_operationLogTextBox, 0, 2);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            footer.Controls.Add(_addButton);
            footer.Controls.Add(_editButton);
            footer.Controls.Add(_deleteButton);
            footer.Controls.Add(_copyNameButton);
            footer.Controls.Add(_copyFetchUrlButton);
            footer.Controls.Add(_copyPushUrlButton);
            footer.Controls.Add(_refreshButton);
            footer.Controls.Add(_closeButton);
            root.Controls.Add(footer, 0, 3);
            Controls.Add(root);
        }

        private static ListView CreateRemoteListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("GitRemoteManagementForm.Accessible.Remotes", "Remotes"),
                TabIndex = 0,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            listView.Columns.Add(UiLocalization.Text("GitRemoteManagementForm.Column.Name", "Name"), 150);
            listView.Columns.Add(UiLocalization.Text("GitRemoteManagementForm.Column.FetchUrl", "Fetch URL"), 360);
            listView.Columns.Add(UiLocalization.Text("GitRemoteManagementForm.Column.PushUrl", "Push URL"), 360);
            UiTheme.ApplyListView(listView);
            return listView;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 118,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }

        private enum RemoteCopyKind
        {
            Name = 0,
            FetchUrl = 1,
            PushUrl = 2
        }
    }
}
