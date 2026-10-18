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
    public class ThreadImportForm : Form
    {
        private readonly IAppServerConnectionClient _connectionClient;
        private readonly IAppServerThreadClient _threadClient;
        private readonly HashSet<string> _existingThreadIds;
        private readonly string _currentWorkspacePath;
        private readonly Func<string, bool> _isWorkspacePathAvailable;
        private readonly Func<AppServerThreadSummary, bool> _shouldShowThread;
        private readonly ListView _threadListView;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _selectAllButton;
        private readonly Button _clearSelectionButton;
        private readonly Button _currentWorkspaceButton;
        private readonly Button _importButton;
        private readonly Button _cancelButton;

        private bool _isLoading;

        public ThreadImportForm(
            IAppServerClient appServerClient,
            IEnumerable<string> existingThreadIds,
            string currentWorkspacePath,
            Func<string, bool> isWorkspacePathAvailable = null)
            : this(appServerClient, existingThreadIds, currentWorkspacePath, isWorkspacePathAvailable, null)
        {
        }

        public ThreadImportForm(
            IAppServerClient appServerClient,
            IEnumerable<string> existingThreadIds,
            string currentWorkspacePath,
            Func<string, bool> isWorkspacePathAvailable,
            Func<AppServerThreadSummary, bool> shouldShowThread)
            : this(
                appServerClient,
                appServerClient,
                existingThreadIds,
                currentWorkspacePath,
                isWorkspacePathAvailable,
                shouldShowThread)
        {
        }

        internal ThreadImportForm(
            IAppServerConnectionClient connectionClient,
            IAppServerThreadClient threadClient,
            IEnumerable<string> existingThreadIds,
            string currentWorkspacePath,
            Func<string, bool> isWorkspacePathAvailable,
            Func<AppServerThreadSummary, bool> shouldShowThread)
        {
            _connectionClient = connectionClient ?? throw new ArgumentNullException(nameof(connectionClient));
            _threadClient = threadClient ?? throw new ArgumentNullException(nameof(threadClient));
            _existingThreadIds = new HashSet<string>(existingThreadIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _currentWorkspacePath = WorkspacePathUtilities.NormalizeLocalOrUnixPath(currentWorkspacePath);
            _isWorkspacePathAvailable = isWorkspacePathAvailable ?? IsLocalWorkspacePathAvailable;
            _shouldShowThread = shouldShowThread ?? (_ => true);

            Text = UiLocalization.Text("ThreadImportForm.Title", "Sync Thread History");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(860, 560);
            Size = new Size(980, 640);
            ShowInTaskbar = false;
            KeyPreview = true;
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
            _refreshButton = CreateButton(
                UiLocalization.Text("ThreadImportForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("ThreadImportForm.Accessible.Refresh", "Refresh"));
            _selectAllButton = CreateButton(
                UiLocalization.Text("ThreadImportForm.Button.SelectAll", "Select All(&A)"),
                UiLocalization.Text("ThreadImportForm.Accessible.SelectAll", "Select All"));
            _clearSelectionButton = CreateButton(
                UiLocalization.Text("ThreadImportForm.Button.ClearSelection", "Clear Selection(&N)"),
                UiLocalization.Text("ThreadImportForm.Accessible.ClearSelection", "Clear Selection"));
            _currentWorkspaceButton = CreateButton(
                UiLocalization.Text("ThreadImportForm.Button.CurrentWorkspace", "Current Workspace(&W)"),
                UiLocalization.Text("ThreadImportForm.Accessible.CurrentWorkspace", "Current Workspace"));
            _importButton = CreateButton(
                UiLocalization.Text("ThreadImportForm.Button.Import", "Sync(&S)"),
                UiLocalization.Text("ThreadImportForm.Accessible.Import", "Sync"));
            _cancelButton = CreateButton(
                UiLocalization.Text("ThreadImportForm.Button.Close", "Close(&C)"),
                UiLocalization.Text("ThreadImportForm.Accessible.Close", "Close"));
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_selectAllButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_clearSelectionButton, UiGlyph.Cancel);
            UiTheme.ApplyButton(_currentWorkspaceButton, UiGlyph.Workspace);
            UiTheme.ApplyButton(_importButton, UiGlyph.Download);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _refreshButton.Click += RefreshButton_Click;
            _selectAllButton.Click += SelectAllButton_Click;
            _clearSelectionButton.Click += ClearSelectionButton_Click;
            _currentWorkspaceButton.Click += CurrentWorkspaceButton_Click;
            _importButton.Click += ImportButton_Click;
            _cancelButton.Click += CancelButton_Click;
            _threadListView.ItemChecked += ThreadListView_ItemChecked;
            _threadListView.KeyDown += ThreadListView_KeyDown;
            KeyDown += ThreadImportForm_KeyDown;

            BuildLayout();
            Shown += OnShown;
            CancelButton = _cancelButton;
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

                var summary = item.Tag as AppServerThreadSummary;
                if (summary != null)
                {
                    result.Add(summary);
                }
            }

            return result;
        }

        private async void OnShown(object sender, EventArgs e)
        {
            await LoadThreadsAsync();
            this.BeginInvokeSafe(() =>
            {
                if (_threadListView.Items.Count > 0)
                {
                    _threadListView.Items[0].Selected = true;
                    _threadListView.Focus();
                }
                else
                {
                    _threadListView.Focus();
                }
            });
        }

        private async System.Threading.Tasks.Task LoadThreadsAsync()
        {
            if (_isLoading)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("ThreadImportForm.Action.LoadThreadHistory", "Load thread history")))
            {
                return;
            }

            _isLoading = true;
            SetStatus(UiLocalization.Text("ThreadImportForm.Status.Loading", "Loading local thread history..."));
            ToggleButtons(false);

            try
            {
                var threads = await _threadClient.ListThreadsAsync();
                BindThreads(threads);
                SetStatus(_threadListView.Items.Count == 0
                    ? UiLocalization.Text("ThreadImportForm.Status.NoNewThreads", "No new thread history to sync.")
                    : UiLocalization.Text("ThreadImportForm.Status.SelectThreads", "Please select thread history to sync."));
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("ThreadImportForm.Status.LoadFailed", "Failed to load thread history."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                ToggleButtons(true);
                this.BeginInvokeSafe(() =>
                {
                    if (_threadListView.Items.Count > 0)
                    {
                        if (_threadListView.SelectedItems.Count == 0)
                        {
                            _threadListView.Items[0].Selected = true;
                        }

                        _threadListView.Focus();
                    }
                });
            }
        }

        private void BindThreads(List<AppServerThreadSummary> threads)
        {
            _threadListView.BeginUpdate();
            _threadListView.Items.Clear();

            if (threads != null)
            {
                foreach (var thread in threads.OrderByDescending(item => item.UpdatedAt))
                {
                    if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId) || _existingThreadIds.Contains(thread.ThreadId))
                    {
                        continue;
                    }

                    if (!_isWorkspacePathAvailable(thread.WorkingDirectory))
                    {
                        continue;
                    }

                    if (!_shouldShowThread(thread))
                    {
                        continue;
                    }

                    var item = new ListViewItem(new[]
                    {
                        GetDisplayTitle(thread),
                        thread.WorkingDirectory,
                        string.IsNullOrWhiteSpace(thread.Source)
                            ? UiLocalization.Text("ThreadImportForm.Value.Unknown", "(Unknown)")
                            : thread.Source,
                        thread.UpdatedAt.ToString("yyyy-MM-dd HH:mm")
                    });
                    item.Tag = thread;
                    item.Checked = true;
                    _threadListView.Items.Add(item);
                }
            }

            _threadListView.EndUpdate();
            UpdateImportButtonState();
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
                Text = UiLocalization.Text("ThreadImportForm.Header", "Select thread history to sync"),
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
                    "ThreadImportForm.Tip",
                    "Use arrow keys to browse, space to check, F5 to reload, and Esc to close. Only thread history not yet imported into MyCodex is shown."),
                Dock = DockStyle.Top,
                Height = 36,
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
                Width = 640,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            buttonsPanel.Controls.Add(_refreshButton);
            buttonsPanel.Controls.Add(_selectAllButton);
            buttonsPanel.Controls.Add(_clearSelectionButton);
            buttonsPanel.Controls.Add(_currentWorkspaceButton);
            buttonsPanel.Controls.Add(_importButton);

            footerPanel.Controls.Add(buttonsPanel);
            footerPanel.Controls.Add(_statusLabel);

            mainPanel.Controls.Add(_threadListView);
            mainPanel.Controls.Add(footerPanel);
            mainPanel.Controls.Add(tip);
            mainPanel.Controls.Add(title);

            Controls.Add(mainPanel);
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            await LoadThreadsAsync();
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

            foreach (ListViewItem item in _threadListView.Items)
            {
                var summary = item.Tag as AppServerThreadSummary;
                item.Checked = summary != null &&
                    string.Equals(WorkspacePathUtilities.NormalizeLocalOrUnixPath(summary.WorkingDirectory), _currentWorkspacePath, StringComparison.OrdinalIgnoreCase);
            }

            UpdateImportButtonState();
        }

        private void ImportButton_Click(object sender, EventArgs e)
        {
            if (_threadListView.CheckedItems.Count == 0)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("ThreadImportForm.Message.SelectAtLeastOne", "Please select at least one thread history item."),
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

        private void ThreadListView_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            UpdateImportButtonState();
        }

        private void ThreadListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !_isLoading && _threadListView.CheckedItems.Count > 0)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ImportButton_Click(_importButton, EventArgs.Empty);
            }
        }

        private async void ThreadImportForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CancelButton_Click(_cancelButton, EventArgs.Empty);
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await LoadThreadsAsync();
            }
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
                    UiLocalization.Text("ThreadImportForm.Status.ActionFailedDisconnected", "{0} failed because App Server is not connected."),
                    actionName));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void ToggleButtons(bool enabled)
        {
            _refreshButton.Enabled = enabled;
            _selectAllButton.Enabled = enabled;
            _clearSelectionButton.Enabled = enabled;
            _currentWorkspaceButton.Enabled = enabled && !string.IsNullOrWhiteSpace(_currentWorkspacePath);
            _importButton.Enabled = enabled && _threadListView.CheckedItems.Count > 0;
            _cancelButton.Enabled = enabled;
        }

        private void SetAllCheckedState(bool isChecked)
        {
            foreach (ListViewItem item in _threadListView.Items)
            {
                item.Checked = isChecked;
            }

            UpdateImportButtonState();
        }

        private void UpdateImportButtonState()
        {
            if (_isLoading)
            {
                return;
            }

            _importButton.Enabled = _threadListView.CheckedItems.Count > 0;
        }

        private static string GetDisplayTitle(AppServerThreadSummary thread)
        {
            var workspaceName = GetWorkspaceName(thread.WorkingDirectory);

            if (!string.IsNullOrWhiteSpace(thread.Title))
            {
                return string.Format("{0} - {1}", workspaceName, thread.Title);
            }

            if (!string.IsNullOrWhiteSpace(thread.Preview))
            {
                var preview = thread.Preview.Length > 50 ? thread.Preview.Substring(0, 50) + "..." : thread.Preview;
                return string.Format("{0} - {1}", workspaceName, preview);
            }

            return string.Format("{0} - {1}", workspaceName, thread.ThreadId);
        }

        private static string GetWorkspaceName(string workingDirectory)
        {
            var normalizedPath = WorkspacePathUtilities.NormalizeLocalOrUnixPath(workingDirectory);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return UiLocalization.Text("ThreadImportForm.Value.UnknownWorkspace", "Unknown Workspace");
            }

            var trimmedPath = normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var directoryName = Path.GetFileName(trimmedPath);
            return string.IsNullOrWhiteSpace(directoryName) ? normalizedPath : directoryName;
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
                AccessibleName = UiLocalization.Text("ThreadImportForm.Accessible.ThreadHistory", "Thread History"),
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                UseCompatibleStateImageBehavior = false,
                LabelWrap = false
            };
            UiTheme.ApplyListView(listView);

            listView.Columns.Add(UiLocalization.Text("ThreadImportForm.Column.Title", "Title"), 260, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ThreadImportForm.Column.Workspace", "Workspace"), 360, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ThreadImportForm.Column.Source", "Source"), 90, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ThreadImportForm.Column.UpdatedAt", "Updated At"), 140, HorizontalAlignment.Left);
            return listView;
        }
    }
}
