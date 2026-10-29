using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
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
    public sealed partial class McpManagementForm : Form
    {
        private readonly IAppServerConnectionClient _connectionClient;
        private readonly IAppServerConfigClient _configClient;
        private readonly IAppServerMcpClient _mcpClient;
        private readonly IScreenReaderService _screenReaderService;
        private readonly McpReauthenticationState _mcpReauthenticationState;
        private readonly bool _ownsMcpReauthenticationState;
        private readonly ListView _serverListView;
        private readonly ComboBox _filterComboBox;
        private readonly Label _statusLabel;
        private readonly Button _addButton;
        private readonly Button _deleteButton;
        private readonly Button _enableButton;
        private readonly Button _disableButton;
        private readonly Button _settingsButton;
        private readonly Button _advancedDiagnosticsButton;
        private readonly Button _refreshButton;
        private readonly Button _loginButton;
        private readonly Button _closeButton;
        private readonly ContextMenu _serverContextMenu;
        private readonly MenuItem _addMenuItem;
        private readonly MenuItem _deleteMenuItem;
        private readonly MenuItem _enableMenuItem;
        private readonly MenuItem _disableMenuItem;
        private readonly MenuItem _settingsMenuItem;
        private readonly MenuItem _advancedDiagnosticsMenuItem;
        private readonly MenuItem _loginMenuItem;

        private readonly CancellationTokenSource _lifetimeCancellation = new CancellationTokenSource();
        private bool _isBusy;
        private string _pendingLoginServerName;
        private string _preferredServerName;
        private McpReauthenticationNotification _pendingReauthenticationNotification;
        private List<AppServerMcpServerStatus> _servers;

        public McpManagementForm(IAppServerClient appServerClient)
            : this(appServerClient, null)
        {
        }

        public McpManagementForm(IAppServerClient appServerClient, IScreenReaderService screenReaderService)
            : this(appServerClient, screenReaderService, null)
        {
        }

        internal McpManagementForm(
            IAppServerClient appServerClient,
            IScreenReaderService screenReaderService,
            McpReauthenticationState mcpReauthenticationState,
            AuthenticationMcpCoordinator nativeMcp = null)
            : this(
                appServerClient,
                appServerClient,
                appServerClient,
                screenReaderService,
                mcpReauthenticationState,
                nativeMcp)
        {
        }

        internal McpManagementForm(
            IAppServerConnectionClient connectionClient,
            IAppServerConfigClient configClient,
            IAppServerMcpClient mcpClient,
            IScreenReaderService screenReaderService,
            McpReauthenticationState mcpReauthenticationState,
            AuthenticationMcpCoordinator nativeMcp = null)
        {
            _connectionClient = connectionClient ?? throw new ArgumentNullException(nameof(connectionClient));
            _configClient = configClient ?? throw new ArgumentNullException(nameof(configClient));
            _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));
            _nativeMcp = nativeMcp;
            _screenReaderService = screenReaderService;
            _ownsMcpReauthenticationState = mcpReauthenticationState == null;
            _mcpReauthenticationState = mcpReauthenticationState ?? new McpReauthenticationState();

            Text = UiLocalization.Text("McpManagementForm.Title", "MCP Management");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(980, 640);
            Size = new Size(1120, 760);
            ShowInTaskbar = false;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _serverListView = CreateServerListView();
            _filterComboBox = CreateFilterComboBox();
            _filterComboBox.Items.Add(UiLocalization.Text("McpManagementForm.Filter.All", "All"));
            _filterComboBox.Items.Add(UiLocalization.Text("McpManagementForm.Filter.Enabled", "Enabled"));
            _filterComboBox.Items.Add(UiLocalization.Text("McpManagementForm.Filter.Disabled", "Disabled"));
            _filterComboBox.SelectedIndex = 0;
            _servers = new List<AppServerMcpServerStatus>();
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
                UiLocalization.Text("McpManagementForm.Button.Refresh", "Refresh Status(&R)"),
                UiLocalization.Text("McpManagementForm.Accessible.Refresh", "Refresh Status"),
                0);
            _addButton = CreateButton(
                UiLocalization.Text("McpManagementForm.Button.Add", "Add(&A)..."),
                UiLocalization.Text("McpManagementForm.Accessible.Add", "Add"),
                1);
            _settingsButton = CreateButton(
                UiLocalization.Text("McpManagementForm.Button.Settings", "Settings(&S)..."),
                UiLocalization.Text("McpManagementForm.Accessible.Settings", "Settings"),
                2);
            _advancedDiagnosticsButton = CreateButton(
                UiLocalization.Text("McpManagementForm.Button.AdvancedDiagnostics", "Advanced Diagnostics(&V)..."),
                UiLocalization.Text("McpManagementForm.Accessible.AdvancedDiagnostics", "Advanced Diagnostics"),
                3);
            _loginButton = CreateButton(
                UiLocalization.Text("McpManagementForm.Button.Login", "Login(&G)"),
                UiLocalization.Text("McpManagementForm.Accessible.Login", "Login"),
                4);
            _enableButton = CreateButton(
                UiLocalization.Text("McpManagementForm.Button.Enable", "Enable(&E)"),
                UiLocalization.Text("McpManagementForm.Accessible.Enable", "Enable"),
                5);
            _disableButton = CreateButton(
                UiLocalization.Text("McpManagementForm.Button.Disable", "Disable(&D)"),
                UiLocalization.Text("McpManagementForm.Accessible.Disable", "Disable"),
                6);
            _deleteButton = CreateButton(
                UiLocalization.Text("McpManagementForm.Button.Delete", "Delete(&X)"),
                UiLocalization.Text("McpManagementForm.Accessible.Delete", "Delete"),
                7);
            _closeButton = CreateButton(
                UiLocalization.Text("McpManagementForm.Button.Close", "Close(&C)"),
                UiLocalization.Text("McpManagementForm.Accessible.Close", "Close"),
                8);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_addButton, UiGlyph.NewSession);
            UiTheme.ApplyButton(_deleteButton, UiGlyph.Cancel);
            UiTheme.ApplyButton(_enableButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_disableButton, UiGlyph.Cancel);
            UiTheme.ApplyButton(_settingsButton, UiGlyph.Model);
            UiTheme.ApplyButton(_advancedDiagnosticsButton, UiGlyph.Info);
            UiTheme.ApplyButton(_loginButton, UiGlyph.Account);
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);

            _serverContextMenu = new ContextMenu();
            _addMenuItem = new MenuItem(UiLocalization.Text("McpManagementForm.Menu.Add", "Add(&A)..."));
            _deleteMenuItem = new MenuItem(UiLocalization.Text("McpManagementForm.Menu.Delete", "Delete(&X)"));
            _enableMenuItem = new MenuItem(UiLocalization.Text("McpManagementForm.Menu.Enable", "Enable(&E)"));
            _disableMenuItem = new MenuItem(UiLocalization.Text("McpManagementForm.Menu.Disable", "Disable(&D)"));
            _settingsMenuItem = new MenuItem(UiLocalization.Text("McpManagementForm.Menu.Settings", "Settings(&S)..."));
            _advancedDiagnosticsMenuItem = new MenuItem(UiLocalization.Text("McpManagementForm.Menu.AdvancedDiagnostics", "Advanced Diagnostics(&V)..."));
            _loginMenuItem = new MenuItem(UiLocalization.Text("McpManagementForm.Menu.Login", "Login(&G)"));
            _serverContextMenu.MenuItems.Add(_addMenuItem);
            _serverContextMenu.MenuItems.Add(_settingsMenuItem);
            _serverContextMenu.MenuItems.Add(_advancedDiagnosticsMenuItem);
            _serverContextMenu.MenuItems.Add(_loginMenuItem);
            _serverContextMenu.MenuItems.Add(_enableMenuItem);
            _serverContextMenu.MenuItems.Add(_disableMenuItem);
            _serverContextMenu.MenuItems.Add("-");
            _serverContextMenu.MenuItems.Add(_deleteMenuItem);

            _serverListView.ContextMenu = _serverContextMenu;
            _filterComboBox.SelectedIndexChanged += FilterComboBox_SelectedIndexChanged;
            _serverListView.SelectedIndexChanged += ServerListView_SelectedIndexChanged;
            _serverListView.KeyDown += ServerListView_KeyDown;
            _serverListView.MouseDown += ServerListView_MouseDown;
            _serverContextMenu.Popup += ServerContextMenu_Popup;
            _addButton.Click += AddButton_Click;
            _deleteButton.Click += DeleteButton_Click;
            _enableButton.Click += EnableButton_Click;
            _disableButton.Click += DisableButton_Click;
            _settingsButton.Click += SettingsButton_Click;
            _advancedDiagnosticsButton.Click += AdvancedDiagnosticsButton_Click;
            _refreshButton.Click += RefreshButton_Click;
            _loginButton.Click += LoginButton_Click;
            _closeButton.Click += CloseButton_Click;
            _addMenuItem.Click += AddButton_Click;
            _deleteMenuItem.Click += DeleteButton_Click;
            _enableMenuItem.Click += EnableButton_Click;
            _disableMenuItem.Click += DisableButton_Click;
            _settingsMenuItem.Click += SettingsButton_Click;
            _advancedDiagnosticsMenuItem.Click += AdvancedDiagnosticsButton_Click;
            _loginMenuItem.Click += LoginButton_Click;
            Load += OnLoad;
            Shown += OnShown;
            FormClosed += OnFormClosed;
            KeyDown += McpManagementForm_KeyDown;
            _connectionClient.EventReceived += AppServerClient_EventReceived;

            InitializeNativeMcpActions();
            BuildLayout();
            CancelButton = _closeButton;
            SetStatus(UiLocalization.Text("McpManagementForm.Status.Preparing", "Preparing to read MCP server status."));
            RefreshActionButtons();
            PrepareReauthenticationNotification(_mcpReauthenticationState.ConsumePending());
        }

        private void OnLoad(object sender, EventArgs e)
        {
            ActiveControl = _serverListView;
            _serverListView.Select();
        }

        private async void OnShown(object sender, EventArgs e)
        {
            await LoadServerStatusesAsync(true);
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            _connectionClient.EventReceived -= AppServerClient_EventReceived;
            _lifetimeCancellation.Cancel();
        }

        private async void McpManagementForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (_isBusy)
            {
                return;
            }

            if (e.KeyCode == Keys.F5)
            {
                e.SuppressKeyPress = true;
                await LoadServerStatusesAsync(true);
            }
        }

        private async void RefreshButton_Click(object sender, EventArgs e)
        {
            await LoadServerStatusesAsync(true);
        }

        private async void AddButton_Click(object sender, EventArgs e)
        {
            await AddServerAsync();
        }

        private async void DeleteButton_Click(object sender, EventArgs e)
        {
            await DeleteSelectedServerAsync();
        }

        private async void EnableButton_Click(object sender, EventArgs e)
        {
            await SetSelectedServerEnabledAsync(true);
        }

        private async void DisableButton_Click(object sender, EventArgs e)
        {
            await SetSelectedServerEnabledAsync(false);
        }

        private async void SettingsButton_Click(object sender, EventArgs e)
        {
            await OpenSelectedServerSettingsAsync();
        }

        private void AdvancedDiagnosticsButton_Click(object sender, EventArgs e)
        {
            OpenSelectedServerDiagnostics();
        }

        private async void LoginButton_Click(object sender, EventArgs e)
        {
            var server = GetSelectedServer();
            if (server == null || !server.SupportsOAuth || server.Enabled == false || _isBusy)
            {
                return;
            }

            SetBusy(true);
            if (!await EnsureAppServerAvailableForActionAsync(
                    UiLocalization.Text("McpManagementForm.Action.StartLogin", "Start MCP login"),
                    AppServerProtocolFeature.McpOAuth))
            {
                if (!IsDisposed && !Disposing)
                {
                    SetBusy(false);
                }
                return;
            }

            try
            {
                SetStatus(string.Format(
                    UiLocalization.Text("McpManagementForm.Status.OpeningLoginPage", "Opening login page for {0}..."),
                    server.ServerName));

                var result = await _mcpClient.StartMcpServerOauthLoginAsync(server.ServerName);
                if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
                {
                    return;
                }
                _pendingLoginServerName = server.ServerName;

                if (!string.IsNullOrWhiteSpace(result != null ? result.AuthUrl : null))
                {
                    OpenExternalUrl(result.AuthUrl);
                    SetStatus(string.Format(
                        UiLocalization.Text("McpManagementForm.Status.LoginPageOpened", "{0} login page has opened. Refresh to view latest status after completion."),
                        server.ServerName));
                }
                else
                {
                    SetStatus(string.Format(
                        UiLocalization.Text("McpManagementForm.Status.LoginStarted", "{0} login flow has started. Refresh to view latest status after completion."),
                        server.ServerName));
                }
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
                {
                    return;
                }
                SetStatus(UiLocalization.Text("McpManagementForm.Status.StartLoginFailed", "Failed to start MCP login."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    SetBusy(false);
                    if (_serverListView.Items.Count > 0)
                    {
                        this.BeginInvokeSafe(() => _serverListView.Focus());
                    }
                }
            }
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void FilterComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            ApplyServerFilter();
            UpdateStatusForCurrentFilter(true);
        }

        private void ServerListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            RefreshActionButtons();
        }

        private void ServerListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter || _isBusy)
            {
                return;
            }

            e.SuppressKeyPress = true;
            ShowSelectedServerDetails();
        }

        private void ServerListView_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _serverListView.HitTest(e.Location);
            if (hit == null || hit.Item == null)
            {
                return;
            }

            hit.Item.Selected = true;
            hit.Item.Focused = true;
        }

        private void ServerContextMenu_Popup(object sender, EventArgs e)
        {
            RefreshContextMenuItems();
        }

        private void AppServerClient_EventReceived(object sender, AppServerEventArgs e)
        {
            if (e == null || IsDisposed)
            {
                return;
            }

            if (string.Equals(e.EventType, "mcp_startup_status_updated", StringComparison.OrdinalIgnoreCase))
            {
                if (InvokeRequired || !IsHandleCreated)
                {
                    this.BeginInvokeSafe(() => AppServerClient_EventReceived(sender, e));
                    return;
                }

                if (string.Equals(e.Reason, "reauthenticationRequired", StringComparison.Ordinal))
                {
                    if (!_ownsMcpReauthenticationState)
                    {
                        this.BeginInvokeSafe(ConsumeSharedReauthenticationNotification);
                        return;
                    }

                    var notification = _mcpReauthenticationState.Record(e);
                    _mcpReauthenticationState.ConsumePending();
                    PrepareReauthenticationNotification(notification);
                    ApplyReauthenticationNotification(notification, true);
                    return;
                }

                if (_ownsMcpReauthenticationState)
                {
                    _mcpReauthenticationState.Record(e);
                }
                ClearPreparedReauthenticationNotification(e.McpServerName);
                SetStatus(e.Content + UiLocalization.Text("McpManagementForm.Status.RefreshHintSuffix", " Please refresh to view the latest status."));
                return;
            }

            if (string.IsNullOrWhiteSpace(_pendingLoginServerName))
            {
                return;
            }

            if (!string.Equals(e.RequestId, _pendingLoginServerName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!string.Equals(e.EventType, "mcp_oauth_login_completed", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(e.EventType, "error", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (InvokeRequired || !IsHandleCreated)
            {
                this.BeginInvokeSafe(() => AppServerClient_EventReceived(sender, e));
                return;
            }

            _pendingLoginServerName = null;
            if (string.Equals(e.EventType, "mcp_oauth_login_completed", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(e.Content + UiLocalization.Text("McpManagementForm.Status.RefreshHintSuffix", " Please refresh to view the latest status."));
            }
            else
            {
                SetStatus(e.Content);
            }

            RefreshActionButtons();
        }

        private void ConsumeSharedReauthenticationNotification()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            var notification = _mcpReauthenticationState.ConsumePending();
            PrepareReauthenticationNotification(notification);
            ApplyReauthenticationNotification(notification, false);
        }

        private void PrepareReauthenticationNotification(McpReauthenticationNotification notification)
        {
            if (notification == null)
            {
                return;
            }

            _pendingReauthenticationNotification = notification;
            _preferredServerName = notification.ServerName;
        }

        private void ApplyReauthenticationNotification(
            McpReauthenticationNotification notification,
            bool announce)
        {
            if (notification == null)
            {
                return;
            }

            var instruction = McpReauthenticationGuidance.BuildInstruction(notification.ServerName);
            SetStatus(instruction);
            if (announce && notification.ShouldSpeak)
            {
                _screenReaderService?.Speak(instruction, true, false, true);
            }
        }

        private void ClearPreparedReauthenticationNotification(string serverName)
        {
            if (_pendingReauthenticationNotification == null ||
                !AreSameServer(_pendingReauthenticationNotification.ServerName, serverName))
            {
                return;
            }

            _pendingReauthenticationNotification = null;
            _preferredServerName = null;
        }

        private static bool AreSameServer(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(second);
            }

            return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private async System.Threading.Tasks.Task LoadServerStatusesAsync(bool updateStatusWhenEmpty)
        {
            if (_isBusy)
            {
                return;
            }

            SetBusy(true);
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("McpManagementForm.Action.LoadStatus", "Load MCP server status")))
            {
                if (!IsDisposed && !Disposing)
                {
                    SetBusy(false);
                }
                return;
            }

            try
            {
                SetStatus(UiLocalization.Text("McpManagementForm.Status.Loading", "Loading MCP server status..."));

                var servers = _nativeMcp == null
                    ? await _mcpClient.ListMcpServerStatusesAsync()
                    : await _nativeMcp.ReadAsync(_lifetimeCancellation.Token);
                if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
                {
                    return;
                }
                BindServers(servers);
                UpdateStatusForCurrentFilter(updateStatusWhenEmpty);
                if (_nativeMcp != null) SetStatus(NativeMcpHint());
                var pendingNotification = _pendingReauthenticationNotification;
                _pendingReauthenticationNotification = null;
                ApplyReauthenticationNotification(pendingNotification, false);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                SetStatus(UiLocalization.Text("McpManagementForm.Status.LoadFailed", "Failed to load MCP server status."));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    SetBusy(false);
                }
            }
        }

        private async System.Threading.Tasks.Task SetSelectedServerEnabledAsync(bool enabled)
        {
            var server = GetSelectedServer();
            if (server == null || _isBusy)
            {
                return;
            }

            if (server.ConfigValues == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("McpManagementForm.Message.NoWritableConfigForToggle", "Current server has no writable config source and cannot switch enabled state directly."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (server.Enabled.HasValue && server.Enabled.Value == enabled)
            {
                return;
            }

            await WriteServerConfigEditsAsync(
                server.ServerName,
                new[]
                {
                    AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(server.ServerName, "enabled"), enabled)
                },
                string.Format(
                    UiLocalization.Text("McpManagementForm.Status.SavingServerSettings", "Saving settings for {0}..."),
                    server.ServerName),
                string.Format(
                    UiLocalization.Text("McpManagementForm.Status.SaveServerSettingsFailed", "Failed to save settings for {0}."),
                    server.ServerName),
                string.Format(
                    UiLocalization.Text(
                        enabled ? "McpManagementForm.Status.ServerEnabledTemplate" : "McpManagementForm.Status.ServerDisabledTemplate",
                        enabled ? "{0} has been enabled." : "{0} has been disabled."),
                    server.ServerName),
                server.ServerName);
        }

        private async System.Threading.Tasks.Task AddServerAsync()
        {
            if (_isBusy)
            {
                return;
            }

            using (var form = new McpServerSettingsForm(null, GetServerNames()))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.EditedConfig == null)
                {
                    return;
                }

                await WriteServerConfigEditsAsync(
                    form.EditedServerName,
                    BuildServerSettingEdits(form.EditedServerName, form.EditedConfig),
                    string.Format(
                        UiLocalization.Text("McpManagementForm.Status.AddingServer", "Adding {0}..."),
                        form.EditedServerName),
                    string.Format(
                        UiLocalization.Text("McpManagementForm.Status.AddServerFailed", "Failed to add {0}."),
                        form.EditedServerName),
                    string.Format(
                        UiLocalization.Text("McpManagementForm.Status.ServerAdded", "{0} has been added."),
                        form.EditedServerName),
                    form.EditedServerName);
            }
        }

        private async System.Threading.Tasks.Task DeleteSelectedServerAsync()
        {
            var server = GetSelectedServer();
            if (server == null || _isBusy)
            {
                return;
            }

            if (server.IsRequired == true)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("McpManagementForm.Message.RequiredServerCannotDelete", "Current server is marked as required and cannot be deleted."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (server.ConfigValues == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("McpManagementForm.Message.NoWritableConfigForDelete", "Current server has no writable config source and cannot be deleted directly."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text("McpManagementForm.Message.ConfirmDeleteServer", "Are you sure to delete {0}? Its config will be removed from MCP server list."),
                    server.ServerName),
                AppConstants.AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                return;
            }

            await WriteServerConfigEditsAsync(
                server.ServerName,
                new[]
                {
                    AppServerConfigWriteEdit.CreateReplace(BuildServerConfigKeyPath(server.ServerName), null)
                },
                string.Format(
                    UiLocalization.Text("McpManagementForm.Status.DeletingServer", "Deleting {0}..."),
                    server.ServerName),
                string.Format(
                    UiLocalization.Text("McpManagementForm.Status.DeleteServerFailed", "Failed to delete {0}."),
                    server.ServerName),
                string.Format(
                    UiLocalization.Text("McpManagementForm.Status.ServerDeleted", "{0} has been deleted."),
                    server.ServerName),
                null);
        }

        private async System.Threading.Tasks.Task OpenSelectedServerSettingsAsync()
        {
            var server = GetSelectedServer();
            if (server == null || _isBusy || (_nativeMcp != null && (!_nativeMcp.CanWrite || !_nativeMcp.IsManaged(server.ServerName))))
            {
                return;
            }

            using (var form = new McpServerSettingsForm(server, GetServerNames()))
            {
                if (form.ShowDialog(this) != DialogResult.OK || form.EditedConfig == null)
                {
                    return;
                }

                await WriteServerConfigEditsAsync(
                    form.EditedServerName,
                    BuildServerSettingEdits(form.EditedServerName, form.EditedConfig),
                    string.Format(
                        UiLocalization.Text("McpManagementForm.Status.SavingServerSettings", "Saving settings for {0}..."),
                        form.EditedServerName),
                    string.Format(
                        UiLocalization.Text("McpManagementForm.Status.SaveServerSettingsFailed", "Failed to save settings for {0}."),
                        form.EditedServerName),
                    string.Format(
                        UiLocalization.Text("McpManagementForm.Status.ServerSettingsSaved", "Settings for {0} have been saved."),
                        form.EditedServerName),
                    form.EditedServerName);
            }
        }

        private void OpenSelectedServerDiagnostics()
        {
            var server = GetSelectedServer();
            if (server == null || _isBusy)
            {
                return;
            }

            using (var form = new McpDiagnosticsForm(_mcpClient, server))
            {
                form.ShowDialog(this);
            }
        }

        internal static string BuildResourceReadFailureMessageForTests(string serverName, string uri, Exception exception)
        {
            return BuildResourceReadFailureMessage(serverName, uri, exception);
        }

        internal static string BuildResourceReadFailureMessage(string serverName, string uri, Exception exception)
        {
            var detail = exception != null ? exception.Message : string.Empty;
            if (IsUnexpectedMcpResourceResponse(detail))
            {
                return string.Format(
                    UiLocalization.Text(
                        "McpManagementForm.Message.ResourceReadUnexpectedResponse.Template",
                        "This MCP resource cannot be displayed because the server returned an unsupported resource response format.\r\n\r\nServer: {0}\r\nURI: {1}\r\n\r\nDetails: {2}"),
                    serverName ?? string.Empty,
                    uri ?? string.Empty,
                    detail);
            }

            return string.IsNullOrWhiteSpace(detail)
                ? UiLocalization.Text("McpManagementForm.Message.ResourceReadFailed", "Failed to read MCP resource.")
                : detail;
        }

        private static bool IsUnexpectedMcpResourceResponse(string message)
        {
            return !string.IsNullOrWhiteSpace(message) &&
                   message.IndexOf("resources/read failed", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   message.IndexOf("Unexpected response type", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async System.Threading.Tasks.Task WriteServerConfigEditsAsync(
            string serverName,
            IReadOnlyCollection<AppServerConfigWriteEdit> edits,
            string savingStatus,
            string failureStatus,
            string successStatus,
            string selectedServerNameAfterReload)
        {
            if (string.IsNullOrWhiteSpace(serverName) || edits == null || edits.Count == 0)
            {
                return;
            }

            if (_isBusy)
            {
                return;
            }

            if (_nativeMcp != null)
            {
                await RunNativeMcpMutationAsync(
                    () => _nativeMcp.WriteAsync(serverName, edits, _lifetimeCancellation.Token),
                    successStatus, selectedServerNameAfterReload);
                return;
            }

            SetBusy(true);
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("McpManagementForm.Action.WriteConfig", "Write MCP config")))
            {
                if (!IsDisposed && !Disposing)
                {
                    SetBusy(false);
                }
                return;
            }

            try
            {
                SetStatus(savingStatus);
                var snapshot = await _configClient.ReadConfigAsync(null, true);
                var guard = AppServerConfigWriteGuard.Evaluate(snapshot, edits);
                if (!guard.CanWrite)
                {
                    SetStatus(UiLocalization.Text(
                        "McpManagementForm.Status.ConfigWriteRequiresReload",
                        "MCP configuration changed or could not be verified. Use Target Reload, review it, and try again."));
                    return;
                }

                await _configClient.WriteConfigBatchAsync(
                    edits,
                    expectedVersion: guard.ExpectedVersion,
                    reloadUserConfig: true);
                await _mcpClient.ReloadMcpServerConfigAsync();
                if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
                {
                    return;
                }
                var message = UiLocalization.Text(
                    "McpManagementForm.Message.ConfigWriteConflict",
                    "MCP configuration was not changed. Another client may have changed it. Use Target Reload, review the effective configuration, and try again.");
                SetStatus(message);
                MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            finally
            {
                if (!IsDisposed && !Disposing)
                {
                    SetBusy(false);
                }
            }

            _preferredServerName = selectedServerNameAfterReload;
            await LoadServerStatusesAsync(true);
            SetStatus(successStatus);
        }

        private void BindServers(IEnumerable<AppServerMcpServerStatus> servers)
        {
            _servers = (servers ?? Enumerable.Empty<AppServerMcpServerStatus>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.ServerName))
                .ToList();

            ApplyServerFilter();
        }

        private void ApplyServerFilter()
        {
            var selectedServerName = !string.IsNullOrWhiteSpace(_preferredServerName)
                ? _preferredServerName
                : GetSelectedServerName();
            var servers = GetVisibleServers().ToList();
            _serverListView.BeginUpdate();

            try
            {
                _serverListView.Items.Clear();

                foreach (var server in servers)
                {
                    var item = new ListViewItem(new[]
                    {
                        server.ServerName,
                        BuildStatusText(server),
                        BuildServerListSummary(server)
                    })
                    {
                        Tag = server
                    };

                    if (server.Enabled == false)
                    {
                        item.ForeColor = SystemColors.GrayText;
                    }
                    else if (!string.IsNullOrWhiteSpace(server.ErrorText) ||
                             string.Equals(server.Status, "systemError", StringComparison.OrdinalIgnoreCase))
                    {
                        item.ForeColor = Color.Firebrick;
                    }

                    _serverListView.Items.Add(item);
                }

                SelectServerByName(selectedServerName);
            }
            finally
            {
                _serverListView.EndUpdate();
            }

            _preferredServerName = null;
            RefreshActionButtons();
        }

        private IEnumerable<AppServerMcpServerStatus> GetVisibleServers()
        {
            var filter = GetCurrentServerFilter();
            IEnumerable<AppServerMcpServerStatus> query = _servers;

            switch (filter)
            {
                case McpServerStatusFilter.Enabled:
                    query = query.Where(item => item.Enabled == true);
                    break;
                case McpServerStatusFilter.Disabled:
                    query = query.Where(item => item.Enabled == false);
                    break;
            }

            if (filter == McpServerStatusFilter.All)
            {
                query = query
                    .OrderByDescending(item => item.Enabled != false)
                    .ThenBy(item => item.ServerName, StringComparer.CurrentCultureIgnoreCase);
            }
            else
            {
                query = query.OrderBy(item => item.ServerName, StringComparer.CurrentCultureIgnoreCase);
            }

            return query;
        }

        private McpServerStatusFilter GetCurrentServerFilter()
        {
            switch (_filterComboBox.SelectedIndex)
            {
                case 1:
                    return McpServerStatusFilter.Enabled;
                case 2:
                    return McpServerStatusFilter.Disabled;
                default:
                    return McpServerStatusFilter.All;
            }
        }

        private void SelectServerByName(string serverName)
        {
            if (_serverListView.Items.Count == 0)
            {
                return;
            }

            ListViewItem targetItem = null;
            if (!string.IsNullOrWhiteSpace(serverName))
            {
                foreach (ListViewItem item in _serverListView.Items)
                {
                    var server = item.Tag as AppServerMcpServerStatus;
                    if (server != null && string.Equals(server.ServerName, serverName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetItem = item;
                        break;
                    }
                }
            }

            if (targetItem == null)
            {
                targetItem = _serverListView.Items[0];
            }

            targetItem.Selected = true;
            targetItem.Focused = true;
            targetItem.EnsureVisible();
        }

        private void UpdateStatusForCurrentFilter(bool updateStatusWhenEmpty)
        {
            if (_servers.Count == 0)
            {
                if (updateStatusWhenEmpty)
                {
                    SetStatus(UiLocalization.Text("McpManagementForm.Status.NoServers", "No MCP servers to display."));
                }

                return;
            }

            if (_serverListView.Items.Count == 0)
            {
                SetStatus(UiLocalization.Text("McpManagementForm.Status.NoServersForFilter", "No MCP servers match the current filter."));
                return;
            }

            SetStatus(string.Format(
                UiLocalization.Text("McpManagementForm.Status.Loaded", "Loaded {0} MCP servers. Select one and press Enter to open details."),
                _servers.Count));
        }

        private void ShowSelectedServerDetails()
        {
            var selectedServer = GetSelectedServer();
            if (selectedServer == null)
            {
                return;
            }

            using (var form = new McpServerDetailsForm(
                string.Format(
                    UiLocalization.Text("McpServerDetailsForm.Title.Template", "MCP Server Details - {0}"),
                    selectedServer.ServerName),
                string.Format(
                    UiLocalization.Text("McpServerDetailsForm.Header.Template", "Details for {0}"),
                    selectedServer.ServerName),
                BuildServerDetailText(selectedServer)))
            {
                form.ShowDialog(this);
            }
        }

        private static string BuildServerDetailText(AppServerMcpServerStatus server)
        {
            return server == null || string.IsNullOrWhiteSpace(server.DetailText)
                ? UiLocalization.Text("McpManagementForm.Detail.NoMoreDetails", "Current server did not return additional details.")
                : server.DetailText;
        }

        private void RefreshActionButtons()
        {
            var selectedServer = GetSelectedServer();
            var canEdit = !_isBusy && selectedServer != null;
            var hasConfig = canEdit && selectedServer.ConfigValues != null;

            _addButton.Enabled = !_isBusy;
            _filterComboBox.Enabled = !_isBusy;
            _deleteButton.Enabled = hasConfig && selectedServer.IsRequired != true;
            _enableButton.Enabled = hasConfig && selectedServer.Enabled != true;
            _disableButton.Enabled = hasConfig && selectedServer.Enabled != false;
            _settingsButton.Enabled = canEdit;
            _advancedDiagnosticsButton.Enabled = canEdit;
            _refreshButton.Enabled = !_isBusy;
            _loginButton.Enabled = canEdit && selectedServer.SupportsOAuth && selectedServer.Enabled != false;

            RefreshContextMenuItems();
        }

        private void RefreshContextMenuItems()
        {
            var selectedServer = GetSelectedServer();
            var canEdit = !_isBusy && selectedServer != null;
            var hasConfig = canEdit && selectedServer.ConfigValues != null;

            _addMenuItem.Enabled = !_isBusy;
            _deleteMenuItem.Enabled = hasConfig && selectedServer.IsRequired != true;
            _enableMenuItem.Enabled = hasConfig && selectedServer.Enabled != true;
            _disableMenuItem.Enabled = hasConfig && selectedServer.Enabled != false;
            _settingsMenuItem.Enabled = canEdit;
            _advancedDiagnosticsMenuItem.Enabled = canEdit;
            _loginMenuItem.Enabled = canEdit && selectedServer.SupportsOAuth && selectedServer.Enabled != false;
            RefreshNativeMcpActions();
        }

        private void SetBusy(bool isBusy)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            _isBusy = isBusy;
            UseWaitCursor = isBusy;
            RefreshActionButtons();
        }

        private void SetStatus(string text)
        {
            if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (string.Equals(_statusLabel.Text, text, StringComparison.Ordinal))
            {
                return;
            }

            _statusLabel.Text = text;
        }

        private async System.Threading.Tasks.Task<bool> EnsureAppServerAvailableForActionAsync(
            string actionName,
            AppServerProtocolFeature requiredFeature = AppServerProtocolFeature.McpManagement)
        {
            try
            {
                await _connectionClient.ConnectAsync();
                if (!AppServerProtocolFeatureSupport.IsSupported(
                        _connectionClient.ProtocolCapabilities,
                        requiredFeature))
                {
                    if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
                    {
                        return false;
                    }

                    var featureName = requiredFeature == AppServerProtocolFeature.McpOAuth
                        ? UiLocalization.Text("McpManagementForm.Feature.McpOAuth", "MCP OAuth")
                        : UiLocalization.Text("McpManagementForm.Feature.McpManagement", "MCP management");
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
                if (IsDisposed || Disposing || _lifetimeCancellation.IsCancellationRequested)
                {
                    return false;
                }

                SetStatus(string.Format(
                    UiLocalization.Text("McpManagementForm.Status.ActionFailedDisconnected", "{0} failed because App Server is not connected."),
                    actionName));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private string GetSelectedServerName()
        {
            var selectedServer = GetSelectedServer();
            return selectedServer != null ? selectedServer.ServerName : null;
        }

        private IEnumerable<string> GetServerNames()
        {
            return _serverListView.Items
                .Cast<ListViewItem>()
                .Select(item => (item.Tag as AppServerMcpServerStatus)?.ServerName)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
        }

        private AppServerMcpServerStatus GetSelectedServer()
        {
            if (_serverListView.SelectedItems.Count == 0)
            {
                return null;
            }

            return _serverListView.SelectedItems[0].Tag as AppServerMcpServerStatus;
        }

        private void BuildLayout()
        {
            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 8,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false,
                TabIndex = 0
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                Text = UiLocalization.Text(
                    "McpManagementForm.Header",
                    "Filter MCP servers, refresh status, reload config, and manage one server at a time."),
                Dock = DockStyle.Fill,
                Height = 34,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var tipLabel = new Label
            {
                Text = UiLocalization.Text(
                    "McpManagementForm.Tip",
                    "Use the filter to show all, enabled, or disabled servers. Press Enter on the list to open details or F5 to refresh status."),
                Dock = DockStyle.Fill,
                Height = 34,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var filterLabel = new Label
            {
                Text = UiLocalization.Text("McpManagementForm.Label.Filter", "Filter"),
                Dock = DockStyle.Fill,
                Height = 24,
                Margin = new Padding(0, 8, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(filterLabel, true);

            var listLabel = new Label
            {
                Text = UiLocalization.Text("McpManagementForm.Label.Servers", "Servers"),
                Dock = DockStyle.Fill,
                Height = 24,
                Margin = new Padding(0, 8, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(listLabel, true);

            var actionLabel = new Label
            {
                Text = UiLocalization.Text("McpManagementForm.Label.Actions", "Actions"),
                Dock = DockStyle.Fill,
                Height = 24,
                Margin = new Padding(0, 8, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };
            UiTheme.ApplyLabel(actionLabel, true);

            var filterPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 0
            };
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            filterPanel.Controls.Add(filterLabel, 0, 0);
            filterPanel.Controls.Add(_filterComboBox, 1, 0);

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false,
                TabIndex = 2
            };
            buttonsPanel.Controls.Add(_refreshButton);
            buttonsPanel.Controls.Add(_addButton);
            buttonsPanel.Controls.Add(_settingsButton);
            buttonsPanel.Controls.Add(_advancedDiagnosticsButton);
            buttonsPanel.Controls.Add(_loginButton);
            buttonsPanel.Controls.Add(_enableButton);
            buttonsPanel.Controls.Add(_disableButton);
            buttonsPanel.Controls.Add(_deleteButton);
            buttonsPanel.Controls.Add(_importNativeMcpButton);
            buttonsPanel.Controls.Add(_reloadNativeMcpButton);

            mainLayout.Controls.Add(titleLabel, 0, 0);
            mainLayout.Controls.Add(tipLabel, 0, 1);
            mainLayout.Controls.Add(filterPanel, 0, 2);
            mainLayout.Controls.Add(listLabel, 0, 3);
            mainLayout.Controls.Add(_serverListView, 0, 4);
            mainLayout.Controls.Add(actionLabel, 0, 5);
            mainLayout.Controls.Add(buttonsPanel, 0, 6);
            mainLayout.Controls.Add(_statusLabel, 0, 7);

            Controls.Add(mainLayout);
        }

        private static Button CreateButton(string text, string accessibleName, int tabIndex)
        {
            return new Button
            {
                Text = text,
                AccessibleName = accessibleName,
                AutoSize = true,
                MinimumSize = new Size(108, 0),
                Margin = new Padding(0, 0, 8, 0),
                UseMnemonic = true,
                TabIndex = tabIndex
            };
        }

        private static ListView CreateServerListView()
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                UseCompatibleStateImageBehavior = false,
                LabelWrap = false,
                AccessibleName = UiLocalization.Text("McpManagementForm.Accessible.Servers", "MCP Servers"),
                TabIndex = 1
            };
            UiTheme.ApplyListView(listView);

            listView.Columns.Add(UiLocalization.Text("McpManagementForm.Column.Server", "Server"), 200, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("McpManagementForm.Column.Status", "Status"), 110, HorizontalAlignment.Left);
            listView.Columns.Add(UiLocalization.Text("McpManagementForm.Column.Summary", "Summary"), 650, HorizontalAlignment.Left);
            return listView;
        }

        private static ComboBox CreateFilterComboBox()
        {
            var comboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = UiLocalization.Text("McpManagementForm.Accessible.Filter", "Filter"),
                TabIndex = 0
            };
            UiTheme.ApplyComboBox(comboBox);
            return comboBox;
        }

        private static string BuildStatusText(AppServerMcpServerStatus server)
        {
            if (server == null)
            {
                return UiLocalization.Text("McpManagementForm.Value.Unknown", "Unknown");
            }

            if (server.Enabled.HasValue)
            {
                return server.Enabled.Value
                    ? UiLocalization.Text("McpManagementForm.Value.Enabled", "Enabled")
                    : UiLocalization.Text("McpManagementForm.Value.Disabled", "Disabled");
            }

            if (server.ToolCount.HasValue || server.ResourceCount.HasValue || server.ResourceTemplateCount.HasValue)
            {
                return UiLocalization.Text("McpManagementForm.Value.Loaded", "Loaded");
            }

            switch ((server.Status ?? string.Empty).Trim())
            {
                case "active":
                    return UiLocalization.Text("McpManagementForm.Value.Active", "Active");
                case "idle":
                    return UiLocalization.Text("McpManagementForm.Value.Idle", "Idle");
                case "notLoaded":
                    return UiLocalization.Text("McpManagementForm.Value.NotLoaded", "Not Loaded");
                case "systemError":
                    return UiLocalization.Text("McpManagementForm.Value.SystemError", "System Error");
                default:
                    return string.IsNullOrWhiteSpace(server.Status)
                        ? UiLocalization.Text("McpManagementForm.Value.Unknown", "Unknown")
                        : server.Status;
            }
        }

        private static string BuildAuthText(AppServerMcpServerStatus server)
        {
            if (server == null || string.IsNullOrWhiteSpace(server.AuthStatus))
            {
                return server != null && server.SupportsOAuth
                    ? UiLocalization.Text("McpManagementForm.Value.NotProvided", "Not Provided")
                    : UiLocalization.Text("McpManagementForm.Value.Unsupported", "Unsupported");
            }

            switch ((server.AuthStatus ?? string.Empty).Trim())
            {
                case "unsupported":
                    return UiLocalization.Text("McpManagementForm.Value.Unsupported", "Unsupported");
                case "notLoggedIn":
                    return UiLocalization.Text("McpManagementForm.Value.NotLoggedIn", "Not Logged In");
                case "bearerToken":
                    return "Bearer Token";
                case "oAuth":
                    return "OAuth";
                case "pendingInit":
                    return UiLocalization.Text("McpManagementForm.Value.PendingInit", "Initializing");
                case "notFound":
                    return UiLocalization.Text("McpManagementForm.Value.NotConfiguredOrLoggedIn", "Not Configured or Not Logged In");
                default:
                    return server.AuthStatus;
            }
        }

        private static string BuildCapabilityText(AppServerMcpServerStatus server)
        {
            if (server == null)
            {
                return UiLocalization.Text("McpManagementForm.Value.NotProvided", "Not Provided");
            }

            var unknown = UiLocalization.Text("McpManagementForm.Value.Unknown", "Unknown");
            var toolCount = server.ToolCount.HasValue ? server.ToolCount.Value.ToString() : unknown;
            var resourceCount = server.ResourceCount.HasValue ? server.ResourceCount.Value.ToString() : unknown;
            var templateCount = server.ResourceTemplateCount.HasValue ? server.ResourceTemplateCount.Value.ToString() : unknown;
            return string.Format(
                UiLocalization.Text("McpManagementForm.Summary.Capability", "Tools {0} / Resources {1} / Templates {2}"),
                toolCount,
                resourceCount,
                templateCount);
        }

        private static string BuildServerListSummary(AppServerMcpServerStatus server)
        {
            if (server == null)
            {
                return UiLocalization.Text("McpManagementForm.Value.NotProvided", "Not Provided");
            }

            var parts = new List<string>
            {
                UiLocalization.Text("McpManagementForm.Summary.AuthPrefix", "Auth: ") + BuildAuthText(server),
                BuildCapabilityText(server)
            };

            if (!string.IsNullOrWhiteSpace(server.SourceSummary))
            {
                parts.Add(UiLocalization.Text("McpManagementForm.Summary.SourcePrefix", "Source: ") + server.SourceSummary.Trim());
            }

            if (!string.IsNullOrWhiteSpace(server.ErrorText))
            {
                parts.Add(UiLocalization.Text("McpManagementForm.Summary.ErrorPrefix", "Error: ") + server.ErrorText.Trim());
            }

            return string.Join(
                UiLocalization.Text("McpManagementForm.Summary.Separator", "; "),
                parts.Where(item => !string.IsNullOrWhiteSpace(item)));
        }

        private static string BuildServerConfigKeyPath(string serverName)
        {
            return "mcp_servers." + (serverName ?? string.Empty).Trim();
        }

        private static string BuildServerConfigFieldKeyPath(string serverName, string fieldName)
        {
            return BuildServerConfigKeyPath(serverName) + "." + fieldName;
        }

        private static IReadOnlyCollection<AppServerConfigWriteEdit> BuildServerSettingEdits(string serverName, IDictionary<string, object> config)
        {
            var edits = new List<AppServerConfigWriteEdit>();
            var keyPrefix = BuildServerConfigKeyPath(serverName);

            edits.Add(AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(serverName, "enabled"), JsonValueReader.GetValue(config, "enabled")));
            edits.Add(AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(serverName, "url"), JsonValueReader.GetString(config, "url")));
            edits.Add(AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(serverName, "baseUrl"), JsonValueReader.GetString(config, "baseUrl")));
            edits.Add(AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(serverName, "serverUrl"), JsonValueReader.GetString(config, "serverUrl")));
            edits.Add(AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(serverName, "endpoint"), JsonValueReader.GetString(config, "endpoint")));
            edits.Add(AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(serverName, "command"), JsonValueReader.GetString(config, "command")));
            edits.Add(AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(serverName, "cwd"), JsonValueReader.GetString(config, "cwd")));
            edits.Add(AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(serverName, "args"), GetArrayValue(config, "args")));
            edits.Add(AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(serverName, "env"), GetDictionaryValue(config, "env")));
            edits.Add(AppServerConfigWriteEdit.CreateReplace(BuildServerConfigFieldKeyPath(serverName, "env_vars"), GetDictionaryValue(config, "env_vars")));

            if (config != null)
            {
                foreach (var pair in config)
                {
                    if (pair.Key == null)
                    {
                        continue;
                    }

                    switch (pair.Key)
                    {
                        case "enabled":
                        case "url":
                        case "baseUrl":
                        case "serverUrl":
                        case "endpoint":
                        case "command":
                        case "cwd":
                        case "args":
                        case "env":
                        case "env_vars":
                            continue;
                    }

                    if (pair.Value == null)
                    {
                        continue;
                    }

                    edits.Add(AppServerConfigWriteEdit.CreateReplace(keyPrefix + "." + pair.Key, pair.Value));
                }
            }

            return edits;
        }

        private void OpenExternalUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                ExternalUrlLauncher.Open(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text(
                        "McpManagementForm.Message.OpenBrowserFailedPrefix",
                        "Cannot open browser automatically. Please visit this URL manually:") +
                    Environment.NewLine + Environment.NewLine + url + Environment.NewLine + Environment.NewLine + ex.Message,
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private static Dictionary<string, object> CloneDictionary(IDictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source)
            {
                result[pair.Key] = CloneValue(pair.Value);
            }

            return result;
        }

        private static object CloneValue(object value)
        {
            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                return CloneDictionary(dictionary);
            }

            if (value == null || value is string)
            {
                return value;
            }

            var enumerable = value as IEnumerable;
            if (enumerable != null)
            {
                var items = new List<object>();
                foreach (var item in enumerable)
                {
                    items.Add(CloneValue(item));
                }

                return items.ToArray();
            }

            return value;
        }

        private static object GetArrayValue(IDictionary<string, object> source, string key)
        {
            var rawValue = JsonValueReader.GetValue(source, key);
            if (rawValue == null || rawValue is string)
            {
                return null;
            }

            var enumerable = rawValue as IEnumerable;
            if (enumerable == null)
            {
                return null;
            }

            var items = new List<object>();
            foreach (var item in enumerable)
            {
                items.Add(item);
            }

            return items.Count == 0 ? null : items.ToArray();
        }

        private static Dictionary<string, object> GetDictionaryValue(IDictionary<string, object> source, string key)
        {
            return CloneDictionary(JsonValueReader.GetValue(source, key) as IDictionary<string, object>);
        }
    }

    internal enum McpServerStatusFilter
    {
        All,
        Enabled,
        Disabled
    }

    internal sealed class ResourceUriInputForm : Form
    {
        private readonly TextBox _uriTextBox;
        private readonly ListBox _resourceListBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public ResourceUriInputForm()
            : this(null)
        {
        }

        public ResourceUriInputForm(IEnumerable<AppServerMcpResourceSummary> resources)
        {
            Text = UiLocalization.Text("McpResourceUriInputForm.Title", "MCP Resource URI");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(640, 300);
            UiTheme.ApplyForm(this);

            _resourceListBox = new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("McpResourceUriInputForm.Accessible.Resources", "Resources"),
                TabIndex = 0
            };
            UiTheme.ApplyListBox(_resourceListBox);
            _resourceListBox.SelectedIndexChanged += ResourceListBox_SelectedIndexChanged;

            _uriTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("McpResourceUriInputForm.Accessible.Uri", "Resource URI"),
                TabIndex = 1
            };
            UiTheme.ApplyTextBox(_uriTextBox, false, false);

            _okButton = new Button
            {
                Text = UiLocalization.Text("Common.OK", "&OK"),
                AccessibleName = UiLocalization.Text("Common.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = 2,
                DialogResult = DialogResult.None
            };
            _okButton.Click += OkButton_Click;
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("Common.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("Common.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 3,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var resourcesLabel = new Label
            {
                Text = UiLocalization.Text("McpResourceUriInputForm.Label.Resources", "Resources"),
                Dock = DockStyle.Fill,
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(resourcesLabel, true);

            var label = new Label
            {
                Text = UiLocalization.Text("McpResourceUriInputForm.Label.Uri", "Resource URI"),
                Dock = DockStyle.Fill,
                AutoSize = true,
                TabStop = false
            };
            UiTheme.ApplyLabel(label, true);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(0, 12, 0, 0),
                TabStop = false
            };
            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(_okButton);

            root.Controls.Add(resourcesLabel, 0, 0);
            root.Controls.Add(_resourceListBox, 0, 1);
            root.Controls.Add(label, 0, 2);
            root.Controls.Add(_uriTextBox, 0, 3);
            root.Controls.Add(buttons, 0, 4);
            Controls.Add(root);

            BindResources(resources);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        public string ResourceUri
        {
            get { return _uriTextBox.Text != null ? _uriTextBox.Text.Trim() : string.Empty; }
        }

        private void BindResources(IEnumerable<AppServerMcpResourceSummary> resources)
        {
            var items = (resources ?? Enumerable.Empty<AppServerMcpResourceSummary>())
                .Where(item => item != null && !item.IsTemplate && !string.IsNullOrWhiteSpace(item.Uri))
                .ToList();

            if (items.Count == 0)
            {
                _resourceListBox.Items.Add(UiLocalization.Text(
                    "McpResourceUriInputForm.Value.NoResourceDetails",
                    "No resource URI details were returned. Enter a URI manually."));
                _resourceListBox.Enabled = false;
                _uriTextBox.Focus();
                return;
            }

            foreach (var item in items)
            {
                _resourceListBox.Items.Add(new ResourceListItem(item));
            }

            _resourceListBox.SelectedIndex = 0;
        }

        private void ResourceListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            var item = _resourceListBox.SelectedItem as ResourceListItem;
            if (item == null || item.Resource == null)
            {
                return;
            }

            _uriTextBox.Text = item.Resource.Uri ?? string.Empty;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ResourceUri))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("McpResourceUriInputForm.Message.UriRequired", "Please enter a resource URI."),
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _uriTextBox.Focus();
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private sealed class ResourceListItem
        {
            public ResourceListItem(AppServerMcpResourceSummary resource)
            {
                Resource = resource;
            }

            public AppServerMcpResourceSummary Resource { get; private set; }

            public override string ToString()
            {
                if (Resource == null)
                {
                    return string.Empty;
                }

                var title = !string.IsNullOrWhiteSpace(Resource.Title)
                    ? Resource.Title
                    : !string.IsNullOrWhiteSpace(Resource.Name)
                        ? Resource.Name
                        : Resource.Uri;
                if (string.IsNullOrWhiteSpace(Resource.Description))
                {
                    return title + " - " + Resource.Uri;
                }

                return title + " - " + Resource.Description + " - " + Resource.Uri;
            }
        }
    }
}
