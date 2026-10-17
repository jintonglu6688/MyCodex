using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal enum RuntimeFileBrowserSelectionMode
    {
        Directory,
        File
    }

    internal sealed class RuntimeFileBrowserSelectionEventArgs : EventArgs
    {
        public RuntimeFileBrowserSelectionEventArgs(IEnumerable<RuntimeFileBrowserSelectedPath> selectedPaths)
        {
            SelectedPaths = (selectedPaths ?? Enumerable.Empty<RuntimeFileBrowserSelectedPath>()).ToList();
            var first = SelectedPaths.FirstOrDefault();
            Path = first == null ? null : first.Path;
            IsDirectory = first != null && first.IsDirectory;
        }

        public string Path { get; }

        public bool IsDirectory { get; }

        public IReadOnlyList<RuntimeFileBrowserSelectedPath> SelectedPaths { get; }
    }

    internal sealed class RuntimeFileBrowserSelectedPath
    {
        public RuntimeFileBrowserSelectedPath(string path, bool isDirectory)
        {
            Path = path;
            IsDirectory = isDirectory;
        }

        public string Path { get; }

        public bool IsDirectory { get; }
    }

    internal sealed class RuntimeFileBrowserForm : Form
    {
        private readonly Func<string, Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>>> _listDirectoryAsync;
        private readonly RuntimeFileBrowserSelectionMode _selectionMode;
        private readonly bool _allowMultiSelect;
        private readonly TextBox _pathTextBox;
        private readonly ToolStrip _navigationToolStrip;
        private readonly ToolStripButton _goButton;
        private readonly ToolStripButton _homeButton;
        private readonly ToolStripButton _upButton;
        private readonly ToolStripButton _refreshButton;
        private readonly ListView _entryListView;
        private readonly TextBox _targetTextBox;
        private readonly Label _statusLabel;
        private readonly Button _selectButton;
        private readonly Button _closeButton;
        private string _currentPath;
        private bool _isLoading;

        internal bool IsBusyForRuntimeTransition
        {
            get { return _isLoading; }
        }

        public RuntimeFileBrowserForm(
            string title,
            string initialPath,
            RuntimeFileBrowserSelectionMode selectionMode,
            Func<string, Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>>> listDirectoryAsync,
            bool allowMultiSelect = false)
        {
            if (listDirectoryAsync == null)
            {
                throw new ArgumentNullException(nameof(listDirectoryAsync));
            }

            _selectionMode = selectionMode;
            _allowMultiSelect = allowMultiSelect;
            _listDirectoryAsync = listDirectoryAsync;
            _currentPath = NormalizeInitialPath(initialPath);

            Text = string.IsNullOrWhiteSpace(title)
                ? UiLocalization.Text("RuntimeFileBrowserForm.Title", "Browse Runtime Files")
                : title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(760, 520);
            MinimumSize = new Size(640, 420);
            UiTheme.ApplyForm(this);

            _pathTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("RuntimeFileBrowserForm.Accessible.Address", "Address"),
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_pathTextBox, false, false);
            _pathTextBox.KeyDown += PathTextBox_KeyDown;

            _navigationToolStrip = CreateNavigationToolStrip();
            _goButton = CreateToolStripButton(
                UiLocalization.Text("RuntimeFileBrowserForm.Button.Go", "Go(&G)"),
                UiLocalization.Text("RuntimeFileBrowserForm.Accessible.Go", "Go"));
            _homeButton = CreateToolStripButton(
                UiLocalization.Text("RuntimeFileBrowserForm.Button.Home", "Home(&H)"),
                UiLocalization.Text("RuntimeFileBrowserForm.Accessible.Home", "Home"));
            _upButton = CreateToolStripButton(
                UiLocalization.Text("RuntimeFileBrowserForm.Button.Up", "Up(&U)"),
                UiLocalization.Text("RuntimeFileBrowserForm.Accessible.Up", "Up"));
            _refreshButton = CreateToolStripButton(
                UiLocalization.Text("RuntimeFileBrowserForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("RuntimeFileBrowserForm.Accessible.Refresh", "Refresh"));
            _navigationToolStrip.Items.AddRange(new ToolStripItem[]
            {
                _goButton,
                _homeButton,
                _upButton,
                new ToolStripSeparator(),
                _refreshButton
            });

            _entryListView = CreateEntryListView();
            _targetTextBox = new TextBox
            {
                AccessibleName = _selectionMode == RuntimeFileBrowserSelectionMode.Directory
                    ? UiLocalization.Text("RuntimeFileBrowserForm.Accessible.TargetFolder", "Folder")
                    : UiLocalization.Text("RuntimeFileBrowserForm.Accessible.TargetFile", "File"),
                ReadOnly = true,
                TabIndex = 6,
                TabStop = true
            };
            UiTheme.ApplyTextBox(_targetTextBox, false, true);
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, false);

            _selectButton = CreateButton(
                _selectionMode == RuntimeFileBrowserSelectionMode.File
                    ? UiLocalization.Text("RuntimeFileBrowserForm.Button.SelectFile", "Select File(&S)")
                    : UiLocalization.Text("RuntimeFileBrowserForm.Button.SelectFolder", "Select Folder(&S)"),
                UiLocalization.Text("RuntimeFileBrowserForm.Accessible.Select", "Select"),
                _selectionMode == RuntimeFileBrowserSelectionMode.File ? UiGlyph.Attach : UiGlyph.Ok,
                7);
            _closeButton = CreateButton(
                UiLocalization.Text("RuntimeFileBrowserForm.Button.Close", "Close(&C)"),
                UiLocalization.Text("RuntimeFileBrowserForm.Accessible.Close", "Close"),
                UiGlyph.Cancel,
                8);

            _goButton.Click += async (sender, e) => await LoadEntriesAsync(_pathTextBox.Text);
            _homeButton.Click += async (sender, e) => await LoadEntriesAsync(NormalizeInitialPath(initialPath));
            _upButton.Click += async (sender, e) => await NavigateUpAsync();
            _refreshButton.Click += async (sender, e) => await LoadEntriesAsync(_currentPath, GetSelectedEntryName());
            _selectButton.Click += (sender, e) => SelectCurrentTarget();
            _closeButton.Click += (sender, e) => Close();
            _entryListView.ItemActivate += async (sender, e) => await ActivateSelectedEntryAsync();
            _entryListView.KeyDown += EntryListView_KeyDown;
            _entryListView.SelectedIndexChanged += (sender, e) => UpdateTargetText();

            CancelButton = _closeButton;
            BuildLayout();
        }

        public event EventHandler<RuntimeFileBrowserSelectionEventArgs> PathSelected;

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = _entryListView;
            await LoadEntriesAsync(_currentPath);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Alt | Keys.D))
            {
                _pathTextBox.Focus();
                _pathTextBox.SelectAll();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async Task LoadEntriesAsync(string path, string childToSelect = null, bool forceListFocus = false)
        {
            if (_isLoading)
            {
                return;
            }

            var normalizedPath = NormalizeTypedPath(path);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                ShowValidationMessage(UiLocalization.Text("RuntimeFileBrowserForm.Message.PathRequired", "Please enter a path."));
                return;
            }

            if (!WorkspacePathUtilities.LooksLikeUnixAbsolutePath(normalizedPath))
            {
                ShowValidationMessage(UiLocalization.Text("RuntimeFileBrowserForm.Message.AbsolutePathRequired", "Please enter an absolute Unix path, such as /home/name/project or /Users/name/project."));
                return;
            }

            SetLoadingState(true);
            try
            {
                var entries = await _listDirectoryAsync(normalizedPath);
                _currentPath = WorkspacePathUtilities.NormalizeUnixAbsolutePath(normalizedPath);
                _pathTextBox.Text = _currentPath;
                PopulateEntries(entries ?? new List<RuntimeFileSystemEntrySnapshot>(), childToSelect);
                UpdateNavigationState();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _statusLabel.Text = UiLocalization.Text("RuntimeFileBrowserForm.Status.LoadFailed", "Unable to load this path.");
            }
            finally
            {
                SetLoadingState(false);
                if (forceListFocus || ActiveControl == null || (!_entryListView.Focused && ActiveControl != _entryListView))
                {
                    BeginInvoke(new Action(FocusEntryList));
                }
            }
        }

        private void PopulateEntries(IReadOnlyList<RuntimeFileSystemEntrySnapshot> entries, string childToSelect)
        {
            _entryListView.BeginUpdate();
            try
            {
                _entryListView.Items.Clear();
                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Name) || string.IsNullOrWhiteSpace(entry.FullPath))
                    {
                        continue;
                    }

                    if (_selectionMode == RuntimeFileBrowserSelectionMode.Directory && !entry.IsDirectory)
                    {
                        continue;
                    }

                    var item = new ListViewItem(entry.Name)
                    {
                        Tag = entry
                    };
                    item.SubItems.Add(entry.IsDirectory
                        ? UiLocalization.Text("RuntimeFileBrowserForm.EntryType.Folder", "Folder")
                        : UiLocalization.Text("RuntimeFileBrowserForm.EntryType.File", "File"));
                    item.SubItems.Add(entry.IsDirectory ? string.Empty : FormatSize(entry.Size));
                    item.SubItems.Add(FormatModifiedAt(entry.ModifiedAt));
                    item.SubItems.Add(entry.FullPath);
                    _entryListView.Items.Add(item);
                }
            }
            finally
            {
                _entryListView.EndUpdate();
            }

            FocusInitialItem(childToSelect);
            UpdateTargetText();
            _statusLabel.Text = string.Format(
                UiLocalization.Text("RuntimeFileBrowserForm.Status.Count.Template", "{0} items. Enter opens folders; select confirms the target."),
                _entryListView.Items.Count);
        }

        private void FocusInitialItem(string childToSelect)
        {
            ListViewItem itemToFocus = null;
            if (!string.IsNullOrWhiteSpace(childToSelect))
            {
                itemToFocus = _entryListView.Items
                    .Cast<ListViewItem>()
                    .FirstOrDefault(item => string.Equals(item.Text, childToSelect, StringComparison.OrdinalIgnoreCase));
            }

            if (itemToFocus == null && _entryListView.Items.Count > 0)
            {
                itemToFocus = _entryListView.Items[0];
            }

            if (itemToFocus == null)
            {
                return;
            }

            foreach (var item in _entryListView.SelectedItems.Cast<ListViewItem>().ToList())
            {
                item.Selected = false;
            }

            itemToFocus.Focused = true;
            itemToFocus.EnsureVisible();
        }

        private void FocusEntryList()
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            ActiveControl = _entryListView;
            _entryListView.Focus();
            if (_entryListView.FocusedItem == null && _entryListView.Items.Count > 0)
            {
                _entryListView.Items[0].Focused = true;
                _entryListView.Items[0].EnsureVisible();
            }
        }

        private async Task ActivateSelectedEntryAsync()
        {
            var entry = GetSelectedEntry() ?? GetFocusedEntry();
            if (entry == null)
            {
                return;
            }

            if (entry.IsDirectory)
            {
                await LoadEntriesAsync(entry.FullPath);
                return;
            }

            if (_selectionMode == RuntimeFileBrowserSelectionMode.File)
            {
                SelectPath(entry.FullPath, false);
            }
        }

        private void SelectCurrentTarget()
        {
            var selectedEntries = GetSelectedEntries();
            if (_selectionMode == RuntimeFileBrowserSelectionMode.File)
            {
                if (selectedEntries.Count == 0)
                {
                    ShowValidationMessage(UiLocalization.Text("RuntimeFileBrowserForm.Message.SelectFile", "Please select a file."));
                    return;
                }

                if (selectedEntries.Any(entry => entry.IsDirectory))
                {
                    ShowValidationMessage(UiLocalization.Text("RuntimeFileBrowserForm.Message.SelectFilesOnly", "Please select files only."));
                    return;
                }

                SelectPaths(selectedEntries.Select(entry => new RuntimeFileBrowserSelectedPath(entry.FullPath, false)));
                return;
            }

            var selectedDirectories = selectedEntries.Where(entry => entry.IsDirectory).ToList();
            if (selectedDirectories.Count > 0)
            {
                SelectPaths(selectedDirectories.Select(entry => new RuntimeFileBrowserSelectedPath(entry.FullPath, true)));
                return;
            }

            SelectPath(_currentPath, true);
        }

        private void SelectPath(string path, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            SelectPaths(new[] { new RuntimeFileBrowserSelectedPath(path, isDirectory) });
        }

        private void SelectPaths(IEnumerable<RuntimeFileBrowserSelectedPath> selectedPaths)
        {
            var normalizedPaths = (selectedPaths ?? Enumerable.Empty<RuntimeFileBrowserSelectedPath>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => new RuntimeFileBrowserSelectedPath(WorkspacePathUtilities.NormalizeUnixAbsolutePath(item.Path), item.IsDirectory))
                .ToList();
            if (normalizedPaths.Count == 0)
            {
                return;
            }

            PathSelected?.Invoke(this, new RuntimeFileBrowserSelectionEventArgs(normalizedPaths));
            Close();
        }

        private async Task NavigateUpAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentPath) || string.Equals(_currentPath, "/", StringComparison.Ordinal))
            {
                return;
            }

            var trimmed = _currentPath.TrimEnd('/');
            var childName = trimmed.Substring(trimmed.LastIndexOf('/') + 1);
            var slashIndex = trimmed.LastIndexOf('/');
            var parentPath = slashIndex <= 0 ? "/" : trimmed.Substring(0, slashIndex);
            await LoadEntriesAsync(parentPath, childName);
        }

        private void EntryListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && _selectionMode == RuntimeFileBrowserSelectionMode.Directory && GetSelectedEntry() == null)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SelectPath(_currentPath, true);
                return;
            }

            if (e.KeyCode == Keys.Enter && _selectionMode == RuntimeFileBrowserSelectionMode.Directory && _allowMultiSelect && _entryListView.SelectedItems.Count > 1)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SelectCurrentTarget();
                return;
            }

            if (e.KeyCode == Keys.Enter && _selectionMode == RuntimeFileBrowserSelectionMode.File && _allowMultiSelect && _entryListView.SelectedItems.Count > 1)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SelectCurrentTarget();
                return;
            }

            if (e.KeyCode == Keys.Back)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _ = NavigateUpAsync();
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _ = LoadEntriesAsync(_currentPath, GetSelectedEntryName());
            }
        }

        private async void PathTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            await LoadEntriesAsync(_pathTextBox.Text);
        }

        private RuntimeFileSystemEntrySnapshot GetSelectedEntry()
        {
            return _entryListView.SelectedItems.Count > 0
                ? _entryListView.SelectedItems[0].Tag as RuntimeFileSystemEntrySnapshot
                : null;
        }

        private RuntimeFileSystemEntrySnapshot GetFocusedEntry()
        {
            return _entryListView.FocusedItem != null
                ? _entryListView.FocusedItem.Tag as RuntimeFileSystemEntrySnapshot
                : null;
        }

        private IReadOnlyList<RuntimeFileSystemEntrySnapshot> GetSelectedEntries()
        {
            return _entryListView.SelectedItems
                .Cast<ListViewItem>()
                .Select(item => item.Tag as RuntimeFileSystemEntrySnapshot)
                .Where(entry => entry != null)
                .ToList();
        }

        private string GetSelectedEntryName()
        {
            var entry = GetSelectedEntry();
            return entry == null ? null : entry.Name;
        }

        private void UpdateTargetText()
        {
            if (_selectionMode == RuntimeFileBrowserSelectionMode.Directory)
            {
                var entries = GetSelectedEntries().Where(entry => entry.IsDirectory).ToList();
                TextBoxCaretHelper.SetTextAndMoveToStart(
                    _targetTextBox,
                    entries.Count > 0
                        ? FormatTargetNames(entries)
                        : GetPathName(_currentPath));
                return;
            }

            var fileEntries = GetSelectedEntries().Where(entry => !entry.IsDirectory).ToList();
            TextBoxCaretHelper.SetTextAndMoveToStart(
                _targetTextBox,
                fileEntries.Count > 0 ? FormatTargetNames(fileEntries) : string.Empty);
        }

        private void SetLoadingState(bool isLoading)
        {
            _isLoading = isLoading;
            UseWaitCursor = isLoading;
            _goButton.Enabled = !isLoading;
            _homeButton.Enabled = !isLoading;
            _refreshButton.Enabled = !isLoading;
            _selectButton.Enabled = !isLoading;
            UpdateNavigationState();
            if (isLoading)
            {
                _statusLabel.Text = UiLocalization.Text("RuntimeFileBrowserForm.Status.Loading", "Loading...");
            }
        }

        private void UpdateNavigationState()
        {
            _upButton.Enabled = !_isLoading &&
                                !string.IsNullOrWhiteSpace(_currentPath) &&
                                !string.Equals(_currentPath, "/", StringComparison.Ordinal);
        }

        private void ShowValidationMessage(string message)
        {
            MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            _pathTextBox.Focus();
        }

        private static string NormalizeInitialPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? "/home" : NormalizeTypedPath(path);
        }

        private static string NormalizeTypedPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        }

        private static string GetPathName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var trimmed = path.TrimEnd('/');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return "/";
            }

            var slashIndex = trimmed.LastIndexOf('/');
            return slashIndex >= 0 ? trimmed.Substring(slashIndex + 1) : trimmed;
        }

        private static string FormatTargetNames(IReadOnlyList<RuntimeFileSystemEntrySnapshot> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return string.Empty;
            }

            return string.Join("; ", entries.Select(entry => entry.Name));
        }

        private static string FormatModifiedAt(DateTime? modifiedAt)
        {
            return modifiedAt.HasValue ? modifiedAt.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty;
        }

        private static string FormatSize(long? size)
        {
            if (!size.HasValue || size.Value < 0)
            {
                return string.Empty;
            }

            var value = (double)size.Value;
            var units = new[] { "B", "KB", "MB", "GB", "TB" };
            var unitIndex = 0;
            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? string.Format("{0:0} {1}", value, units[unitIndex])
                : string.Format("{0:0.#} {1}", value, units[unitIndex]);
        }

        private Button CreateButton(string text, string accessibleName, UiGlyph glyph, int tabIndex)
        {
            var button = new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                Width = 104,
                Height = 32,
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
            UiTheme.ApplyButton(button, glyph);
            return button;
        }

        private static ToolStrip CreateNavigationToolStrip()
        {
            var toolStrip = new ToolStrip
            {
                Dock = DockStyle.Fill,
                GripStyle = ToolStripGripStyle.Hidden,
                TabStop = false,
                RenderMode = ToolStripRenderMode.System
            };
            UiTheme.ApplyToolStrip(toolStrip);
            return toolStrip;
        }

        private static ToolStripButton CreateToolStripButton(string text, string accessibleName)
        {
            return new ToolStripButton
            {
                Text = text,
                AccessibleName = accessibleName,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoToolTip = false
            };
        }

        private ListView CreateEntryListView()
        {
            var listView = new ListView
            {
                AccessibleName = UiLocalization.Text("RuntimeFileBrowserForm.Accessible.Content", "Items"),
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = _allowMultiSelect,
                View = View.Details,
                TabIndex = 5,
                UseCompatibleStateImageBehavior = false
            };
            listView.Columns.Add(UiLocalization.Text("RuntimeFileBrowserForm.Column.Name", "Name"), 220);
            listView.Columns.Add(UiLocalization.Text("RuntimeFileBrowserForm.Column.Type", "Type"), 88);
            listView.Columns.Add(UiLocalization.Text("RuntimeFileBrowserForm.Column.Size", "Size"), 96);
            listView.Columns.Add(UiLocalization.Text("RuntimeFileBrowserForm.Column.Modified", "Modified"), 132);
            listView.Columns.Add(UiLocalization.Text("RuntimeFileBrowserForm.Column.Path", "Path"), 420);
            UiTheme.ApplyListView(listView);
            return listView;
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            var pathPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor
            };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

            var pathLabel = new Label
            {
                Text = UiLocalization.Text("RuntimeFileBrowserForm.Label.Address", "Address(&D):"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(pathLabel, true);
            pathLabel.UseMnemonic = true;

            pathPanel.Controls.Add(pathLabel, 0, 0);
            pathPanel.Controls.Add(_pathTextBox, 1, 0);

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                Width = 232,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor
            };
            buttonPanel.Controls.Add(_selectButton);

            root.Controls.Add(_navigationToolStrip, 0, 0);
            root.Controls.Add(pathPanel, 0, 1);
            root.Controls.Add(_entryListView, 0, 2);
            root.Controls.Add(CreateTargetPanel(), 0, 3);
            root.Controls.Add(_statusLabel, 0, 4);
            root.Controls.Add(buttonPanel, 0, 5);
            Controls.Add(root);
        }

        private Control CreateTargetPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var label = new Label
            {
                Dock = DockStyle.Fill,
                Text = _selectionMode == RuntimeFileBrowserSelectionMode.Directory
                    ? UiLocalization.Text("RuntimeFileBrowserForm.Label.TargetFolder", "Folder:")
                    : UiLocalization.Text("RuntimeFileBrowserForm.Label.TargetFile", "File:"),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);

            panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(_targetTextBox, 1, 0);
            return panel;
        }
    }
}
