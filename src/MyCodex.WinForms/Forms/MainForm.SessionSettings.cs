using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Infrastructure.Markets;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private MenuItem _settingsMenuItem;
        private MenuItem _defaultModelMenuItem;
        private MenuItem _defaultPermissionMenuItem;
        private MenuItem _optionsMenuItem;
        private MenuItem _appServerRuntimeSettingsMenuItem;
        private MenuItem _extensionsMenuItem;
        private MenuItem _mcpManagementMenuItem;
        private MenuItem _skillsManagementMenuItem;
        private MenuItem _pluginManagementMenuItem;
        private MenuItem _remoteServiceMenuItem;
        private WslAppServerSettingsForm _appServerRuntimeSettingsForm;
        private MenuItem _sessionModelMenuItem;
        private MenuItem _sessionPermissionMenuItem;
        private MenuItem _sessionResetSettingsMenuItem;
        private AppServerConfigSnapshot _globalServerConfigSnapshot;
        private AppServerConfigRequirementsSnapshot _globalConfigRequirementsSnapshot;

        private void ConfigureSessionSettingsUi()
        {
            _settingsMenuItem = new MenuItem(UiLocalization.Text("Menu.Settings", "&Settings"));
            _defaultModelMenuItem = new MenuItem();
            _defaultPermissionMenuItem = new MenuItem();
            _optionsMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.Options", "&Options..."));
            _appServerRuntimeSettingsMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.AppServerRuntime", "App Server Runtime..."));
            _extensionsMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.Extensions", "E&xtensions"));
            _mcpManagementMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.McpManagement", "MCP Management..."));
            _skillsManagementMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.SkillsManagement", "Skills Management..."));
            _pluginManagementMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.PluginManagement", "Plugin Management..."));
            _remoteServiceMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.RemoteService", "Phone Connection(&R)..."));
            _sessionModelMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.SessionModel", "Model"));
            _sessionPermissionMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.SessionPermission", "Permission"));
            _sessionResetSettingsMenuItem = new MenuItem(UiLocalization.Text("Settings.Menu.SessionReset", "&Reset to Defaults"));

            _defaultModelMenuItem.Click += DefaultModelMenuItem_Click;
            _defaultPermissionMenuItem.Click += DefaultPermissionMenuItem_Click;
            _optionsMenuItem.Click += OptionsMenuItem_Click;
            _appServerRuntimeSettingsMenuItem.Click += AppServerRuntimeSettingsMenuItem_Click;
            _mcpManagementMenuItem.Click += McpManagementMenuItem_Click;
            _skillsManagementMenuItem.Click += SkillsManagementMenuItem_Click;
            _pluginManagementMenuItem.Click += PluginManagementMenuItem_Click;
            _remoteServiceMenuItem.Click += RemoteServiceMenuItem_Click;
            _sessionModelMenuItem.Click += SessionModelMenuItem_Click;
            _sessionPermissionMenuItem.Click += SessionPermissionMenuItem_Click;
            _sessionResetSettingsMenuItem.Click += SessionResetSettingsMenuItem_Click;
            _settingsMenuItem.Popup += SettingsMenuItem_Popup;
            _sessionContextMenu.Popup += SessionSettingsContextMenu_Popup;

            AssembleSettingsAndMainMenu();

            _sessionContextMenu.MenuItems.Add(3, _sessionModelMenuItem);
            _sessionContextMenu.MenuItems.Add(4, _sessionPermissionMenuItem);
            _sessionContextMenu.MenuItems.Add(5, _sessionResetSettingsMenuItem);
            _sessionContextMenu.MenuItems.Add(6, new MenuItem("-"));

            ApplySessionSettingsLocalizedText();
            UpdateSessionSettingsMenuText();
        }

        private void AssembleSettingsAndMainMenu()
        {
            _settingsMenuItem.MenuItems.Clear();
            _settingsMenuItem.MenuItems.Add(_defaultModelMenuItem);
            _settingsMenuItem.MenuItems.Add(_defaultPermissionMenuItem);
            _settingsMenuItem.MenuItems.Add(_optionsMenuItem);
            if (_targetsMenuItem == null)
            {
                _settingsMenuItem.MenuItems.Add(_appServerRuntimeSettingsMenuItem);
            }
            else
            {
                _settingsMenuItem.MenuItems.Add(_targetsMenuItem);
            }
            _extensionsMenuItem.MenuItems.Clear();
            _extensionsMenuItem.MenuItems.Add(_pluginManagementMenuItem);
            _extensionsMenuItem.MenuItems.Add(_skillsManagementMenuItem);
            _extensionsMenuItem.MenuItems.Add(_mcpManagementMenuItem);
            _settingsMenuItem.MenuItems.Add(_extensionsMenuItem);
            _settingsMenuItem.MenuItems.Add(_remoteServiceMenuItem);

            _mainMenu.MenuItems.Clear();
            _mainMenu.MenuItems.AddRange(new[]
            {
                _fileMenuItem, _settingsMenuItem, _gitMenuItem, _accountMenuItem, _helpMenuItem
            });
        }

        private void ApplySessionSettingsLocalizedText()
        {
            if (_settingsMenuItem != null)
            {
                _settingsMenuItem.Text = UiLocalization.Text("Menu.Settings", "&Settings");
            }

            if (_extensionsMenuItem != null)
            {
                _extensionsMenuItem.Text = UiLocalization.Text("Settings.Menu.Extensions", "E&xtensions");
            }

            if (_optionsMenuItem != null)
            {
                _optionsMenuItem.Text = UiLocalization.Text("Settings.Menu.Options", "&Options...");
            }

            if (_appServerRuntimeSettingsMenuItem != null)
            {
                _appServerRuntimeSettingsMenuItem.Text = UiLocalization.Text("Settings.Menu.AppServerRuntime", "App Server Runtime...");
            }

            if (_mcpManagementMenuItem != null)
            {
                _mcpManagementMenuItem.Text = UiLocalization.Text("Settings.Menu.McpManagement", "MCP Management...");
            }

            if (_skillsManagementMenuItem != null)
            {
                _skillsManagementMenuItem.Text = UiLocalization.Text("Settings.Menu.SkillsManagement", "Skills Management...");
            }

            if (_pluginManagementMenuItem != null)
            {
                _pluginManagementMenuItem.Text = UiLocalization.Text("Settings.Menu.PluginManagement", "Plugin Management...");
            }

            if (_remoteServiceMenuItem != null)
            {
                _remoteServiceMenuItem.Text = UiLocalization.Text("Settings.Menu.RemoteService", "Phone Connection(&R)...");
            }

            if (_sessionResetSettingsMenuItem != null)
            {
                _sessionResetSettingsMenuItem.Text = UiLocalization.Text("Settings.Menu.SessionReset", "&Reset to Defaults");
            }

            if (_sessionModelMenuItem != null)
            {
                _sessionModelMenuItem.Text = UiLocalization.Text("Settings.Menu.SessionModel", "Model");
            }

            if (_sessionPermissionMenuItem != null)
            {
                _sessionPermissionMenuItem.Text = UiLocalization.Text("Settings.Menu.SessionPermission", "Permission");
            }
            ApplyTargetMenuText();
        }

        internal async Task RefreshOfficialConfigStateAfterConnectAsync()
        {
            await RefreshGlobalServerConfigAsync(false);
            if (!await WaitForTargetExitDecisionAsync()) return;
            await PromptForStartupFastModeIfNeededAsync();
            if (!await WaitForTargetExitDecisionAsync()) return;
            await RefreshKnownUnixWorkspaceGitInfoAsync();
        }

        private void SettingsMenuItem_Popup(object sender, EventArgs e)
        {
            var state = MainFormAuthenticationStatePlanner.BuildSettingsMenuState(
                _currentInteractionMode == AppInteractionMode.Office,
                IsCurrentWorkspaceAvailable());
            _appServerRuntimeSettingsMenuItem.Enabled = state.AppServerRuntimeSettingsEnabled;
            _appServerRuntimeSettingsMenuItem.Visible = state.AppServerRuntimeSettingsVisible;
            _optionsMenuItem.Enabled = state.OptionsEnabled;
            _mcpManagementMenuItem.Enabled = state.McpManagementEnabled;
            _skillsManagementMenuItem.Enabled = state.SkillsManagementEnabled;
            _pluginManagementMenuItem.Enabled = state.PluginManagementEnabled;
            UpdateSessionSettingsMenuText();
        }

        private async void DefaultModelMenuItem_Click(object sender, EventArgs e)
        {
            await OpenModelSettingsAsync(null, true);
        }

        private async void DefaultPermissionMenuItem_Click(object sender, EventArgs e)
        {
            await OpenPermissionSettingsAsync(null, true);
        }

        private async void OptionsMenuItem_Click(object sender, EventArgs e)
        {
            await OpenOptionsAsync();
        }

        private async void AppServerRuntimeSettingsMenuItem_Click(object sender, EventArgs e)
        {
            await OpenAppServerRuntimeSettingsAsync();
        }

        private async void ExternalAgentConfigImportMenuItem_Click(object sender, EventArgs e)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.ImportSync.ExternalAgentConfigImport", "Import External Agent Config(&C)...")))
            {
                return;
            }

            if (!AppServerProtocolFeatureSupport.IsSupported(
                    _appServerClient.ProtocolCapabilities,
                    AppServerProtocolFeature.ExternalAgentConfigImport))
            {
                var message = UiLocalization.Text(
                    "MainForm.Status.ExternalAgentConfigImportUnavailable",
                    "External agent config import is unavailable for the connected App Server runtime.");
                SetStatus(message);
                MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var form = new ExternalAgentConfigImportForm(_appServerClient, GetImportSyncWorkspacePath(sender)))
            {
                if (form.ShowDialog(this) != DialogResult.OK || !form.ImportCompleted)
                {
                    return;
                }
            }

            await RefreshAfterExternalAgentConfigImportAsync();
        }

        private async void McpManagementMenuItem_Click(object sender, EventArgs e)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.SessionSettings.Action.OpenMcpManagement", "Open MCP Management")))
            {
                return;
            }

            var runtimeSource = _appConfigSnapshot?.CodexRuntimeSource;
            var nativeClient = AuthenticationCenterClient.CreateMcpManagementClient(runtimeSource,
                string.Equals(runtimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase) ? GetCurrentWslConfig() : null,
                CodexRuntimeSources.IsRemoteUnixSsh(runtimeSource) ? GetCurrentRemoteMacConfig() : null);
            var nativeMcp = nativeClient == null ? null : new AuthenticationMcpCoordinator(
                nativeClient, _appServerClient, AcquireAuthenticationTransition);
            using (var form = new McpManagementForm(
                _appServerClient,
                _screenReaderService,
                _mcpReauthenticationState,
                nativeMcp))
            {
                form.ShowDialog(this);
            }
        }

        private async void SkillsManagementMenuItem_Click(object sender, EventArgs e)
        {
            if (!IsCurrentWorkspaceAvailable() || _currentWorkspace == null || string.IsNullOrWhiteSpace(_currentWorkspace.RootPath))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.SessionSettings.Message.OpenWorkspaceBeforeSkills", "Please open an available workspace before managing skills."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.SessionSettings.Action.OpenSkillsManagement", "Open Skills Management")))
            {
                return;
            }

            using (var form = new SkillsManagementForm(
                _appServerClient,
                _currentWorkspace.RootPath))
            {
                form.ShowDialog(this);
            }
        }

        private async void PluginManagementMenuItem_Click(object sender, EventArgs e)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.SessionSettings.Action.OpenPluginManagement", "Open Plugin Management")))
            {
                return;
            }

            using (var form = new PluginManagementForm(
                _appServerClient,
                _pluginMarketCatalogClient,
                _screenReaderService))
            {
                form.ShowDialog(this);
            }
        }

        private void RemoteServiceMenuItem_Click(object sender, EventArgs e)
        {
            SetStatus(UiLocalization.Text("MainForm.SessionSettings.Action.OpenRemoteService", "Open Phone Connection"));
            OpenRemoteServiceManagement();
        }

        private void OpenRemoteServiceManagement()
        {
            var configStore = new JsonRemoteServiceConfigStore(_appConfigService);
            PrepareOwnedRemoteDisplayName(configStore, configStore.Load());
            var relayClient = new RelayHostClient();
            var pairingWorkflow = new SecureRemotePairingWorkflow(
                relayClient,
                _remoteIdentityStore,
                _remoteBindingStore);
            using (var form = new RemoteServiceManagementForm(
                configStore,
                relayClient,
                _screenReaderService,
                _remoteHostRealtimeClient,
                false,
                _embeddedRelayService,
                _remoteIdentityStore,
                pairingWorkflow,
                _remoteBindingStore,
                new EmbeddedRelayFirewallService(),
                cancellationToken => _remoteHostServiceController.ApplyConfiguredModeAsync(cancellationToken),
                targetOnly: true, showConnectionSettings: ShowSharedRemoteServiceSettings))
            {
                form.ShowDialog(this);
            }

            SetStatus(UiLocalization.Text("MainForm.Status.RemoteServiceOpened", "Phone Connection opened."));
            this.BeginInvokeSafe(async () => await RestartRemoteHostServiceAsync().ConfigureAwait(true));
        }

        private IRelayHostRealtimeClient CreateRemoteHostRealtimeClient()
        {
            var snapshotCache = new RemoteConversationSnapshotCache();
            var messageReader = new AppServerCodingRemoteThreadMessageReader(_appStateStore, _appServerClient, AppInteractionMode.Coding, snapshotCache);
            var officeMessageReader = new AppServerCodingRemoteThreadMessageReader(_appStateStore, _appServerClient, AppInteractionMode.Office, snapshotCache);
            var mutationSink = new WinFormsRemoteHostMutationSink(this, HandleRemoteHostMutation);
            var messageSender = new AppServerCodingRemoteMessageSender(
                _appStateStore,
                _sessionThreadService,
                GetEffectiveApprovalMode,
                GetEffectiveModelId,
                GetEffectiveReasoningEffort,
                BuildCollaborationModeForSend,
                BuildThreadToolContext,
                mutationSink,
                AppInteractionMode.Coding,
                TryDispatchRemoteActiveTurnMessageAsync);
            var officeMessageSender = new AppServerCodingRemoteMessageSender(
                _appStateStore,
                _sessionThreadService,
                GetEffectiveApprovalMode,
                GetEffectiveModelId,
                GetEffectiveReasoningEffort,
                BuildCollaborationModeForSend,
                BuildThreadToolContext,
                mutationSink,
                AppInteractionMode.Office,
                TryDispatchRemoteActiveTurnMessageAsync);
            var workspaceOperator = new AppServerCodingRemoteWorkspaceOperator(_appStateStore, _appServerClient);
            var sessionOperator = new AppServerCodingRemoteSessionOperator(_appStateStore, _appServerClient);
            var officeSessionOperator = new AppServerCodingRemoteSessionOperator(_appStateStore, _appServerClient, AppInteractionMode.Office);
            var sessionCreator = new AppServerCodingRemoteSessionCreator(_appStateStore);
            var officeSessionCreator = new AppServerCodingRemoteSessionCreator(_appStateStore, AppInteractionMode.Office);
            var sessionThreadOperator = new AppServerCodingRemoteSessionThreadOperator(_appStateStore, _appServerClient);
            var sessionStopper = new AppServerCodingRemoteSessionStopper(_appStateStore, _appServerClient, CanInterruptThread, GetInterruptibleTurnId);
            var officeSessionStopper = new AppServerCodingRemoteSessionStopper(_appStateStore, _appServerClient, CanInterruptThread, GetInterruptibleTurnId, AppInteractionMode.Office);
            var interactionResponder = new AppServerCodingRemoteInteractionResponder(_appServerClient);
            var attachmentCatalogOperator = new AppServerCodingRemoteAttachmentCatalogOperator(
                _appStateStore,
                _appServerClient);
            var officeAttachmentCatalogOperator = new AppServerCodingRemoteAttachmentCatalogOperator(
                _appStateStore,
                _appServerClient,
                AppInteractionMode.Office);
            var sessionSettingsOperator = new AppServerCodingRemoteSessionSettingsOperator(
                _appStateStore,
                GetRemoteAvailableModelsAsync,
                GetConfiguredDefaultModelId,
                GetConfiguredDefaultReasoningEffort,
                GetConfiguredDefaultApprovalMode,
                GetEffectiveModelId,
                GetEffectiveReasoningEffort,
                GetEffectiveApprovalMode,
                SupportsReasoningEffortForModel);
            var officeSessionSettingsOperator = new AppServerCodingRemoteSessionSettingsOperator(
                _appStateStore,
                GetRemoteAvailableModelsAsync,
                GetConfiguredDefaultModelId,
                GetConfiguredDefaultReasoningEffort,
                GetConfiguredDefaultApprovalMode,
                GetEffectiveModelId,
                GetEffectiveReasoningEffort,
                GetEffectiveApprovalMode,
                SupportsReasoningEffortForModel,
                AppInteractionMode.Office);
            var codingRemoteService = new CodingRemoteService(_appStateStore, messageReader, messageSender, workspaceOperator, sessionOperator, sessionSettingsOperator, sessionCreator, sessionThreadOperator, sessionStopper, interactionResponder, attachmentCatalogOperator);
            var officeRemoteService = CodingRemoteService.CreateOffice(
                _appStateStore,
                _appConfigStore,
                _officeWorkspaceService,
                officeMessageReader,
                officeMessageSender,
                officeSessionOperator,
                officeSessionSettingsOperator,
                officeSessionCreator,
                officeSessionStopper,
                interactionResponder,
                officeAttachmentCatalogOperator,
                modeSettingsLoader: config => _modeSettingsStore.LoadForCurrentRuntimeAsync(config));
            var planExecutionResponder = new WinFormsCodingRemotePlanExecutionResponder(RespondRemotePlanExecutionAsync);
            var dispatcher = new RemoteCommandDispatcher(
                codingRemoteService,
                officeRemoteService,
                new RemoteWriteCoordinator(),
                mutationSink,
                planExecutionResponder,
                BuildHostSettingsOperator(),
                _appServerClient,
                pendingInteractions: _pendingRemoteInteractions.ListPending);
            _remoteCommandDispatcher = dispatcher;
            var realtimeClient = new RelayHostRealtimeClient(
                dispatcher,
                new SecureRelayAuthenticator(
                    new HttpRelayTransport(),
                    _remoteIdentityStore),
                _remoteBindingStore,
                snapshotCache);
            _remoteAppServerEventForwarder = new RemoteAppServerEventForwarder(
                FindSessionByThreadId,
                realtimeClient,
                WinFormsCodingRemoteEventMapper.TryMap,
                snapshotCache,
                pendingInteractions: _pendingRemoteInteractions);
            return realtimeClient;
        }

        private async Task StartRemoteHostServiceAsync()
        {
            if (_remoteHostServiceController == null)
            {
                return;
            }

            try
            {
                await _remoteHostServiceController.StartIfConfiguredAsync(_targetShutdownCancellation.Token).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppendLog("Remote host channel start failed: " + RelaySecretRedactor.Redact(ex.Message));
            }
        }

        private async Task RestartRemoteHostServiceAsync()
        {
            if (_remoteHostServiceController == null)
            {
                return;
            }

            try
            {
                await _remoteHostServiceController.RestartIfConfiguredAsync(System.Threading.CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppendLog("Remote host channel restart failed: " + RelaySecretRedactor.Redact(ex.Message));
            }
        }

        private void DisposeRemoteHostService()
        {
            if (_remoteHostServiceController != null)
            {
                try
                {
                    using (var cancellation = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3)))
                    {
                        // Run off the UI thread so a continuation that captures the UI context cannot deadlock exit.
                        var controller = _remoteHostServiceController;
                        var token = cancellation.Token;
                        Task.Run(() => controller.ShutdownAsync(token)).GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("Remote service shutdown failed: " + RelaySecretRedactor.Redact(ex.Message));
                }

                _remoteHostServiceController.Dispose();
            }
        }

        private async Task RefreshAfterExternalAgentConfigImportAsync()
        {
            var message = UiLocalization.Text(
                "MainForm.Status.ExternalAgentConfigImportCompleted",
                "External agent configuration imported");
            SetStatus(message);
            AddChatHistoryItem(GetStatusSenderText(), message, ChatHistoryCategory.FileChange);
            _globalServerConfigSnapshot = null;
            _globalConfigRequirementsSnapshot = null;
            await RefreshGlobalServerConfigAsync(false);
            await RunAutomaticThreadSyncAsync();
        }

        private string GetCurrentWorkspacePathOrEmpty()
        {
            return _currentWorkspace == null || string.IsNullOrWhiteSpace(_currentWorkspace.RootPath)
                ? string.Empty
                : _currentWorkspace.RootPath;
        }

        private string GetImportSyncWorkspacePath(object sender)
        {
            if (ReferenceEquals(sender, _workspaceExternalAgentConfigImportMenuItem))
            {
                var selectedItem = GetSelectedWorkspaceListItem();
                if (selectedItem != null &&
                    selectedItem.Workspace != null &&
                    !string.IsNullOrWhiteSpace(selectedItem.Workspace.RootPath))
                {
                    return selectedItem.Workspace.RootPath;
                }
            }

            return GetCurrentWorkspacePathOrEmpty();
        }

        private Task OpenAppServerFilesForCurrentWorkspaceAsync()
        {
            return ShowAppServerFilesAsync(GetCurrentWorkspacePathOrEmpty(), true);
        }

        private Task OpenAppServerFilesFromDefaultRootAsync()
        {
            return ShowAppServerFilesAsync(GetDefaultAppServerFileBrowserPath(), false);
        }

        private async Task ShowAppServerFilesForWorkspaceAsync(string workspacePath)
        {
            await ShowAppServerFilesAsync(workspacePath, true);
        }

        private async Task ShowAppServerFilesAsync(string initialPath, bool requirePath)
        {
            if (_isRuntimeWindowTransitionInProgress)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Workspace.AppServerFiles", "File Browser(&F)...")))
            {
                return;
            }

            if (_isRuntimeWindowTransitionInProgress)
            {
                return;
            }

            if (!AppServerProtocolFeatureSupport.IsSupported(
                    _appServerClient.ProtocolCapabilities,
                    AppServerProtocolFeature.FileSystem))
            {
                var message = UiLocalization.Text(
                    "MainForm.Status.AppServerFilesUnavailable",
                    "App Server file browsing is unavailable for the connected App Server runtime.");
                SetStatus(message);
                MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (requirePath && string.IsNullOrWhiteSpace(initialPath))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.SessionSettings.Message.OpenWorkspaceBeforeAppServerFiles", "Please open an available workspace before browsing App Server files."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _appServerFileWindowManager.OpenBrowser(
                string.IsNullOrWhiteSpace(initialPath) ? string.Empty : initialPath.Trim());
        }

        private string GetDefaultAppServerFileBrowserPath()
        {
            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                var officeWorkspace = EnsureOfficeWorkspace();
                return officeWorkspace == null
                    ? _resolvedOfficeRootPath ?? string.Empty
                    : officeWorkspace.RootPath;
            }

            if (IsRemoteMacRuntimeEnabled())
            {
                var userName = GetCurrentRemoteMacConfig().UserName;
                return string.IsNullOrWhiteSpace(userName)
                    ? "/Users"
                    : "/Users/" + userName.Trim();
            }

            if (IsWslRuntimeEnabled())
            {
                return "/home";
            }

            return string.Empty;
        }

        private async void SessionModelMenuItem_Click(object sender, EventArgs e)
        {
            var session = GetSelectedSessionForSettings();
            if (session == null)
            {
                return;
            }

            await OpenModelSettingsAsync(session, false);
        }

        private async void SessionPermissionMenuItem_Click(object sender, EventArgs e)
        {
            var session = GetSelectedSessionForSettings();
            if (session == null)
            {
                return;
            }

            await OpenPermissionSettingsAsync(session, false);
        }





        private async Task<AppServerPermissionProfileListResult> ReadPermissionProfilesForOptionsAsync(AppServerConfigSnapshot officialSnapshot)
        {
            if (officialSnapshot == null || _appServerClient == null)
            {
                return null;
            }

            try
            {
                return await _appServerClient.ListPermissionProfilesAsync(
                    GetCurrentWorkspacePathOrEmpty(),
                    100,
                    null)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to read App Server permission profiles: {0}", ex.Message);
                return new AppServerPermissionProfileListResult();
            }
        }

        private async Task RestartAppServerAfterOfficeCliEnvironmentChangeAsync()
        {
            try
            {
                if (!await EnsurePendingBundledRuntimeUpdateAsync()) return;
                SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.OfficeCliRestarting", "Restarting Codex App Server for OfficeCLI changes..."));
                await _appServerClient.RestartAsync();
                SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.OfficeCliRestarted", "Codex App Server restarted for OfficeCLI changes"));
            }
            catch (Exception ex)
            {
                AddChatHistoryItem(GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.OfficeCliRestartFailed", "Failed to restart Codex App Server after OfficeCLI changes"));
            }
        }

        private static string NormalizeOptionalText(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private async Task EnsureAvailableModelsLoadedAsync(WorkspaceInfo workspace = null)
        {
            workspace = workspace ?? _currentWorkspace;
            await RefreshModelConfigurationAsync(workspace != null ? workspace.RootPath : null, false);
        }

        private async Task<IReadOnlyList<AppServerModelOption>> GetRemoteAvailableModelsAsync(System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshModelConfigurationAsync(null, true);
            cancellationToken.ThrowIfCancellationRequested();
            return MainFormModelOptionComposer.BuildOrderedModelOptions(_availableModels).ToList();
        }

        private void SessionSettingsContextMenu_Popup(object sender, EventArgs e)
        {
            var session = GetSelectedSessionForSettings();
            var state = MainFormAuthenticationStatePlanner.BuildSessionSettingsContextMenuState(
                session != null,
                HasModelOverride(session),
                HasApprovalOverride(session),
                HasPlanModeOverride(session));
            _sessionModelMenuItem.Enabled = state.SessionModelEnabled;
            _sessionPermissionMenuItem.Enabled = state.SessionPermissionEnabled;
            _sessionResetSettingsMenuItem.Enabled = state.SessionResetSettingsEnabled;
            UpdateSessionSettingsMenuText();
        }

        private ChatSession GetSelectedSessionForSettings()
        {
            var item = GetSelectedSessionListItem();
            return item != null ? item.Session : null;
        }

        private void SessionResetSettingsMenuItem_Click(object sender, EventArgs e)
        {
            var session = GetSelectedSessionForSettings();
            if (session == null)
            {
                return;
            }

            var wasPlanModeEnabled = session.IsPlanModeEnabled;
            session.ModelId = null;
            session.ReasoningEffort = null;
            session.ApprovalMode = null;
            session.IsPlanModeEnabled = false;
            if (!wasPlanModeEnabled || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                _sessionsPendingDefaultCollaborationMode.Remove(session.Id);
            }
            else
            {
                _sessionsPendingDefaultCollaborationMode.Add(session.Id);
            }
            PersistState();
            RefreshSessionSettingsState();

            if (_currentSession != null && string.Equals(_currentSession.Id, session.Id, StringComparison.OrdinalIgnoreCase))
            {
                AddChatHistoryItem(
                    GetStatusSenderText(),
                    UiLocalization.Text("MainForm.SessionSettings.Chat.SessionResetToDefault", "Current session restored to follow default config."),
                    ChatHistoryCategory.System);
            }

            SetStatus(UiLocalization.Text("MainForm.SessionSettings.Status.SessionResetToDefault", "Session restored to follow default config"));
        }

        private void RefreshSessionSettingsState()
        {
            RefreshCommandState();
            RefreshSessionList();
            RefreshHeader();
            UpdateSessionSettingsMenuText();
        }

        private ApprovalMode GetEffectiveApprovalMode(ChatSession session)
        {
            if (session != null && session.ApprovalMode.HasValue)
            {
                return session.ApprovalMode.Value;
            }

            return GetConfiguredDefaultApprovalMode();
        }

        private string GetEffectiveModelId(ChatSession session)
        {
            return ResolveSessionModelSettings(session).ModelId;
        }

        private string GetEffectiveReasoningEffort(ChatSession session)
        {
            return ResolveSessionModelSettings(session).ReasoningEffort;
        }

        private bool IsExplicitSessionModelUnavailable(ChatSession session)
        {
            return session != null &&
                IsAvailableModelsScopeCurrent() &&
                !string.Equals(session.ModelId, ResolveSessionModelSettings(new ChatSession { WorkspaceId = session.WorkspaceId }).ModelId, StringComparison.OrdinalIgnoreCase) &&
                !SessionDefaultsService.IsExplicitConversationModelAvailable(session.ModelId, _availableModels);
        }

        private bool BlockSendForUnavailableSessionModel(ChatSession session)
        {
            if (!IsExplicitSessionModelUnavailable(session))
            {
                return false;
            }

            var message = UiLocalization.Text(
                "MainForm.Status.SessionModelUnavailable",
                "The selected model is unavailable. Select a model before sending.");
            AddChatHistoryItem(GetErrorSenderText(), message, ChatHistoryCategory.Error);
            SetHighPriorityStatus(message);
            SpeakAuxiliaryTextWithScreenReader(message);
            return true;
        }

        private static ApprovalMode GetBuiltInDefaultApprovalMode()
        {
            return SessionDefaultsService.BuiltInDefaultApprovalMode;
        }

        private string GetConfiguredDefaultModelId()
        {
            return ResolveSessionDefaults().ModelId;
        }

        private string GetConfiguredDefaultReasoningEffort()
        {
            return ResolveSessionDefaults().ReasoningEffort;
        }

        private ApprovalMode GetConfiguredDefaultApprovalMode()
        {
            return ResolveSessionDefaults().ApprovalMode;
        }

        private void SetConfiguredDefaultApprovalMode(ApprovalMode approvalMode)
        {
            _sessionDefaultsService.SetApprovalMode(_appConfigSnapshot, approvalMode);
        }

        private bool SupportsReasoningEffortForModel(string modelId)
        {
            return _availableModels.Any(model =>
                model != null &&
                string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase) &&
                model.ReasoningEfforts.Count > 0);
        }

        private SessionDefaultsResolution ResolveSessionDefaults()
        {
            return _sessionDefaultsService.Resolve(
                _appConfigSnapshot,
                _availableModels,
                _globalConfigRequirementsSnapshot,
                _globalServerConfigSnapshot);
        }

        private bool HasApprovalOverride(ChatSession session)
        {
            return session != null && session.ApprovalMode.HasValue;
        }

        private bool HasModelOverride(ChatSession session)
        {
            return session != null && !string.IsNullOrWhiteSpace(session.ModelId);
        }

        private bool HasPlanModeOverride(ChatSession session)
        {
            return session != null && session.IsPlanModeEnabled;
        }

        private void UpdateSessionSettingsMenuText()
        {
            var session = GetSelectedSessionForSettings();
            var text = MainFormAuthenticationStatePlanner.BuildSessionSettingsMenuText(
                UiLocalization.Text("MainForm.SessionSettings.Menu.DefaultModel.Template", "Default Model: {0}"),
                BuildDefaultModelSummaryText(),
                UiLocalization.Text("MainForm.SessionSettings.Menu.DefaultPermission.Template", "Default Permission: {0}"),
                BuildDefaultPermissionSummaryText(),
                session != null,
                UiLocalization.Text("Settings.Menu.SessionModel", "Model"),
                UiLocalization.Text("MainForm.SessionSettings.Menu.SessionModel.Template", "Model: {0}"),
                session != null ? BuildSessionModelMenuSummary(session) : null,
                UiLocalization.Text("Settings.Menu.SessionPermission", "Permission"),
                UiLocalization.Text("MainForm.SessionSettings.Menu.SessionPermission.Template", "Permission: {0}"),
                session != null ? BuildSessionPermissionMenuSummary(session) : null);

            if (_defaultModelMenuItem != null)
            {
                _defaultModelMenuItem.Text = text.DefaultModelText;
            }

            if (_defaultPermissionMenuItem != null)
            {
                _defaultPermissionMenuItem.Text = text.DefaultPermissionText;
            }

            if (_sessionModelMenuItem != null)
            {
                _sessionModelMenuItem.Text = text.SessionModelText;
            }

            if (_sessionPermissionMenuItem != null)
            {
                _sessionPermissionMenuItem.Text = text.SessionPermissionText;
            }
        }







        private string BuildModelSummaryText(string modelId, string reasoningEffort, string emptyText)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return emptyText;
            }

            var modelText = MainFormModelOptionComposer.GetModelVersionLabel(modelId, _availableModels);
            if (!SupportsReasoningEffortForModel(modelId) && string.IsNullOrWhiteSpace(reasoningEffort))
            {
                return modelText;
            }

            var effortText = MainFormModelOptionComposer.GetReasoningEffortText(reasoningEffort);
            return string.IsNullOrWhiteSpace(effortText)
                ? modelText
                : string.Format(
                    UiLocalization.Text("MainForm.SessionSettings.ModelSummary.Template", "{0} ({1})"),
                    modelText,
                    effortText);
        }

        private ChatSession FindSessionById(string sessionId)
        {
            return SessionCollectionQueries.FindSessionById(_sessions, sessionId);
        }

        private static string GetStatusSenderText()
        {
            return ChatHistoryItemFactory.GetStatusSenderText();
        }

        private static string GetErrorSenderText()
        {
            return ChatHistoryItemFactory.GetErrorSenderText();
        }
    }
}
