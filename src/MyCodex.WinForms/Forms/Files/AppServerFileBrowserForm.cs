using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    internal sealed class AppServerFileBrowserPathChangedEventArgs : EventArgs
    {
        public AppServerFileBrowserPathChangedEventArgs(string currentPath)
        {
            CurrentPath = currentPath ?? string.Empty;
        }

        public string CurrentPath { get; private set; }
    }

    internal sealed partial class AppServerFileBrowserForm : Form, IAppServerFileWatchTarget
    {
        internal const string ClipboardFormat = "MyCodex.AppServerFileBrowserPaths";

        private readonly IAppServerFileSystemClient _client;
        private readonly IAppServerFuzzyFileSearchClient _searchClient;
        private readonly bool _isSearchSupported;
        private readonly Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>, Task> _attachPathsAsync;
        private readonly Action<IReadOnlyList<RuntimeFileBrowserSelectedPath>, string> _copiedPaths;
        private readonly Func<IReadOnlyList<string>, bool> _confirmDelete;
        private readonly Action<string> _announce;
        private readonly string _homePath;
        private Func<string, Task> _previewFileAsync;
        private readonly ToolStrip _navigationToolStrip;
        private readonly ToolStripButton _homeButton;
        private readonly ToolStripButton _upButton;
        private readonly ToolStripButton _refreshButton;
        private readonly TextBox _addressTextBox;
        private readonly ListView _itemListView;
        private readonly ContextMenu _itemContextMenu;
        private readonly MenuItem _newFolderMenuItem;
        private readonly MenuItem _newFileMenuItem;
        private readonly MenuItem _deleteMenuItem;
        private readonly MenuItem _previewMenuItem;
        private readonly MenuItem _copyMenuItem;
        private readonly MenuItem _pasteMenuItem;
        private readonly MenuItem _copyPathMenuItem;
        private readonly MenuItem _attachPathMenuItem;
        private readonly MenuItem _refreshMenuItem;
        private readonly ToolStripButton _newFolderButton;
        private readonly ToolStripButton _newFileButton;
        private readonly ToolStripButton _deleteButton;
        private readonly ToolStripButton _previewButton;
        private readonly ToolStripButton _copyButton;
        private readonly ToolStripButton _copyPathButton;
        private readonly ToolStripButton _attachButton;
        private readonly TextBox _searchTextBox;
        private readonly Button _searchButton;
        private readonly TextBox _selectionTextBox;
        private readonly Label _statusLabel;
        private readonly Func<IReadOnlyList<AppServerFileSystemEntry>> _windowsDriveRootProvider;
        private readonly AppServerFileBrowserMetadataLoader _metadataLoader;
        private readonly Timer _fileChangeTimer;
        private readonly HashSet<string> _pendingChangedPaths = new HashSet<string>(StringComparer.Ordinal);
        private string _currentPath;
        private bool _isLoading;
        private bool _isSearchMode;
        private bool _isModalDialogActive;
        private BrowserEntry _lastFocusedEntry;

        internal bool IsBusyForRuntimeTransition
        {
            get { return _isLoading || _isModalDialogActive; }
        }
        private int _metadataLoadGeneration;
        private Task _metadataLoadTask = Task.CompletedTask;
        private bool _pendingFileRefresh;
        private bool _pendingFullMetadataRefresh;
        private int _activeColumnIndex;

        public event EventHandler<AppServerFileBrowserPathChangedEventArgs> CurrentPathChanged;

        public AppServerFileBrowserForm(
            IAppServerFileSystemClient client,
            string initialPath,
            Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>, Task> attachPathsAsync,
            Action<string> announce)
            : this(client, initialPath, attachPathsAsync, null, announce, null)
        {
        }

        public AppServerFileBrowserForm(
            IAppServerFileSystemClient client,
            string initialPath,
            Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>, Task> attachPathsAsync,
            Action<IReadOnlyList<RuntimeFileBrowserSelectedPath>, string> copiedPaths,
            Action<string> announce)
            : this(client, initialPath, attachPathsAsync, copiedPaths, announce, null)
        {
        }

        public AppServerFileBrowserForm(
            IAppServerFileSystemClient client,
            string initialPath,
            Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>, Task> attachPathsAsync,
            Action<IReadOnlyList<RuntimeFileBrowserSelectedPath>, string> copiedPaths,
            Action<string> announce,
            Func<IReadOnlyList<string>, bool> confirmDelete)
            : this(client, initialPath, attachPathsAsync, copiedPaths, announce, confirmDelete, GetWindowsDriveRootEntries, null)
        {
        }

        private AppServerFileBrowserForm(
            IAppServerFileSystemClient client,
            string initialPath,
            Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>, Task> attachPathsAsync,
            Action<IReadOnlyList<RuntimeFileBrowserSelectedPath>, string> copiedPaths,
            Action<string> announce,
            Func<IReadOnlyList<string>, bool> confirmDelete,
            Func<IReadOnlyList<AppServerFileSystemEntry>> windowsDriveRootProvider,
            Func<string, Task> previewFileAsync)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _metadataLoader = new AppServerFileBrowserMetadataLoader(_client);
            _fileChangeTimer = new Timer { Interval = 300 };
            _fileChangeTimer.Tick += async (sender, e) => await FlushPendingFileChangesAsync();
            _searchClient = client as IAppServerFuzzyFileSearchClient;
            var appServerClient = client as IAppServerClient;
            _isSearchSupported = _searchClient != null &&
                                 (appServerClient == null || AppServerProtocolFeatureSupport.IsSupported(
                                     appServerClient.ProtocolCapabilities,
                                     AppServerProtocolFeature.FuzzyFileSearch));
            _homePath = string.IsNullOrWhiteSpace(initialPath) ? string.Empty : initialPath.Trim();
            _currentPath = _homePath;
            _attachPathsAsync = attachPathsAsync;
            _copiedPaths = copiedPaths;
            _confirmDelete = confirmDelete ?? ConfirmDelete;
            _announce = announce;
            _windowsDriveRootProvider = windowsDriveRootProvider ?? GetWindowsDriveRootEntries;
            _previewFileAsync = previewFileAsync ?? PreviewFileWithOwnedWindowAsync;

            UpdateWindowTitle();
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = true;
            ClientSize = new Size(860, 560);
            MinimumSize = new Size(700, 440);
            UiTheme.ApplyForm(this);

            _navigationToolStrip = CreateNavigationToolStrip();
            _homeButton = CreateToolStripButton(
                UiLocalization.Text("AppServerFileBrowserForm.Button.Home", "Home(&H)"),
                UiLocalization.Text("AppServerFileBrowserForm.Accessible.Home", "Home"));
            _upButton = CreateToolStripButton(
                UiLocalization.Text("AppServerFileBrowserForm.Button.Up", "Up(&U)"),
                UiLocalization.Text("AppServerFileBrowserForm.Accessible.Up", "Up"));
            _refreshButton = CreateToolStripButton(
                UiLocalization.Text("AppServerFileBrowserForm.Button.Refresh", "Refresh(&R)"),
                UiLocalization.Text("AppServerFileBrowserForm.Accessible.Refresh", "Refresh"));
            _newFolderButton = CreateToolStripButton(
                UiLocalization.Text("AppServerFileBrowserForm.Menu.NewFolder", "New Folder(&F)..."),
                UiLocalization.Text("AppServerFileBrowserForm.Accessible.NewFolder", "New Folder"));
            _newFileButton = CreateToolStripButton(
                UiLocalization.Text("AppServerFileBrowserForm.Menu.NewFile", "New File(&N)..."),
                UiLocalization.Text("AppServerFileBrowserForm.Accessible.NewFile", "New File"));
            _deleteButton = CreateToolStripButton(
                UiLocalization.Text("AppServerFileBrowserForm.Menu.Delete", "Delete(&D)"),
                UiLocalization.Text("AppServerFileBrowserForm.Accessible.Delete", "Delete"));
            _previewButton = CreateToolStripButton(
                UiLocalization.Text("AppServerFileBrowserForm.Button.Preview", "Preview(&V)"),
                UiLocalization.Text("AppServerFileBrowserForm.Accessible.Preview", "Preview"));
            _copyButton = CreateToolStripButton(
                UiLocalization.Text("AppServerFileBrowserForm.Button.Copy", "Copy(&C)"),
                UiLocalization.Text("AppServerFileBrowserForm.Accessible.Copy", "Copy"));
            _copyPathButton = CreateToolStripButton(
                UiLocalization.Text("AppServerFileBrowserForm.Button.CopyPath", "Copy Path(&Y)"),
                UiLocalization.Text("AppServerFileBrowserForm.Accessible.CopyPath", "Copy Path"));
            _attachButton = CreateToolStripButton(
                UiLocalization.Text("AppServerFileBrowserForm.Button.Attach", "Attach(&A)"),
                UiLocalization.Text("AppServerFileBrowserForm.Accessible.Attach", "Attach"));
            _navigationToolStrip.Items.AddRange(new ToolStripItem[]
            {
                _homeButton,
                _upButton,
                new ToolStripSeparator(),
                _refreshButton,
                new ToolStripSeparator(),
                _newFolderButton,
                _newFileButton,
                _deleteButton,
                new ToolStripSeparator(),
                _previewButton,
                _copyButton,
                _copyPathButton,
                _attachButton
            });

            _addressTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("AppServerFileBrowserForm.Accessible.Address", "Address"),
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(_addressTextBox, false, false);

            _searchTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("AppServerFileBrowserForm.Accessible.Search", "Search"),
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(_searchTextBox, false, false);

            _searchButton = new Button
            {
                Text = UiLocalization.Text("AppServerFileBrowserForm.Button.Search", "Go(&G)"),
                AccessibleName = UiLocalization.Text("AppServerFileBrowserForm.Accessible.SearchButton", "Search"),
                TabIndex = 2,
                TabStop = false,
                UseMnemonic = true
            };
            UiTheme.ApplyButton(_searchButton, UiGlyph.Search);

            _itemListView = CreateItemListView();
            _itemContextMenu = new ContextMenu();
            _newFolderMenuItem = new MenuItem(UiLocalization.Text("AppServerFileBrowserForm.Menu.NewFolder", "New Folder(&F)..."));
            _newFileMenuItem = new MenuItem(UiLocalization.Text("AppServerFileBrowserForm.Menu.NewFile", "New File(&N)..."));
            _deleteMenuItem = new MenuItem(UiLocalization.Text("AppServerFileBrowserForm.Menu.Delete", "Delete(&D)"));
            _previewMenuItem = new MenuItem(UiLocalization.Text("AppServerFileBrowserForm.Button.Preview", "Preview(&V)"));
            _copyMenuItem = new MenuItem(UiLocalization.Text("AppServerFileBrowserForm.Button.Copy", "Copy(&C)"));
            _pasteMenuItem = new MenuItem(UiLocalization.Text("AppServerFileBrowserForm.Menu.Paste", "Paste(&P)"));
            _copyPathMenuItem = new MenuItem(UiLocalization.Text("AppServerFileBrowserForm.Button.CopyPath", "Copy Path(&Y)"));
            _attachPathMenuItem = new MenuItem(UiLocalization.Text("AppServerFileBrowserForm.Button.Attach", "Attach(&A)"));
            _refreshMenuItem = new MenuItem(UiLocalization.Text("AppServerFileBrowserForm.Button.Refresh", "Refresh(&R)"));
            _selectionTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("AppServerFileBrowserForm.Accessible.Selection", "Selection"),
                ReadOnly = true,
                TabIndex = 2,
                TabStop = false
            };
            UiTheme.ApplyTextBox(_selectionTextBox, false, true);

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(_statusLabel, false);

            _homeButton.Click += async (sender, e) => await LoadEntriesAsync(_homePath);
            _upButton.Click += async (sender, e) => await NavigateUpAsync();
            _refreshButton.Click += async (sender, e) => await LoadEntriesAsync(
                _currentPath,
                GetFocusedName(),
                true,
                false,
                _activeColumnIndex);
            _newFolderButton.Click += async (sender, e) => await CreateFolderAsync();
            _newFileButton.Click += async (sender, e) => await CreateFileAsync();
            _deleteButton.Click += async (sender, e) => await DeleteSelectedAsync();
            _previewButton.Click += async (sender, e) => await PreviewFocusedFileAsync();
            _copyButton.Click += (sender, e) => CopySelectedFiles();
            _copyPathButton.Click += (sender, e) => CopySelectedPaths();
            _attachButton.Click += async (sender, e) => await AttachSelectedPathsAsync();
            _addressTextBox.KeyDown += AddressTextBox_KeyDown;
            _searchTextBox.KeyDown += SearchTextBox_KeyDown;
            _searchButton.Click += async (sender, e) => await SearchCurrentPathAsync();
            _itemListView.ItemActivate += async (sender, e) => await ActivateFocusedItemAsync();
            _itemListView.KeyDown += ItemListView_KeyDown;
            _itemListView.MouseDown += ItemListView_MouseDown;
            _itemListView.SelectedIndexChanged += (sender, e) => UpdateSelectionText();
            _itemContextMenu.Popup += ItemContextMenu_Popup;
            _newFolderMenuItem.Click += async (sender, e) => await CreateFolderAsync();
            _newFileMenuItem.Click += async (sender, e) => await CreateFileAsync();
            _deleteMenuItem.Click += async (sender, e) => await DeleteSelectedAsync();
            _previewMenuItem.Click += async (sender, e) => await PreviewFocusedFileAsync();
            _copyMenuItem.Click += (sender, e) => CopySelectedFiles();
            _pasteMenuItem.Click += async (sender, e) => await PasteFilesAsync();
            _copyPathMenuItem.Click += (sender, e) => CopySelectedPaths();
            _attachPathMenuItem.Click += async (sender, e) => await AttachSelectedPathsAsync();
            _refreshMenuItem.Click += async (sender, e) => await LoadEntriesAsync(
                _currentPath,
                GetFocusedName(),
                true,
                false,
                _activeColumnIndex);
            _itemContextMenu.MenuItems.Add(_newFolderMenuItem);
            _itemContextMenu.MenuItems.Add(_newFileMenuItem);
            _itemContextMenu.MenuItems.Add("-");
            _itemContextMenu.MenuItems.Add(_previewMenuItem);
            _itemContextMenu.MenuItems.Add(_deleteMenuItem);
            _itemContextMenu.MenuItems.Add("-");
            _itemContextMenu.MenuItems.Add(_copyMenuItem);
            _itemContextMenu.MenuItems.Add(_pasteMenuItem);
            _itemContextMenu.MenuItems.Add(_copyPathMenuItem);
            _itemContextMenu.MenuItems.Add(_attachPathMenuItem);
            _itemContextMenu.MenuItems.Add("-");
            _itemContextMenu.MenuItems.Add(_refreshMenuItem);
            _itemListView.ContextMenu = _itemContextMenu;

            BuildLayout();
        }

        internal static AppServerFileBrowserForm CreateForTests(
            IAppServerFileSystemClient client,
            string initialPath,
            IReadOnlyList<AppServerFileSystemEntry> windowsDriveRootEntries)
        {
            return new AppServerFileBrowserForm(
                client,
                initialPath,
                paths => Task.CompletedTask,
                null,
                message => { },
                null,
                () => windowsDriveRootEntries ?? new AppServerFileSystemEntry[0],
                null);
        }

        internal int ItemCountForTests
        {
            get { return _itemListView.Items.Count; }
        }

        internal string FocusedItemTextForTests
        {
            get
            {
                var entry = GetFocusedEntry();
                if (entry != null)
                {
                    return entry.Name;
                }

                return _lastFocusedEntry == null ? null : _lastFocusedEntry.Name;
            }
        }

        internal string CurrentPathForTests
        {
            get { return _currentPath; }
        }

        public string WatchPath
        {
            get { return _currentPath; }
        }

        internal bool NavigationToolStripTabStopForTests
        {
            get { return _navigationToolStrip.TabStop; }
        }

        internal bool SelectionTabStopForTests
        {
            get { return _selectionTextBox.TabStop; }
        }

        internal Task LoadCurrentPathForTestsAsync()
        {
            return LoadEntriesAsync(_currentPath);
        }

        internal Task LoadPathForTestsAsync(string path)
        {
            return LoadEntriesAsync(path);
        }

        internal Task MetadataLoadForTestsAsync
        {
            get { return _metadataLoadTask; }
        }

        internal Task FlushPendingFileChangesForTestsAsync()
        {
            return FlushPendingFileChangesAsync();
        }

        internal Task ClearSearchForTestsAsync()
        {
            return ClearSearchAsync();
        }

        internal void SetModalDialogActiveForTests(bool active)
        {
            _isModalDialogActive = active;
            if (!active)
            {
                SchedulePendingFileRefresh();
            }
        }

        internal void SetPreviewRequestHandlerForTests(Func<string, Task> previewFileAsync)
        {
            _previewFileAsync = previewFileAsync ?? PreviewFileWithOwnedWindowAsync;
        }

        public Task HandleFileSystemChangedAsync(AppServerFileSystemChangedEvent change)
        {
            if (IsDisposed || Disposing)
            {
                return Task.CompletedTask;
            }

            _pendingFileRefresh = true;
            var changedPaths = change == null ? null : change.ChangedPaths;
            if (changedPaths == null || changedPaths.Count == 0)
            {
                _pendingFullMetadataRefresh = true;
            }
            else
            {
                var hasValidChangedPath = false;
                foreach (var path in changedPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
                {
                    hasValidChangedPath = true;
                    _pendingChangedPaths.Add(path.Trim());
                }

                if (!hasValidChangedPath)
                {
                    _pendingFullMetadataRefresh = true;
                }
            }

            if (_isLoading || _isSearchMode || _isModalDialogActive)
            {
                _statusLabel.Text = UiLocalization.Text(
                    "AppServerFileBrowserForm.Status.FilesChangedRefreshAvailable",
                    "Files changed. Refresh to update this view.");
                return Task.CompletedTask;
            }

            SchedulePendingFileRefresh();
            return Task.CompletedTask;
        }

        public void HandleWatchUnavailable(string message)
        {
            _statusLabel.Text = string.IsNullOrWhiteSpace(message)
                ? UiLocalization.Text("AppServerFileBrowserForm.Status.LiveRefreshUnavailable", "Live refresh unavailable.")
                : message;
        }

        internal Task SearchForTestsAsync(string query)
        {
            _searchTextBox.Text = query ?? string.Empty;
            return SearchCurrentPathAsync();
        }

        internal Task ActivateFocusedItemForTestsAsync()
        {
            return ActivateFocusedItemAsync();
        }

        internal string CopySelectedPathsForTests()
        {
            return CopySelectedPaths(false);
        }

        internal string CopySelectedFilesForTests()
        {
            return CopySelectedFiles(false);
        }

        internal static IDataObject BuildCopyFileDataObjectForTests(IReadOnlyList<RuntimeFileBrowserSelectedPath> paths)
        {
            return BuildCopyFileDataObject(paths);
        }

        internal Task AttachSelectedPathsForTestsAsync()
        {
            return AttachSelectedPathsAsync();
        }

        internal Task CreateFolderForTestsAsync(string name)
        {
            return CreateFolderAsync(name);
        }

        internal Task CreateFileForTestsAsync(string name)
        {
            return CreateFileAsync(name);
        }

        internal Task DeleteSelectedForTestsAsync()
        {
            return DeleteSelectedAsync();
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = _itemListView;
            await LoadEntriesAsync(_currentPath);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fileChangeTimer.Stop();
                _fileChangeTimer.Dispose();
                _metadataLoadGeneration++;
                _metadataLoader.Clear();
            }

            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                if (_isSearchMode || !string.IsNullOrWhiteSpace(_searchTextBox.Text))
                {
                    _ = ClearSearchAsync();
                    return true;
                }

                Close();
                return true;
            }

            if (keyData == (Keys.Alt | Keys.D))
            {
                _addressTextBox.Focus();
                _addressTextBox.SelectAll();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async Task LoadEntriesAsync(
            string path,
            string childToFocus = null,
            bool refreshAllMetadata = false,
            bool preserveControlFocus = false,
            int? columnToFocus = null)
        {
            if (_isLoading)
            {
                return;
            }

            SetLoadingState(true);
            try
            {
                var trimmedPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
                var entries = string.IsNullOrWhiteSpace(trimmedPath)
                    ? _windowsDriveRootProvider()
                    : await _client.ReadDirectoryAsync(trimmedPath);
                if (IsDisposed || Disposing)
                {
                    return;
                }

                var pathChanged = !AreEquivalentBrowserPaths(_currentPath, trimmedPath);
                if (pathChanged || refreshAllMetadata)
                {
                    _metadataLoader.Clear();
                }

                _isSearchMode = false;
                if (!string.IsNullOrWhiteSpace(_searchTextBox.Text))
                {
                    _searchTextBox.Clear();
                }
                _currentPath = trimmedPath;
                if (pathChanged)
                {
                    OnCurrentPathChanged();
                }
                _addressTextBox.Text = string.IsNullOrWhiteSpace(_currentPath)
                    ? UiLocalization.Text("AppServerFileBrowserForm.Address.WindowsRoot", "This PC")
                    : _currentPath;
                UpdateWindowTitle();
                PopulateEntries(entries ?? new List<AppServerFileSystemEntry>(), childToFocus, null, columnToFocus);
                StartMetadataLoad();
                UpdateNavigationState();
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _statusLabel.Text = UiLocalization.Text("AppServerFileBrowserForm.Status.LoadFailed", "Unable to load this path.");
                }
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    SetLoadingState(false);
                    SchedulePendingFileRefresh();
                    if (!preserveControlFocus &&
                        (ActiveControl == null || (!_itemListView.Focused && ActiveControl != _itemListView)))
                    {
                        _itemListView.Focus();
                    }
                }
            }
        }

        private void PopulateEntries(
            IReadOnlyList<AppServerFileSystemEntry> entries,
            string childToFocus,
            int? fallbackFocusIndex,
            int? columnToFocus = null)
        {
            _itemListView.BeginUpdate();
            try
            {
                _itemListView.Items.Clear();
                foreach (var entry in entries
                    .Where(ShouldShowEntry)
                    .OrderBy(item => item, AppServerFileSystemEntryNaturalComparer.Instance))
                {
                    var browserEntry = new BrowserEntry(
                        entry.FileName.Trim(),
                        CombineChildPath(_currentPath, entry.FileName.Trim()),
                        entry.IsDirectory,
                        entry.IsFile || !entry.IsDirectory);
                    var item = new ListViewItem(browserEntry.Name) { Tag = browserEntry };
                    item.SubItems.Add(AppServerFileBrowserMetadataLoader.UnavailableValue);
                    item.SubItems.Add(browserEntry.IsDirectory
                        ? UiLocalization.Text("AppServerFileBrowserForm.EntryType.Folder", "Folder")
                        : UiLocalization.Text("AppServerFileBrowserForm.EntryType.File", "File"));
                    _itemListView.Items.Add(item);
                }
            }
            finally
            {
                _itemListView.EndUpdate();
            }

            FocusInitialItem(childToFocus, fallbackFocusIndex, columnToFocus);
            UpdateSelectionText();
            _statusLabel.Text = string.Format(
                UiLocalization.Text("AppServerFileBrowserForm.Status.Count.Template", "{0} items. Enter opens folders."),
                _itemListView.Items.Count);
        }

        private void PopulateSearchResults(IReadOnlyList<AppServerFuzzyFileSearchResult> results)
        {
            _itemListView.BeginUpdate();
            try
            {
                _itemListView.Items.Clear();
                foreach (var entry in (results ?? new List<AppServerFuzzyFileSearchResult>())
                    .Select(BuildSearchEntry)
                    .Where(entry => entry != null)
                    .OrderBy(item => item, BrowserEntryNaturalComparer.Instance))
                {
                    var item = new ListViewItem(entry.Name) { Tag = entry };
                    item.SubItems.Add(AppServerFileBrowserMetadataLoader.UnavailableValue);
                    item.SubItems.Add(entry.IsDirectory
                        ? UiLocalization.Text("AppServerFileBrowserForm.EntryType.Folder", "Folder")
                        : UiLocalization.Text("AppServerFileBrowserForm.EntryType.File", "File"));
                    _itemListView.Items.Add(item);
                }
            }
            finally
            {
                _itemListView.EndUpdate();
            }

            _isSearchMode = true;
            _metadataLoadGeneration++;
            FocusInitialItem(null, null, null);
            UpdateSelectionText();
            _statusLabel.Text = _itemListView.Items.Count == 0
                ? UiLocalization.Text("AppServerFileBrowserForm.Status.SearchNoResults", "No results.")
                : string.Format(
                    UiLocalization.Text("AppServerFileBrowserForm.Status.SearchResults.Template", "{0} result(s)."),
                    _itemListView.Items.Count);
        }

        private void FocusInitialItem(string childToFocus, int? fallbackFocusIndex, int? columnToFocus)
        {
            var itemToFocus = !string.IsNullOrWhiteSpace(childToFocus)
                ? _itemListView.Items.Cast<ListViewItem>().FirstOrDefault(item =>
                    string.Equals((item.Tag as BrowserEntry)?.Name, childToFocus, StringComparison.OrdinalIgnoreCase))
                : null;
            if (itemToFocus == null && fallbackFocusIndex.HasValue && _itemListView.Items.Count > 0)
            {
                var index = Math.Min(fallbackFocusIndex.Value, _itemListView.Items.Count - 1);
                itemToFocus = _itemListView.Items[Math.Max(0, index)];
            }
            if (itemToFocus == null && _itemListView.Items.Count > 0)
            {
                itemToFocus = _itemListView.Items[0];
            }

            if (itemToFocus == null)
            {
                return;
            }

            _activeColumnIndex = Math.Max(0, Math.Min(columnToFocus ?? 0, _itemListView.Columns.Count - 1));
            _itemListView.SelectedItems.Clear();
            itemToFocus.Focused = true;
            itemToFocus.Selected = true;
            itemToFocus.EnsureVisible();
            _lastFocusedEntry = itemToFocus.Tag as BrowserEntry;
        }

        private async Task ActivateFocusedItemAsync()
        {
            var entry = GetFocusedEntry() ?? GetSelectedEntries().FirstOrDefault();
            if (entry != null && entry.IsDirectory)
            {
                await LoadEntriesAsync(entry.FullPath);
                return;
            }

            await PreviewFocusedFileAsync();
        }

        private async Task PreviewFocusedFileAsync()
        {
            var entry = GetFocusedEntry() ?? GetSelectedEntries().FirstOrDefault();
            if (entry == null || entry.IsDirectory)
            {
                return;
            }

            await _previewFileAsync(entry.FullPath);
        }

        private async Task PreviewFileWithOwnedWindowAsync(string path)
        {
            var result = await _client.ReadFileAsync(path);
            var form = new AppServerFilePreviewForm(
                path,
                result == null ? null : result.DataBase64,
                data => _client.WriteFileAsync(path, data));
            form.FormClosed += (sender, e) => form.Dispose();
            form.Show(this);
            form.Activate();
        }

        private async Task NavigateUpAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                return;
            }

            var trimmed = _currentPath.TrimEnd('/', '\\');
            var childName = ExtractName(trimmed);
            var parent = GetParentPath(trimmed);
            if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, trimmed, StringComparison.Ordinal))
            {
                return;
            }

            await LoadEntriesAsync(parent, childName);
        }

        private async void AddressTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            await LoadEntriesAsync(_addressTextBox.Text);
        }

        private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            await SearchCurrentPathAsync();
        }

        private async Task SearchCurrentPathAsync()
        {
            var query = string.IsNullOrWhiteSpace(_searchTextBox.Text) ? string.Empty : _searchTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                await ClearSearchAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                _statusLabel.Text = UiLocalization.Text("AppServerFileBrowserForm.Status.SearchRequiresPath", "Open a folder before searching.");
                _announce?.Invoke(_statusLabel.Text);
                return;
            }

            if (!_isSearchSupported)
            {
                _statusLabel.Text = UiLocalization.Text("AppServerFileBrowserForm.Status.SearchUnavailable", "Search is unavailable for the connected App Server runtime.");
                _announce?.Invoke(_statusLabel.Text);
                return;
            }

            SetLoadingState(true);
            try
            {
                _statusLabel.Text = UiLocalization.Text("AppServerFileBrowserForm.Status.Searching", "Searching...");
                var result = await _searchClient.StartFuzzyFileSearchAsync(
                    new AppServerFuzzyFileSearchRequest
                    {
                        Query = query,
                        Roots = new List<string> { _currentPath },
                        CancellationToken = Guid.NewGuid().ToString("N")
                    },
                    default);
                PopulateSearchResults(result == null ? null : result.Files);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _statusLabel.Text = UiLocalization.Text("AppServerFileBrowserForm.Status.SearchFailed", "Search failed.");
            }
            finally
            {
                SetLoadingState(false);
                SchedulePendingFileRefresh();
                if (_itemListView.Items.Count > 0)
                {
                    _itemListView.Focus();
                }
            }
        }

        private async Task ClearSearchAsync()
        {
            _searchTextBox.Clear();
            _isSearchMode = false;
            if (_pendingFileRefresh)
            {
                await FlushPendingFileChangesAsync();
            }
            else
            {
                await LoadEntriesAsync(_currentPath, GetFocusedName());
            }
        }

        private void ItemListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Modifiers == Keys.None && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right))
            {
                var nextColumn = _activeColumnIndex + (e.KeyCode == Keys.Left ? -1 : 1);
                if (nextColumn >= 0 && nextColumn < _itemListView.Columns.Count)
                {
                    _activeColumnIndex = nextColumn;
                    AnnounceActiveColumn();
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
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
                _ = LoadEntriesAsync(_currentPath, GetFocusedName(), true, false, _activeColumnIndex);
                return;
            }

            if (e.Control && e.Shift && e.KeyCode == Keys.N)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _ = CreateFolderAsync();
                return;
            }

            if (e.Control && e.KeyCode == Keys.N)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _ = CreateFileAsync();
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _ = DeleteSelectedAsync();
                return;
            }

            if (e.Control && e.KeyCode == Keys.C)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CopySelectedFiles();
                return;
            }

            if (e.Modifiers == Keys.Control && e.KeyCode == Keys.V)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                _ = PasteFilesAsync();
                return;
            }

            if (e.KeyCode == Keys.Apps || (e.Shift && e.KeyCode == Keys.F10))
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ShowItemContextMenu();
            }
        }

        private void AnnounceActiveColumn()
        {
            var item = _itemListView.FocusedItem ??
                       _itemListView.SelectedItems.Cast<ListViewItem>().FirstOrDefault() ??
                       _itemListView.Items.Cast<ListViewItem>().FirstOrDefault(candidate =>
                           ReferenceEquals(candidate.Tag, _lastFocusedEntry));
            if (item == null || _activeColumnIndex >= item.SubItems.Count)
            {
                return;
            }

            EnsureActiveColumnVisible(item);
            _announce?.Invoke(item.SubItems[_activeColumnIndex].Text);
        }

        private void EnsureActiveColumnVisible(ListViewItem item)
        {
            item.EnsureVisible();
            var bounds = item.SubItems[_activeColumnIndex].Bounds;
            var horizontalDelta = bounds.Left < 0
                ? bounds.Left
                : bounds.Right > _itemListView.ClientSize.Width
                    ? bounds.Right - _itemListView.ClientSize.Width
                    : 0;
            if (horizontalDelta != 0)
            {
                SendMessage(_itemListView.Handle, 0x1014, new IntPtr(horizontalDelta), IntPtr.Zero);
            }
        }

        private void ItemListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var item = _itemListView.GetItemAt(e.X, e.Y);
            if (item == null || item.Selected)
            {
                return;
            }

            _itemListView.SelectedItems.Clear();
            item.Selected = true;
            item.Focused = true;
            _lastFocusedEntry = item.Tag as BrowserEntry;
        }

        private void ShowItemContextMenu()
        {
            var focusedItem = _itemListView.FocusedItem ??
                              (_itemListView.Items.Count > 0 ? _itemListView.Items[0] : null);
            if (focusedItem == null)
            {
                _itemContextMenu.Show(_itemListView, new Point(0, 0));
                return;
            }

            if (_itemListView.SelectedItems.Count == 0)
            {
                focusedItem.Selected = true;
            }

            _lastFocusedEntry = focusedItem.Tag as BrowserEntry;
            var bounds = focusedItem.Bounds;
            _itemContextMenu.Show(_itemListView, new Point(bounds.Left + 16, bounds.Bottom));
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        private void ItemContextMenu_Popup(object sender, EventArgs e)
        {
            var canOperate = !_isLoading;
            var canModifyCurrentDirectory = canOperate && !string.IsNullOrWhiteSpace(_currentPath);
            var selected = GetSelectedEntries();
            var focused = GetFocusedEntry();
            _newFolderMenuItem.Enabled = canModifyCurrentDirectory;
            _newFileMenuItem.Enabled = canModifyCurrentDirectory;
            _deleteMenuItem.Enabled = canModifyCurrentDirectory && selected.Count > 0;
            _previewMenuItem.Enabled = canOperate && focused != null && !focused.IsDirectory;
            _copyMenuItem.Enabled = canOperate && selected.Count > 0;
            _pasteMenuItem.Enabled = canModifyCurrentDirectory && !_isSearchMode && HasPasteFiles();
            _copyPathMenuItem.Enabled = canOperate && selected.Count > 0;
            _attachPathMenuItem.Enabled = canOperate && selected.Count > 0;
            _refreshMenuItem.Enabled = canOperate && !string.IsNullOrWhiteSpace(_currentPath);
        }

        private Task CreateFolderAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                return Task.CompletedTask;
            }

            var name = ShowNameDialog(
                UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Title.NewFolder", "New Folder"),
                UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Label.Name", "Name(&N):"),
                UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Button.Create", "Create(&C)"),
                UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Accessible.Create", "Create"));
            return string.IsNullOrWhiteSpace(name) ? Task.CompletedTask : CreateFolderAsync(name);
        }

        private async Task CreateFolderAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                return;
            }

            var targetPath = CombineChildPath(_currentPath, name);
            await RunOperationAsync(
                targetPath,
                () => _client.CreateDirectoryAsync(targetPath, true),
                UiLocalization.Text("AppServerFileBrowserForm.Status.Created", "Item created."));
        }

        private Task CreateFileAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                return Task.CompletedTask;
            }

            var name = ShowNameDialog(
                UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Title.NewFile", "New File"),
                UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Label.Name", "Name(&N):"),
                UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Button.Create", "Create(&C)"),
                UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Accessible.Create", "Create"));
            return string.IsNullOrWhiteSpace(name) ? Task.CompletedTask : CreateFileAsync(name);
        }

        private async Task CreateFileAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                return;
            }

            var targetPath = CombineChildPath(_currentPath, name);
            await RunOperationAsync(
                targetPath,
                () => _client.WriteFileAsync(targetPath, new byte[0]),
                UiLocalization.Text("AppServerFileBrowserForm.Status.Created", "Item created."));
        }

        private async Task DeleteSelectedAsync()
        {
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                return;
            }

            var selected = GetSelectedEntries();
            if (selected.Count == 0)
            {
                return;
            }

            var selectedPaths = selected.Select(entry => entry.FullPath).ToList();
            if (!_confirmDelete(selectedPaths))
            {
                return;
            }

            var fallbackFocusIndex = ResolveDeleteFallbackFocusIndex(GetSelectedItemIndices());
            SetLoadingState(true);
            try
            {
                foreach (var entry in selected)
                {
                    await _client.RemoveAsync(entry.FullPath, true, null);
                }

                await LoadEntriesAfterOperationAsync(null, fallbackFocusIndex);
                SetOperationStatus(UiLocalization.Text("AppServerFileBrowserForm.Status.Deleted", "Item deleted."));
            }
            catch (Exception ex)
            {
                ShowOperationFailure(ex.Message);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async Task RunOperationAsync(string childToFocusPath, Func<Task> operationAsync, string successMessage)
        {
            if (operationAsync == null)
            {
                return;
            }

            var childToFocus = ExtractName(childToFocusPath);
            SetLoadingState(true);
            try
            {
                await operationAsync();
                await LoadEntriesAfterOperationAsync(childToFocus, null);
                SetOperationStatus(successMessage);
            }
            catch (Exception ex)
            {
                ShowOperationFailure(ex.Message);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private async Task LoadEntriesAfterOperationAsync(string childToFocus, int? fallbackFocusIndex)
        {
            var entries = await _client.ReadDirectoryAsync(_currentPath);
            _metadataLoader.Clear();
            PopulateEntries(
                entries ?? new List<AppServerFileSystemEntry>(),
                childToFocus,
                fallbackFocusIndex,
                _activeColumnIndex);
            StartMetadataLoad();
            UpdateNavigationState();
        }

        private void ShowOperationFailure(string message)
        {
            var text = string.IsNullOrWhiteSpace(message)
                ? UiLocalization.Text("AppServerFileBrowserForm.Status.OperationFailed", "File operation failed.")
                : message;
            MessageBox.Show(this, text, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _statusLabel.Text = text;
        }

        private void SetOperationStatus(string message)
        {
            _statusLabel.Text = message;
            _announce?.Invoke(message);
        }

        private void OnCurrentPathChanged()
        {
            var handler = CurrentPathChanged;
            if (handler != null)
            {
                handler(this, new AppServerFileBrowserPathChangedEventArgs(_currentPath));
            }
        }

        private string ShowNameDialog(string title, string label, string acceptText, string acceptAccessibleName)
        {
            using (var form = new NameDialog(title, label, acceptText, acceptAccessibleName))
            {
                _isModalDialogActive = true;
                try
                {
                    return form.ShowDialog(this) == DialogResult.OK ? form.ItemName : null;
                }
                finally
                {
                    _isModalDialogActive = false;
                    SchedulePendingFileRefresh();
                }
            }
        }

        private bool ConfirmDelete(IReadOnlyList<string> paths)
        {
            var message = paths == null || paths.Count == 0
                ? string.Format(UiLocalization.Text("AppServerFileBrowserForm.Confirm.DeleteMany.Template", "Delete {0} selected items?"), 0)
                : paths.Count == 1
                    ? string.Format(UiLocalization.Text("AppServerFileBrowserForm.Confirm.DeleteOne.Template", "Delete {0}?"), paths[0])
                    : string.Format(UiLocalization.Text("AppServerFileBrowserForm.Confirm.DeleteMany.Template", "Delete {0} selected items?"), paths.Count) +
                        Environment.NewLine +
                        string.Join(Environment.NewLine, paths);
            return MessageBox.Show(
                this,
                message,
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private string CopySelectedPaths(bool updateClipboard = true)
        {
            var paths = GetSelectedPathItems();
            if (paths.Count == 0)
            {
                return string.Empty;
            }

            var clipboardText = string.Join(Environment.NewLine, paths.Select(item => item.Path));
            if (updateClipboard)
            {
                Clipboard.SetText(clipboardText, TextDataFormat.UnicodeText);
            }

            var message = string.Format(
                UiLocalization.Text("AppServerFileBrowserForm.Status.Copied.Template", "Copied {0} item(s)."),
                paths.Count);
            _statusLabel.Text = message;
            _announce?.Invoke(message);
            return clipboardText;
        }

        private string CopySelectedFiles(bool updateClipboard = true)
        {
            var paths = GetSelectedPathItems();
            if (paths.Count == 0)
            {
                return string.Empty;
            }

            var clipboardText = string.Join(Environment.NewLine, paths.Select(item => item.Path));
            var clipboardMarker = Guid.NewGuid().ToString("N");
            if (updateClipboard)
            {
                Clipboard.SetDataObject(BuildCopyFileDataObject(paths, clipboardMarker), true);
            }

            _copiedPaths?.Invoke(paths, clipboardMarker);

            var message = string.Format(
                UiLocalization.Text("AppServerFileBrowserForm.Status.Copied.Template", "Copied {0} item(s)."),
                paths.Count);
            _statusLabel.Text = message;
            _announce?.Invoke(message);
            return clipboardText;
        }

        private async Task AttachSelectedPathsAsync()
        {
            var paths = GetSelectedPathItems();
            if (paths.Count == 0 || _attachPathsAsync == null)
            {
                return;
            }

            await _attachPathsAsync(paths);
            _statusLabel.Text = UiLocalization.Text("AppServerFileBrowserForm.Status.Attached", "Attachment added.");
        }

        private IReadOnlyList<RuntimeFileBrowserSelectedPath> GetSelectedPathItems()
        {
            return GetSelectedEntries()
                .Select(entry => new RuntimeFileBrowserSelectedPath(entry.FullPath, entry.IsDirectory))
                .ToList();
        }

        private IReadOnlyList<BrowserEntry> GetSelectedEntries()
        {
            var selected = _itemListView.SelectedItems
                .Cast<ListViewItem>()
                .Select(item => item.Tag as BrowserEntry)
                .Where(entry => entry != null)
                .ToList();
            if (selected.Count == 0 && _lastFocusedEntry != null)
            {
                selected.Add(_lastFocusedEntry);
            }

            return selected;
        }

        private static IDataObject BuildCopyFileDataObject(
            IReadOnlyList<RuntimeFileBrowserSelectedPath> paths,
            string clipboardMarker = null)
        {
            var normalizedPaths = (paths ?? Array.Empty<RuntimeFileBrowserSelectedPath>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => new RuntimeFileBrowserSelectedPath(item.Path.Trim(), item.IsDirectory))
                .ToList();
            var clipboardData = new DataObject();
            if (CanUseWindowsFileDropList(normalizedPaths))
            {
                var fileDropList = new StringCollection();
                fileDropList.AddRange(normalizedPaths.Select(item => item.Path).ToArray());
                clipboardData.SetFileDropList(fileDropList);
            }

            clipboardData.SetData(
                ClipboardFormat,
                string.IsNullOrWhiteSpace(clipboardMarker)
                    ? Guid.NewGuid().ToString("N")
                    : clipboardMarker);
            return clipboardData;
        }

        private void UpdateWindowTitle()
        {
            var title = UiLocalization.Text("AppServerFileBrowserForm.Title", "File Browser");
            var scopeName = GetWindowTitleScopeName(_currentPath);
            Text = string.Format(
                UiLocalization.Text("AppServerFileBrowserForm.Title.Template", "{0} - {1}"),
                scopeName,
                title);
        }

        private static string GetWindowTitleScopeName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return UiLocalization.Text("AppServerFileBrowserForm.Address.WindowsRoot", "This PC");
            }

            var trimmed = path.Trim();
            var withoutTrailingSeparators = trimmed.TrimEnd('\\', '/');
            if (string.IsNullOrEmpty(withoutTrailingSeparators))
            {
                return trimmed;
            }

            if (withoutTrailingSeparators.Length == 2 &&
                char.IsLetter(withoutTrailingSeparators[0]) &&
                withoutTrailingSeparators[1] == ':')
            {
                return withoutTrailingSeparators;
            }

            var separatorIndex = Math.Max(
                withoutTrailingSeparators.LastIndexOf('\\'),
                withoutTrailingSeparators.LastIndexOf('/'));
            return separatorIndex >= 0 && separatorIndex < withoutTrailingSeparators.Length - 1
                ? withoutTrailingSeparators.Substring(separatorIndex + 1)
                : withoutTrailingSeparators;
        }

        private static bool CanUseWindowsFileDropList(IReadOnlyList<RuntimeFileBrowserSelectedPath> paths)
        {
            return paths != null &&
                   paths.Count > 0 &&
                   paths.All(item => item != null && LooksLikeWindowsBrowserPath(item.Path));
        }

        private static bool LooksLikeWindowsAbsolutePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.Length >= 3 &&
                   char.IsLetter(path[0]) &&
                   path[1] == ':' &&
                   (path[2] == '\\' || path[2] == '/');
        }

        private static bool AreEquivalentBrowserPaths(string first, string second)
        {
            var left = string.IsNullOrWhiteSpace(first) ? string.Empty : first.Trim();
            var right = string.IsNullOrWhiteSpace(second) ? string.Empty : second.Trim();
            if (LooksLikeWindowsBrowserPath(left) && LooksLikeWindowsBrowserPath(right))
            {
                left = left.Replace('/', '\\');
                right = right.Replace('/', '\\');
                while (left.EndsWith("\\", StringComparison.Ordinal) && left.Length > 3)
                {
                    left = left.Substring(0, left.Length - 1);
                }

                while (right.EndsWith("\\", StringComparison.Ordinal) && right.Length > 3)
                {
                    right = right.Substring(0, right.Length - 1);
                }

                return string.Equals(
                    left,
                    right,
                    StringComparison.OrdinalIgnoreCase);
            }

            while (left.EndsWith("/", StringComparison.Ordinal) && left.Length > 1)
            {
                left = left.Substring(0, left.Length - 1);
            }

            while (right.EndsWith("/", StringComparison.Ordinal) && right.Length > 1)
            {
                right = right.Substring(0, right.Length - 1);
            }

            return string.Equals(left, right, StringComparison.Ordinal);
        }

        private static bool LooksLikeWindowsBrowserPath(string path)
        {
            return LooksLikeWindowsAbsolutePath(path) ||
                   (!string.IsNullOrWhiteSpace(path) && path.StartsWith("\\\\", StringComparison.Ordinal));
        }

        private IReadOnlyList<int> GetSelectedItemIndices()
        {
            return _itemListView.SelectedIndices.Cast<int>().ToList();
        }

        private BrowserEntry GetFocusedEntry()
        {
            return _itemListView.FocusedItem == null
                ? _lastFocusedEntry
                : _itemListView.FocusedItem.Tag as BrowserEntry;
        }

        private string GetFocusedName()
        {
            var entry = GetFocusedEntry();
            return entry == null ? null : entry.Name;
        }

        private void UpdateSelectionText()
        {
            var entries = GetSelectedEntries();
            _selectionTextBox.Text = entries.Count == 0
                ? string.Empty
                : string.Join("; ", entries.Select(entry => entry.Name));
            _selectionTextBox.SelectionStart = 0;
            _selectionTextBox.SelectionLength = 0;
        }

        private void SetLoadingState(bool isLoading)
        {
            _isLoading = isLoading;
            UseWaitCursor = isLoading;
            _homeButton.Enabled = !isLoading;
            _refreshButton.Enabled = !isLoading;
            _newFolderButton.Enabled = !isLoading && !string.IsNullOrWhiteSpace(_currentPath);
            _newFileButton.Enabled = !isLoading && !string.IsNullOrWhiteSpace(_currentPath);
            _deleteButton.Enabled = !isLoading && !string.IsNullOrWhiteSpace(_currentPath);
            _previewButton.Enabled = !isLoading;
            _copyButton.Enabled = !isLoading;
            _copyPathButton.Enabled = !isLoading;
            _attachButton.Enabled = !isLoading;
            _searchButton.Enabled = !isLoading;
            _searchTextBox.Enabled = !isLoading;
            UpdateNavigationState();
            if (isLoading)
            {
                _statusLabel.Text = UiLocalization.Text("AppServerFileBrowserForm.Status.Loading", "Loading...");
            }
            else
            {
                SchedulePendingFileRefresh();
            }
        }

        private void UpdateNavigationState()
        {
            _upButton.Enabled = !_isLoading && !string.IsNullOrWhiteSpace(GetParentPath(_currentPath));
        }

        private static string CombineChildPath(string parent, string child)
        {
            var trimmedChild = (child ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(parent))
            {
                return trimmedChild;
            }

            var trimmedParent = parent.Trim();
            if (trimmedParent.EndsWith("/", StringComparison.Ordinal) || trimmedParent.EndsWith("\\", StringComparison.Ordinal))
            {
                return trimmedParent + trimmedChild;
            }

            var separator = trimmedParent.IndexOf('\\') >= 0 || (trimmedParent.Length >= 2 && trimmedParent[1] == ':')
                ? "\\"
                : "/";
            return trimmedParent + separator + trimmedChild;
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalized = path.TrimEnd('/', '\\');
            if (normalized.Length <= 1)
            {
                return null;
            }

            if (normalized.Length == 2 && normalized[1] == ':')
            {
                return null;
            }

            var separator = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
            if (separator < 0)
            {
                return null;
            }

            if (separator == 0)
            {
                return normalized[0].ToString();
            }

            if (separator == 2 && normalized.Length > 2 && normalized[1] == ':')
            {
                return normalized.Substring(0, 3);
            }

            return normalized.Substring(0, separator);
        }

        private static string ExtractName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var trimmed = path.TrimEnd('/', '\\');
            var separator = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
            return separator >= 0 ? trimmed.Substring(separator + 1) : trimmed;
        }

        private static BrowserEntry BuildSearchEntry(AppServerFuzzyFileSearchResult result)
        {
            if (result == null)
            {
                return null;
            }

            var fullPath = string.IsNullOrWhiteSpace(result.Path) ? string.Empty : result.Path.Trim();
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return null;
            }

            var name = string.IsNullOrWhiteSpace(result.FileName)
                ? ExtractName(fullPath)
                : result.FileName.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = fullPath;
            }

            var isDirectory = string.Equals(result.MatchType, "directory", StringComparison.OrdinalIgnoreCase);
            return new BrowserEntry(name, fullPath, isDirectory, !isDirectory);
        }

        internal static int ResolveDeleteFallbackFocusIndex(IEnumerable<int> selectedIndices)
        {
            if (selectedIndices == null)
            {
                return 0;
            }

            var ordered = selectedIndices.OrderBy(index => index).ToList();
            return ordered.Count == 0 ? 0 : ordered[0];
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

        private static ListView CreateItemListView()
        {
            var listView = new ListView
            {
                AccessibleName = UiLocalization.Text("AppServerFileBrowserForm.Accessible.Items", "Items"),
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = true,
                View = View.Details,
                TabIndex = 3,
                UseCompatibleStateImageBehavior = false
            };
            listView.Columns.Add(UiLocalization.Text("AppServerFileBrowserForm.Column.Name", "Name"), 260);
            listView.Columns.Add(UiLocalization.Text("AppServerFileBrowserForm.Column.Modified", "Date modified"), 160);
            listView.Columns.Add(UiLocalization.Text("AppServerFileBrowserForm.Column.Type", "Type"), 96);
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            var addressPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor
            };
            addressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            addressPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            addressPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var addressLabel = new Label
            {
                Text = UiLocalization.Text("AppServerFileBrowserForm.Label.Address", "Address(&D):"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false,
                UseMnemonic = true
            };
            UiTheme.ApplyLabel(addressLabel, true);
            addressPanel.Controls.Add(addressLabel, 0, 0);
            addressPanel.Controls.Add(_addressTextBox, 1, 0);

            var searchPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor
            };
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            searchPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var searchLabel = new Label
            {
                Text = UiLocalization.Text("AppServerFileBrowserForm.Label.Search", "Search(&S):"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false,
                UseMnemonic = true
            };
            UiTheme.ApplyLabel(searchLabel, true);
            searchPanel.Controls.Add(searchLabel, 0, 0);
            searchPanel.Controls.Add(_searchTextBox, 1, 0);
            searchPanel.Controls.Add(_searchButton, 2, 0);

            var selectionPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UiTheme.WindowBackColor
            };
            selectionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            selectionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            selectionPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var selectionLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = UiLocalization.Text("AppServerFileBrowserForm.Label.Selection", "Selection:"),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(selectionLabel, true);
            selectionPanel.Controls.Add(selectionLabel, 0, 0);
            selectionPanel.Controls.Add(_selectionTextBox, 1, 0);

            root.Controls.Add(_navigationToolStrip, 0, 0);
            root.Controls.Add(addressPanel, 0, 1);
            root.Controls.Add(searchPanel, 0, 2);
            root.Controls.Add(_itemListView, 0, 3);
            root.Controls.Add(selectionPanel, 0, 4);
            root.Controls.Add(_statusLabel, 0, 5);
            Controls.Add(root);
        }

        private static IReadOnlyList<AppServerFileSystemEntry> GetWindowsDriveRootEntries()
        {
            try
            {
                return DriveInfo.GetDrives()
                    .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(drive => new AppServerFileSystemEntry
                    {
                        FileName = drive.Name,
                        IsDirectory = true,
                        IsFile = false
                    })
                    .ToList();
            }
            catch
            {
                return new List<AppServerFileSystemEntry>();
            }
        }

        private bool ShouldShowEntry(AppServerFileSystemEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FileName))
            {
                return false;
            }

            if (!entry.IsDirectory || !IsWindowsDriveRootPath(_currentPath))
            {
                return true;
            }

            return !WindowsDriveRootSystemDirectoryNames.Contains(entry.FileName.Trim());
        }

        private static bool IsWindowsDriveRootPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var trimmed = path.Trim();
            return trimmed.Length == 3 &&
                   char.IsLetter(trimmed[0]) &&
                   trimmed[1] == ':' &&
                   (trimmed[2] == '\\' || trimmed[2] == '/');
        }

        private static readonly HashSet<string> WindowsDriveRootSystemDirectoryNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "$Recycle.Bin",
                "System Volume Information",
                "Recovery",
                "Config.Msi",
                "MSOCache"
            };

        private sealed class AppServerFileSystemEntryNaturalComparer : IComparer<AppServerFileSystemEntry>
        {
            public static readonly AppServerFileSystemEntryNaturalComparer Instance =
                new AppServerFileSystemEntryNaturalComparer();

            public int Compare(AppServerFileSystemEntry x, AppServerFileSystemEntry y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x == null)
                {
                    return 1;
                }

                if (y == null)
                {
                    return -1;
                }

                if (x.IsDirectory != y.IsDirectory)
                {
                    return x.IsDirectory ? -1 : 1;
                }

                var result = NaturalStringComparer.Compare(x.FileName, y.FileName);
                return result != 0
                    ? result
                    : StringComparer.CurrentCultureIgnoreCase.Compare(x.FileName, y.FileName);
            }
        }

        private sealed class BrowserEntryNaturalComparer : IComparer<BrowserEntry>
        {
            public static readonly BrowserEntryNaturalComparer Instance =
                new BrowserEntryNaturalComparer();

            public int Compare(BrowserEntry x, BrowserEntry y)
            {
                if (ReferenceEquals(x, y))
                {
                    return 0;
                }

                if (x == null)
                {
                    return 1;
                }

                if (y == null)
                {
                    return -1;
                }

                if (x.IsDirectory != y.IsDirectory)
                {
                    return x.IsDirectory ? -1 : 1;
                }

                var result = NaturalStringComparer.Compare(x.Name, y.Name);
                return result != 0
                    ? result
                    : StringComparer.CurrentCultureIgnoreCase.Compare(x.Name, y.Name);
            }
        }

        private static class NaturalStringComparer
        {
            public static int Compare(string x, string y)
            {
                try
                {
                    return StrCmpLogicalW(x ?? string.Empty, y ?? string.Empty);
                }
                catch (DllNotFoundException)
                {
                    return StringComparer.CurrentCultureIgnoreCase.Compare(x, y);
                }
                catch (EntryPointNotFoundException)
                {
                    return StringComparer.CurrentCultureIgnoreCase.Compare(x, y);
                }
            }

            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
            private static extern int StrCmpLogicalW(string psz1, string psz2);
        }

        private sealed class BrowserEntry
        {
            public BrowserEntry(string name, string fullPath, bool isDirectory, bool isFile)
            {
                Name = name;
                FullPath = fullPath;
                IsDirectory = isDirectory;
                IsFile = isFile;
            }

            public string Name { get; private set; }

            public string FullPath { get; private set; }

            public bool IsDirectory { get; private set; }

            public bool IsFile { get; private set; }
        }

        private sealed class NameDialog : Form
        {
            private readonly TextBox _nameTextBox;
            private readonly Button _acceptButton;
            private readonly Button _cancelButton;

            public NameDialog(string title, string labelText, string acceptText, string acceptAccessibleName)
            {
                Text = title;
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MinimizeBox = false;
                MaximizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new Size(420, 132);
                UiTheme.ApplyForm(this);

                _nameTextBox = new TextBox
                {
                    AccessibleName = UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Accessible.Name", "Name"),
                    TabIndex = 0
                };
                UiTheme.ApplyTextBox(_nameTextBox, false, false);

                _acceptButton = CreateDialogButton(acceptText, acceptAccessibleName, 1);
                _cancelButton = CreateDialogButton(
                    UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Button.Cancel", "&Cancel"),
                    UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Accessible.Cancel", "Cancel"),
                    2);
                UiTheme.ApplyDialogButton(_acceptButton, UiGlyph.Ok);
                UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);
                _acceptButton.Click += AcceptButton_Click;
                _cancelButton.Click += (sender, e) =>
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                AcceptButton = _acceptButton;
                CancelButton = _cancelButton;

                BuildLayout(labelText);
                Shown += (sender, e) => _nameTextBox.Focus();
            }

            public string ItemName { get; private set; }

            private void AcceptButton_Click(object sender, EventArgs e)
            {
                var name = (_nameTextBox.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("AppServerFileBrowserForm.NameDialog.Message.NameRequired", "Please enter a name."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    _nameTextBox.Focus();
                    return;
                }

                ItemName = name;
                DialogResult = DialogResult.OK;
                Close();
            }

            private void BuildLayout(string labelText)
            {
                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    Padding = new Padding(UiTheme.OuterPadding),
                    BackColor = UiTheme.WindowBackColor
                };
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

                var label = new Label
                {
                    Dock = DockStyle.Fill,
                    Text = labelText,
                    TextAlign = ContentAlignment.MiddleLeft,
                    TabStop = false,
                    UseMnemonic = true
                };
                UiTheme.ApplyLabel(label, true);

                var footer = new FlowLayoutPanel
                {
                    Dock = DockStyle.Right,
                    FlowDirection = FlowDirection.LeftToRight,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    WrapContents = false,
                    BackColor = UiTheme.WindowBackColor
                };
                footer.Controls.Add(_cancelButton);
                footer.Controls.Add(_acceptButton);

                root.Controls.Add(label, 0, 0);
                root.Controls.Add(_nameTextBox, 0, 1);
                root.Controls.Add(footer, 0, 2);
                Controls.Add(root);
            }

            private static Button CreateDialogButton(string text, string accessibleName, int tabIndex)
            {
                return new Button
                {
                    Text = text,
                    AccessibleName = accessibleName,
                    Width = 112,
                    Height = UiTheme.ButtonHeight,
                    UseMnemonic = true,
                    TabIndex = tabIndex
                };
            }
        }
    }
}
