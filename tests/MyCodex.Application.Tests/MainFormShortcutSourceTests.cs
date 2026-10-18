using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormShortcutSourceTests
    {
        [Test]
        public void ProcessCmdKey_CtrlEOpensAppServerFileBrowser()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("keyData == (Keys.Control | Keys.E)"));
            Assert.That(source, Does.Contain("OpenAppServerFilesForCurrentWorkspaceAsync();"));
        }

        [Test]
        public void ProcessCmdKey_AltEOpensAppServerFileBrowserRoot()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs").Replace("\r\n", "\n");

            Assert.That(source, Does.Contain("keyData == (Keys.Alt | Keys.E)"));
            Assert.That(source, Does.Contain("OpenAppServerFilesFromDefaultRootAsync();"));
            Assert.That(source, Does.Not.Contain("if (keyData == (Keys.Alt | Keys.E))\n            {\n                ShowRuntimeFileExplorer();"));
        }

        [Test]
        public void ProcessCmdKey_CtrlTopRowDigitsSelectModeTabsOnlyWhenEnabled()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "protected override bool ProcessCmdKey");

            Assert.That(method, Does.Contain("if (Enabled && keyData == (Keys.Control | Keys.D1))"));
            Assert.That(method, Does.Contain("SelectInteractionModeTab(AppInteractionMode.Coding);"));
            Assert.That(method, Does.Contain("if (Enabled && keyData == (Keys.Control | Keys.D2))"));
            Assert.That(method, Does.Contain("SelectInteractionModeTab(AppInteractionMode.Office);"));
            Assert.That(method, Does.Not.Contain("Keys.Control | Keys.NumPad1"));
            Assert.That(method, Does.Not.Contain("Keys.Control | Keys.NumPad2"));
            Assert.That(method, Does.Contain("keyData == (Keys.Alt | Keys.D1) || keyData == (Keys.Alt | Keys.NumPad1)"));
            Assert.That(method, Does.Contain("keyData == (Keys.Alt | Keys.D2) || keyData == (Keys.Alt | Keys.NumPad2)"));
        }

        [Test]
        public void ProcessCmdKey_LeavesCtrlTabNavigationToTabControl()
        {
            var method = RepositorySource.ExtractUntilNextPrivateMember(RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs"), "protected override bool ProcessCmdKey");

            Assert.That(method, Does.Not.Contain("Keys.Control | Keys.Tab"));
            Assert.That(method, Does.Not.Contain("Keys.Control | Keys.Shift | Keys.Tab"));
            Assert.That(method, Does.Contain("return base.ProcessCmdKey(ref msg, keyData);"));
        }

        [Test]
        public void ProcessCmdKey_EscapeUsesWindowCloseSettingForMainWindow()
        {
            var method = RepositorySource.ExtractUntilNextPrivateMember(RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs"), "protected override bool ProcessCmdKey");

            Assert.That(method, Does.Contain("if (keyData == Keys.Escape)"));
            Assert.That(method, Does.Contain("if (_appConfigSnapshot == null || _appConfigSnapshot.CloseSessionWorkbenchOnEscape)"));
            Assert.That(method, Does.Contain("HideMainWindowToTray();"));
        }

        [Test]
        public void FileMenu_ExposesGlobalFileBrowserEntry()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_openGlobalFileBrowserMenuItem"));
            Assert.That(source, Does.Contain("MainMenu.OpenGlobalFileBrowser"));
            Assert.That(source, Does.Contain("_openGlobalFileBrowserMenuItem.Click += OnOpenGlobalFileBrowserMenuItemClick"));
            Assert.That(source, Does.Contain("_fileMenuItem.MenuItems.Add(_openGlobalFileBrowserMenuItem)"));
            Assert.That(source, Does.Contain("OpenAppServerFilesFromDefaultRootAsync();"));
            Assert.That(source, Does.Contain("Open Global File Browser(&E)..."));
        }

        [Test]
        public void FileMenu_ExposesWorkspaceAndSessionShortcuts()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_openWorkspaceFileMenuItem = new MenuItem"));
            Assert.That(source, Does.Contain("Shortcut = Shortcut.CtrlO"));
            Assert.That(source, Does.Contain("_newSessionFileMenuItem = new MenuItem"));
            Assert.That(source, Does.Contain("Shortcut = Shortcut.CtrlN"));
        }

        [Test]
        public void WorkspaceAndSessionLists_ExposePrimaryKeyboardActions()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var workspaceKeyDown = RepositorySource.ExtractUntilNextPrivateMember(source, "private async void WorkspaceList_KeyDown");
            var sessionKeyDown = RepositorySource.ExtractUntilNextPrivateMember(source, "private async void SessionList_KeyDown");

            Assert.That(workspaceKeyDown, Does.Contain("e.KeyCode == Keys.Delete"));
            Assert.That(workspaceKeyDown, Does.Contain("await DeleteSelectedWorkspaceAsync();"));
            Assert.That(sessionKeyDown, Does.Contain("e.KeyCode == Keys.Delete"));
            Assert.That(sessionKeyDown, Does.Contain("await DeleteSelectedSessionAsync();"));
            Assert.That(sessionKeyDown, Does.Contain("e.KeyCode == Keys.F2"));
            Assert.That(sessionKeyDown, Does.Contain("await RenameSelectedSessionAsync();"));
        }

        [Test]
        public void ContextMenus_ShowShortcutHintsForMirroredKeyboardActions()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            AssertMenuItemShortcut(source, "_workspaceNewMenuItem", "Shortcut.CtrlO");
            AssertMenuItemShortcut(source, "_workspaceAppServerFilesMenuItem", "Shortcut.CtrlE");
            AssertMenuItemShortcut(source, "_workspaceRemoveMenuItem", "Shortcut.Del");
            AssertMenuItemShortcut(source, "_sessionNewMenuItem", "Shortcut.CtrlN");
            AssertMenuItemShortcut(source, "_sessionRenameMenuItem", "Shortcut.F2");
            AssertMenuItemShortcut(source, "_sessionRemoveMenuItem", "Shortcut.Del");
            AssertMenuItemShortcut(source, "_removeSelectedAttachmentMenuItem", "Shortcut.Del");
        }

        [Test]
        public void FileMenu_DoesNotExposeLegacyRuntimeFileExplorer()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Not.Contain("_fileMenuItem.MenuItems.Add(_runtimeFileExplorerMenuItem)"));
            Assert.That(source, Does.Not.Contain("MainMenu.RuntimeFileExplorer"));
            Assert.That(source, Does.Not.Contain("RuntimeFileExplorerForm"));
            Assert.That(source, Does.Not.Contain("ShowRuntimeFileExplorer"));
        }

        [Test]
        public void AccountMenu_DoesNotExposeStandaloneOpenRouterOrGatewayEntries()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Not.Contain("_openRouterMenuItem"));
            Assert.That(source, Does.Not.Contain("_gatewayCenterMenuItem"));
            Assert.That(source, Does.Not.Contain("OnOpenRouterMenuItemClick"));
            Assert.That(source, Does.Not.Contain("OnGatewayCenterMenuItemClick"));
            Assert.That(source, Does.Not.Contain("_accountMenuItem.MenuItems.Add(_openRouterMenuItem)"));
            Assert.That(source, Does.Not.Contain("_accountMenuItem.MenuItems.Add(_gatewayCenterMenuItem)"));
        }

        [Test]
        public void AccountMenu_OpensBackendAuthenticationCenterWithoutLegacySourceSwitching()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("OnAuthenticationCenterMenuItemClick"));
            Assert.That(source, Does.Contain("new AuthenticationCenterForm("));
            Assert.That(source, Does.Not.Contain("AreEquivalentAuthenticationSourceSelections"));
            var authentication = RepositorySource.ReadSourceFile("MainForm.AuthenticationCenter.cs");
            Assert.That(authentication, Does.Contain("AuthenticationCenterClient.CreateLocal("));
            Assert.That(authentication, Does.Not.Contain("RestartAppServerForRuntimeSettingsAsync("));
        }

        [Test]
        public void ModelList_UsesOnlyAppServerObservations()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("new AppServerModelConfigurationService(_appServerClient)"));
            var configurationSource = RepositorySource.ReadSourceFile("AppServerModelConfigurationService.cs");
            Assert.That(configurationSource, Does.Contain("await _client.ListModelsAsync()"));
            Assert.That(source, Does.Not.Contain("GetCurrentBuiltInSourceModelIds()"));
            Assert.That(source, Does.Not.Contain("AddBuiltInSourceModels"));
            Assert.That(source, Does.Not.Contain("OpenRouterSourcePlanner.BuildEnabledModels"));
            Assert.That(source, Does.Not.Contain("ModelConversionSourcePlanner.BuildEnabledModels"));
        }

        [Test]
        public void AppServerConnection_DoesNotPrepareLegacyModelRelayForAnyTarget()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task<bool> EnsureAppServerConnectedAsync()");

            var gatewayReadyIndex = method.IndexOf("EnsureActiveGatewayRuntimeReadyForActionAsync", StringComparison.Ordinal);

            Assert.That(gatewayReadyIndex, Is.EqualTo(-1));
        }

        [Test]
        public void AppServerAvailableForAction_DoesNotRefreshLegacyAuthenticationSourceForAnyTarget()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task<bool> EnsureAppServerAvailableForActionAsync(string actionName)");

            var refreshIndex = method.IndexOf("await RefreshCurrentAuthenticationSourceSelectionAsync();", StringComparison.Ordinal);

            Assert.That(refreshIndex, Is.EqualTo(-1));
        }

        [Test]
        public void TargetReloadMenu_UsesTheExplicitAppServerRestartPath()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var handler = RepositorySource.ExtractUntilNextPrivateMember(source, "private async void OnTargetReloadMenuItemClick");
            var fileMenuPopup = RepositorySource.ExtractUntilNextPrivateMember(source, "private void FileMenuItem_Popup");

            Assert.That(source, Does.Contain("_targetReloadMenuItem"));
            Assert.That(source, Does.Contain("_targetReloadMenuItem.Click += OnTargetReloadMenuItemClick"));
            Assert.That(source, Does.Contain("_fileMenuItem.MenuItems.Add(_targetReloadMenuItem)"));
            Assert.That(fileMenuPopup, Does.Contain("var isWindowsTarget = IsCurrentExecutionTargetWindows();"));
            Assert.That(fileMenuPopup, Does.Contain("_targetReloadMenuItem.Visible = isWindowsTarget;"));
            Assert.That(fileMenuPopup, Does.Contain("_targetReloadMenuItem.Enabled = isWindowsTarget;"));
            Assert.That(handler, Does.Contain("if (!IsCurrentExecutionTargetWindows())"));
            Assert.That(handler, Does.Contain("MessageBoxDefaultButton.Button2"));
            Assert.That(handler, Does.Contain("await RestartAppServerForRuntimeSettingsAsync("));
        }

        [Test]
        public void AutomaticReconnect_DoesNotUseTheExplicitTargetReloadCommand()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var reconnect = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task<bool> TryRecoverAppServerConnectionAsync(");

            Assert.That(reconnect, Does.Not.Contain("RestartAppServerForRuntimeSettingsAsync"));
            Assert.That(reconnect, Does.Not.Contain("_appServerClient.RestartAsync"));
            Assert.That(reconnect, Does.Not.Contain("OnTargetReloadMenuItemClick"));
            Assert.That(reconnect, Does.Not.Contain("ReloadLocalStateForCurrentRuntimeProfileAsync"));
            Assert.That(reconnect, Does.Not.Contain("RefreshCurrentAuthenticationSourceSelectionAsync"));
            Assert.That(reconnect, Does.Not.Contain("FileSystemWatcher"));
            Assert.That(reconnect, Does.Not.Contain("Timer"));
            Assert.That(reconnect, Does.Not.Contain("Activated"));
            Assert.That(reconnect, Does.Contain("await EnsureAppServerConnectedAsync()"));
            Assert.That(reconnect, Does.Contain("await RefreshAuthenticationStateAsync(false)"));
            Assert.That(reconnect, Does.Contain("await LoadAvailableModelsAsync(false)"));
            Assert.That(reconnect, Does.Contain("await RefreshOfficialConfigStateAfterConnectAsync()"));
        }

        [Test]
        public void RuntimeProfileReload_LoadsTargetRuntimeStateWithoutAuthenticationSourceSelection()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task ReloadLocalStateForCurrentRuntimeProfileAsync(bool saveCurrentComposeState = true)");

            var reloadIndex = method.IndexOf("ReloadLocalStateForCurrentRuntimeProfile(saveCurrentComposeState);", StringComparison.Ordinal);

            Assert.That(reloadIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(method, Does.Not.Contain("RefreshCurrentAuthenticationSourceSelectionAsync"));
        }

        [Test]
        public void RuntimeSettingsSwitch_SavesOldRuntimeDraftBeforeChangingRuntimeAndReloadsWithoutResaving()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task OpenAppServerRuntimeSettingsAsync()");

            var saveIndex = method.IndexOf("SaveCurrentComposeState();", StringComparison.Ordinal);
            var persistIndex = method.IndexOf("PersistState();", StringComparison.Ordinal);
            var runtimeAssignmentIndex = method.IndexOf("_appConfigSnapshot.CodexRuntimeSource = form.SelectedRuntimeSource;", StringComparison.Ordinal);

            Assert.That(saveIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(persistIndex, Is.GreaterThan(saveIndex));
            Assert.That(runtimeAssignmentIndex, Is.GreaterThan(persistIndex));
            Assert.That(source, Does.Contain("ReloadLocalStateForCurrentRuntimeProfileAsync(false)"));
        }

        [Test]
        public void RuntimeTargetChange_IsMandatoryImmediateStopAndReconnectEvenWhenDisconnected()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var open = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task OpenAppServerRuntimeSettingsAsync()");
            var hardSwitch = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task SwitchRuntimeTargetAfterSaveAsync(");
            var stopAndRestart = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task StopAndRestartRuntimeAfterSaveAsync(");

            Assert.That(open, Does.Contain("_runtimeTargetIdentityService.ResolveIdentity(_appConfigSnapshot)"));
            Assert.That(open, Does.Contain("_runtimeTargetIdentityService.ResolveIdentity(nextRuntimeSnapshot)"));
            Assert.That(open, Does.Contain("if (runtimeTargetChanged)"));
            Assert.That(open.IndexOf("if (runtimeTargetChanged)", StringComparison.Ordinal),
                Is.LessThan(open.IndexOf("if (!wasConnectedForRestartPrompt)", StringComparison.Ordinal)));
            Assert.That(hardSwitch, Does.Contain("await StopAndRestartRuntimeAfterSaveAsync("));
            Assert.That(stopAndRestart, Does.Contain("await StopCurrentRuntimeActivityAsync(reason);"));
            Assert.That(stopAndRestart, Does.Contain("await RestartAppServerForRuntimeSettingsAsync("));
            Assert.That(hardSwitch, Does.Not.Contain("AppServerRuntimeSavedNextConnect"));
            Assert.That(hardSwitch, Does.Not.Contain("AppServerRuntimeSavedRestartLater"));
        }

        [Test]
        public void RuntimeTargetChange_SavesNewConfigThenUsesTargetDefaultMode()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var open = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task OpenAppServerRuntimeSettingsAsync()");
            var restart = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task RestartAppServerForRuntimeSettingsAsync(");
            var assignIndex = open.IndexOf("_appConfigSnapshot.CodexRuntimeSource = form.SelectedRuntimeSource;", StringComparison.Ordinal);
            var saveIndex = open.IndexOf("SaveAppConfig()", assignIndex, StringComparison.Ordinal);
            var switchIndex = open.IndexOf("SwitchRuntimeTargetAfterSaveAsync(", saveIndex, StringComparison.Ordinal);

            Assert.That(assignIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(saveIndex, Is.GreaterThan(assignIndex));
            Assert.That(switchIndex, Is.GreaterThan(saveIndex));
            Assert.That(restart, Does.Contain("await _appServerClient.RestartAsync();"));
            Assert.That(restart, Does.Contain("useTargetDefaultMode && await LoadModeSettingsForCurrentRuntimeAsync()"));
            Assert.That(restart, Does.Contain("nextMode = _activeModeSettings.DefaultStartupMode;"));
            Assert.That(restart, Does.Contain("await ReloadLocalStateForCurrentRuntimeProfileAsync(true);"));
            Assert.That(restart, Does.Contain("await RefreshActiveOfficeRuntimeRootAsync();"));
            Assert.That(restart, Does.Contain("SelectInteractionModeTab(nextMode);"));
            Assert.That(restart, Does.Contain("_automationRuntimeHost?.Start();"));
        }

        [Test]
        public void RuntimeTargetSwitchUsesTargetDefaultWhileSameTargetRestartPreservesSelectedMode()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var targetSwitch = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task SwitchRuntimeTargetAfterSaveAsync(");
            var sameTargetRestart = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task RestartCurrentRuntimeAfterSaveAsync(");

            Assert.That(targetSwitch, Does.Contain("true,"));
            Assert.That(sameTargetRestart, Does.Contain("false,"));
        }

        [Test]
        public void RuntimeTargetChange_ConfigSaveFailureRestoresOldSnapshotWithoutStoppingActivity()
        {
            var open = RepositorySource.ExtractUntilNextPrivateMember(RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs"), "private async Task OpenAppServerRuntimeSettingsAsync()");
            var saveFailureIndex = open.IndexOf("if (!SaveAppConfig())", StringComparison.Ordinal);
            var restoreIndex = open.IndexOf("_appConfigSnapshot.CodexRuntimeSource = previousSource;", saveFailureIndex, StringComparison.Ordinal);
            var returnIndex = open.IndexOf("return;", restoreIndex, StringComparison.Ordinal);
            var stopIndex = open.IndexOf("SwitchRuntimeTargetAfterSaveAsync(", returnIndex, StringComparison.Ordinal);

            Assert.That(saveFailureIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(restoreIndex, Is.GreaterThan(saveFailureIndex));
            Assert.That(returnIndex, Is.GreaterThan(restoreIndex));
            Assert.That(stopIndex, Is.GreaterThan(returnIndex));
            Assert.That(open.Substring(saveFailureIndex, returnIndex - saveFailureIndex),
                Does.Not.Contain("StopCurrentRuntimeActivityAsync"));
        }

        [Test]
        public void RuntimeTargetRestartFailure_RemainsOnNewTargetTabDisconnectedWithoutFallback()
        {
            var restart = RepositorySource.ExtractUntilNextPrivateMember(RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs"), "private async Task RestartAppServerForRuntimeSettingsAsync(");
            var catchIndex = restart.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
            var failure = restart.Substring(catchIndex);

            Assert.That(failure, Does.Contain("_connectionStatus = GetConnectionStatusDisconnectedText();"));
            Assert.That(failure, Does.Contain("await ReloadLocalStateForCurrentRuntimeProfileAsync(true);"));
            Assert.That(failure, Does.Contain("SelectInteractionModeTab(nextMode);"));
            Assert.That(failure, Does.Not.Contain("previousRuntime"));
            Assert.That(failure, Does.Not.Contain("WindowsRuntimeProfileId"));
            Assert.That(failure, Does.Not.Contain("ConnectAsync"));
            Assert.That(failure, Does.Not.Contain("_automationRuntimeHost?.Start();"));
        }

        [Test]
        public void RateLimitAutoReset_UsesBackgroundTimerAndSelectedCredit()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_rateLimitAutoResetTimer = new Timer"));
            Assert.That(source, Does.Contain("Interval = RateLimitAutoResetCheckIntervalMilliseconds"));
            Assert.That(source, Does.Contain("_rateLimitAutoResetTimer.Tick += RateLimitAutoResetTimer_Tick"));
            Assert.That(source, Does.Contain("UpdateRateLimitAutoResetState();"));
            Assert.That(source, Does.Contain("new RateLimitResetCreditAutoResetPlanner()"));
            Assert.That(source, Does.Contain("SelectCreditToAutoReset("));
            Assert.That(source, Does.Contain("ConsumeRateLimitResetCreditAsync("));
            Assert.That(source, Does.Contain("credit.Id"));
            Assert.That(source, Does.Contain("DisposeRateLimitAutoResetTimerOnExit"));
        }

        [Test]
        public void RuntimeSettingsRestart_LoadsTargetRuntimeStateBeforeRuntimeMetadata()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task RestartAppServerForRuntimeSettingsAsync(");

            var reloadIndex = method.IndexOf("await ReloadLocalStateForCurrentRuntimeProfileAsync(true);", StringComparison.Ordinal);
            var authenticationIndex = method.IndexOf("await RefreshAuthenticationStateAsync(false, false);", StringComparison.Ordinal);
            var modelsIndex = method.IndexOf("await LoadAvailableModelsAsync(false, false);", StringComparison.Ordinal);
            var configIndex = method.IndexOf("await RefreshOfficialConfigStateAfterConnectAsync();", StringComparison.Ordinal);

            Assert.That(reloadIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(authenticationIndex, Is.GreaterThan(reloadIndex));
            Assert.That(modelsIndex, Is.GreaterThan(reloadIndex));
            Assert.That(configIndex, Is.GreaterThan(reloadIndex));
        }

        [Test]
        public void ApprovalDialog_SendsDecisionWithoutConnectionReadinessProbe()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task ShowApprovalRequestDialogAsync(AppServerEventArgs approvalRequest)");

            var resolveIndex = method.IndexOf("ResolveApprovalRequestAsync", StringComparison.Ordinal);
            var gatewayReadyIndex = method.IndexOf("EnsureActiveGatewayRuntimeReadyForActionAsync", StringComparison.Ordinal);
            var appServerReadyIndex = method.IndexOf("EnsureAppServerAvailableForActionAsync", StringComparison.Ordinal);

            Assert.That(resolveIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(gatewayReadyIndex < 0 || gatewayReadyIndex > resolveIndex, Is.True);
            Assert.That(appServerReadyIndex < 0 || appServerReadyIndex > resolveIndex, Is.True);
        }

        [Test]
        public void ApprovalDialog_NotifiesOnceBeforeWaitingForItsTargetForeground()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var eventMethod = RepositorySource.ExtractUntilNextPrivateMember(source, "private void AppServerClientOnEventReceived");
            var approvalCaseIndex = eventMethod.IndexOf("case AppServerEventRoute.ApprovalRequested:", StringComparison.Ordinal);
            var resolvedCaseIndex = eventMethod.IndexOf("case AppServerEventRoute.ApprovalResolved:", approvalCaseIndex, StringComparison.Ordinal);
            Assert.That(approvalCaseIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(resolvedCaseIndex, Is.GreaterThan(approvalCaseIndex));

            var approvalCase = eventMethod.Substring(approvalCaseIndex, resolvedCaseIndex - approvalCaseIndex);
            Assert.That(approvalCase, Does.Contain("QueueApprovalRequestDialog(e);"));
            Assert.That(approvalCase, Does.Not.Contain("HandleApprovalRequestedNotification(e);"));

            var showMethod = RepositorySource.ExtractUntilNextPrivateMember(source, "private async Task ShowApprovalRequestDialogAsync(AppServerEventArgs approvalRequest)");
            var notifyIndex = showMethod.IndexOf("HandleApprovalRequestedNotification(approvalRequest);", StringComparison.Ordinal);
            var waitIndex = showMethod.IndexOf("await WaitToPresentTargetInteractionAsync(approvalRequest.RequestId)", StringComparison.Ordinal);
            var showDialogIndex = showMethod.IndexOf("form.ShowDialog(this);", StringComparison.Ordinal);
            Assert.That(notifyIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(waitIndex, Is.GreaterThan(notifyIndex));
            Assert.That(showDialogIndex, Is.GreaterThan(waitIndex));
            var presentation = RepositorySource.ReadSourceFile("MainForm.InteractionPresentation.cs");
            Assert.That(presentation, Does.Contain("MainWindowHotKeyNativeMethods.GetForegroundWindow()"));
            Assert.That(presentation, Does.Contain("_targetWindowGroup.GetForms().Contains(foreground)"));
            Assert.That(presentation, Does.Contain("FindVisibleModalDialog(this)"));
            Assert.That(presentation, Does.Not.Contain("Form.ActiveForm"));
        }

        [Test]
        public void AppServerFileBrowserClipboardPaste_UsesCustomMarkerWithoutClipboardText()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs") + Environment.NewLine +
                         RepositorySource.ReadSourceFile("AppServerFileWindowManager.cs");

            Assert.That(source, Does.Contain("GetDataPresent(AppServerFileBrowserForm.ClipboardFormat)"));
            Assert.That(source, Does.Not.Contain("Clipboard.ContainsText(TextDataFormat.UnicodeText)"));
            Assert.That(source, Does.Not.Contain("Clipboard.GetText(TextDataFormat.UnicodeText)"));
            Assert.That(source, Does.Not.Contain("ClipboardText"));
        }

        [Test]
        public void AttachmentMenuKeyboardOpen_RestoresFocusWhenMenuCloses()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("ShowPopupMenuBelowControl(_attachmentMenu, _attachmentButton, true)"));
            Assert.That(source, Does.Contain("menu.Collapse += RestoreFocusOnCollapse"));
            Assert.That(source, Does.Contain("control.Focus();"));
        }

        [Test]
        public void AttachmentMenuKeyboardOpen_SuppressesSyntheticButtonClick()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_suppressNextAttachmentButtonClick = true;"));
            Assert.That(source, Does.Contain("if (_suppressNextAttachmentButtonClick)"));
            Assert.That(source, Does.Contain("_suppressNextAttachmentButtonClick = false;"));
        }

        [Test]
        public void MainForm_AccountUsesAltC_UserCenterUsesAltU_AndAttachmentUsesCtrlShiftA()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_accountMenuItem = new MenuItem(UiLocalization.Text(\"MainMenu.Account\", \"A&ccount\"))"));
            Assert.That(source, Does.Not.Contain("keyData == (Keys.Alt | Keys.A)"));
            Assert.That(source, Does.Contain("keyData == (Keys.Alt | Keys.U) && _userCenterMenuItem.Enabled"));
            Assert.That(source, Does.Contain("OnUserCenterMenuItemClick(_userCenterMenuItem, EventArgs.Empty);"));
            Assert.That(source, Does.Contain("keyData == (Keys.Control | Keys.Shift | Keys.A)"));
            Assert.That(source, Does.Contain("OpenAttachmentMenuFromKeyboard();"));
        }

        [Test]
        public void HelpMenu_ExposesLogDirectoryEntry()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_openLogDirectoryMenuItem"));
            Assert.That(source, Does.Contain("MainMenu.OpenLogDirectory"));
            Assert.That(source, Does.Contain("_openLogDirectoryMenuItem.Click += OnOpenLogDirectoryMenuItemClick"));
            Assert.That(source, Does.Contain("_helpMenuItem.MenuItems.Add(_openLogDirectoryMenuItem)"));
            Assert.That(source, Does.Contain("GetLogDirectory()"));
            Assert.That(source, Does.Contain("UseShellExecute = true"));
        }

        [Test]
        public void HelpMenu_ExposesOfficialWebsiteEntry()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_officialWebsiteMenuItem"));
            Assert.That(source, Does.Contain("MainMenu.OfficialWebsite"));
            Assert.That(source, Does.Contain("_officialWebsiteMenuItem.Click += OnOfficialWebsiteMenuItemClick"));
            Assert.That(source, Does.Contain("_helpMenuItem.MenuItems.Add(_officialWebsiteMenuItem)"));
            Assert.That(source, Does.Contain("ExternalUrlLauncher.Open(\"https://mycodex.jtl.world/\")"));
        }

        [Test]
        public void ChatHistoryList_UsesLeftRightForTurnAnchorNavigation()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var method = RepositorySource.ExtractUntilNextPrivateMember(source, "private void ChatHistoryListBox_AnchorNavigationRequested");

            Assert.That(source, Does.Contain("_chatHistoryListBox.AnchorNavigationRequested += ChatHistoryListBox_AnchorNavigationRequested"));
            Assert.That(method, Does.Contain("TryMoveChatHistorySelectionByTurn(e.Direction)"));
            Assert.That(method, Does.Not.Contain("e.Control && e.KeyCode == Keys.Up"));
            Assert.That(method, Does.Not.Contain("e.Control && e.KeyCode == Keys.Down"));
        }

        private static void AssertMenuItemShortcut(string source, string fieldName, string shortcut)
        {
            var initializerPattern =
                Regex.Escape(fieldName) +
                @"\s*=\s*new\s+MenuItem\s*\([\s\S]*?\)\s*\{[\s\S]*?Shortcut\s*=\s*" +
                Regex.Escape(shortcut) +
                @"[\s\S]*?\};";
            var assignment = fieldName + ".Shortcut = " + shortcut + ";";
            var hasShortcut = Regex.IsMatch(source, initializerPattern) ||
                source.Contains(assignment);

            Assert.That(hasShortcut, Is.True, fieldName + " should show " + shortcut + " in its context menu.");
        }
    }
}
