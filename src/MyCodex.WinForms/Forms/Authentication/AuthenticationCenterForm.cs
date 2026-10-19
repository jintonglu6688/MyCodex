using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class AuthenticationCenterForm : Form
    {
        private readonly IAuthenticationCenterClient _client;
        private readonly IScreenReaderService _screenReader;
        private readonly Func<AuthenticationProvider, CancellationToken, Task<AuthenticationApplyResult>> _apply;
        private readonly Func<AuthenticationProvider, CancellationToken, Task<AuthenticationDeleteResult>> _delete;
        private readonly Func<AuthenticationProviderEdit, CancellationToken, Task<AuthenticationSaveResult>> _save;
        private readonly Func<string, CancellationToken, Task<bool>> _deleteAccount;
        private readonly Func<CancellationToken, Task<bool>> _reconnect;
        private readonly Func<string> _officialIdentity;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly ListView _providersList;
        private readonly Label _targetLabel;
        private readonly Label _statusLabel;
        private bool _supportsConfigEditing;
        private bool _supportsFullConfigEditor;
        private bool _supportsSessionHistory;
        private bool _supportsInactiveSaveWhileBusy;
        private readonly Button _addButton;
        private readonly Button _accountsButton;
        private readonly Button _refreshButton;
        private readonly Button _updateBackendButton;
        private readonly Button _retryButton;
        private readonly Func<bool, CancellationToken, Task<AuthenticationSessionHistoryResult>> _setSessionHistory;
        private readonly Func<CancellationToken, Task> _updateBackend;
        private readonly ContextMenu _providerContextMenu;
        private readonly MenuItem _enableMenuItem;
        private readonly MenuItem _editMenuItem;
        private readonly MenuItem _copyMenuItem;
        private readonly MenuItem _deleteMenuItem;
        private readonly MenuItem _reauthenticateMenuItem;
        private readonly Label _identityLabel;
        private bool _reconnectPending;
        private bool _busy;
        private bool _loaded;
        private bool _updateAvailable;
        private bool _supportsContextSettings;
        private bool _disposed;
        private string _currentProviderId;
        private string _liveState;
        private string _liveAccountLabel;
        private string _shownLoadError;
        private IReadOnlyList<AuthenticationProviderPreset> _presets;
        private readonly AuthenticationModelMetadataService _modelMetadata;

        public AuthenticationCenterForm(IAuthenticationCenterClient client, IScreenReaderService screenReader = null,
            Func<AuthenticationProvider, CancellationToken, Task<AuthenticationApplyResult>> apply = null,
            Func<CancellationToken, Task<bool>> reconnect = null, Func<string> officialIdentity = null, bool reconnectPending = false,
            Func<AuthenticationProvider, CancellationToken, Task<AuthenticationDeleteResult>> delete = null,
            Func<AuthenticationProviderEdit, CancellationToken, Task<AuthenticationSaveResult>> save = null,
            Func<string, CancellationToken, Task<bool>> deleteAccount = null,
            Func<CancellationToken, Task> updateBackend = null,
            IModelMetadataCatalogSource modelMetadataCatalog = null,
            Func<bool, CancellationToken, Task<AuthenticationSessionHistoryResult>> setSessionHistory = null,
            Func<Func<CancellationToken, Task<AuthenticationStackState>>, CancellationToken, Task<AuthenticationStackResult>> changeStack = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (modelMetadataCatalog != null)
            {
                _modelMetadata = new AuthenticationModelMetadataService(modelMetadataCatalog, async token =>
                    _presets ?? (_presets = await _client.ReadPresetsAsync(token)));
            }
            _screenReader = screenReader;
            _apply = apply;
            _delete = delete;
            _save = save;
            _deleteAccount = deleteAccount;
            _updateBackend = updateBackend;
            _setSessionHistory = setSessionHistory;
            _changeStack = changeStack;
            _reconnect = reconnect;
            _officialIdentity = officialIdentity;
            _reconnectPending = reconnectPending;
            Text = UiLocalization.Text("AuthenticationCenter.Title", "Authentication Center");
            AccessibleName = Text;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 460);
            Size = new Size(940, 580);
            UiTheme.ApplyForm(this);
            _providersList = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, TabIndex = 0,
                AccessibleName = UiLocalization.Text("AuthenticationCenter.Providers", "Providers")
            };
            UiTheme.ApplyListView(_providersList);
            _providersList.Columns.Add(UiLocalization.Text("AuthenticationCenter.Column.Name", "Name"), 200);
            _providersList.Columns.Add(UiLocalization.Text("AuthenticationCenter.Column.Kind", "Type"), 230);
            _providersList.Columns.Add(UiLocalization.Text("AuthenticationCenter.Column.Model", "Model"), 160);
            _providersList.Columns.Add(UiLocalization.Text("AuthenticationCenter.Column.Connection", "Connection"), 120);
            _providersList.Columns.Add(UiLocalization.Text("AuthenticationCenter.Column.State", "State"), 100);
            _providersList.Columns.Add(UiLocalization.Text("AuthenticationCenter.Column.Account", "Account"), 180);
            _providersList.Columns.Add(UiLocalization.Text("Aggregation.Column", "Aggregation"), 90);
            _targetLabel = new Label { Dock = DockStyle.Fill, AutoSize = true, TabStop = false };
            _statusLabel = new Label { Dock = DockStyle.Fill, AutoSize = true, TabStop = false };
            _identityLabel = new Label { Dock = DockStyle.Fill, AutoSize = true, TabStop = false };
            _addButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationCenter.Button.Add", "&Add"), 1);
            _accountsButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationCenter.Button.Accounts", "Saved accoun&ts"), 3);
            _refreshButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationCenter.Button.Refresh", "&Refresh"), 5);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);
            _updateBackendButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationCenter.Button.UpdateBackend", "&Update backend"), 6);
            _retryButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationCenter.Button.RetryConnection", "Retry co&nnection"), 7);
            var close = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationCenter.Button.Close", "&Close"), 9);
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, TabStop = false, TabIndex = 3 };
            actions.Controls.AddRange(new Control[] { _addButton, _accountsButton, _refreshButton, _updateBackendButton, _retryButton, close });
            _sessionHistoryCheckBox = CreateSessionHistoryCheckBox();
            _sessionHistoryRetryButton = AuthenticationCenterUiText.Button(UiLocalization.Text("SessionHistory.Button.Retry", "Retry movin&g sessions"), 1);
            _sessionHistoryRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, TabStop = false, TabIndex = 2, Visible = false };
            _sessionHistoryRow.Controls.AddRange(new Control[] { _sessionHistoryCheckBox, _sessionHistoryRetryButton });
            _aggregationLabel = new Label { AutoSize = true, TabStop = false, MaximumSize = new Size(880, 0), Margin = new Padding(4) };
            _aggregationButton = AuthenticationCenterUiText.Button(AggregationButtonText(false), 0);
            _aggregationMemberButton = AuthenticationCenterUiText.Button(MemberButtonText(false), 1);
            _aggregationCatalogButton = AuthenticationCenterUiText.Button(UiLocalization.Text("Aggregation.Button.AdoptCatalog", "Use aggregated catal&og"), 2);
            _classicSubagentsCheckBox = CreateClassicSubagentsCheckBox();
            _aggregationRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, TabStop = false, TabIndex = 1, Visible = false };
            _aggregationRow.Controls.AddRange(new Control[] { _aggregationLabel, _aggregationButton, _aggregationMemberButton, _aggregationCatalogButton, _classicSubagentsCheckBox });
            _aggregationRow.SetFlowBreak(_aggregationLabel, true);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(12), TabStop = false };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(_targetLabel, 0, 0);
            root.Controls.Add(_identityLabel, 0, 1);
            root.Controls.Add(_providersList, 0, 2);
            root.Controls.Add(_aggregationRow, 0, 3);
            root.Controls.Add(_sessionHistoryRow, 0, 4);
            root.Controls.Add(_statusLabel, 0, 5);
            root.Controls.Add(actions, 0, 6);
            Controls.Add(root);
            _providerContextMenu = new ContextMenu();
            _enableMenuItem = new MenuItem(UiLocalization.Text("AuthenticationCenter.Button.Enable", "&Enable"));
            _editMenuItem = new MenuItem(UiLocalization.Text("AuthenticationCenter.Button.Edit", "&Edit"));
            _copyMenuItem = new MenuItem(UiLocalization.Text("AuthenticationCenter.Button.Copy", "Co&py"));
            _deleteMenuItem = new MenuItem(UiLocalization.Text("AuthenticationCenter.Button.Delete", "&Delete"));
            _reauthenticateMenuItem = new MenuItem(UiLocalization.Text("AuthenticationCenter.Button.Reauthenticate", "Sign in &again"));
            _aggregationMemberMenuItem = new MenuItem(MemberButtonText(false)) { Visible = false };
            _providerContextMenu.MenuItems.Add(_enableMenuItem);
            _providerContextMenu.MenuItems.Add(_aggregationMemberMenuItem);
            _providerContextMenu.MenuItems.Add(_editMenuItem);
            _providerContextMenu.MenuItems.Add(_copyMenuItem);
            _providerContextMenu.MenuItems.Add(_reauthenticateMenuItem);
            _providerContextMenu.MenuItems.Add("-");
            _providerContextMenu.MenuItems.Add(_deleteMenuItem);
            _providerContextMenu.Popup += (sender, args) => UpdateContextMenu();
            _providersList.ContextMenu = _providerContextMenu;
            _providersList.SelectedIndexChanged += (sender, args) => UpdateActions();
            _providersList.DoubleClick += async (sender, args) => await EditAsync(SelectedProvider);
            _providersList.PreviewKeyDown += (sender, args) =>
            {
                if (args.KeyData == Keys.Enter) { args.IsInputKey = true; }
            };
            _providersList.KeyDown += async (sender, args) =>
            {
                if (args.KeyData == Keys.Enter)
                {
                    args.Handled = args.SuppressKeyPress = true;
                    await ApplySelectedAsync();
                }
            };
            _addButton.Click += async (sender, args) => await AddAsync();
            _accountsButton.Click += async (sender, args) =>
            {
                using (var accounts = new AuthenticationAccountsForm(_client, _deleteAccount))
                {
                    accounts.ShowDialog(this);
                    _reconnectPending |= accounts.ReconnectPending;
                }
                await RefreshAsync();
                if (!IsDisposed) _providersList.Focus();
            };
            _refreshButton.Click += async (sender, args) => { _shownLoadError = null; await RefreshAsync(); };
            _updateBackendButton.Click += async (sender, args) => await UpdateBackendAsync();
            _retryButton.Click += async (sender, args) => await RetryConnectionAsync();
            _sessionHistoryCheckBox.Click += async (sender, args) => await ToggleSessionHistoryAsync();
            _sessionHistoryRetryButton.Click += async (sender, args) => await RetrySessionHistoryAsync();
            _aggregationButton.Click += async (sender, args) => await ToggleAggregationAsync();
            _aggregationMemberButton.Click += async (sender, args) => await ToggleSelectedMemberAsync();
            _aggregationMemberMenuItem.Click += async (sender, args) => await ToggleSelectedMemberAsync();
            _aggregationCatalogButton.Click += async (sender, args) => await AdoptAggregationCatalogAsync();
            _classicSubagentsCheckBox.Click += async (sender, args) => await ToggleClassicSubagentsAsync();
            _enableMenuItem.Click += async (sender, args) => await ApplySelectedAsync();
            _editMenuItem.Click += async (sender, args) => await EditAsync(SelectedProvider);
            _copyMenuItem.Click += async (sender, args) => await CopySelectedAsync();
            _deleteMenuItem.Click += async (sender, args) => await DeleteSelectedAsync();
            _reauthenticateMenuItem.Click += async (sender, args) => await EditAsync(SelectedProvider, reauthenticate: true);
            KeyPreview = true;
            KeyDown += async (sender, args) =>
            {
                if (args.KeyCode == Keys.F5)
                {
                    args.SuppressKeyPress = true;
                    await RefreshAsync();
                }
            };
            Shown += async (sender, args) => await RefreshAsync();
            UpdateActions();
            ActiveControl = _providersList;
        }

        private AuthenticationProvider SelectedProvider
        {
            get
            {
                return _providersList.SelectedItems.Count == 1 ? _providersList.SelectedItems[0].Tag as AuthenticationProvider : null;
            }
        }

        internal async Task RefreshAsync(string selectedId = null)
        {
            if (_busy || IsDisposed)
            {
                return;
            }
            selectedId = selectedId ?? SelectedProvider?.Id;
            _busy = true;
            UpdateActions();
            Announce(UiLocalization.Text("AuthenticationCenter.Status.Loading", "Reading provider configurations…"));
            try
            {
                var snapshot = await _client.ReadAsync(_lifetime.Token);
                if (IsDisposed)
                {
                    return;
                }
                    _supportsContextSettings = snapshot.SupportsContextSettings;
                _supportsConfigEditing = snapshot.SupportsConfigEditing;
            _supportsFullConfigEditor = snapshot.SupportsFullConfigEditor;
            _supportsSessionHistory = snapshot.SupportsSessionHistory;
            _supportsInactiveSaveWhileBusy = snapshot.SupportsInactiveSaveWhileBusy;
                _supportsAggregation = snapshot.SupportsAggregation;
                var aggregationNote = await ReadStackStateAsync();
                if (IsDisposed) return;
                _liveState = snapshot.LiveState;
                _liveAccountLabel = snapshot.LiveAccountLabel;
                _currentProviderId = string.IsNullOrEmpty(_liveState) || _liveState == "current"
                    ? snapshot.CurrentProviderId : null;
                selectedId = selectedId ?? _currentProviderId;
                _targetLabel.Text = snapshot.TargetName + Environment.NewLine + snapshot.CodexHome;
                RefreshOfficialIdentity();
                _providersList.BeginUpdate();
                try
                {
                    _providersList.Items.Clear();
                    var selectionFound = false;
                    foreach (var provider in snapshot.Providers)
                    {
                        var current = string.Equals(provider.Id, _currentProviderId, StringComparison.Ordinal);
                        var item = new ListViewItem(new[] { provider.Name, provider.PresetName ?? AuthenticationCenterUiText.Kind(provider.Kind), provider.Model,
                            AuthenticationCenterUiText.Connection(provider.Kind),
                            provider.NeedsReauthentication
                                ? UiLocalization.Text("AuthenticationCenter.State.NeedsReauthentication", "Sign-in required")
                                : current
                                ? (provider.Version == snapshot.CurrentProviderVersion
                                    ? UiLocalization.Text("AuthenticationCenter.State.Current", "Current")
                                    : UiLocalization.Text("AuthenticationCenter.State.Changed", "Saved changes"))
                                : UiLocalization.Text("AuthenticationCenter.State.Saved", "Saved"), provider.AccountLabel ?? string.Empty,
                            AggregationColumnText(provider.Id) }) { Tag = provider };
                        _providersList.Items.Add(item);
                        item.Selected = provider.Id == selectedId;
                        item.Focused = item.Selected;
                        selectionFound |= provider.Id == selectedId;
                    }
                    if (!selectionFound && _providersList.Items.Count > 0)
                    {
                        _providersList.Items[0].Selected = true;
                        _providersList.Items[0].Focused = true;
                    }
                }
                finally
                {
                    _providersList.EndUpdate();
                }
                _loaded = true;
                _updateAvailable = false;
                var routeError = !snapshot.RouteRequired ? null
                    : !snapshot.ManualRestartRequired && !snapshot.StartupRecoveryReady
                        ? AuthenticationCenterUiText.Error(new AuthenticationCenterException("startup_registration_failed"))
                        : !snapshot.RouterRunning
                            ? UiLocalization.Text("AuthenticationCenter.Error.RouterUnavailable", "The current configuration requires conversion, but its route is unavailable. Refresh or switch to a direct provider.")
                            : null;
                var readyStatus = snapshot.RouteRequired
                    ? routeError ?? (snapshot.ManualRestartRequired
                                ? UiLocalization.Text("AuthenticationCenter.Status.ManualRestart", "Conversion routing is ready. After this target restarts, open MyCodex to restore the route before using its CLI.")
                                : string.Format(UiLocalization.Text("AuthenticationCenter.Status.RouterReady", "Configurations loaded. Conversion routing is ready; active requests: {0}."), snapshot.RouteInFlight))
                    : UiLocalization.Text("AuthenticationCenter.Status.Ready", "Configurations loaded. Saving does not switch the current provider.");
                    if (!snapshot.RouteRequired) readyStatus = UiLocalization.Text("AuthenticationCenter.Status.NativeReady", "Configurations loaded. Editing the current provider reads global settings; saving it updates them immediately.");
                if (snapshot.Aggregating && snapshot.RouterRunning)
                    readyStatus = UiLocalization.Text("Aggregation.Status.Ready", "Configurations loaded. Aggregation is on; enabling a provider makes it the default.");
                var sessionHistoryNote = await ReadSessionHistoryStateAsync();
                if (IsDisposed) return;
                readyStatus = AuthenticationCenterUiText.LiveStatus(snapshot, readyStatus);
                if (aggregationNote != null) readyStatus += " " + aggregationNote;
                Announce(sessionHistoryNote == null ? readyStatus : readyStatus + " " + sessionHistoryNote);
                ShowLoadError(string.Join(Environment.NewLine, new[]
                {
                    routeError,
                    AuthenticationCenterUiText.SyncErrorText(snapshot),
                    aggregationNote,
                    _sessionHistory == null ? sessionHistoryNote : null
                }.Where(text => !string.IsNullOrEmpty(text))));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (!IsDisposed)
                {
                    _loaded = false;
                    _updateAvailable = error is AuthenticationCenterException failure && failure.Code == "backend_update_available";
                    _providersList.Items.Clear();
                    Announce(AuthenticationCenterUiText.Error(error));
                    // An available backend update is an offer with its own button, not a failure.
                    ShowLoadError(_updateAvailable ? null : AuthenticationCenterUiText.Error(error));
                }
            }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    UpdateActions();
                    if (_updateAvailable && _updateBackendButton.Visible) _updateBackendButton.Focus();
                }
            }
        }

        private async Task AddAsync()
        {
            if (_busy || !_loaded)
            {
                return;
            }
            if (_presets == null)
            {
                _busy = true;
                UpdateActions();
                try
                {
                    _presets = await _client.ReadPresetsAsync(_lifetime.Token);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception error)
                {
                    if (!IsDisposed) AnnounceError(AuthenticationCenterUiText.Error(error));
                    return;
                }
                finally
                {
                    _busy = false;
                    if (!IsDisposed) UpdateActions();
                }
            }
            if (IsDisposed) return;
            using (var selector = new AuthenticationProviderSelectionForm(_presets))
            {
                if (selector.ShowDialog(this) == DialogResult.OK)
                {
                    await EditAsync(null, selector.SelectedTemplate);
                }
            }
            if (!IsDisposed)
            {
                _providersList.Focus();
            }
        }

        private async Task EditAsync(AuthenticationProvider provider, AuthenticationProviderPreset template = null, bool reauthenticate = false)
        {
            if (_busy || !_loaded || _reconnectPending || (provider == null && template == null))
            {
                return;
            }
            if (provider != null)
            {
                _busy = true;
                UpdateActions();
                try { provider = await _client.ReadForEditAsync(provider, _lifetime.Token); }
                catch (OperationCanceledException) { return; }
                catch (Exception error) { if (!IsDisposed) AnnounceError(AuthenticationCenterUiText.Error(error)); return; }
                finally { _busy = false; if (!IsDisposed) UpdateActions(); }
            }
            if (IsDisposed) return;
            using (var editor = new AuthenticationProviderEditForm(_client, provider, null, template, reauthenticate, SaveForEditor(), _supportsContextSettings, _supportsConfigEditing, _modelMetadata, _supportsFullConfigEditor))
            {
                if (editor.ShowDialog(this) == DialogResult.OK)
                {
                    await CompleteProviderSaveAsync(editor.SaveResult);
                }
                else if (editor.SaveOutcomeUnknown)
                {
                    _reconnectPending = true;
                    await RefreshAsync();
                    AnnounceError(AuthenticationCenterUiText.Error(new AuthenticationCenterException("operation_unknown")));
                }
            }
            if (!IsDisposed)
            {
                _providersList.Focus();
            }
        }

        // A provider other than the one in use can be saved while work runs; the work keeps its configuration.
        private Func<AuthenticationProviderEdit, CancellationToken, Task<AuthenticationSaveResult>> SaveForEditor()
        {
            if (_save == null) return null;
            var current = _currentProviderId;
            var allowed = _supportsInactiveSaveWhileBusy && !string.IsNullOrEmpty(current);
            return (edit, token) =>
            {
                edit.AllowWhileBusy = allowed && !string.Equals(edit.Id, current, StringComparison.Ordinal);
                return _save(edit, token);
            };
        }

        internal async Task CompleteProviderSaveAsync(AuthenticationSaveResult result)
        {
            var saved = result.Provider;
            _reconnectPending = saved.GlobalApplied && !result.Reconnected;
            await RefreshAsync(saved.Id);
            if (IsDisposed) return;
            if (_reconnectPending)
                AnnounceError(UiLocalization.Text("AuthenticationCenter.Status.ReconnectFailed", "Global configuration is applied, but MyCodex could not reconnect. Retry the connection; the global change has not been rolled back."));
            else if (saved.RecoveryRequired && SelectedProvider?.Id == saved.Id)
                Announce(UiLocalization.Text("AuthenticationCenter.Status.SavedForEnable", "Configuration saved. Select Enable to back up the old configuration automatically, switch to this provider and reconnect. The current configuration has not changed."));
        }

        internal async Task CopySelectedAsync()
        {
            var provider = SelectedProvider;
            if (_busy || !_loaded || provider == null)
            {
                return;
            }
            await MutateAsync(async () =>
            {
                var copy = await _client.CopyAsync(provider, provider.Name + UiLocalization.Text("AuthenticationCenter.CopySuffix", " (copy)"), _lifetime.Token);
                return copy.Id;
            });
        }

        private async Task DeleteSelectedAsync()
        {
            var provider = SelectedProvider;
            if (_busy || !_loaded || provider == null || provider.Id == _currentProviderId)
            {
                return;
            }
            var isChatGpt = string.Equals(provider.Kind, "chatgpt", StringComparison.Ordinal);
            var confirmation = UiLocalization.Text("AuthenticationCenter.DeleteConfirmation", "Delete the selected saved configuration? This cannot be undone.");
            if (MessageBox.Show(this, confirmation, Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                return;
            }
            AuthenticationDeleteResult result = null;
            await MutateAsync(async () =>
            {
                result = _delete != null
                    ? await _delete(provider, _lifetime.Token)
                    : new AuthenticationDeleteResult
                    {
                        AccountRemoved = await _client.DeleteAsync(provider, _lifetime.Token),
                        Reconnected = true
                    };
                _reconnectPending = result.AccountRemoved && !result.Reconnected;
                return null;
            }, () => isChatGpt
                ? result.AccountRemoved
                    ? result.Reconnected
                        ? UiLocalization.Text("AuthenticationCenter.Status.AccountSignedOut", "Configuration deleted. The account was signed out and its saved credentials were removed.")
                        : UiLocalization.Text("AuthenticationCenter.Status.AccountReconnectFailed", "Configuration and account deleted, but MyCodex could not reconnect. Retry the connection.")
                    : UiLocalization.Text("AuthenticationCenter.Status.AccountStillUsed", "Configuration deleted. The account remains available to other configurations.")
                : UiLocalization.Text("AuthenticationCenter.Status.Deleted", "Configuration deleted."));
        }

        private async Task MutateAsync(Func<Task<string>> mutation, Func<string> successStatus = null)
        {
            _busy = true;
            UpdateActions();
            try
            {
                var selectedId = await mutation();
                _busy = false;
                if (!IsDisposed)
                {
                    await RefreshAsync(selectedId);
                    if (_loaded && successStatus != null)
                    {
                        // A deletion that could not reconnect is reported as a failure.
                        if (_reconnectPending) AnnounceError(successStatus());
                        else Announce(successStatus());
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (!IsDisposed)
                {
                    if (error is AuthenticationCenterException deletionError &&
                        deletionError.Code == "account_delete_recovery_required")
                    {
                        _reconnectPending = true;
                    }
                    AnnounceError(AuthenticationCenterUiText.Error(error));
                }
            }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    UpdateActions();
                }
            }
        }

        private void UpdateActions()
        {
            var provider = SelectedProvider;
            _addButton.Enabled = !_busy && _loaded;
            _accountsButton.Enabled = !_busy && _loaded && !_reconnectPending;
            _copyMenuItem.Enabled = !_busy && _loaded && provider != null;
            _editMenuItem.Enabled = !_busy && _loaded && provider != null;
            _deleteMenuItem.Enabled = !_busy && _loaded && provider != null && provider.Id != _currentProviderId;
            _refreshButton.Enabled = !_busy;
            _updateBackendButton.Visible = _updateBackend != null && _updateAvailable;
            _updateBackendButton.Enabled = !_busy && _updateAvailable;
            _enableMenuItem.Enabled = !_busy && _loaded && !_reconnectPending && _apply != null && provider != null &&
                (provider.Kind == "chatgpt"
                    ? !string.IsNullOrWhiteSpace(provider.AccountId) && !provider.NeedsReauthentication
                    : (provider.Kind == "official_api" || provider.Kind == "responses" || provider.Kind == "chat_completions" || provider.Kind == "anthropic") &&
                        (provider.HasApiKey || (provider.Advanced != null && provider.Advanced.TryGetValue("envKey", out var envKey) && !string.IsNullOrWhiteSpace(envKey as string))));
            _retryButton.Enabled = !_busy && _reconnectPending && _reconnect != null;
            _retryButton.Visible = _reconnectPending;
            UpdateSessionHistoryControls();
            UpdateAggregationControls();
            if (_reconnectPending)
            {
                _addButton.Enabled = _editMenuItem.Enabled = _copyMenuItem.Enabled = _deleteMenuItem.Enabled = false;
            }
            UpdateContextMenu();
        }

        internal async Task UpdateBackendAsync()
        {
            if (_busy || !_updateAvailable || _updateBackend == null) return;
            _busy = true;
            UpdateActions();
            Announce(UiLocalization.Text("AuthenticationCenter.Status.UpdatingBackend", "Checking active requests and updating the target backend…"));
            try
            {
                await _updateBackend(_lifetime.Token);
                if (IsDisposed) return;
                _busy = false;
                _updateAvailable = false;
                await RefreshAsync();
                if (!IsDisposed) _providersList.Focus();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!IsDisposed) AnnounceError(AuthenticationCenterUiText.Error(error));
            }
            finally
            {
                _busy = false;
                if (!IsDisposed) UpdateActions();
            }
        }

        private void UpdateContextMenu()
        {
            _reauthenticateMenuItem.Visible = SelectedProvider?.Kind == "chatgpt";
            _reauthenticateMenuItem.Enabled = !_busy && _loaded && SelectedProvider?.Kind == "chatgpt";
            if (_reconnectPending) _reauthenticateMenuItem.Enabled = false;
        }

        internal async Task ApplySelectedAsync()
        {
            var provider = SelectedProvider;
            if (_busy || !_loaded || _apply == null || provider == null || !_enableMenuItem.Enabled)
            {
                return;
            }
            _busy = true;
            UpdateActions();
            Announce(UiLocalization.Text("AuthenticationCenter.Status.Applying", "Checking active work and applying the global configuration…"));
            try
            {
                var result = await _apply(provider, _lifetime.Token);
                if (IsDisposed)
                {
                    return;
                }
                if (!result.GlobalApplied)
                {
                    throw new AuthenticationCenterException("operation_unknown");
                }
                _reconnectPending = result.GlobalApplied && !result.Reconnected;
                _busy = false;
                await RefreshAsync(provider.Id);
                if (!IsDisposed)
                {
                    if (result.Reconnected)
                    {
                        Announce(UiLocalization.Text("AuthenticationCenter.Status.Applied", "Global configuration applied and MyCodex reconnected. Existing threads may require a compatible model or a new conversation."));
                        // The one spoken result here: the switch finishes away from the focused control.
                        _screenReader?.Speak(string.Format(UiLocalization.Text("AuthenticationCenter.Speech.Enabled", "{0} enabled."), provider.Name), true, false, true);
                    }
                    else
                    {
                        AnnounceError(UiLocalization.Text("AuthenticationCenter.Status.ReconnectFailed", "Global configuration is applied, but MyCodex could not reconnect. Retry the connection; the global change has not been rolled back."));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) Announce(UiLocalization.Text("AuthenticationCenter.Status.EnableCancelled", "Enable cancelled. Saved configurations were retained and the current configuration was not replaced."));
            }
            catch (Exception error)
            {
                if (!IsDisposed)
                {
                    if (error is AuthenticationCenterException failure && failure.Code == "operation_unknown")
                        _reconnectPending = true;
                    AnnounceError(AuthenticationCenterUiText.Error(error));
                }
            }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    UpdateActions();
                }
            }
        }

        internal async Task RetryConnectionAsync()
        {
            if (_busy || !_reconnectPending || _reconnect == null)
            {
                return;
            }
            _busy = true;
            UpdateActions();
            try
            {
                var connected = await _reconnect(_lifetime.Token);
                if (IsDisposed)
                {
                    return;
                }
                _reconnectPending = !connected;
                RefreshOfficialIdentity();
                if (connected) Announce(UiLocalization.Text("AuthenticationCenter.Status.Reconnected", "MyCodex reconnected. No provider configuration was reapplied."));
                else AnnounceError(UiLocalization.Text("AuthenticationCenter.Status.ReconnectFailed", "Global configuration is applied, but MyCodex could not reconnect. Retry the connection; the global change has not been rolled back."));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (!IsDisposed)
                {
                    AnnounceError(AuthenticationCenterUiText.Error(error));
                }
            }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    UpdateActions();
                }
            }
        }

        private void RefreshOfficialIdentity()
        {
            var identity = string.IsNullOrEmpty(_liveState) ? _officialIdentity?.Invoke() : _liveAccountLabel;
            if (string.IsNullOrEmpty(identity))
            {
                identity = _liveState == "current" || _liveState == "signed_out"
                    ? UiLocalization.Text("AuthenticationCenter.Identity.None", "No ChatGPT account reported")
                    : UiLocalization.Text("AuthenticationCenter.Identity.Unknown", "Unknown");
            }
            _identityLabel.Text = string.Format(UiLocalization.Text("AuthenticationCenter.Identity.Template", "Official identity: {0}"),
                identity);
        }

        private void Announce(string text)
        {
            _statusLabel.Text = text;
        }

        private void AnnounceError(string text)
        {
            Announce(text);
            AuthenticationCenterUiText.ShowError(this, text);
        }

        // A refresh follows most operations, so a loading problem is shown once until it changes or the user refreshes.
        private void ShowLoadError(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                _shownLoadError = null;
                return;
            }
            if (string.Equals(text, _shownLoadError, StringComparison.Ordinal)) return;
            _shownLoadError = text;
            AuthenticationCenterUiText.ShowError(this, text);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _lifetime.Cancel();
                _lifetime.Dispose();
                _providerContextMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
