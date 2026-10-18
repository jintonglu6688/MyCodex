using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public class ThreadAssociationForm : Form
    {
        private readonly IAppServerConnectionClient _connectionClient;
        private readonly IAppServerThreadClient _threadClient;
        private readonly List<ChatSession> _existingSessions;
        private readonly WorkspaceInfo _targetWorkspace;
        private readonly HashSet<string> _ignoredThreadIds;
        private readonly ComboBox _filterComboBox;
        private readonly ListView _threadListView;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _selectAllButton;
        private readonly Button _clearSelectionButton;
        private readonly Button _associateButton;
        private readonly Button _cancelButton;
        private List<ThreadAssociationCandidate> _candidates = new List<ThreadAssociationCandidate>();
        private bool _isLoading;
        private bool _isBinding;

        public ThreadAssociationForm(
            IAppServerClient appServerClient,
            IEnumerable<ChatSession> existingSessions,
            WorkspaceInfo targetWorkspace,
            ISet<string> ignoredThreadIds = null)
            : this(appServerClient, appServerClient, existingSessions, targetWorkspace, ignoredThreadIds)
        {
        }

        internal ThreadAssociationForm(
            IAppServerConnectionClient connectionClient,
            IAppServerThreadClient threadClient,
            IEnumerable<ChatSession> existingSessions,
            WorkspaceInfo targetWorkspace,
            ISet<string> ignoredThreadIds = null)
        {
            _connectionClient = connectionClient ?? throw new ArgumentNullException(nameof(connectionClient));
            _threadClient = threadClient ?? throw new ArgumentNullException(nameof(threadClient));
            _existingSessions = new List<ChatSession>(existingSessions ?? Enumerable.Empty<ChatSession>());
            _targetWorkspace = targetWorkspace ?? throw new ArgumentNullException(nameof(targetWorkspace));
            _ignoredThreadIds = new HashSet<string>(ignoredThreadIds ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);

            Text = UiLocalization.Text("ThreadAssociationForm.Title", "Import Other Sessions");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 580);
            Size = new Size(1040, 680);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _filterComboBox = CreateFilterComboBox();
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
                UiLocalization.Text("ThreadAssociationForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("ThreadAssociationForm.Accessible.Refresh", "Refresh"),
                96);
            _selectAllButton = CreateButton(
                UiLocalization.Text("ThreadAssociationForm.Button.SelectAll", "Select All(&A)"),
                UiLocalization.Text("ThreadAssociationForm.Accessible.SelectAll", "Select All"),
                112);
            _clearSelectionButton = CreateButton(
                UiLocalization.Text("ThreadAssociationForm.Button.ClearSelection", "Clear Selection(&N)"),
                UiLocalization.Text("ThreadAssociationForm.Accessible.ClearSelection", "Clear Selection"),
                144);
            _associateButton = CreateButton(
                UiLocalization.Text("ThreadAssociationForm.Button.Associate", "Import(&S)"),
                UiLocalization.Text("ThreadAssociationForm.Accessible.Associate", "Import"),
                112);
            _cancelButton = CreateButton(
                UiLocalization.Text("ThreadAssociationForm.Button.Close", "Close(&C)"),
                UiLocalization.Text("ThreadAssociationForm.Accessible.Close", "Close"),
                96);

            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_selectAllButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_clearSelectionButton, UiGlyph.Cancel);
            UiTheme.ApplyButton(_associateButton, UiGlyph.Download);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);

            _filterComboBox.SelectedIndexChanged += FilterComboBox_SelectedIndexChanged;
            _refreshButton.Click += RefreshButton_Click;
            _selectAllButton.Click += SelectAllButton_Click;
            _clearSelectionButton.Click += ClearSelectionButton_Click;
            _associateButton.Click += AssociateButton_Click;
            _cancelButton.Click += CancelButton_Click;
            _threadListView.ItemChecked += ThreadListView_ItemChecked;
            _threadListView.KeyDown += ThreadListView_KeyDown;
            KeyDown += ThreadAssociationForm_KeyDown;

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

                var candidate = item.Tag as ThreadAssociationCandidate;
                if (candidate != null && candidate.IsLinkable && candidate.Thread != null)
                {
                    result.Add(candidate.Thread);
                }
            }

            return result;
        }

        internal int VisibleItemCountForTests
        {
            get { return _threadListView.Items.Count; }
        }

        internal ThreadAssociationFilterMode SelectedFilterModeForTests
        {
            get { return GetSelectedFilterMode(); }
        }

        internal string FirstStatusTextForTests
        {
            get { return _threadListView.Items.Count == 0 ? string.Empty : _threadListView.Items[0].SubItems[4].Text; }
        }

        internal void SelectFilterForTests(ThreadAssociationFilterMode filterMode)
        {
            for (var i = 0; i < _filterComboBox.Items.Count; i++)
            {
                var item = _filterComboBox.Items[i] as FilterChoiceItem;
                if (item != null && item.Mode == filterMode)
                {
                    _filterComboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        internal void BindThreadsForTests(IEnumerable<AppServerThreadSummary> threads)
        {
            BindThreads(threads);
        }

        private async void OnShown(object sender, EventArgs e)
        {
            await LoadThreadsAsync();
            FocusThreadList();
        }

        private async System.Threading.Tasks.Task LoadThreadsAsync()
        {
            if (_isLoading)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("ThreadAssociationForm.Action.LoadThreads", "Load sessions")))
            {
                return;
            }

            _isLoading = true;
            SetStatus(UiLocalization.Text("ThreadAssociationForm.Status.Loading", "Loading local sessions..."));
            ToggleButtons(false);
            try
            {
                BindThreads(await _threadClient.ListThreadsAsync());
                SetStatus(_threadListView.Items.Count == 0
                    ? UiLocalization.Text("ThreadAssociationForm.Status.NoSessions", "No sessions match the current filter.")
                    : UiLocalization.Text("ThreadAssociationForm.Status.SelectSessions", "Select sessions to import into the current workspace."));
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("ThreadAssociationForm.Status.LoadFailed", "Failed to load sessions."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _isLoading = false;
                ToggleButtons(true);
                FocusThreadList();
            }
        }

        private void BindThreads(IEnumerable<AppServerThreadSummary> threads)
        {
            _candidates = ThreadAssociationPlanner.BuildCandidates(
                    threads,
                    _existingSessions,
                    _targetWorkspace.Id,
                    _ignoredThreadIds)
                .OrderByDescending(candidate => candidate.Thread.UpdatedAt)
                .ToList();
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            _isBinding = true;
            _threadListView.BeginUpdate();
            _threadListView.Items.Clear();
            var filterMode = GetSelectedFilterMode();
            foreach (var candidate in _candidates)
            {
                if (!ThreadAssociationPlanner.IsVisible(candidate, filterMode))
                {
                    continue;
                }

                var thread = candidate.Thread;
                var item = new ListViewItem(new[]
                {
                    GetDisplayTitle(thread),
                    thread.WorkingDirectory,
                    string.IsNullOrWhiteSpace(thread.Source)
                        ? UiLocalization.Text("ThreadAssociationForm.Value.Unknown", "(Unknown)")
                        : thread.Source,
                    thread.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                    GetStatusText(candidate.Status)
                });
                item.Tag = candidate;
                item.Checked = false;
                _threadListView.Items.Add(item);
            }

            _threadListView.EndUpdate();
            _isBinding = false;
            UpdateAssociateButtonState();
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
                Text = UiLocalization.Text("ThreadAssociationForm.Header", "Import sessions into current workspace"),
                Dock = DockStyle.Top,
                Height = 32,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var filterPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 42,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var filterLabel = new Label
            {
                Text = UiLocalization.Text("ThreadAssociationForm.Label.Filter", "Filter(&F)"),
                AccessibleName = UiLocalization.Text("ThreadAssociationForm.Accessible.Filter", "Filter"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(filterLabel, true);
            filterPanel.Controls.Add(filterLabel, 0, 0);
            filterPanel.Controls.Add(_filterComboBox, 1, 0);

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
                Width = 620,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            buttonsPanel.Controls.Add(_refreshButton);
            buttonsPanel.Controls.Add(_selectAllButton);
            buttonsPanel.Controls.Add(_clearSelectionButton);
            buttonsPanel.Controls.Add(_associateButton);
            buttonsPanel.Controls.Add(_cancelButton);

            footerPanel.Controls.Add(buttonsPanel);
            footerPanel.Controls.Add(_statusLabel);

            mainPanel.Controls.Add(_threadListView);
            mainPanel.Controls.Add(footerPanel);
            mainPanel.Controls.Add(filterPanel);
            mainPanel.Controls.Add(title);
            Controls.Add(mainPanel);
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            await LoadThreadsAsync();
        }

        private void SelectAllButton_Click(object sender, EventArgs e)
        {
            SetAllLinkableCheckedState(true);
        }

        private void ClearSelectionButton_Click(object sender, EventArgs e)
        {
            SetAllLinkableCheckedState(false);
        }

        private void AssociateButton_Click(object sender, EventArgs e)
        {
            if (GetSelectedThreads().Count == 0)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("ThreadAssociationForm.Message.SelectAtLeastOne", "Please select at least one session to import."),
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

        private void FilterComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_candidates != null)
            {
                ApplyFilter();
            }
        }

        private void ThreadListView_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (_isBinding || e == null || e.Item == null)
            {
                return;
            }

            var candidate = e.Item.Tag as ThreadAssociationCandidate;
            if (candidate != null && !candidate.IsLinkable && e.Item.Checked)
            {
                e.Item.Checked = false;
                return;
            }

            UpdateAssociateButtonState();
        }

        private void ThreadListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !_isLoading && GetSelectedThreads().Count > 0)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                AssociateButton_Click(_associateButton, EventArgs.Empty);
            }
        }

        private async void ThreadAssociationForm_KeyDown(object sender, KeyEventArgs e)
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

        private void SetAllLinkableCheckedState(bool isChecked)
        {
            foreach (ListViewItem item in _threadListView.Items)
            {
                var candidate = item.Tag as ThreadAssociationCandidate;
                item.Checked = candidate != null && candidate.IsLinkable && isChecked;
            }

            UpdateAssociateButtonState();
        }

        private void UpdateAssociateButtonState()
        {
            if (_isLoading)
            {
                return;
            }

            _associateButton.Enabled = GetSelectedThreads().Count > 0;
        }

        private void ToggleButtons(bool enabled)
        {
            _filterComboBox.Enabled = enabled;
            _refreshButton.Enabled = enabled;
            _selectAllButton.Enabled = enabled;
            _clearSelectionButton.Enabled = enabled;
            _associateButton.Enabled = enabled && GetSelectedThreads().Count > 0;
            _cancelButton.Enabled = enabled;
        }

        private void FocusThreadList()
        {
            this.BeginInvokeSafe(() =>
            {
                if (_threadListView.Items.Count > 0 && _threadListView.SelectedItems.Count == 0)
                {
                    _threadListView.Items[0].Selected = true;
                }

                _threadListView.Focus();
            });
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
                    UiLocalization.Text("ThreadAssociationForm.Status.ActionFailedDisconnected", "{0} failed because App Server is not connected."),
                    actionName));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private ThreadAssociationFilterMode GetSelectedFilterMode()
        {
            var item = _filterComboBox.SelectedItem as FilterChoiceItem;
            return item == null ? ThreadAssociationFilterMode.Linkable : item.Mode;
        }

        private static string GetDisplayTitle(AppServerThreadSummary thread)
        {
            if (thread == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(thread.Title))
            {
                return thread.Title;
            }

            if (!string.IsNullOrWhiteSpace(thread.Preview))
            {
                var preview = thread.Preview.Trim();
                return preview.Length > 50 ? preview.Substring(0, 50) + "..." : preview;
            }

            return thread.ThreadId ?? string.Empty;
        }

        private static string GetStatusText(ThreadAssociationStatus status)
        {
            switch (status)
            {
                case ThreadAssociationStatus.AssociatedCurrentWorkspace:
                    return UiLocalization.Text("ThreadAssociationForm.StatusValue.CurrentWorkspace", "Already imported into current workspace");
                case ThreadAssociationStatus.AssociatedElsewhere:
                    return UiLocalization.Text("ThreadAssociationForm.StatusValue.OtherWorkspace", "Already imported elsewhere");
                default:
                    return UiLocalization.Text("ThreadAssociationForm.StatusValue.Unassociated", "Unassociated");
            }
        }

        private static Button CreateButton(string text, string accessibleName, int width)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = width,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true
            };
        }

        private static ComboBox CreateFilterComboBox()
        {
            var comboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("ThreadAssociationForm.Accessible.Filter", "Filter"),
                TabIndex = 0
            };
            UiTheme.ApplyComboBox(comboBox);
            comboBox.Items.Add(new FilterChoiceItem(
                ThreadAssociationFilterMode.Linkable,
                UiLocalization.Text("ThreadAssociationForm.Filter.Linkable", "Importable sessions")));
            comboBox.Items.Add(new FilterChoiceItem(
                ThreadAssociationFilterMode.All,
                UiLocalization.Text("ThreadAssociationForm.Filter.All", "All sessions")));
            comboBox.Items.Add(new FilterChoiceItem(
                ThreadAssociationFilterMode.Unassociated,
                UiLocalization.Text("ThreadAssociationForm.Filter.Unassociated", "Unassociated sessions")));
            comboBox.Items.Add(new FilterChoiceItem(
                ThreadAssociationFilterMode.AssociatedElsewhere,
                UiLocalization.Text("ThreadAssociationForm.Filter.OtherWorkspace", "Already imported elsewhere")));
            comboBox.Items.Add(new FilterChoiceItem(
                ThreadAssociationFilterMode.AssociatedCurrentWorkspace,
                UiLocalization.Text("ThreadAssociationForm.Filter.CurrentWorkspace", "Already imported into current workspace")));
            comboBox.SelectedIndex = 0;
            return comboBox;
        }

        private static ListView CreateThreadListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                AccessibleName = UiLocalization.Text("ThreadAssociationForm.Accessible.Sessions", "Sessions"),
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                UseCompatibleStateImageBehavior = false,
                LabelWrap = false,
                TabIndex = 1
            };
            UiTheme.ApplyListView(listView);

            listView.Columns.Add(UiLocalization.Text("ThreadAssociationForm.Column.Title", "Title"), 260, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ThreadAssociationForm.Column.Workspace", "Original Workspace"), 360, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ThreadAssociationForm.Column.Source", "Source"), 90, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ThreadAssociationForm.Column.UpdatedAt", "Updated At"), 140, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("ThreadAssociationForm.Column.Status", "Status"), 180, HorizontalAlignment.Left);
            return listView;
        }

        private sealed class FilterChoiceItem
        {
            public FilterChoiceItem(ThreadAssociationFilterMode mode, string text)
            {
                Mode = mode;
                Text = text ?? string.Empty;
            }

            public ThreadAssociationFilterMode Mode { get; }

            private string Text { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
