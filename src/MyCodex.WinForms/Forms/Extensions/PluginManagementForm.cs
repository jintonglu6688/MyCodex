using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class PluginManagementForm : Form
    {
        private readonly IAppServerClient _appServerClient;
        private readonly IPluginMarketCatalogClient _pluginMarketCatalogClient;
        private readonly IScreenReaderService _screenReaderService;
        private readonly TabControl _tabControl;
        private readonly TabPage _installedTabPage;
        private readonly TabPage _marketTabPage;
        private readonly ListView _installedListView;
        private readonly ListView _marketListView;
        private readonly TextBox _marketSearchTextBox;
        private readonly Button _marketSearchButton;
        private readonly TreeView _marketSourceTreeView;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly Button _refreshMarketplaceButton;
        private readonly Button _registerMarketplaceButton;
        private readonly Button _installButton;
        private readonly Button _uninstallButton;
        private readonly Button _openAuthButton;
        private readonly Button _openFolderButton;
        private readonly Button _closeButton;
        private readonly ContextMenu _pluginContextMenu;
        private readonly MenuItem _registerMarketplaceMenuItem;
        private readonly MenuItem _installMenuItem;
        private readonly MenuItem _uninstallMenuItem;
        private readonly MenuItem _openAuthMenuItem;
        private readonly MenuItem _openFolderMenuItem;
        private readonly Dictionary<string, AppServerPluginDetails> _detailCache;
        private readonly Dictionary<string, List<AppServerPluginAppInfo>> _authAppsCache;
        private readonly HashSet<string> _loadedRemoteMarketManifestUrls;
        private readonly CancellationTokenSource _lifetimeCancellation;

        private bool _isBusy;
        private string _preferredInstalledPluginId;
        private string _preferredMarketPluginId;
        private List<AppServerPluginSummary> _plugins;
        private List<AppServerPluginSummary> _installedPlugins;
        private List<PluginMarketListItem> _remoteMarketItems;
        private PluginMarketCatalogSnapshot _remoteMarketCatalog;
        private PluginMarketBrowseScope _currentMarketScope;
        private string _currentMarketKeyword;

        public PluginManagementForm(
            IAppServerClient appServerClient,
            IPluginMarketCatalogClient pluginMarketCatalogClient,
            IScreenReaderService screenReaderService = null)
        {
            _appServerClient = appServerClient ?? throw new ArgumentNullException(nameof(appServerClient));
            _pluginMarketCatalogClient = pluginMarketCatalogClient ?? throw new ArgumentNullException(nameof(pluginMarketCatalogClient));
            _screenReaderService = screenReaderService;
            _lifetimeCancellation = new CancellationTokenSource();

            Text = UiLocalization.Text("PluginManagementForm.Title", "Plugin Management (Preview)");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1020, 680);
            Size = new Size(1180, 800);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _plugins = new List<AppServerPluginSummary>();
            _installedPlugins = new List<AppServerPluginSummary>();
            _remoteMarketItems = new List<PluginMarketListItem>();
            _loadedRemoteMarketManifestUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _currentMarketScope = PluginMarketBrowseScope.All();
            _currentMarketKeyword = string.Empty;
            _detailCache = new Dictionary<string, AppServerPluginDetails>(StringComparer.OrdinalIgnoreCase);
            _authAppsCache = new Dictionary<string, List<AppServerPluginAppInfo>>(StringComparer.OrdinalIgnoreCase);

            _installedListView = CreatePluginListView(
                UiLocalization.Text("PluginManagementForm.Accessible.InstalledPlugins", "Installed Plugins"),
                UiLocalization.Text("PluginManagementForm.Column.PluginStatus", "Plugin Status"),
                null);
            _marketListView = CreatePluginListView(
                UiLocalization.Text("PluginManagementForm.Accessible.MarketPlugins", "Market Plugins"),
                UiLocalization.Text("PluginManagementForm.Column.Status", "Status"),
                UiLocalization.Text("PluginManagementForm.Column.Source", "Source"));

            _marketSearchTextBox = CreateMarketSearchTextBox();
            _marketSearchButton = CreateButton(
                UiLocalization.Text("PluginManagementForm.Button.SearchMarket", "&Search"),
                UiLocalization.Text("PluginManagementForm.Accessible.MarketSearch", "Market Search"),
                1);
            _marketSourceTreeView = CreateMarketSourceTreeView();
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 8, 0, 0),
                TabStop = false
            };
            _refreshButton = CreateButton(
                UiLocalization.Text("PluginManagementForm.Button.Refresh", "&Refresh"),
                UiLocalization.Text("PluginManagementForm.Accessible.Refresh", "Refresh"),
                0);
            _refreshMarketplaceButton = CreateButton(
                UiLocalization.Text("PluginManagementForm.Button.RefreshMarketplace", "Refresh &Marketplace"),
                UiLocalization.Text("PluginManagementForm.Accessible.RefreshMarketplace", "Refresh marketplace"),
                1);
            _registerMarketplaceButton = CreateButton(
                UiLocalization.Text("PluginManagementForm.Button.RegisterMarketplace", "Re&gister Marketplace"),
                UiLocalization.Text("PluginManagementForm.Accessible.RegisterMarketplace", "Register marketplace"),
                2);
            _installButton = CreateButton(
                UiLocalization.Text("PluginManagementForm.Button.Install", "&Install"),
                UiLocalization.Text("PluginManagementForm.Accessible.Install", "Install"),
                3);
            _uninstallButton = CreateButton(
                UiLocalization.Text("PluginManagementForm.Button.Uninstall", "&Uninstall"),
                UiLocalization.Text("PluginManagementForm.Accessible.Uninstall", "Uninstall"),
                4);
            _openAuthButton = CreateButton(
                UiLocalization.Text("PluginManagementForm.Button.OpenAuth", "Open &Auth"),
                UiLocalization.Text("PluginManagementForm.Accessible.OpenAuth", "Authorization"),
                5);
            _openFolderButton = CreateButton(
                UiLocalization.Text("PluginManagementForm.Button.OpenFolder", "Open F&older"),
                UiLocalization.Text("PluginManagementForm.Accessible.OpenFolder", "Open Folder"),
                6);
            _closeButton = CreateButton(
                UiLocalization.Text("PluginManagementForm.Button.Close", "&Close"),
                UiLocalization.Text("PluginManagementForm.Accessible.Close", "Close"),
                7);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            FormInteractionConventions.ConfigureRefreshButton(_refreshMarketplaceButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_refreshMarketplaceButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_registerMarketplaceButton, UiGlyph.Download);
            UiTheme.ApplyButton(_installButton, UiGlyph.Download);
            UiTheme.ApplyButton(_uninstallButton, UiGlyph.Cancel);
            UiTheme.ApplyButton(_openAuthButton, UiGlyph.Account);
            UiTheme.ApplyButton(_openFolderButton, UiGlyph.Workspace);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _pluginContextMenu = new ContextMenu();
            _registerMarketplaceMenuItem = new MenuItem(UiLocalization.Text("PluginManagementForm.Menu.RegisterMarketplace", "Re&gister Marketplace"));
            _installMenuItem = new MenuItem(UiLocalization.Text("PluginManagementForm.Menu.Install", "&Install"));
            _uninstallMenuItem = new MenuItem(UiLocalization.Text("PluginManagementForm.Menu.Uninstall", "&Uninstall"));
            _openAuthMenuItem = new MenuItem(UiLocalization.Text("PluginManagementForm.Menu.OpenAuth", "Open &Auth"));
            _openFolderMenuItem = new MenuItem(UiLocalization.Text("PluginManagementForm.Menu.OpenFolder", "Open F&older"));
            _pluginContextMenu.MenuItems.Add(_registerMarketplaceMenuItem);
            _pluginContextMenu.MenuItems.Add(_installMenuItem);
            _pluginContextMenu.MenuItems.Add(_uninstallMenuItem);
            _pluginContextMenu.MenuItems.Add("-");
            _pluginContextMenu.MenuItems.Add(_openAuthMenuItem);
            _pluginContextMenu.MenuItems.Add(_openFolderMenuItem);

            _installedListView.ContextMenu = _pluginContextMenu;
            _marketListView.ContextMenu = _pluginContextMenu;

            _installedTabPage = new TabPage
            {
                Text = UiLocalization.Text("PluginManagementForm.Tab.Installed", "Installed"),
                AccessibleName = UiLocalization.Text("PluginManagementForm.Accessible.InstalledTab", "Installed"),
                BackColor = UiTheme.WindowBackColor
            };
            _marketTabPage = new TabPage
            {
                Text = UiLocalization.Text("PluginManagementForm.Tab.Market", "Market"),
                AccessibleName = UiLocalization.Text("PluginManagementForm.Accessible.MarketTab", "Market"),
                BackColor = UiTheme.WindowBackColor
            };
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                TabIndex = 0
            };
            _tabControl.TabPages.Add(_installedTabPage);
            _tabControl.TabPages.Add(_marketTabPage);

            _tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;
            _marketSearchTextBox.KeyDown += MarketSearchTextBox_KeyDown;
            _marketSearchButton.Click += MarketSearchButton_Click;
            _marketSourceTreeView.KeyDown += MarketSourceTreeView_KeyDown;
            _installedListView.SelectedIndexChanged += PluginListView_SelectedIndexChanged;
            _marketListView.SelectedIndexChanged += PluginListView_SelectedIndexChanged;
            _installedListView.KeyDown += InstalledListView_KeyDown;
            _marketListView.KeyDown += MarketListView_KeyDown;
            _installedListView.MouseDown += PluginListView_MouseDown;
            _marketListView.MouseDown += PluginListView_MouseDown;
            _pluginContextMenu.Popup += PluginContextMenu_Popup;
            _refreshButton.Click += RefreshButton_Click;
            _refreshMarketplaceButton.Click += RefreshMarketplaceButton_Click;
            _registerMarketplaceButton.Click += RegisterMarketplaceButton_Click;
            _installButton.Click += InstallButton_Click;
            _uninstallButton.Click += UninstallButton_Click;
            _openAuthButton.Click += OpenAuthButton_Click;
            _openFolderButton.Click += OpenFolderButton_Click;
            _closeButton.Click += CloseButton_Click;
            _registerMarketplaceMenuItem.Click += RegisterMarketplaceButton_Click;
            _installMenuItem.Click += InstallButton_Click;
            _uninstallMenuItem.Click += UninstallButton_Click;
            _openAuthMenuItem.Click += OpenAuthButton_Click;
            _openFolderMenuItem.Click += OpenFolderButton_Click;
            Load += OnLoad;
            Shown += OnShown;
            KeyDown += PluginManagementForm_KeyDown;

            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(UiLocalization.Text("PluginManagementForm.Status.PreparingLoadList", "Preparing to load plugin list."));
            RefreshActionButtons();
        }

        private void OnLoad(object sender, EventArgs e)
        {
            ActiveControl = _tabControl;
            _installedListView.Select();
        }

        private async void OnShown(object sender, EventArgs e)
        {
            await LoadDataAsync();
        }

        private async void PluginManagementForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                await LoadDataAsync();
            }
        }

        private void TabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshActionButtons();
            UpdateStatusForCurrentView();
        }

        private async void MarketSearchTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !_isBusy)
            {
                e.SuppressKeyPress = true;
                await SearchSelectedMarketScopeAsync();
            }
        }

        private async void MarketSearchButton_Click(object sender, EventArgs e)
        {
            await SearchSelectedMarketScopeAsync();
        }

        private async void MarketSourceTreeView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !_isBusy)
            {
                e.SuppressKeyPress = true;
                await BrowseSelectedMarketScopeAsync();
            }
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            await LoadDataAsync();
        }

        private async void RefreshMarketplaceButton_Click(object sender, EventArgs e)
        {
            await RefreshMarketplaceAsync();
        }

        private async void RegisterMarketplaceButton_Click(object sender, EventArgs e)
        {
            await RegisterSelectedMarketplaceAsync();
        }

        private async void InstallButton_Click(object sender, EventArgs e)
        {
            await InstallSelectedPluginAsync();
        }

        private async void UninstallButton_Click(object sender, EventArgs e)
        {
            await UninstallSelectedPluginAsync();
        }

        private async void OpenAuthButton_Click(object sender, EventArgs e)
        {
            await OpenSelectedPluginAuthAsync();
        }

        private void OpenFolderButton_Click(object sender, EventArgs e)
        {
            OpenSelectedPluginFolder();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void PluginListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshActionButtons();
        }

        private void PluginListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var listView = sender as ListView;
            if (listView == null)
            {
                return;
            }

            var hit = listView.HitTest(e.Location);
            if (hit == null || hit.Item == null)
            {
                return;
            }

            hit.Item.Selected = true;
            hit.Item.Focused = true;
        }

        private async void InstalledListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && _installedListView.Focused && !_isBusy)
            {
                var plugin = GetSelectedInstalledPlugin();
                if (plugin != null)
                {
                    e.SuppressKeyPress = true;
                    await ReadPluginDetailsAsync(plugin);
                }
            }
        }

        private async void MarketListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && _marketListView.Focused && !_isBusy)
            {
                var navigationItem = GetSelectedMarketNavigationItem();
                if (navigationItem != null)
                {
                    e.SuppressKeyPress = true;
                    await BrowseMarketScopeAsync(_currentMarketScope, _currentMarketKeyword, navigationItem.TargetPage, true);
                    return;
                }

                var item = GetSelectedMarketItem();
                if (item != null)
                {
                    e.SuppressKeyPress = true;
                    await OpenMarketItemDetailsAsync(item);
                }
            }
        }

        private void PluginContextMenu_Popup(object sender, EventArgs e)
        {
            RefreshContextMenuItems();
        }

        private async System.Threading.Tasks.Task LoadDataAsync()
        {
            if (_isBusy)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("PluginManagementForm.Action.LoadPlugins", "Load plugin list")))
            {
                return;
            }

            Exception remoteError = null;

            try
            {
                SetBusy(true);
                SetStatus(UiLocalization.Text("PluginManagementForm.Status.LoadingAll", "Loading installed plugins and market catalog..."));

                var plugins = await _appServerClient.ListPluginsAsync();
                _plugins = (plugins ?? new List<AppServerPluginSummary>())
                    .Where(item => item != null)
                    .ToList();
                _installedPlugins = await LoadInstalledPluginObservationAsync(_plugins);

                try
                {
                    using (var operationCancellation = CreateOperationCancellationTokenSource())
                    {
                        _remoteMarketCatalog = await _pluginMarketCatalogClient.LoadIndexAsync(operationCancellation.Token);
                        _remoteMarketItems = new List<PluginMarketListItem>();
                        _loadedRemoteMarketManifestUrls.Clear();
                    }
                }
                catch (Exception ex)
                {
                    _remoteMarketCatalog = null;
                    _remoteMarketItems = new List<PluginMarketListItem>();
                    _loadedRemoteMarketManifestUrls.Clear();
                    remoteError = ex;
                }

                RebuildInstalledList();
                RebuildMarketSourceTree();
                ClearMarketList();
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("PluginManagementForm.Status.LoadPluginsFailed", "Failed to load plugin list."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            if (remoteError != null)
            {
                SetStatus(UiLocalization.Text(
                    "PluginManagementForm.Status.RemoteMarketLoadFailed",
                    "Installed plugins are loaded, but the remote market could not be loaded."));
                return;
            }

            UpdateStatusForCurrentView();
        }

        private async System.Threading.Tasks.Task EnsureRemoteMarketItemsLoadedAsync(
            PluginMarketBrowseScope scope,
            CancellationToken cancellationToken)
        {
            var markets = GetRemoteMarketSummaries();
            if (scope == null || scope.AllSources)
            {
                for (var i = 0; i < markets.Count; i++)
                {
                    await EnsureRemoteMarketLoadedAsync(markets[i], cancellationToken);
                }

                return;
            }

            var marketSummary = markets.FirstOrDefault(item =>
                string.Equals(GetMarketDisplayName(item), scope.MarketName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.MarketId, scope.MarketName, StringComparison.OrdinalIgnoreCase));
            if (marketSummary != null)
            {
                await EnsureRemoteMarketLoadedAsync(marketSummary, cancellationToken);
            }
        }

        private async System.Threading.Tasks.Task EnsureRemoteMarketLoadedAsync(
            PluginMarketSnapshot marketSummary,
            CancellationToken cancellationToken)
        {
            if (marketSummary == null || string.IsNullOrWhiteSpace(marketSummary.ManifestUrl))
            {
                return;
            }

            var manifestUrl = marketSummary.ManifestUrl.Trim();
            if (_loadedRemoteMarketManifestUrls.Contains(manifestUrl))
            {
                return;
            }

            var items = await LoadRemoteMarketItemsAsync(marketSummary, cancellationToken);
            _remoteMarketItems.AddRange(items);
            _loadedRemoteMarketManifestUrls.Add(manifestUrl);
        }

        private async System.Threading.Tasks.Task<List<PluginMarketListItem>> LoadRemoteMarketItemsAsync(
            PluginMarketSnapshot marketSummary,
            CancellationToken cancellationToken)
        {
            var items = new List<PluginMarketListItem>();
            if (marketSummary == null || string.IsNullOrWhiteSpace(marketSummary.ManifestUrl))
            {
                return items;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var market = await _pluginMarketCatalogClient.LoadMarketAsync(marketSummary.ManifestUrl, cancellationToken);
            if (market == null || market.Plugins == null)
            {
                return items;
            }

            if (string.IsNullOrWhiteSpace(market.AppServerSource))
            {
                market.AppServerSource = marketSummary.AppServerSource;
            }

            var marketName = GetMarketDisplayName(market);
            foreach (var entry in market.Plugins.Where(item => item != null && !string.IsNullOrWhiteSpace(item.PluginId)))
            {
                items.Add(new PluginMarketListItem
                {
                    MarketName = marketName,
                    PluginId = entry.PluginId,
                    DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.PluginId : entry.DisplayName,
                    Summary = entry.Summary,
                    AppServerSource = market.AppServerSource,
                    IsRemoteCatalog = true,
                    RemoteEntry = CloneRemoteEntry(entry, market)
                });
            }

            return items;
        }

        private async System.Threading.Tasks.Task OpenMarketItemDetailsAsync(PluginMarketListItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.AppServerPlugin != null)
            {
                await ReadPluginDetailsAsync(item.AppServerPlugin);
                return;
            }

            ShowRemoteMarketDetails(item);
            SetStatus(string.Format(
                UiLocalization.Text("PluginManagementForm.Status.RemotePluginDetailsRead.Template", "Details for market plugin {0} are shown."),
                GetMarketItemDisplayName(item)));
        }

        private async System.Threading.Tasks.Task SearchSelectedMarketScopeAsync()
        {
            await BrowseMarketScopeAsync(GetSelectedMarketScope(), _marketSearchTextBox.Text, 1, true);
        }

        private async System.Threading.Tasks.Task BrowseSelectedMarketScopeAsync()
        {
            _marketSearchTextBox.Text = string.Empty;
            await BrowseMarketScopeAsync(GetSelectedMarketScope(), string.Empty, 1, true);
        }

        private async System.Threading.Tasks.Task BrowseMarketScopeAsync(
            PluginMarketBrowseScope scope,
            string keyword,
            int page,
            bool focusList)
        {
            if (_isBusy)
            {
                return;
            }

            var normalizedScope = scope ?? PluginMarketBrowseScope.All();
            var normalizedKeyword = keyword ?? string.Empty;
            try
            {
                SetBusy(true);
                SetStatus(BuildMarketLoadingStatus(normalizedScope, normalizedKeyword));
                using (var operationCancellation = CreateOperationCancellationTokenSource())
                {
                    await EnsureRemoteMarketItemsLoadedAsync(normalizedScope, operationCancellation.Token);
                }

                RebuildMarketSourceTreeForScope(normalizedScope);
                var allItems = PluginMarketFilter.Apply(
                        GetAvailableMarketItems(),
                        normalizedScope.AllSources ? null : normalizedScope.MarketName,
                        normalizedKeyword,
                        normalizedScope.Category)
                    .OrderBy(item => item.MarketName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(item => GetMarketItemDisplayName(item), StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                var totalCount = allItems.Count;
                var totalPages = PluginMarketPaging.GetTotalPages(totalCount);
                var normalizedPage = totalPages == 0 ? 1 : Math.Max(1, Math.Min(page, totalPages));
                var pageItems = allItems
                    .Skip((normalizedPage - 1) * PluginMarketPaging.PageSize)
                    .Take(PluginMarketPaging.PageSize)
                    .ToList();

                _currentMarketScope = normalizedScope;
                _currentMarketKeyword = normalizedKeyword;
                FillMarketList(pageItems, normalizedPage, totalCount);
                SetStatus(BuildMarketPageStatus(normalizedScope, normalizedPage, totalCount));
                if (focusList)
                {
                    _marketListView.Focus();
                }
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.MarketBrowseFailed.Template", "Market browse failed: {0}"),
                    ex.Message));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async System.Threading.Tasks.Task RefreshMarketplaceAsync()
        {
            if (_isBusy)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("PluginManagementForm.Action.RefreshMarketplace", "Refresh marketplace")))
            {
                return;
            }

            if (!EnsureMarketplaceMutationAvailable())
            {
                return;
            }

            var marketplaceName = GetSelectedMarketSource();
            try
            {
                SetBusy(true);
                _refreshMarketplaceButton.Enabled = false;
                SetStatus(UiLocalization.Text("PluginManagementForm.Status.RefreshingMarketplace", "Refreshing marketplace source..."), true);

                var result = await _appServerClient.UpgradeMarketplaceAsync(marketplaceName, _lifetimeCancellation.Token);
                var resultMessage = result == null || string.IsNullOrWhiteSpace(result.Message)
                    ? UiLocalization.Text("PluginManagementForm.Status.RefreshMarketplaceSucceeded", "Marketplace source refreshed.")
                    : result.Message.Trim();
                SetStatus(resultMessage, true);
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.RefreshMarketplaceFailed.Template", "Marketplace source refresh failed: {0}"),
                    ex.Message), true);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            await LoadDataAsync();
        }

        private async System.Threading.Tasks.Task ReadPluginDetailsAsync(AppServerPluginSummary plugin)
        {
            if (plugin == null || _isBusy)
            {
                return;
            }

            var details = GetCachedPluginDetails(plugin);
            if (details == null && string.IsNullOrWhiteSpace(plugin.MarketplacePath))
            {
                ShowPluginDetails(plugin, null);
                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.PluginDetailsRead.Template", "Details for {0} loaded."),
                    GetPluginDisplayName(plugin)));
                return;
            }

            if (details == null &&
                !await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("PluginManagementForm.Action.ReadPluginDetails", "Read plugin details")))
            {
                return;
            }

            try
            {
                if (details == null)
                {
                    SetBusy(true);
                    SetStatus(string.Format(
                        UiLocalization.Text("PluginManagementForm.Status.ReadingPluginDetails.Template", "Reading details for {0}..."),
                        GetPluginDisplayName(plugin)));

                    details = await _appServerClient.ReadPluginAsync(plugin.MarketplacePath, plugin.Name);
                    var pluginKey = GetPluginKey(plugin);
                    if (!string.IsNullOrWhiteSpace(pluginKey))
                    {
                        _detailCache[pluginKey] = details;
                        CacheAuthApps(plugin, GetAuthApps(details));
                    }
                }

                ShowPluginDetails(plugin, details);
                RefreshActionButtons();
                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.PluginDetailsRead.Template", "Details for {0} loaded."),
                    GetPluginDisplayName(plugin)));
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("PluginManagementForm.Status.ReadPluginDetailsFailed", "Failed to read plugin details."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async System.Threading.Tasks.Task InstallSelectedPluginAsync()
        {
            var item = GetSelectedMarketItem();
            if (item == null || _isBusy)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("PluginManagementForm.Action.InstallPlugin", "Install plugin")))
            {
                return;
            }

            if (!EnsureMarketplaceMutationAvailable())
            {
                return;
            }

            var displayName = GetMarketItemDisplayName(item);
            List<AppServerPluginAppInfo> authApps = new List<AppServerPluginAppInfo>();

            if (!CanInstallMarketItemSource(item))
            {
                var message = GetInstallSourceUnavailableMessage(item);
                SetStatus(message, true);
                MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                SetBusy(true);
                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.InstallingPlugin.Template", "Installing plugin {0}..."),
                    displayName));

                var plugin = item.AppServerPlugin;
                var result = await InstallAppServerPluginAsync(plugin);
                var pluginKey = GetPluginKey(plugin);
                _preferredInstalledPluginId = pluginKey;
                if (!string.IsNullOrWhiteSpace(pluginKey))
                {
                    _detailCache.Remove(pluginKey);
                    authApps = result != null && result.AppsNeedingAuth != null
                        ? result.AppsNeedingAuth.Where(app => app != null).ToList()
                        : new List<AppServerPluginAppInfo>();
                    CacheAuthApps(plugin, authApps);
                }
            }
            catch (Exception)
            {
                var message = UiLocalization.Text("PluginManagementForm.Status.InstallPluginFailed", "Failed to install plugin.");
                SetStatus(message, true);
                MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            await LoadDataAsync();

            if (authApps.Count > 0)
            {
                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.PluginInstalledNeedsAuth.Template", "{0} installed. {1} authorization item(s) still required."),
                    displayName,
                    authApps.Count), true);
                MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text("PluginManagementForm.Message.PluginInstalledNeedsAuth.Template", "{0} is installed, but {1} authorization item(s) are still required. Use \"Open Auth\" to continue."),
                        displayName,
                        authApps.Count),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.PluginInstalled.Template", "{0} installed."),
                    displayName), true);
            }
        }

        private async System.Threading.Tasks.Task<AppServerPluginInstallResult> InstallAppServerPluginAsync(AppServerPluginSummary plugin)
        {
            if (!PluginAppServerInstallSourceResolver.CanInstall(plugin))
            {
                throw new InvalidOperationException(UiLocalization.Text(
                    "PluginManagementForm.Message.PluginInstallSourceUnavailable",
                    "This plugin market item does not include an installable marketplace source."));
            }

            if (PluginAppServerInstallSourceResolver.HasRemoteMarketplaceInstallSource(plugin))
            {
                return await _appServerClient.InstallRemotePluginAsync(
                    PluginAppServerInstallSourceResolver.ResolveRemoteMarketplaceName(plugin),
                    PluginAppServerInstallSourceResolver.ResolveRemotePluginId(plugin));
            }

            return await _appServerClient.InstallPluginAsync(plugin.MarketplacePath, plugin.Name);
        }

        // Removed local retry and cache-management path: marketplace target state belongs to App Server.

        private async System.Threading.Tasks.Task UninstallSelectedPluginAsync()
        {
            var plugin = GetSelectedInstalledPlugin();
            var uninstallPluginId = PluginAppServerInstallSourceResolver.ResolveUninstallPluginId(plugin);
            if (plugin == null || _isBusy || !plugin.Installed || string.IsNullOrWhiteSpace(uninstallPluginId))
            {
                return;
            }

            var confirmResult = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text("PluginManagementForm.Message.ConfirmUninstall.Template", "Are you sure you want to uninstall {0}?"),
                    GetPluginDisplayName(plugin)),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirmResult != DialogResult.Yes)
            {
                return;
            }

            var appServerAvailable = await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("PluginManagementForm.Action.UninstallPlugin", "Uninstall plugin"));
            if (!appServerAvailable)
            {
                return;
            }

            if (!EnsureMarketplaceMutationAvailable())
            {
                return;
            }

            try
            {
                SetBusy(true);
                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.UninstallingPlugin.Template", "Uninstalling plugin {0}..."),
                    GetPluginDisplayName(plugin)),
                    true);

                await _appServerClient.UninstallPluginAsync(uninstallPluginId);

                var pluginKey = GetPluginKey(plugin);
                if (!string.IsNullOrWhiteSpace(pluginKey))
                {
                    _preferredMarketPluginId = pluginKey;
                    _detailCache.Remove(pluginKey);
                    RemoveCachedAuthApps(plugin);
                }
            }
            catch (Exception ex)
            {
                SetStatus(
                    UiLocalization.Text("PluginManagementForm.Status.UninstallPluginFailed", "Failed to uninstall plugin."),
                    true);
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                SetBusy(false);
            }

            await LoadDataAsync();
            if (PluginUninstallWorkflow.IsStillInstalled(plugin, _installedPlugins))
            {
                var message = PluginUninstallStatusMessageComposer.BuildStillInstalledMessage(plugin);
                SetStatus(message, true);
                MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetStatus(string.Format(
                UiLocalization.Text("PluginManagementForm.Status.PluginUninstalled.Template", "{0} uninstalled."),
                GetPluginDisplayName(plugin)),
                true);
        }

        private async System.Threading.Tasks.Task OpenSelectedPluginAuthAsync()
        {
            var plugin = GetSelectedInstalledPlugin();
            if (plugin == null || _isBusy)
            {
                return;
            }

            var authApps = GetCachedAuthApps(plugin)
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.InstallUrl))
                .ToList();
            if (authApps.Count == 0 &&
                CanRefreshPluginAuthorization(plugin))
            {
                try
                {
                    SetBusy(true);
                    SetStatus(string.Format(
                        UiLocalization.Text("PluginManagementForm.Status.LoadingAuthorization.Template", "Loading authorization for {0}..."),
                        GetPluginDisplayName(plugin)),
                        true);

                    var result = await InstallAppServerPluginAsync(plugin);
                    CacheAuthApps(
                        plugin,
                        result == null || result.AppsNeedingAuth == null
                            ? new List<AppServerPluginAppInfo>()
                            : result.AppsNeedingAuth.Where(item => item != null).ToList());
                    authApps = GetCachedAuthApps(plugin)
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.InstallUrl))
                        .ToList();
                }
                catch (Exception ex)
                {
                    SetStatus(UiLocalization.Text(
                        "PluginManagementForm.Status.LoadAuthorizationFailed",
                        "Failed to load plugin authorization."));
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                finally
                {
                    SetBusy(false);
                }
            }

            if (authApps.Count == 0)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("PluginManagementForm.Message.NoAuthEntries", "No pending authorization entry is available. This plugin may already be connected."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (authApps.Count > 1)
            {
                var result = MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text("PluginManagementForm.Message.ConfirmOpenAllAuth.Template", "Current plugin has {0} authorization entries. Open all?"),
                        authApps.Count),
                    AppConstants.AppName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1);
                if (result != DialogResult.Yes)
                {
                    return;
                }
            }

            try
            {
                foreach (var app in authApps)
                {
                    OpenExternalUrl(app.InstallUrl);
                }

                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.AuthOpened.Template", "Opened authorization entries for {0}."),
                    GetPluginDisplayName(plugin)));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenSelectedPluginFolder()
        {
            var plugin = GetSelectedInstalledPlugin();
            if (plugin == null)
            {
                return;
            }

            var pluginFolderPath = ResolveSelectedPluginFolderPath(plugin);
            if (string.IsNullOrWhiteSpace(pluginFolderPath))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("PluginManagementForm.Message.PluginFolderMissing", "Current plugin folder does not exist and cannot be opened."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = pluginFolderPath,
                    UseShellExecute = true
                });
                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.PluginFolderOpened.Template", "Opened folder for {0}."),
                    GetPluginDisplayName(plugin)));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RebuildInstalledList()
        {
            var selectedPluginId = !string.IsNullOrWhiteSpace(_preferredInstalledPluginId)
                ? _preferredInstalledPluginId
                : GetSelectedInstalledPluginId();
            var plugins = GetInstalledPluginsForView()
                .Where(item => item != null && item.Installed)
                .OrderBy(item => GetPluginDisplayName(item), StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _installedListView.BeginUpdate();
            try
            {
                _installedListView.Items.Clear();
                foreach (var plugin in plugins)
                {
                    var item = new ListViewItem(GetPluginDisplayName(plugin));
                    item.SubItems.Add(BuildPluginStatusText(plugin));
                    item.SubItems.Add(GetPluginListSummary(plugin));
                    item.Tag = plugin;
                    _installedListView.Items.Add(item);
                }

                SelectPluginById<AppServerPluginSummary>(_installedListView, selectedPluginId, GetPluginKey);
            }
            finally
            {
                _installedListView.EndUpdate();
            }

            _preferredInstalledPluginId = null;
            RefreshActionButtons();
        }

        private void ClearMarketList()
        {
            _marketListView.Items.Clear();
            _preferredMarketPluginId = null;
            RefreshActionButtons();
        }

        private void FillMarketList(List<PluginMarketListItem> visibleItems, int page, int totalCount)
        {
            var selectedPluginId = !string.IsNullOrWhiteSpace(_preferredMarketPluginId)
                ? _preferredMarketPluginId
                : GetSelectedMarketPluginId();
            _marketListView.BeginUpdate();
            try
            {
                _marketListView.Items.Clear();
                foreach (var item in visibleItems ?? new List<PluginMarketListItem>())
                {
                    _marketListView.Items.Add(CreateMarketListItem(item));
                }

                foreach (var navigationItem in PluginMarketPaging.GetNavigationItems(
                    page,
                    totalCount,
                    UiLocalization.Text("PluginManagementForm.Navigation.PreviousPage", "Previous page"),
                    UiLocalization.Text("PluginManagementForm.Navigation.NextPage", "Next page")))
                {
                    _marketListView.Items.Add(CreateMarketNavigationListItem(navigationItem));
                }

                SelectPluginById<PluginMarketListItem>(_marketListView, selectedPluginId, GetMarketItemKey);
                if (_marketListView.SelectedItems.Count == 0 && _marketListView.Items.Count > 0)
                {
                    _marketListView.Items[0].Selected = true;
                    _marketListView.Items[0].Focused = true;
                }
            }
            finally
            {
                _marketListView.EndUpdate();
            }

            _preferredMarketPluginId = null;
            RefreshActionButtons();
        }

        private IEnumerable<PluginMarketListItem> GetAvailableMarketItems()
        {
            var items = PluginMarketListComposer.ComposeAvailableItems(
                _plugins,
                _remoteMarketItems,
                UiLocalization.Text("PluginManagementForm.Source.RuntimeMarket", "Runtime Market"));
            var visibleItems = new List<PluginMarketListItem>();

            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }

                var viewItem = CloneMarketListItem(item);
                if (viewItem.IsRemoteCatalog && viewItem.RemoteEntry != null)
                {
                    var installedPlugin = FindInstalledPlugin(viewItem);
                    var isInstalled = installedPlugin != null;
                    var installedVersion = installedPlugin == null ? null : installedPlugin.LocalVersion;
                    viewItem.InstalledVersion = installedVersion;
                    viewItem.AvailabilityStatus = PluginMarketStatusResolver.ResolveRemoteStatus(
                        isInstalled,
                        installedVersion,
                        viewItem.RemoteEntry.Version);
                    if (viewItem.AvailabilityStatus == PluginMarketAvailabilityStatus.None)
                    {
                        continue;
                    }
                }
                else
                {
                    viewItem.AvailabilityStatus = PluginMarketAvailabilityStatus.InstallAvailable;
                }

                visibleItems.Add(viewItem);
            }

            return visibleItems;
        }

        private List<AppServerPluginSummary> GetInstalledPluginsForView()
        {
            return _installedPlugins == null
                ? new List<AppServerPluginSummary>()
                : DeduplicatePluginSummariesForView(_installedPlugins);
        }

        private static List<AppServerPluginSummary> DeduplicatePluginSummariesForView(IEnumerable<AppServerPluginSummary> plugins)
        {
            var result = new List<AppServerPluginSummary>();
            var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (plugins == null)
            {
                return result;
            }

            foreach (var plugin in plugins.Where(item => item != null))
            {
                if (AddPluginIdentityKeysForCandidate(existingIds, GetPluginKey(plugin), plugin.DisplayName))
                {
                    result.Add(plugin);
                    continue;
                }

                var existingIndex = FindMatchingPluginSummaryIndex(result, plugin);
                if (existingIndex < 0)
                {
                    continue;
                }

                if (ShouldPreferPluginSummary(plugin, result[existingIndex]))
                {
                    result[existingIndex] = plugin;
                }
            }

            return result;
        }

        private static int FindMatchingPluginSummaryIndex(IReadOnlyList<AppServerPluginSummary> plugins, AppServerPluginSummary candidate)
        {
            if (plugins == null || candidate == null)
            {
                return -1;
            }

            var candidateKey = GetPluginKey(candidate);
            var candidateDisplayKey = GetPluginDisplayKey(candidate);
            for (var i = 0; i < plugins.Count; i++)
            {
                var plugin = plugins[i];
                if (plugin == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(candidateKey) && PluginIdentityMatches(plugin, candidateKey))
                {
                    return i;
                }

                if (!HasMarketplaceQualifier(candidateKey) &&
                    !HasMarketplaceQualifier(GetPluginKey(plugin)) &&
                    !string.IsNullOrWhiteSpace(candidateDisplayKey) &&
                    string.Equals(GetPluginDisplayKey(plugin), candidateDisplayKey, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool ShouldPreferPluginSummary(AppServerPluginSummary candidate, AppServerPluginSummary existing)
        {
            if (candidate == null)
            {
                return false;
            }

            if (existing == null)
            {
                return true;
            }

            if (candidate.Enabled != existing.Enabled)
            {
                return candidate.Enabled;
            }

            if (candidate.Installed != existing.Installed)
            {
                return candidate.Installed;
            }

            if (HasPluginDetailsSource(candidate) != HasPluginDetailsSource(existing))
            {
                return HasPluginDetailsSource(candidate);
            }

            return false;
        }

        private static bool HasPluginDetailsSource(AppServerPluginSummary plugin)
        {
            return plugin != null &&
                (!string.IsNullOrWhiteSpace(plugin.MarketplacePath) ||
                 !string.IsNullOrWhiteSpace(plugin.SourcePath));
        }

        private AppServerPluginSummary FindInstalledPlugin(PluginMarketListItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.PluginId))
            {
                return null;
            }

            var pluginId = GetMarketItemPluginIdentity(item);
            return (_installedPlugins ?? new List<AppServerPluginSummary>())
                .Where(plugin => plugin != null && plugin.Installed)
                .FirstOrDefault(plugin => PluginIdentityMatches(plugin, pluginId));
        }

        private static string GetMarketItemPluginIdentity(PluginMarketListItem item)
        {
            if (item == null)
            {
                return null;
            }

            if (item.AppServerPlugin != null)
            {
                return GetPluginKey(item.AppServerPlugin);
            }

            var pluginId = string.IsNullOrWhiteSpace(item.PluginId) ? null : item.PluginId.Trim();
            if (string.IsNullOrWhiteSpace(pluginId) || HasMarketplaceQualifier(pluginId))
            {
                return pluginId;
            }

            var marketId = item.RemoteEntry == null ? null : item.RemoteEntry.MarketId;
            return string.IsNullOrWhiteSpace(marketId)
                ? pluginId
                : pluginId + "@" + marketId;
        }

        private List<PluginMarketSnapshot> GetRemoteMarketSummaries()
        {
            return _remoteMarketCatalog == null || _remoteMarketCatalog.Markets == null
                ? new List<PluginMarketSnapshot>()
                : _remoteMarketCatalog.Markets.Where(item => item != null).ToList();
        }

        private static string GetMarketItemCategory(PluginMarketListItem item)
        {
            if (item == null)
            {
                return null;
            }

            if (item.RemoteEntry != null && !string.IsNullOrWhiteSpace(item.RemoteEntry.Category))
            {
                return item.RemoteEntry.Category.Trim();
            }

            return item.AppServerPlugin == null || string.IsNullOrWhiteSpace(item.AppServerPlugin.Category)
                ? null
                : item.AppServerPlugin.Category.Trim();
        }


        private void RebuildMarketSourceTree()
        {
            RebuildMarketSourceTreeForScope(_currentMarketScope);
        }

        private void RebuildMarketSourceTreeForScope(PluginMarketBrowseScope selectedScope)
        {
            var selectedKey = (selectedScope ?? PluginMarketBrowseScope.All()).Key;
            var sourceNames = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var market in GetRemoteMarketSummaries())
            {
                var marketName = GetMarketDisplayName(market);
                if (!string.IsNullOrWhiteSpace(marketName))
                {
                    sourceNames.Add(marketName);
                }
            }

            foreach (var item in GetAvailableMarketItems())
            {
                if (item != null && !string.IsNullOrWhiteSpace(item.MarketName))
                {
                    sourceNames.Add(item.MarketName.Trim());
                }
            }

            _marketSourceTreeView.BeginUpdate();
            try
            {
                _marketSourceTreeView.Nodes.Clear();
                _marketSourceTreeView.Nodes.Add(CreateMarketSourceNode(
                    UiLocalization.Text("PluginManagementForm.Filter.AllSources", "All Sources"),
                    PluginMarketBrowseScope.All()));

                foreach (var sourceName in sourceNames.OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase))
                {
                    var marketNode = CreateMarketSourceNode(sourceName, PluginMarketBrowseScope.ForMarket(sourceName));
                    foreach (var category in GetLoadedCategoriesForMarket(sourceName))
                    {
                        marketNode.Nodes.Add(CreateMarketSourceNode(category, PluginMarketBrowseScope.ForCategory(sourceName, category)));
                    }

                    _marketSourceTreeView.Nodes.Add(marketNode);
                }

                _marketSourceTreeView.ExpandAll();
                var node = FindMarketSourceNode(_marketSourceTreeView.Nodes, selectedKey) ?? _marketSourceTreeView.Nodes.Cast<TreeNode>().FirstOrDefault();
                if (node != null)
                {
                    _marketSourceTreeView.SelectedNode = node;
                }
            }
            finally
            {
                _marketSourceTreeView.EndUpdate();
            }
        }

        private string ResolveSelectedPluginFolderPath(AppServerPluginSummary plugin)
        {
            return plugin == null || string.IsNullOrWhiteSpace(plugin.SourcePath) || !Directory.Exists(plugin.SourcePath)
                ? null
                : plugin.SourcePath;
        }

        private static string ResolveExistingPluginFolderPath(
            string sourcePath,
            string trackedSourcePath,
            string cacheDirectory)
        {
            if (!string.IsNullOrWhiteSpace(sourcePath) && Directory.Exists(sourcePath))
            {
                return sourcePath;
            }

            if (!string.IsNullOrWhiteSpace(trackedSourcePath) && Directory.Exists(trackedSourcePath))
            {
                return trackedSourcePath;
            }

            return !string.IsNullOrWhiteSpace(cacheDirectory) && Directory.Exists(cacheDirectory)
                ? cacheDirectory
                : null;
        }

        private List<string> GetLoadedCategoriesForMarket(string marketName)
        {
            return GetAvailableMarketItems()
                .Where(item => item != null && string.Equals(item.MarketName, marketName, StringComparison.OrdinalIgnoreCase))
                .Select(GetMarketItemCategory)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static TreeNode CreateMarketSourceNode(string text, PluginMarketBrowseScope scope)
        {
            return new TreeNode(text) { Tag = scope };
        }

        private static TreeNode FindMarketSourceNode(TreeNodeCollection nodes, string key)
        {
            foreach (TreeNode node in nodes)
            {
                var scope = node.Tag as PluginMarketBrowseScope;
                if (scope != null && string.Equals(scope.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return node;
                }

                var child = FindMarketSourceNode(node.Nodes, key);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static string GetMarketScopeDisplayText(PluginMarketBrowseScope scope)
        {
            if (scope == null || scope.AllSources)
            {
                return UiLocalization.Text("PluginManagementForm.Filter.AllSources", "All Sources");
            }

            if (!string.IsNullOrWhiteSpace(scope.Category))
            {
                return scope.MarketName + " / " + scope.Category;
            }

            return scope.MarketName;
        }

        private static string BuildMarketLoadingStatus(PluginMarketBrowseScope scope, string keyword)
        {
            var scopeText = GetMarketScopeDisplayText(scope);
            return string.IsNullOrWhiteSpace(keyword)
                ? string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.LoadingMarketScope.Template", "Loading {0}..."),
                    scopeText)
                : string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.SearchingMarketScope.Template", "Searching {0} for {1}..."),
                    scopeText,
                    keyword.Trim());
        }

        private static string BuildMarketPageStatus(PluginMarketBrowseScope scope, int page, int totalCount)
        {
            var scopeText = GetMarketScopeDisplayText(scope);
            if (totalCount <= 0)
            {
                return string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.NoMarketPluginsInScope.Template", "{0}: no matching plugins."),
                    scopeText);
            }

            var totalPages = PluginMarketPaging.GetTotalPages(totalCount);
            if (totalPages <= 1)
            {
                return string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.MarketScopeLoaded.Template", "{0}: {1} plugin(s)."),
                    scopeText,
                    totalCount);
            }

            return string.Format(
                UiLocalization.Text("PluginManagementForm.Status.MarketPageLoaded.Template", "{0}: page {1} / {2}, {3} plugin(s)."),
                scopeText,
                page,
                totalPages,
                totalCount);
        }

        private void UpdateStatusForCurrentView()
        {
            if (_isBusy)
            {
                return;
            }

            if (IsInstalledTabActive())
            {
                if (_installedListView.Items.Count == 0)
                {
                    SetStatus(UiLocalization.Text(
                        "PluginManagementForm.Status.NoInstalledPlugins",
                        "No installed plugins are available."));
                    return;
                }

                SetStatus(string.Format(
                    UiLocalization.Text(
                        "PluginManagementForm.Status.InstalledSummary.Template",
                        "Installed tab shows {0} plugins. Select a plugin and press Enter to open details, or use the action buttons below."),
                    _installedListView.Items.Count));
                return;
            }

            if (_marketListView.Items.Count == 0)
            {
                SetStatus(UiLocalization.Text(
                    "PluginManagementForm.Status.MarketSelectSource",
                    "Select a market source and press Enter to load plugins."));
                return;
            }

            var pluginRows = _marketListView.Items
                .Cast<ListViewItem>()
                .Where(item => item.Tag is PluginMarketListItem)
                .ToList();
            var visibleSources = pluginRows
                .Select(item => item.SubItems.Count > 2 ? item.SubItems[2].Text : string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Count();

            SetStatus(string.Format(
                UiLocalization.Text(
                    "PluginManagementForm.Status.MarketSummary.Template",
                    "Market tab shows {0} available plugins from {1} sources. Search and source filters only affect the current list."),
                pluginRows.Count,
                visibleSources));
        }

        private void ShowPluginDetails(AppServerPluginSummary plugin, AppServerPluginDetails details)
        {
            var detailText = BuildPluginDetailText(plugin, details, GetCachedAuthApps(plugin));
            var isRemotePlugin = PluginAppServerInstallSourceResolver.IsRemoteMarketplacePlugin(plugin);
            var remoteMarketplaceName = !isRemotePlugin
                ? null
                : PluginAppServerInstallSourceResolver.ResolveRemoteMarketplaceName(plugin);
            var remotePluginId = !isRemotePlugin
                ? null
                : PluginAppServerInstallSourceResolver.ResolveRemotePluginId(plugin);
            using (var form = new PluginDetailsForm(
                string.Format(
                    UiLocalization.Text("PluginDetailsForm.Title.Template", "Plugin Details - {0}"),
                    GetPluginDisplayName(plugin)),
                string.Format(
                    UiLocalization.Text("PluginDetailsForm.Header.Template", "Details for {0}"),
                    GetPluginDisplayName(plugin)),
                detailText,
                _appServerClient,
                remoteMarketplaceName,
                remotePluginId,
                details == null ? null : details.Skills))
            {
                form.ShowDialog(this);
            }
        }

        private void ShowRemoteMarketDetails(PluginMarketListItem item)
        {
            using (var form = new PluginDetailsForm(
                string.Format(
                    UiLocalization.Text("PluginDetailsForm.Title.Template", "Plugin Details - {0}"),
                    GetMarketItemDisplayName(item)),
                string.Format(
                    UiLocalization.Text("PluginDetailsForm.Header.Template", "Details for {0}"),
                    GetMarketItemDisplayName(item)),
                BuildRemoteMarketDetailText(item)))
            {
                form.ShowDialog(this);
            }
        }

        private static string BuildPluginDetailText(
            AppServerPluginSummary plugin,
            AppServerPluginDetails details,
            IReadOnlyCollection<AppServerPluginAppInfo> authApps)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Format(UiLocalization.Text("PluginManagementForm.Detail.Plugin.Template", "Plugin: {0}"), GetPluginDisplayName(plugin)));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.InternalName.Template", "Internal Name: {0}"),
                string.IsNullOrWhiteSpace(plugin.Name) ? UiLocalization.Text("PluginManagementForm.Value.UnknownBrackets", "(Unknown)") : plugin.Name));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.Marketplace.Template", "Marketplace: {0}"),
                string.IsNullOrWhiteSpace(plugin.MarketplaceName) ? UiLocalization.Text("PluginManagementForm.Value.UnknownBrackets", "(Unknown)") : plugin.MarketplaceName));
            builder.AppendLine(string.Format(UiLocalization.Text("PluginManagementForm.Detail.Status.Template", "Status: {0}"), BuildPluginStatusText(plugin)));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.Category.Template", "Category: {0}"),
                string.IsNullOrWhiteSpace(plugin.Category) ? UiLocalization.Text("PluginManagementForm.Value.Uncategorized", "Uncategorized") : plugin.Category));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.Developer.Template", "Developer: {0}"),
                string.IsNullOrWhiteSpace(plugin.DeveloperName) ? UiLocalization.Text("PluginManagementForm.Value.Unknown", "Unknown") : plugin.DeveloperName));
            builder.AppendLine(string.Format(UiLocalization.Text("PluginManagementForm.Detail.Source.Template", "Source: {0}"), BuildPluginSourceText(plugin)));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.InstallPolicy.Template", "Install Policy: {0}"),
                string.IsNullOrWhiteSpace(plugin.InstallPolicy) ? UiLocalization.Text("PluginManagementForm.Value.Unknown", "Unknown") : plugin.InstallPolicy));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.AuthPolicy.Template", "Auth Policy: {0}"),
                string.IsNullOrWhiteSpace(plugin.AuthPolicy) ? UiLocalization.Text("PluginManagementForm.Value.Unknown", "Unknown") : plugin.AuthPolicy));

            if (!string.IsNullOrWhiteSpace(plugin.ShortDescription))
            {
                builder.AppendLine(string.Format(UiLocalization.Text("PluginManagementForm.Detail.ShortDescription.Template", "Summary: {0}"), plugin.ShortDescription.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(plugin.LongDescription))
            {
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("PluginManagementForm.Detail.LongDescription", "Long Description:"));
                builder.AppendLine(plugin.LongDescription.Trim());
            }

            if (plugin.Capabilities != null && plugin.Capabilities.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(string.Format(
                    UiLocalization.Text("PluginManagementForm.Detail.Capabilities.Template", "Capabilities: {0}"),
                    string.Join(UiLocalization.Text("PluginManagementForm.Detail.ListSeparator", ", "), plugin.Capabilities)));
            }

            if (plugin.DefaultPrompts != null && plugin.DefaultPrompts.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("PluginManagementForm.Detail.DefaultPrompts", "Default Prompts:"));
                foreach (var prompt in plugin.DefaultPrompts)
                {
                    builder.AppendLine("- " + prompt);
                }
            }

            if (!string.IsNullOrWhiteSpace(plugin.WebsiteUrl))
            {
                builder.AppendLine();
                builder.AppendLine(string.Format(UiLocalization.Text("PluginManagementForm.Detail.Website.Template", "Website: {0}"), plugin.WebsiteUrl.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(plugin.PrivacyPolicyUrl))
            {
                builder.AppendLine(string.Format(UiLocalization.Text("PluginManagementForm.Detail.PrivacyPolicy.Template", "Privacy Policy: {0}"), plugin.PrivacyPolicyUrl.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(plugin.TermsOfServiceUrl))
            {
                builder.AppendLine(string.Format(UiLocalization.Text("PluginManagementForm.Detail.TermsOfService.Template", "Terms of Service: {0}"), plugin.TermsOfServiceUrl.Trim()));
            }

            if (authApps != null && authApps.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("PluginManagementForm.Detail.PendingAuthApps", "Apps Needing Authorization:"));
                foreach (var app in authApps.Where(item => item != null))
                {
                    builder.AppendLine(string.Format(
                        "- {0}{1}",
                        GetPluginAppDisplayName(app),
                        string.IsNullOrWhiteSpace(app.InstallUrl)
                            ? string.Empty
                            : UiLocalization.Text("PluginManagementForm.Detail.UrlPrefix", ": ") + app.InstallUrl));
                }
            }

            if (details == null)
            {
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("PluginManagementForm.Detail.Hint.ReadDetailForMore", "Tip: Press Enter on this plugin in the list to reopen its details after they are loaded."));
                return builder.ToString().TrimEnd();
            }

            if (!string.IsNullOrWhiteSpace(details.Description))
            {
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("PluginManagementForm.Detail.PluginDescription", "Plugin Description:"));
                builder.AppendLine(details.Description.Trim());
            }

            if (details.Skills != null && details.Skills.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(string.Format(
                    UiLocalization.Text("PluginManagementForm.Detail.SkillsCount.Template", "Skills: {0}"),
                    details.Skills.Count));
                foreach (var skill in details.Skills.Where(item => item != null))
                {
                    builder.AppendLine(string.Format(
                        UiLocalization.Text("PluginManagementForm.Detail.SkillItem.Template", "- {0} ({1})"),
                        GetPluginSkillDisplayName(skill),
                        skill.Enabled
                            ? UiLocalization.Text("PluginManagementForm.Detail.SkillEnabled", "Enabled")
                            : UiLocalization.Text("PluginManagementForm.Detail.SkillDisabled", "Disabled")));
                    if (!string.IsNullOrWhiteSpace(skill.ShortDescription))
                    {
                        builder.AppendLine("  " + skill.ShortDescription.Trim());
                    }
                }
            }

            if (details.Apps != null && details.Apps.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(string.Format(
                    UiLocalization.Text("PluginManagementForm.Detail.AppsCount.Template", "Apps: {0}"),
                    details.Apps.Count));
                foreach (var app in details.Apps.Where(item => item != null))
                {
                    builder.AppendLine(string.Format(
                        UiLocalization.Text("PluginManagementForm.Detail.AppItem.Template", "- {0} ({1})"),
                        GetPluginAppDisplayName(app),
                        GetPluginAppAuthorizationStatus(app)));
                    if (!string.IsNullOrWhiteSpace(app.InstallUrl))
                    {
                        builder.AppendLine("  " + app.InstallUrl.Trim());
                    }
                }
            }

            if (details.McpServers != null && details.McpServers.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine(string.Format(
                    UiLocalization.Text("PluginManagementForm.Detail.McpServers.Template", "MCP Servers: {0}"),
                    string.Join(UiLocalization.Text("PluginManagementForm.Detail.ListSeparator", ", "), details.McpServers)));
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildRemoteMarketDetailText(PluginMarketListItem item)
        {
            var builder = new StringBuilder();
            var entry = item == null ? null : item.RemoteEntry;
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.Plugin.Template", "Plugin: {0}"),
                GetMarketItemDisplayName(item)));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.InternalName.Template", "Internal Name: {0}"),
                string.IsNullOrWhiteSpace(item == null ? null : item.PluginId)
                    ? UiLocalization.Text("PluginManagementForm.Value.UnknownBrackets", "(Unknown)")
                    : item.PluginId));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.Marketplace.Template", "Marketplace: {0}"),
                string.IsNullOrWhiteSpace(item == null ? null : item.MarketName)
                    ? UiLocalization.Text("PluginManagementForm.Value.UnknownBrackets", "(Unknown)")
                    : item.MarketName));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.Status.Template", "Status: {0}"),
                BuildMarketItemStatusText(item)));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.Category.Template", "Category: {0}"),
                string.IsNullOrWhiteSpace(entry == null ? null : entry.Category)
                    ? UiLocalization.Text("PluginManagementForm.Value.Uncategorized", "Uncategorized")
                    : entry.Category));
            builder.AppendLine(string.Format(
                UiLocalization.Text("PluginManagementForm.Detail.AuthPolicy.Template", "Auth Policy: {0}"),
                string.IsNullOrWhiteSpace(entry == null ? null : entry.AuthPolicy)
                    ? UiLocalization.Text("PluginManagementForm.Value.Unknown", "Unknown")
                    : entry.AuthPolicy));

            if (!string.IsNullOrWhiteSpace(entry == null ? null : entry.Version))
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("PluginManagementForm.Detail.Version.Template", "Version: {0}"),
                    entry.Version.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(item == null ? null : item.InstalledVersion))
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("PluginManagementForm.Detail.InstalledVersion.Template", "Installed Version: {0}"),
                    item.InstalledVersion.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(item == null ? null : item.Summary))
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("PluginManagementForm.Detail.ShortDescription.Template", "Summary: {0}"),
                    item.Summary.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(entry == null ? null : entry.DetailsUrl))
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("PluginManagementForm.Detail.DetailsUrl.Template", "Details: {0}"),
                    entry.DetailsUrl.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(entry == null ? null : entry.ManifestUrl))
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("PluginManagementForm.Detail.ManifestUrl.Template", "Manifest: {0}"),
                    entry.ManifestUrl.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(entry == null ? null : entry.PackageUrl))
            {
                builder.AppendLine(string.Format(
                    UiLocalization.Text("PluginManagementForm.Detail.PackageUrl.Template", "Package: {0}"),
                    entry.PackageUrl.Trim()));
            }

            return builder.ToString().TrimEnd();
        }

        private void RefreshActionButtons()
        {
            var plugin = GetSelectedInstalledPlugin();
            var marketItem = GetSelectedMarketItem();
            var authApps = plugin == null ? new List<AppServerPluginAppInfo>() : GetCachedAuthApps(plugin);
            var isInstalledTab = IsInstalledTabActive();

            _refreshButton.Enabled = !_isBusy;
            _refreshMarketplaceButton.Enabled = !_isBusy;
            _registerMarketplaceButton.Enabled = CanRegisterMarketplace(marketItem, isInstalledTab);
            _marketSearchTextBox.Enabled = !_isBusy;
            _marketSearchButton.Enabled = !_isBusy;
            _marketSourceTreeView.Enabled = !_isBusy;
            _installButton.Enabled = CanInstallMarketItem(marketItem, isInstalledTab);
            _uninstallButton.Enabled = CanUninstallPlugin(plugin, isInstalledTab);
            _openAuthButton.Enabled = !_isBusy &&
                isInstalledTab &&
                plugin != null &&
                (authApps.Any(item => item != null && !string.IsNullOrWhiteSpace(item.InstallUrl)) ||
                 CanRefreshPluginAuthorization(plugin));
            _openFolderButton.Enabled = !_isBusy && isInstalledTab && plugin != null && !string.IsNullOrWhiteSpace(ResolveSelectedPluginFolderPath(plugin));
            _closeButton.Enabled = !_isBusy;
        }

        private void RefreshContextMenuItems()
        {
            var plugin = GetSelectedInstalledPlugin();
            var marketItem = GetSelectedMarketItem();
            var authApps = plugin == null ? new List<AppServerPluginAppInfo>() : GetCachedAuthApps(plugin);
            var isInstalledTab = IsInstalledTabActive();

            _installMenuItem.Enabled = CanInstallMarketItem(marketItem, isInstalledTab);
            _registerMarketplaceMenuItem.Enabled = CanRegisterMarketplace(marketItem, isInstalledTab);
            _uninstallMenuItem.Enabled = CanUninstallPlugin(plugin, isInstalledTab);
            _openAuthMenuItem.Enabled = !_isBusy &&
                isInstalledTab &&
                plugin != null &&
                (authApps.Any(item => item != null && !string.IsNullOrWhiteSpace(item.InstallUrl)) ||
                 CanRefreshPluginAuthorization(plugin));
            _openFolderMenuItem.Enabled = !_isBusy && isInstalledTab && plugin != null && !string.IsNullOrWhiteSpace(ResolveSelectedPluginFolderPath(plugin));
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            UseWaitCursor = busy;
            RefreshActionButtons();
            RefreshContextMenuItems();
        }

        private void SetStatus(string text)
        {
            SetStatus(text, false);
        }

        private void SetStatus(string text, bool announce)
        {
            _statusLabel.Text = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
            if (announce && !string.IsNullOrWhiteSpace(_statusLabel.Text))
            {
                _screenReaderService?.Speak(_statusLabel.Text, true, false, true);
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }

        private async System.Threading.Tasks.Task<bool> EnsureAppServerAvailableForActionAsync(string actionName)
        {
            try
            {
                await _appServerClient.ConnectAsync();
                if (!AppServerProtocolFeatureSupport.IsSupported(
                        _appServerClient.ProtocolCapabilities,
                        AppServerProtocolFeature.PluginManagement))
                {
                    var featureName = UiLocalization.Text("PluginManagementForm.Feature.PluginManagement", "Plugin management");
                    var message = string.Format(
                        UiLocalization.Text(
                            "ProtocolCapabilities.Message.FeatureNotSupported.Template",
                            "Current Codex runtime does not support {0}. Switch to a standard or newer runtime to use this feature."),
                        featureName);
                    SetStatus(message);
                    MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                SetStatus(string.Format(
                    UiLocalization.Text("PluginManagementForm.Status.ActionFailedDisconnected.Template", "{0} failed because App Server is not connected."),
                    actionName));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool IsInstalledTabActive()
        {
            return _tabControl.SelectedTab == _installedTabPage;
        }

        private AppServerPluginSummary GetSelectedInstalledPlugin()
        {
            return _installedListView.SelectedItems.Count == 0 ? null : _installedListView.SelectedItems[0].Tag as AppServerPluginSummary;
        }

        private PluginMarketListItem GetSelectedMarketItem()
        {
            return _marketListView.SelectedItems.Count == 0 ? null : _marketListView.SelectedItems[0].Tag as PluginMarketListItem;
        }

        private PluginMarketNavigationItem GetSelectedMarketNavigationItem()
        {
            return _marketListView.SelectedItems.Count == 0 ? null : _marketListView.SelectedItems[0].Tag as PluginMarketNavigationItem;
        }

        private string GetSelectedInstalledPluginId()
        {
            var plugin = GetSelectedInstalledPlugin();
            return GetPluginKey(plugin);
        }

        private string GetSelectedMarketPluginId()
        {
            var item = GetSelectedMarketItem();
            return GetMarketItemKey(item);
        }

        private string GetSelectedMarketSource()
        {
            var scope = GetSelectedMarketScope();
            return scope == null || scope.AllSources ? null : scope.MarketName;
        }

        private PluginMarketBrowseScope GetSelectedMarketScope()
        {
            return _marketSourceTreeView.SelectedNode == null
                ? PluginMarketBrowseScope.All()
                : (_marketSourceTreeView.SelectedNode.Tag as PluginMarketBrowseScope) ?? PluginMarketBrowseScope.All();
        }

        private static void SelectPluginById<T>(ListView listView, string pluginId, Func<T, string> keySelector)
            where T : class
        {
            if (listView == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(pluginId))
            {
                if (listView.Items.Count > 0)
                {
                    listView.Items[0].Selected = true;
                    listView.Items[0].Focused = true;
                }

                return;
            }

            foreach (ListViewItem item in listView.Items)
            {
                var data = item.Tag as T;
                if (data != null && string.Equals(keySelector(data), pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    item.Selected = true;
                    item.Focused = true;
                    item.EnsureVisible();
                    return;
                }
            }

            if (listView.Items.Count > 0)
            {
                listView.Items[0].Selected = true;
                listView.Items[0].Focused = true;
            }
        }

        private AppServerPluginDetails GetCachedPluginDetails(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return null;
            }

            AppServerPluginDetails details;
            return _detailCache.TryGetValue(GetPluginKey(plugin), out details) ? details : null;
        }

        private List<AppServerPluginAppInfo> GetCachedAuthApps(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return new List<AppServerPluginAppInfo>();
            }

            foreach (var key in PluginAppServerInstallSourceResolver.BuildCacheKeys(plugin))
            {
                List<AppServerPluginAppInfo> apps;
                if (_authAppsCache.TryGetValue(key, out apps) && apps != null)
                {
                    return apps.Where(IsPluginAppAuthorizationEntry).ToList();
                }
            }

            var details = GetCachedPluginDetails(plugin);
            return GetAuthApps(details);
        }

        private static bool CanRefreshPluginAuthorization(AppServerPluginSummary plugin)
        {
            return plugin != null &&
                plugin.Installed &&
                PluginAppServerInstallSourceResolver.IsRemoteMarketplacePlugin(plugin) &&
                string.Equals(plugin.AuthPolicy, "ON_INSTALL", StringComparison.OrdinalIgnoreCase);
        }

        private void CacheAuthApps(AppServerPluginSummary plugin, List<AppServerPluginAppInfo> apps)
        {
            if (plugin == null)
            {
                return;
            }

            var normalizedApps = apps == null
                ? new List<AppServerPluginAppInfo>()
                : apps.Where(item => item != null).ToList();
            foreach (var key in PluginAppServerInstallSourceResolver.BuildCacheKeys(plugin))
            {
                _authAppsCache[key] = normalizedApps;
            }
        }

        private void RemoveCachedAuthApps(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return;
            }

            foreach (var key in PluginAppServerInstallSourceResolver.BuildCacheKeys(plugin))
            {
                _authAppsCache.Remove(key);
            }
        }

        private static List<AppServerPluginAppInfo> GetAuthApps(AppServerPluginDetails details)
        {
            return details == null || details.Apps == null
                ? new List<AppServerPluginAppInfo>()
                : details.Apps.Where(IsPluginAppAuthorizationEntry).ToList();
        }

        private static bool IsPluginAppAuthorizationEntry(AppServerPluginAppInfo app)
        {
            return app != null &&
                (app.NeedsAuth || !string.IsNullOrWhiteSpace(app.InstallUrl));
        }

        private static string GetPluginAppAuthorizationStatus(AppServerPluginAppInfo app)
        {
            if (app != null && app.NeedsAuth)
            {
                return UiLocalization.Text(
                    "PluginManagementForm.Detail.AppNeedsAuth",
                    "Authorization Required");
            }

            return app != null && !string.IsNullOrWhiteSpace(app.InstallUrl)
                ? UiLocalization.Text(
                    "PluginManagementForm.Detail.AppAuthAvailable",
                    "Connection Setup Available")
                : UiLocalization.Text(
                    "PluginManagementForm.Detail.AppNoAuth",
                    "No Authorization Required");
        }

        private static string GetPluginKey(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(plugin.PluginId))
            {
                return plugin.PluginId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(plugin.MarketplacePath) && !string.IsNullOrWhiteSpace(plugin.Name))
            {
                return plugin.MarketplacePath.Trim() + "|" + plugin.Name.Trim();
            }

            return plugin.Name;
        }


        private static void AddPluginIdentityKeys(HashSet<string> keys, AppServerPluginSummary plugin)
        {
            if (keys == null || plugin == null)
            {
                return;
            }

            var pluginKey = GetPluginKey(plugin);
            AddPluginIdentityKey(keys, pluginKey);
            AddPluginIdentityKey(keys, GetPluginBaseKey(plugin));
            AddPluginDisplayIdentityKey(keys, plugin.DisplayName);
        }

        private static bool AddPluginIdentityKeysForCandidate(HashSet<string> keys, string pluginId, string displayName)
        {
            if (keys == null)
            {
                return false;
            }

            var identityKey = string.IsNullOrWhiteSpace(pluginId) ? null : pluginId.Trim();
            var baseKey = GetPluginBaseKey(identityKey);
            var displayKey = GetPluginDisplayKey(displayName);
            if (string.IsNullOrWhiteSpace(identityKey) &&
                string.IsNullOrWhiteSpace(baseKey) &&
                string.IsNullOrWhiteSpace(displayKey))
            {
                return false;
            }

            if (HasMarketplaceQualifier(identityKey))
            {
                if (!keys.Add(identityKey))
                {
                    return false;
                }

                AddPluginIdentityKey(keys, baseKey);
                AddPluginDisplayIdentityKey(keys, displayName);
                return true;
            }

            if ((!string.IsNullOrWhiteSpace(identityKey) && keys.Contains(identityKey)) ||
                (!string.IsNullOrWhiteSpace(baseKey) && keys.Contains(baseKey)) ||
                (!string.IsNullOrWhiteSpace(displayKey) && keys.Contains(displayKey)))
            {
                return false;
            }

            AddPluginIdentityKey(keys, identityKey);
            AddPluginDisplayIdentityKey(keys, displayName);
            return true;
        }

        private static bool AddPluginIdentityKey(HashSet<string> keys, string value)
        {
            if (keys == null || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            if (keys.Contains(normalized))
            {
                return false;
            }

            keys.Add(normalized);
            return true;
        }

        private static bool AddPluginDisplayIdentityKey(HashSet<string> keys, string displayName)
        {
            var displayKey = GetPluginDisplayKey(displayName);
            if (keys == null || string.IsNullOrWhiteSpace(displayKey))
            {
                return false;
            }

            keys.Add(displayKey);
            return true;
        }

        private static bool PluginIdentityMatches(AppServerPluginSummary plugin, string pluginId)
        {
            if (plugin == null || string.IsNullOrWhiteSpace(pluginId))
            {
                return false;
            }

            var target = pluginId.Trim();
            var targetBase = GetPluginBaseKey(target);
            var targetDisplay = GetPluginDisplayKey(target);
            var pluginKey = GetPluginKey(plugin);
            var pluginBase = GetPluginBaseKey(plugin);
            var pluginDisplay = GetPluginDisplayKey(plugin);
            if (string.Equals(pluginKey, target, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (HasMarketplaceQualifier(pluginKey) && HasMarketplaceQualifier(target))
            {
                return false;
            }

            return
                string.Equals(pluginKey, targetBase, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pluginBase, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pluginBase, targetBase, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(targetDisplay) &&
                 string.Equals(pluginDisplay, targetDisplay, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetPluginBaseKey(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(plugin.Name))
            {
                return plugin.Name.Trim();
            }

            return GetPluginBaseKey(GetPluginKey(plugin));
        }

        private static string GetPluginBaseKey(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                return null;
            }

            var text = pluginId.Trim();
            var separatorIndex = text.IndexOf('@');
            return separatorIndex > 0 ? text.Substring(0, separatorIndex) : text;
        }

        private static bool HasMarketplaceQualifier(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                return false;
            }

            var text = pluginId.Trim();
            var separatorIndex = text.IndexOf('@');
            return separatorIndex > 0 && separatorIndex < text.Length - 1;
        }

        private static string GetPluginDisplayKey(AppServerPluginSummary plugin)
        {
            return plugin == null ? null : GetPluginDisplayKey(plugin.DisplayName);
        }

        private static string GetPluginDisplayKey(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return null;
            }

            return "display:" + displayName.Trim();
        }

        private static string GetMarketItemKey(PluginMarketListItem item)
        {
            if (item == null)
            {
                return null;
            }

            if (item.AppServerPlugin != null)
            {
                return GetPluginKey(item.AppServerPlugin);
            }

            if (!string.IsNullOrWhiteSpace(item.PluginId))
            {
                return item.PluginId.Trim();
            }

            return item.DisplayName;
        }

        private static string GetPluginDisplayName(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return UiLocalization.Text("PluginManagementForm.Value.UnknownPlugin", "(Unknown Plugin)");
            }

            return !string.IsNullOrWhiteSpace(plugin.DisplayName)
                ? plugin.DisplayName.Trim()
                : (!string.IsNullOrWhiteSpace(plugin.Name)
                    ? plugin.Name.Trim()
                    : UiLocalization.Text("PluginManagementForm.Value.UnnamedPlugin", "(Unnamed Plugin)"));
        }

        private static string GetMarketItemDisplayName(PluginMarketListItem item)
        {
            if (item == null)
            {
                return UiLocalization.Text("PluginManagementForm.Value.UnknownPlugin", "(Unknown Plugin)");
            }

            if (!string.IsNullOrWhiteSpace(item.DisplayName))
            {
                return item.DisplayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(item.PluginId))
            {
                return item.PluginId.Trim();
            }

            return UiLocalization.Text("PluginManagementForm.Value.UnnamedPlugin", "(Unnamed Plugin)");
        }

        private static string BuildMarketItemStatusText(PluginMarketListItem item)
        {
            if (item == null)
            {
                return UiLocalization.Text("PluginManagementForm.Value.Unknown", "Unknown");
            }

            if (item.IsRemoteCatalog && PluginMarketAppServerSourcePolicy.IsAllowed(item.AppServerSource))
            {
                return UiLocalization.Text(
                    "PluginManagementForm.Value.MarketplaceRegistrationRequired",
                    "Register Marketplace First");
            }

            switch (item.AvailabilityStatus)
            {
                case PluginMarketAvailabilityStatus.UpdateAvailable:
                    return UiLocalization.Text("PluginManagementForm.Value.UpdateAvailable", "Update Available");
                case PluginMarketAvailabilityStatus.InstallAvailable:
                default:
                    return UiLocalization.Text("PluginManagementForm.Value.Available", "Available");
            }
        }

        private bool CanInstallMarketItem(PluginMarketListItem item, bool isInstalledTab)
        {
            return !_isBusy &&
                !isInstalledTab &&
                item != null &&
                MarketplaceMutationPolicy.EvaluatePluginMutation(_appServerClient.ProtocolCapabilities).IsAvailable &&
                CanInstallMarketItemSource(item) &&
                item.AvailabilityStatus != PluginMarketAvailabilityStatus.None;
        }

        private bool CanUninstallPlugin(AppServerPluginSummary plugin, bool isInstalledTab)
        {
            return !_isBusy &&
                isInstalledTab &&
                plugin != null &&
                plugin.Installed &&
                MarketplaceMutationPolicy.EvaluatePluginMutation(_appServerClient.ProtocolCapabilities).IsAvailable &&
                !string.IsNullOrWhiteSpace(PluginAppServerInstallSourceResolver.ResolveUninstallPluginId(plugin));
        }

        private bool EnsureMarketplaceMutationAvailable()
        {
            var availability = MarketplaceMutationPolicy.EvaluatePluginMutation(_appServerClient.ProtocolCapabilities);
            if (availability.IsAvailable)
            {
                return true;
            }

            var message = UiLocalization.Text(
                "PluginManagementForm.Message.MarketplaceMutationUnavailable",
                "Marketplace changes are unavailable for the selected App Server runtime. Target state was not changed.");
            SetStatus(message, true);
            MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private bool CanInstallMarketItemSource(PluginMarketListItem item)
        {
            if (item == null)
            {
                return false;
            }

            if (item.AppServerPlugin != null)
            {
                return PluginAppServerInstallSourceResolver.CanInstall(item.AppServerPlugin);
            }

            return false;
        }

        private async System.Threading.Tasks.Task<List<AppServerPluginSummary>> LoadInstalledPluginObservationAsync(
            IEnumerable<AppServerPluginSummary> catalogPlugins)
        {
            var snapshot = await AppServerInstalledPluginObservationService.LoadAsync(
                _appServerClient,
                _appServerClient.ProtocolCapabilities,
                catalogPlugins,
                _lifetimeCancellation.Token);
            return snapshot == null || snapshot.Plugins == null
                ? new List<AppServerPluginSummary>()
                : snapshot.Plugins.Where(plugin => plugin != null).ToList();
        }

        private bool CanInstallRemoteCatalogItem(PluginMarketListItem item)
        {
            return item != null &&
                item.IsRemoteCatalog &&
                PluginMarketAppServerSourcePolicy.IsAllowed(item.AppServerSource);
        }

        private static string GetInstallSourceUnavailableMessage(PluginMarketListItem item)
        {
            if (item != null && item.IsRemoteCatalog)
            {
                if (PluginMarketAppServerSourcePolicy.IsAllowed(item.AppServerSource))
                {
                    return UiLocalization.Text(
                        "PluginManagementForm.Message.MarketplaceRegistrationRequired",
                        "Register this marketplace with the target App Server before installing its plugins.");
                }

                return UiLocalization.Text(
                    "PluginManagementForm.Message.RemoteCatalogInstallUnsupported",
                    "This catalog does not publish a secure App Server marketplace source, so this plugin cannot be installed.");
            }

            return UiLocalization.Text(
                "PluginManagementForm.Message.PluginInstallSourceUnavailable",
                "This plugin market item does not include an installable marketplace source.");
        }


        private static string GetPluginSkillDisplayName(AppServerPluginSkillInfo skill)
        {
            if (skill == null)
            {
                return UiLocalization.Text("PluginManagementForm.Value.UnknownSkill", "(Unknown Skill)");
            }

            return !string.IsNullOrWhiteSpace(skill.DisplayName)
                ? skill.DisplayName.Trim()
                : (!string.IsNullOrWhiteSpace(skill.Name)
                    ? skill.Name.Trim()
                    : UiLocalization.Text("PluginManagementForm.Value.UnnamedSkill", "(Unnamed Skill)"));
        }

        private static string GetPluginAppDisplayName(AppServerPluginAppInfo app)
        {
            if (app == null)
            {
                return UiLocalization.Text("PluginManagementForm.Value.UnknownApp", "(Unknown App)");
            }

            return !string.IsNullOrWhiteSpace(app.Name)
                ? app.Name.Trim()
                : (!string.IsNullOrWhiteSpace(app.Id)
                    ? app.Id.Trim()
                    : UiLocalization.Text("PluginManagementForm.Value.UnnamedApp", "(Unnamed App)"));
        }

        private static string BuildPluginStatusText(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return UiLocalization.Text("PluginManagementForm.Value.Unknown", "Unknown");
            }

            if (plugin.Installed)
            {
                return plugin.Enabled
                    ? UiLocalization.Text("PluginManagementForm.Value.Enabled", "Enabled")
                    : UiLocalization.Text("PluginManagementForm.Value.Disabled", "Disabled");
            }

            return string.Equals(plugin.InstallPolicy, "AVAILABLE", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(plugin.InstallPolicy)
                ? UiLocalization.Text("PluginManagementForm.Value.Available", "Available")
                : plugin.InstallPolicy;
        }

        private static string BuildPluginSourceText(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return UiLocalization.Text("PluginManagementForm.Value.Unknown", "Unknown");
            }

            var sourceType = string.IsNullOrWhiteSpace(plugin.SourceType)
                ? UiLocalization.Text("PluginManagementForm.Value.UnknownSource", "Unknown Source")
                : plugin.SourceType.Trim();
            if (string.IsNullOrWhiteSpace(plugin.SourcePath))
            {
                return sourceType;
            }

            return string.Format(
                UiLocalization.Text("PluginManagementForm.Summary.Source.Template", "{0}: {1}"),
                sourceType,
                plugin.SourcePath.Trim());
        }

        private static string GetPluginListSummary(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return string.Empty;
            }

            var summary = !string.IsNullOrWhiteSpace(plugin.ShortDescription)
                ? plugin.ShortDescription
                : plugin.LongDescription;
            var prefixParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(plugin.MarketplaceName))
            {
                prefixParts.Add(plugin.MarketplaceName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(plugin.Category))
            {
                prefixParts.Add(plugin.Category.Trim());
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                return prefixParts.Count == 0 ? string.Empty : string.Join(" / ", prefixParts);
            }

            summary = summary.Replace("\r", " ").Replace("\n", " ").Trim();
            summary = summary.Length <= 88 ? summary : summary.Substring(0, 88) + "...";
            if (prefixParts.Count == 0)
            {
                return summary;
            }

            return string.Join(" / ", prefixParts) + UiLocalization.Text("PluginManagementForm.Summary.ListSummarySeparator", "; ") + summary;
        }

        private static string GetMarketItemSummary(PluginMarketListItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            var summary = string.IsNullOrWhiteSpace(item.Summary) ? string.Empty : item.Summary.Trim();
            if (summary.Length > 88)
            {
                summary = summary.Substring(0, 88) + "...";
            }

            if (item.IsRemoteCatalog && item.RemoteEntry != null && !string.IsNullOrWhiteSpace(item.RemoteEntry.Category))
            {
                if (string.IsNullOrWhiteSpace(summary))
                {
                    return item.RemoteEntry.Category.Trim();
                }

                return item.RemoteEntry.Category.Trim() + UiLocalization.Text("PluginManagementForm.Summary.ListSummarySeparator", "; ") + summary;
            }

            return summary;
        }

        private ListView CreatePluginListView(string accessibleName, string middleColumnText, string secondaryColumnText)
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                View = View.Details,
                UseCompatibleStateImageBehavior = false,
                LabelWrap = false,
                AccessibleName = accessibleName,
                TabIndex = 1
            };
            UiTheme.ApplyListView(listView);
            listView.Columns.Add(UiLocalization.Text("PluginManagementForm.Column.Plugin", "Plugin"), 240, HorizontalAlignment.Left);
            listView.Columns.Add(middleColumnText, 140, HorizontalAlignment.Left);
            if (!string.IsNullOrWhiteSpace(secondaryColumnText))
            {
                listView.Columns.Add(secondaryColumnText, 150, HorizontalAlignment.Left);
                listView.Columns.Add(UiLocalization.Text("PluginManagementForm.Column.Summary", "Summary"), 470, HorizontalAlignment.Left);
            }
            else
            {
                listView.Columns.Add(UiLocalization.Text("PluginManagementForm.Column.Summary", "Summary"), 620, HorizontalAlignment.Left);
            }
            return listView;
        }

        private static ListViewItem CreateMarketListItem(PluginMarketListItem item)
        {
            var listItem = new ListViewItem(GetMarketItemDisplayName(item));
            listItem.SubItems.Add(BuildMarketItemStatusText(item));
            listItem.SubItems.Add(string.IsNullOrWhiteSpace(item == null ? null : item.MarketName)
                ? UiLocalization.Text("PluginManagementForm.Value.UnknownSource", "Unknown Source")
                : item.MarketName);
            listItem.SubItems.Add(GetMarketItemSummary(item));
            listItem.Tag = item;
            return listItem;
        }

        private static ListViewItem CreateMarketNavigationListItem(PluginMarketNavigationItem navigationItem)
        {
            var listItem = new ListViewItem(navigationItem == null ? string.Empty : navigationItem.Label);
            listItem.SubItems.Add(string.Empty);
            listItem.SubItems.Add(string.Empty);
            listItem.SubItems.Add(string.Empty);
            listItem.Tag = navigationItem;
            return listItem;
        }

        private static TextBox CreateMarketSearchTextBox()
        {
            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("PluginManagementForm.Accessible.MarketSearch", "Market Search"),
                TabIndex = 0
            };
            UiTheme.ApplyTextBox(textBox, false, false);
            return textBox;
        }

        private static TreeView CreateMarketSourceTreeView()
        {
            var treeView = new TreeView
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("PluginManagementForm.Accessible.MarketSources", "Market Sources"),
                HideSelection = false,
                ShowLines = true,
                ShowPlusMinus = true,
                ShowRootLines = true,
                TabIndex = 1
            };
            return treeView;
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(108, 0),
                Margin = new Padding(0, 0, 8, 0),
                AccessibleName = accessibleName,
                TabIndex = tabIndex
            };
        }

        private static Control BuildMarketSectionPanel(string title, Control content)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.Controls.Add(CreateSectionLabel(title), 0, 0);
            panel.Controls.Add(content, 0, 1);
            return panel;
        }

        private void BuildLayout()
        {
            var installedPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.InnerPadding),
                TabStop = false
            };
            installedPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            installedPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            installedPanel.Controls.Add(CreateSectionLabel(
                UiLocalization.Text("PluginManagementForm.Label.InstalledPlugins", "Installed Plugins")), 0, 0);
            installedPanel.Controls.Add(_installedListView, 0, 1);

            var marketSearchPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            marketSearchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            marketSearchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            marketSearchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            marketSearchPanel.Controls.Add(CreateSectionLabel(
                UiLocalization.Text("PluginManagementForm.Label.MarketSearch", "Search")), 0, 0);
            marketSearchPanel.Controls.Add(_marketSearchTextBox, 1, 0);
            marketSearchPanel.Controls.Add(_marketSearchButton, 2, 0);

            _marketSourceTreeView.TabIndex = 2;
            _marketListView.TabIndex = 3;
            var marketSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterWidth = 8,
                TabStop = false,
                BackColor = UiTheme.WindowBackColor
            };
            marketSplit.Panel1.Controls.Add(BuildMarketSectionPanel(
                UiLocalization.Text("PluginManagementForm.Label.MarketSources", "Sources"),
                _marketSourceTreeView));
            marketSplit.Panel2.Controls.Add(BuildMarketSectionPanel(
                UiLocalization.Text("PluginManagementForm.Label.MarketPlugins", "Available Plugins"),
                _marketListView));

            var marketPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.InnerPadding),
                TabStop = false
            };
            marketPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            marketPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            marketPanel.Controls.Add(marketSearchPanel, 0, 0);
            marketPanel.Controls.Add(marketSplit, 0, 1);

            _installedTabPage.Controls.Add(installedPanel);
            _marketTabPage.Controls.Add(marketPanel);

            var actionLabel = CreateSectionLabel(UiLocalization.Text("PluginManagementForm.Label.Actions", "Actions"));
            var actionPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0),
                Padding = new Padding(0),
                AutoSize = true,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 2
            };
            actionPanel.Controls.Add(_refreshButton);
            actionPanel.Controls.Add(_refreshMarketplaceButton);
            actionPanel.Controls.Add(_registerMarketplaceButton);
            actionPanel.Controls.Add(_installButton);
            actionPanel.Controls.Add(_uninstallButton);
            actionPanel.Controls.Add(_openAuthButton);
            actionPanel.Controls.Add(_openFolderButton);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false,
                TabIndex = 0
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_tabControl, 0, 0);
            root.Controls.Add(actionLabel, 0, 1);
            root.Controls.Add(actionPanel, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);

            Controls.Add(root);
        }

        private static Label CreateSectionLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Height = 24,
                Margin = new Padding(0, 8, 8, 8),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);
            return label;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _lifetimeCancellation.Cancel();
            _lifetimeCancellation.Dispose();
            base.OnFormClosed(e);
        }

        private CancellationTokenSource CreateOperationCancellationTokenSource()
        {
            var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(60));
            return cancellationTokenSource;
        }

        private void OpenExternalUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            ExternalUrlLauncher.Open(url);
        }

        private static string GetMarketDisplayName(PluginMarketSnapshot market)
        {
            if (market == null)
            {
                return UiLocalization.Text("PluginManagementForm.Value.UnknownSource", "Unknown Source");
            }

            if (!string.IsNullOrWhiteSpace(market.DisplayName))
            {
                return market.DisplayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(market.MarketId))
            {
                return market.MarketId.Trim();
            }

            return UiLocalization.Text("PluginManagementForm.Value.UnknownSource", "Unknown Source");
        }

        private static PluginMarketEntrySnapshot CloneRemoteEntry(PluginMarketEntrySnapshot entry, PluginMarketSnapshot market)
        {
            return new PluginMarketEntrySnapshot
            {
                MarketId = string.IsNullOrWhiteSpace(entry == null ? null : entry.MarketId)
                    ? (market == null ? null : market.MarketId)
                    : entry.MarketId,
                PluginId = entry == null ? null : entry.PluginId,
                DisplayName = entry == null ? null : entry.DisplayName,
                Version = entry == null ? null : entry.Version,
                Category = entry == null ? null : entry.Category,
                Summary = entry == null ? null : entry.Summary,
                PackageUrl = entry == null ? null : entry.PackageUrl,
                ManifestUrl = entry == null ? null : entry.ManifestUrl,
                DetailsUrl = entry == null ? null : entry.DetailsUrl,
                AuthPolicy = entry == null ? null : entry.AuthPolicy,
                Sha256 = entry == null ? null : entry.Sha256,
                SizeBytes = entry == null ? 0L : entry.SizeBytes
            };
        }

        private static PluginMarketListItem CloneMarketListItem(PluginMarketListItem item)
        {
            return new PluginMarketListItem
            {
                MarketName = item == null ? null : item.MarketName,
                PluginId = item == null ? null : item.PluginId,
                DisplayName = item == null ? null : item.DisplayName,
                Summary = item == null ? null : item.Summary,
                AppServerSource = item == null ? null : item.AppServerSource,
                IsRemoteCatalog = item != null && item.IsRemoteCatalog,
                AvailabilityStatus = item == null ? PluginMarketAvailabilityStatus.None : item.AvailabilityStatus,
                InstalledVersion = item == null ? null : item.InstalledVersion,
                AppServerPlugin = item == null ? null : item.AppServerPlugin,
                RemoteEntry = item == null ? null : CloneRemoteEntry(item.RemoteEntry, null)
            };
        }

        private sealed class PluginMarketBrowseScope
        {
            private PluginMarketBrowseScope(bool allSources, string marketName, string category)
            {
                AllSources = allSources;
                MarketName = marketName;
                Category = category;
            }

            public bool AllSources { get; private set; }

            public string MarketName { get; private set; }

            public string Category { get; private set; }

            public string Key
            {
                get
                {
                    if (AllSources)
                    {
                        return "all";
                    }

                    return "market:" + (MarketName ?? string.Empty) + "|category:" + (Category ?? string.Empty);
                }
            }

            public static PluginMarketBrowseScope All()
            {
                return new PluginMarketBrowseScope(true, null, null);
            }

            public static PluginMarketBrowseScope ForMarket(string marketName)
            {
                return new PluginMarketBrowseScope(false, marketName, null);
            }

            public static PluginMarketBrowseScope ForCategory(string marketName, string category)
            {
                return new PluginMarketBrowseScope(false, marketName, category);
            }
        }
    }
}
