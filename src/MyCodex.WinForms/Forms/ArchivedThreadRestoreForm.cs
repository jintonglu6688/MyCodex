using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public class ArchivedThreadRestoreForm : Form
    {
        private readonly IAppServerConnectionClient _connectionClient;
        private readonly IAppServerThreadClient _threadClient;
        private readonly HashSet<string> _existingThreadIds;
        private readonly string _currentWorkspacePath;
        private readonly Func<string, bool> _isWorkspacePathAvailable;
        private readonly ListView _threadListView;
        private readonly Label _statusLabel;
        private readonly Button _selectAllButton;
        private readonly Button _clearSelectionButton;
        private readonly Button _currentWorkspaceButton;
        private readonly Button _restoreButton;
        private readonly Button _cancelButton;

        private bool _isLoading;
        private bool _isBinding;

        public ArchivedThreadRestoreForm(
            IAppServerClient appServerClient,
            IEnumerable<string> existingThreadIds,
            string currentWorkspacePath,
            Func<string, bool> isWorkspacePathAvailable = null)
            : this(
                appServerClient,
                appServerClient,
                existingThreadIds,
                currentWorkspacePath,
                isWorkspacePathAvailable)
        {
        }

        internal ArchivedThreadRestoreForm(
            IAppServerConnectionClient connectionClient,
            IAppServerThreadClient threadClient,
            IEnumerable<string> existingThreadIds,
            string currentWorkspacePath,
            Func<string, bool> isWorkspacePathAvailable = null)
        {
            _connectionClient = connectionClient ?? throw new ArgumentNullException(nameof(connectionClient));
            _threadClient = threadClient ?? throw new ArgumentNullException(nameof(threadClient));
            _existingThreadIds = new HashSet<string>(existingThreadIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _currentWorkspacePath = WorkspacePathUtilities.NormalizeLocalOrUnixPath(currentWorkspacePath);
            _isWorkspacePathAvailable = isWorkspacePathAvailable ?? IsLocalWorkspacePathAvailable;

            Text = UiLocalization.Text("ArchivedThreadRestoreForm.Title", "Restore Archived Threads");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 560);
            Size = new Size(1020, 660);
            UiTheme.ApplyForm(this);

            _threadListView = CreateThreadListView();
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            _selectAllButton = CreateButton(
                UiLocalization.Text("ArchivedThreadRestoreForm.Button.SelectAll", "Select All(&A)"),
                UiLocalization.Text("ArchivedThreadRestoreForm.Accessible.SelectAll", "Select All"));
            _clearSelectionButton = CreateButton(
                UiLocalization.Text("ArchivedThreadRestoreForm.Button.ClearSelection", "Clear Selection(&N)"),
                UiLocalization.Text("ArchivedThreadRestoreForm.Accessible.ClearSelection", "Clear Selection"));
            _currentWorkspaceButton = CreateButton(
                UiLocalization.Text("ArchivedThreadRestoreForm.Button.CurrentWorkspace", "Current Workspace(&W)"),
                UiLocalization.Text("ArchivedThreadRestoreForm.Accessible.CurrentWorkspace", "Current Workspace"));
            _restoreButton = CreateButton(
                UiLocalization.Text("ArchivedThreadRestoreForm.Button.Restore", "Restore(&R)"),
                UiLocalization.Text("ArchivedThreadRestoreForm.Accessible.Restore", "Restore"));
            _cancelButton = CreateButton(
                UiLocalization.Text("ArchivedThreadRestoreForm.Button.Cancel", "&Cancel"),
                UiLocalization.Text("ArchivedThreadRestoreForm.Accessible.Cancel", "Cancel"));
            UiTheme.ApplyButton(_selectAllButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_clearSelectionButton, UiGlyph.Cancel);
            UiTheme.ApplyButton(_currentWorkspaceButton, UiGlyph.Workspace);
            UiTheme.ApplyButton(_restoreButton, UiGlyph.Download);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _selectAllButton.Click += SelectAllButton_Click;
            _clearSelectionButton.Click += ClearSelectionButton_Click;
            _currentWorkspaceButton.Click += CurrentWorkspaceButton_Click;
            _restoreButton.Click += RestoreButton_Click;
            _cancelButton.Click += CancelButton_Click;
            _threadListView.ItemCheck += ThreadListView_ItemCheck;
            _threadListView.ItemChecked += ThreadListView_ItemChecked;

            BuildLayout();
            Shown += OnShown;
        }

        public List<AppServerThreadSummary> GetSelectedThreads()
        {
            var result = new List<AppServerThreadSummary>();
            foreach (ListViewItem item in _threadListView.Items)
            {
                if (!item.Checked)
                {
                    continue;
                }

                var candidate = item.Tag as ArchivedThreadCandidate;
                if (candidate != null && candidate.CanRestore && candidate.Thread != null)
                {
                    result.Add(candidate.Thread);
                }
            }

            return result;
        }

        private async void OnShown(object sender, EventArgs e)
        {
            await LoadArchivedThreadsAsync();
            this.BeginInvokeSafe(() =>
            {
                if (_threadListView.Items.Count > 0)
                {
                    _threadListView.Items[0].Selected = true;
                    _threadListView.Focus();
                }
                else
                {
                    _restoreButton.Focus();
                }
            });
        }

        private async System.Threading.Tasks.Task LoadArchivedThreadsAsync()
        {
            if (_isLoading)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("ArchivedThreadRestoreForm.Action.LoadArchivedThreads", "Load archived threads")))
            {
                return;
            }

            _isLoading = true;
            SetStatus(UiLocalization.Text("ArchivedThreadRestoreForm.Status.Loading", "Loading archived threads..."));
            ToggleButtons(false);

            try
            {
                var threads = await _threadClient.ListArchivedThreadsAsync(null);
                BindThreads(threads);

                var restorableCount = GetRestorableItemCount();
                if (_threadListView.Items.Count == 0)
                {
                    SetStatus(UiLocalization.Text("ArchivedThreadRestoreForm.Status.NoArchivedThreads", "No archived threads available to restore."));
                }
                else if (restorableCount == 0)
                {
                    SetStatus(UiLocalization.Text("ArchivedThreadRestoreForm.Status.AllAlreadyExists", "All archived threads already exist in current list."));
                }
                else
                {
                    SetStatus(UiLocalization.Text("ArchivedThreadRestoreForm.Status.SelectThreads", "Please select archived threads to restore."));
                }
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("ArchivedThreadRestoreForm.Status.LoadFailed", "Failed to load archived threads."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                ToggleButtons(true);
            }
        }

        private void BindThreads(List<AppServerThreadSummary> threads)
        {
            _isBinding = true;
            _threadListView.BeginUpdate();
            _threadListView.Items.Clear();

            if (threads != null)
            {
                foreach (var thread in threads.OrderByDescending(item => item.UpdatedAt))
                {
                    if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId))
                    {
                        continue;
                    }

                    var candidate = BuildCandidate(thread);
                    var item = new ListViewItem(new[]
                    {
                        GetDisplayTitle(thread),
                        string.IsNullOrWhiteSpace(thread.WorkingDirectory)
                            ? UiLocalization.Text("ArchivedThreadRestoreForm.Value.Missing", "(Missing)")
                            : thread.WorkingDirectory,
                        string.IsNullOrWhiteSpace(thread.Source)
                            ? UiLocalization.Text("ArchivedThreadRestoreForm.Value.Unknown", "(Unknown)")
                            : thread.Source,
                        thread.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                        candidate.StatusText
                    });
                    item.Tag = candidate;
                    item.Checked = candidate.CanRestore;
                    if (!candidate.CanRestore)
                    {
                        item.ForeColor = SystemColors.GrayText;
                    }

                    _threadListView.Items.Add(item);
                }
            }

            _threadListView.EndUpdate();
            _isBinding = false;
            UpdateRestoreButtonState();
        }

        private ArchivedThreadCandidate BuildCandidate(AppServerThreadSummary thread)
        {
            if (string.IsNullOrWhiteSpace(thread.WorkingDirectory))
            {
                return new ArchivedThreadCandidate(
                    thread,
                    false,
                    UiLocalization.Text("ArchivedThreadRestoreForm.Candidate.WorkspaceMissing", "Workspace Missing"));
            }

            if (_existingThreadIds.Contains(thread.ThreadId))
            {
                return new ArchivedThreadCandidate(
                    thread,
                    true,
                    UiLocalization.Text("ArchivedThreadRestoreForm.Candidate.ExistingArchived", "Archived (Local Record Exists)"));
            }

            return _isWorkspacePathAvailable(thread.WorkingDirectory)
                ? new ArchivedThreadCandidate(thread, true, UiLocalization.Text("ArchivedThreadRestoreForm.Candidate.Restorable", "Restorable"))
                : new ArchivedThreadCandidate(thread, true, UiLocalization.Text("ArchivedThreadRestoreForm.Candidate.PathUnavailable", "Path Unavailable (View Only)"));
        }

        private int GetRestorableItemCount()
        {
            var count = 0;
            foreach (ListViewItem item in _threadListView.Items)
            {
                var candidate = item.Tag as ArchivedThreadCandidate;
                if (candidate != null && candidate.CanRestore)
                {
                    count++;
                }
            }

            return count;
        }

        private void BuildLayout()
        {
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };

            var title = new Label
            {
                Text = UiLocalization.Text("ArchivedThreadRestoreForm.Header", "Select archived threads to restore"),
                Dock = DockStyle.Top,
                Height = 32,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var tip = new Label
            {
                Text = UiLocalization.Text(
                    "ArchivedThreadRestoreForm.Tip",
                    "After restoring, threads will appear again in active threads of both MyCodex and Codex."),
                Dock = DockStyle.Top,
                Height = 28,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, 12, 0, 0),
                TabStop = false
            };

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 520,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            buttonsPanel.Controls.Add(_selectAllButton);
            buttonsPanel.Controls.Add(_clearSelectionButton);
            buttonsPanel.Controls.Add(_currentWorkspaceButton);
            buttonsPanel.Controls.Add(_restoreButton);
            buttonsPanel.Controls.Add(_cancelButton);

            footerPanel.Controls.Add(buttonsPanel);
            footerPanel.Controls.Add(_statusLabel);

            mainPanel.Controls.Add(_threadListView);
            mainPanel.Controls.Add(footerPanel);
            mainPanel.Controls.Add(tip);
            mainPanel.Controls.Add(title);

            Controls.Add(mainPanel);
        }

        private void SelectAllButton_Click(object sender, EventArgs e)
        {
            SetAllCheckedState(true);
        }

        private void ClearSelectionButton_Click(object sender, EventArgs e)
        {
            SetAllCheckedState(false);
        }

        private void CurrentWorkspaceButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentWorkspacePath))
            {
                return;
            }

            _isBinding = true;
            try
            {
                foreach (ListViewItem item in _threadListView.Items)
                {
                    var candidate = item.Tag as ArchivedThreadCandidate;
                    item.Checked = candidate != null &&
                        candidate.CanRestore &&
                        string.Equals(WorkspacePathUtilities.NormalizeLocalOrUnixPath(candidate.Thread.WorkingDirectory), _currentWorkspacePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                _isBinding = false;
            }

            UpdateRestoreButtonState();
        }

        private void RestoreButton_Click(object sender, EventArgs e)
        {
            if (GetSelectedThreads().Count == 0)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("ArchivedThreadRestoreForm.Message.SelectAtLeastOne", "Please select at least one archived thread."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void ThreadListView_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_isBinding || e.Index < 0 || e.Index >= _threadListView.Items.Count)
            {
                return;
            }

            var candidate = _threadListView.Items[e.Index].Tag as ArchivedThreadCandidate;
            if (candidate == null || candidate.CanRestore || e.NewValue == CheckState.Unchecked)
            {
                return;
            }

            e.NewValue = CheckState.Unchecked;
        }

        private void ThreadListView_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            UpdateRestoreButtonState();
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private async System.Threading.Tasks.Task<bool> EnsureAppServerAvailableForActionAsync(string actionName)
        {
            try
            {
                await _connectionClient.ConnectAsync();
                return true;
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(
                    UiLocalization.Text("ArchivedThreadRestoreForm.Status.ActionFailedDisconnected", "{0} failed because App Server is not connected."),
                    actionName));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ToggleButtons(bool enabled)
        {
            _selectAllButton.Enabled = enabled;
            _clearSelectionButton.Enabled = enabled;
            _currentWorkspaceButton.Enabled = enabled && !string.IsNullOrWhiteSpace(_currentWorkspacePath);
            _restoreButton.Enabled = enabled && GetSelectedThreads().Count > 0;
            _cancelButton.Enabled = enabled;
        }

        private void SetAllCheckedState(bool isChecked)
        {
            _isBinding = true;
            try
            {
                foreach (ListViewItem item in _threadListView.Items)
                {
                    var candidate = item.Tag as ArchivedThreadCandidate;
                    item.Checked = candidate != null && candidate.CanRestore && isChecked;
                }
            }
            finally
            {
                _isBinding = false;
            }

            UpdateRestoreButtonState();
        }

        private void UpdateRestoreButtonState()
        {
            if (_isLoading)
            {
                return;
            }

            _restoreButton.Enabled = GetSelectedThreads().Count > 0;
        }

        private static string GetDisplayTitle(AppServerThreadSummary thread)
        {
            if (!string.IsNullOrWhiteSpace(thread.Title))
            {
                return thread.Title;
            }

            if (!string.IsNullOrWhiteSpace(thread.Preview))
            {
                var preview = thread.Preview.Length > 50 ? thread.Preview.Substring(0, 50) + "..." : thread.Preview;
                return preview;
            }

            return string.IsNullOrWhiteSpace(thread.ThreadId)
                ? UiLocalization.Text("ArchivedThreadRestoreForm.Value.UnnamedSession", "Unnamed Session")
                : thread.ThreadId;
        }

        private static bool IsLocalWorkspacePathAvailable(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(WorkspacePathUtilities.NormalizeLocalOrUnixPath(path));
        }

        private static Button CreateButton(string text, string accessibleName)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 96,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true
            };
        }

        private static ListView CreateThreadListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                AccessibleName = UiLocalization.Text("ArchivedThreadRestoreForm.Accessible.ArchivedThreads", "Archived Threads"),
                GridLines = true,
                HideSelection = false
            };
            UiTheme.ApplyListView(listView);

            listView.Columns.Add(UiLocalization.Text("ArchivedThreadRestoreForm.Column.Title", "Title"), 260, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ArchivedThreadRestoreForm.Column.Workspace", "Workspace"), 320, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ArchivedThreadRestoreForm.Column.Source", "Source"), 90, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ArchivedThreadRestoreForm.Column.UpdatedAt", "Updated At"), 140, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ArchivedThreadRestoreForm.Column.Status", "Status"), 150, HorizontalAlignment.Left);
            return listView;
        }

        private sealed class ArchivedThreadCandidate
        {
            public ArchivedThreadCandidate(AppServerThreadSummary thread, bool canRestore, string statusText)
            {
                Thread = thread;
                CanRestore = canRestore;
                StatusText = statusText;
            }

            public AppServerThreadSummary Thread { get; }

            public bool CanRestore { get; }

            public string StatusText { get; }
        }
    }
}
