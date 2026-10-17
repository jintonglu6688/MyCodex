using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormModeSourceTests
    {
        [Test]
        public void FileMenu_DoesNotExposeRedundantModeSwitchEntry()
        {
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var layoutSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Layout.cs");
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");

            Assert.That(mainFormSource, Does.Not.Contain("_switchInteractionModeMenuItem"));
            Assert.That(layoutSource, Does.Not.Contain("_switchInteractionModeMenuItem"));
            Assert.That(modesSource, Does.Not.Contain("SwitchInteractionModeMenuItem_Click"));
            Assert.That(modesSource, Does.Not.Contain("UpdateModeMenuState"));
        }

        [Test]
        public void FileMenu_DoesNotExposeLegacyOfficeDocumentTaskEntry()
        {
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var layoutSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Layout.cs");
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");

            Assert.That(mainFormSource, Does.Not.Contain("_officeDocumentsMenuItem"));
            Assert.That(layoutSource, Does.Not.Contain("_officeDocumentsMenuItem.Click += OnOfficeDocumentsMenuItemClick"));
            Assert.That(layoutSource, Does.Not.Contain("_fileMenuItem.MenuItems.Add(_officeDocumentsMenuItem)"));
            Assert.That(modesSource, Does.Not.Contain("_officeDocumentsMenuItem"));
            Assert.That(mainFormSource, Does.Not.Contain("OfficeDocumentTaskForm"));
            Assert.That(mainFormSource, Does.Not.Contain("OpenOfficeDocumentTasks"));
            Assert.That(mainFormSource, Does.Not.Contain("IWorkbenchHost"));
        }

        [Test]
        public void PersistState_WritesCurrentModeAndPerModeSelections()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");

            Assert.That(source, Does.Contain("CurrentInteractionMode = _currentInteractionMode"));
            Assert.That(source, Does.Contain("CurrentCodingWorkspaceId = _currentCodingWorkspaceId"));
            Assert.That(source, Does.Contain("CurrentCodingSessionId = _currentCodingSessionId"));
            Assert.That(source, Does.Contain("CurrentOfficeWorkspaceId = _currentOfficeWorkspaceId"));
            Assert.That(source, Does.Contain("CurrentOfficeSessionId = _currentOfficeSessionId"));
        }

        [Test]
        public void PersistenceRecoveryPending_BlocksModeOnboardingAndAutomaticSaves()
        {
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var automationSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Automation.cs");
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var recoverySource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.PersistenceRecovery.cs");
            var attachmentsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Attachments.cs");
            var runtimeConnectionSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.RuntimeConnection.cs");
            var sessionSettingsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.SessionSettings.cs");
            var appServerEventsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.AppServerEvents.cs");

            Assert.That(mainFormSource, Does.Contain("if (_isPersistenceRecoveryPending)"));
            Assert.That(mainFormSource, Does.Contain("ShowPersistenceRecoveryRequiredAndExit();"));
            Assert.That(modesSource, Does.Contain("if (_isPersistenceRecoveryPending)"));
            Assert.That(mainFormSource, Does.Contain("TryStartPersistenceDependentBackgroundServices();"));
            Assert.That(mainFormSource, Does.Contain("catch (PersistenceRecoveryRequiredException ex)"));
            Assert.That(recoverySource, Does.Contain("_automationRuntimeHost?.Start();"));
            Assert.That(mainFormSource, Does.Contain("SetPersistenceFailureHandler(MarkPersistenceRecoveryPending)"));
            Assert.That(recoverySource, Does.Contain("MarkPersistenceRecoveryPending(ex);"));
            Assert.That(recoverySource, Does.Contain("BeginInvokeSafe(ShowPersistenceRecoveryRequiredAndExit)"));
            Assert.That(automationSource, Does.Contain("LoadAppConfigForAction()"));
            Assert.That(modesSource, Does.Not.Contain("_appConfigStore.Load()"));
            Assert.That(attachmentsSource, Does.Not.Contain("_appConfigStore.Load()"));
            Assert.That(runtimeConnectionSource, Does.Not.Contain("_appConfigStore.Load()"));
            Assert.That(sessionSettingsSource, Does.Not.Contain("_appConfigStore.Load()"));
            Assert.That(appServerEventsSource, Does.Contain("TryHandleAutomationTurnTerminal"));
            Assert.That(recoverySource, Does.Contain("Path.IsPathRooted(affectedPath)"));
            Assert.That(recoverySource, Does.Contain("_persistenceRecoveryAffectedPath"));
            Assert.That(recoverySource, Does.Contain("Automatic saves are disabled"));
            Assert.That(recoverySource, Does.Contain("Close();"));
        }

        [Test]
        public void OptionsApplyPath_PersistsOfficeModeSettings()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OptionsSettings.cs");
            var configSource = ReadSource("src", "MyCodex.Domain", "Entities", "AppConfigSnapshot.cs");
            var modeSettingsSource = ReadSource("src", "MyCodex.Domain", "Entities", "ModeSettingsSnapshot.cs");

            Assert.That(source, Does.Contain("SaveModeSettingsForCurrentRuntimeAsync"));
            Assert.That(source, Does.Contain("await RefreshActiveOfficeRuntimeRootAsync();"));
            Assert.That(configSource, Does.Not.Contain("DefaultStartupMode"));
            Assert.That(configSource, Does.Not.Contain("OfficeRootPath"));
            Assert.That(configSource, Does.Not.Contain("OfficeRuntimeSettings"));
            Assert.That(modeSettingsSource, Does.Contain("public AppInteractionMode DefaultStartupMode"));
            Assert.That(modeSettingsSource, Does.Contain("public string OfficeRootPath"));
            Assert.That(source, Does.Contain("_appConfigSnapshot.OfficeShowActivityPane = form.OfficeShowActivityPane"));
            Assert.That(source, Does.Not.Contain("OfficialOfficeCapabilityEnabled"));
        }

        [Test]
        public void OptionsSave_WritesOnlyTheActiveRuntimeModeSettingsFile()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OptionsSettings.cs");
            var officeRuntimeSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");
            var storeSource = ReadSource("src", "MyCodex.Infrastructure", "Persistence", "JsonModeSettingsStore.cs");

            Assert.That(source, Does.Contain("SaveModeSettingsForCurrentRuntimeAsync(new ModeSettingsSnapshot"));
            Assert.That(officeRuntimeSource, Does.Contain("_modeSettingsStore.SaveForCurrentRuntimeAsync("));
            Assert.That(storeSource, Does.Contain("TryGetRuntimeModeSettingsFilePathForAsyncAccess"));
            Assert.That(storeSource, Does.Contain("GetModeSettingsFilePath()"));
            Assert.That(storeSource, Does.Not.Contain("RuntimeScopeId"));
        }

        [Test]
        public void OfficeDirectoryOptions_UseActiveRuntimeContextAndExistingRuntimeBrowser()
        {
            var optionsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OptionsSettings.cs");
            var officeRuntimeSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");
            var openOptions = ExtractMethod(optionsSource, "private async Task OpenOptionsAsync()");
            var applyOptions = ExtractMethod(optionsSource, "private bool ApplyOptionsFormLocalSettings(");
            var createContext = ExtractMethod(
                officeRuntimeSource,
                "private OfficeDirectoryOptionsContext CreateOfficeDirectoryOptionsContext(");
            var browse = ExtractMethod(
                officeRuntimeSource,
                "private string BrowseActiveOfficeRuntimeDirectory(");

            Assert.That(openOptions, Does.Contain("var officeTarget = GetActiveOfficeRuntimeTarget();"));
            Assert.That(openOptions, Does.Contain("CreateOfficeDirectoryOptionsContext("));
            Assert.That(openOptions, Does.Contain("modeSettingsAvailable"));
            Assert.That(createContext, Does.Contain("RuntimeDisplayName = GetCurrentRuntimeDisplayName()"));
            Assert.That(createContext, Does.Contain("HostPlatform = target.HostPlatform"));
            Assert.That(createContext, Does.Contain("ConfiguredRootPath = _activeModeSettings.OfficeRootPath"));
            Assert.That(createContext, Does.Contain("ModeSettingsAvailable = modeSettingsAvailable"));
            Assert.That(createContext, Does.Contain("EffectiveRootPath = _resolvedOfficeRootPath"));
            Assert.That(browse, Does.Contain("new RuntimeFileBrowserForm("));
            Assert.That(browse, Does.Contain("RuntimeFileBrowserSelectionMode.Directory"));
            Assert.That(browse, Does.Contain("browser.PathSelected +="));
            Assert.That(browse, Does.Contain("browser.ShowDialog(owner);"));
            Assert.That(browse, Does.Contain("_officeRuntimeFileSystem.ListDirectoryAsync("));
            Assert.That(applyOptions, Does.Not.Contain("EnsureDirectoryAsync"));
            Assert.That(applyOptions, Does.Not.Contain("Connect"));
            Assert.That(applyOptions, Does.Not.Contain("Restart"));
        }

        [Test]
        public void OfficeRootChange_PreservesSingletonIdentityAndSessionSelection()
        {
            var workspaceService = new OfficeWorkspaceService();
            var oldDefault = workspaceService.CreateOfficeWorkspace(
                "wsl|ubuntu",
                AppServerHostPlatform.Wsl,
                "/home/alice/MyCodex-office");
            var state = new OfficeRuntimeStateSnapshot
            {
                RuntimeScopeId = "wsl|ubuntu",
                RecentWorkspaces = new List<WorkspaceInfo> { oldDefault },
                CurrentWorkspaceId = oldDefault.Id,
                CurrentSessionId = "session-1"
            };

            var newDefault = workspaceService.CreateOfficeWorkspace(
                "wsl|ubuntu",
                AppServerHostPlatform.Wsl,
                "/opt/MyCodex-office");
            state.RecentWorkspaces[0] = newDefault;

            Assert.That(newDefault.Id, Is.EqualTo(oldDefault.Id));
            Assert.That(state.RecentWorkspaces, Has.Count.EqualTo(1));
            Assert.That(state.CurrentWorkspaceId, Is.EqualTo(newDefault.Id));
            Assert.That(state.CurrentSessionId, Is.EqualTo("session-1"));

            var optionsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OptionsSettings.cs");
            var officeRuntimeSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");
            Assert.That(optionsSource, Does.Not.Contain("Directory.Move"));
            Assert.That(optionsSource, Does.Not.Contain("Directory.Delete"));
            Assert.That(optionsSource, Does.Contain("ResolveAndEnsureOfficeRootAsync"));
            Assert.That(officeRuntimeSource, Does.Contain("EnsureCachedDefaultOfficeWorkspace();"));
            Assert.That(officeRuntimeSource, Does.Not.Contain("Directory.Move"));
            Assert.That(officeRuntimeSource, Does.Not.Contain("Directory.Delete"));
        }

        [Test]
        public void SettingsMenu_KeepsAppServerRuntimeSelectionAvailableInBothModes()
        {
            var sessionSettingsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.SessionSettings.cs");
            var plannerSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainFormAuthenticationStatePlanner.cs");
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");

            Assert.That(sessionSettingsSource, Does.Contain("_settingsMenuItem.MenuItems.Add(_appServerRuntimeSettingsMenuItem)"));
            Assert.That(sessionSettingsSource, Does.Contain("MainFormAuthenticationStatePlanner.BuildSettingsMenuState("));
            Assert.That(sessionSettingsSource, Does.Contain("_appServerRuntimeSettingsMenuItem.Enabled = state.AppServerRuntimeSettingsEnabled"));
            Assert.That(sessionSettingsSource, Does.Contain("_appServerRuntimeSettingsMenuItem.Visible = state.AppServerRuntimeSettingsVisible"));
            Assert.That(plannerSource, Does.Contain("AppServerRuntimeSettingsEnabled = true"));
            Assert.That(plannerSource, Does.Contain("AppServerRuntimeSettingsVisible = true"));
            var handler = ExtractMethod(sessionSettingsSource, "private async void AppServerRuntimeSettingsMenuItem_Click");
            Assert.That(handler, Does.Contain("await OpenAppServerRuntimeSettingsAsync()"));
            Assert.That(handler, Does.Not.Contain("AppInteractionMode.Office"));
            Assert.That(handler, Does.Not.Contain("return;"));
            Assert.That(mainFormSource, Does.Not.Contain("EnforceBundledAppServerRuntime"));
        }

        [Test]
        public void OfficeMode_UsesConfiguredRuntimeForAppServerAndState()
        {
            var programSource = ReadSource("src", "MyCodex.WinForms", "Program.cs");
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var obsoleteModeContextType = "AppServerRuntime" + "ModeContext";
            var obsoleteStoreType = "ModeAware" + "AppConfigStore";
            var obsoleteOfficeOverrideProperty = "UseOfficeLocal" + "AppServerRuntime";
            var obsoleteSnapshotFactory = "CreateBundledRuntime" + "Snapshot";
            var obsoleteModeContextUpdater = "UpdateAppServerRuntime" + "ModeContext";

            Assert.That(programSource, Does.Not.Contain(obsoleteModeContextType));
            Assert.That(programSource, Does.Not.Contain(obsoleteStoreType));
            Assert.That(programSource, Does.Not.Contain("new JsonAuthenticationProfileStore(appConfigService, appConfigStore)"));
            Assert.That(programSource, Does.Not.Contain("new JsonChatGptAuthBackupStore(appConfigService, appConfigStore)"));
            Assert.That(programSource, Does.Not.Contain("new JsonAuthenticationSourceSelectionStore(appConfigService, appConfigStore)"));
            Assert.That(mainFormSource, Does.Not.Contain(obsoleteModeContextType));
            Assert.That(mainFormSource, Does.Not.Contain(obsoleteStoreType));
            Assert.That(mainFormSource, Does.Not.Contain("new JsonAuthenticationSourceSelectionStore"));
            Assert.That(mainFormSource, Does.Not.Contain(obsoleteModeContextUpdater));
            Assert.That(mainFormSource, Does.Contain("runtimeConfigStore.Load(GetConfiguredRuntimeConfigSnapshot())"));
            Assert.That(mainFormSource, Does.Contain("runtimeConfigStore.Save(snapshot, GetConfiguredRuntimeConfigSnapshot())"));
            Assert.That(mainFormSource, Does.Not.Contain("AuthenticationCenterForm("));
            Assert.That(mainFormSource, Does.Not.Contain("new AuthenticationCenterForm("));
            Assert.That(modesSource, Does.Not.Contain(obsoleteOfficeOverrideProperty));
            Assert.That(modesSource, Does.Not.Contain(obsoleteSnapshotFactory));
            Assert.That(modesSource, Does.Contain("GetConfiguredRuntimeProfileId"));
            Assert.That(modesSource, Does.Not.Contain("RuntimeProfileService.WindowsRuntimeProfileId"));
            Assert.That(mainFormSource, Does.Contain("var activeRuntimeProfileId = GetConfiguredRuntimeProfileId();"));
            Assert.That(mainFormSource, Does.Contain("var currentRuntimeProfileId = GetConfiguredRuntimeProfileId();"));
            Assert.That(mainFormSource, Does.Contain("GetConfiguredRuntimeProfileId()"));
        }

        [Test]
        public void PersistedState_LoadsCodingAndOfficeContextsForActiveRuntime()
        {
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var officeRuntimeSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");

            Assert.That(mainFormSource, Does.Contain("var activeOfficeTarget = GetActiveOfficeRuntimeTarget();"));
            Assert.That(mainFormSource, Does.Contain("var activeOfficeState = _officeRuntimeStateService.MigrateLegacyState("));
            Assert.That(mainFormSource, Does.Contain("AddActiveRuntimeWorkspaces(activeCodingWorkspaces, activeOfficeState.RecentWorkspaces);"));
            Assert.That(mainFormSource, Does.Contain("SessionCollectionQueries.IsSessionInActiveRuntimeContexts("));
            Assert.That(mainFormSource, Does.Contain("activeOfficeState.CurrentWorkspaceId"));
            Assert.That(mainFormSource, Does.Contain("activeOfficeState.CurrentSessionId"));
            Assert.That(mainFormSource, Does.Contain("OfficeRuntimeStates = BuildOfficeRuntimeStates(activeOfficeTarget.RuntimeScopeId)"));
            Assert.That(officeRuntimeSource, Does.Contain("CloneCodingWorkspaces"));
            Assert.That(officeRuntimeSource, Does.Contain("CloneOfficeWorkspaces"));
        }

        [Test]
        public void SendPath_IncludesOfficeDeveloperInstructions()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");

            Assert.That(source, Does.Contain("BuildOfficeDeveloperInstructions"));
            Assert.That(source, Does.Contain("OfficeDeveloperInstructionComposer.Compose"));
            Assert.That(source, Does.Contain("officeRootPath"));
            Assert.That(source, Does.Contain("officeWorkspacePath"));
            Assert.That(source, Does.Contain("session.Mode == AppInteractionMode.Office"));
        }

        [Test]
        public void InternalToolContext_SkipsOfficeSessions()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.InternalTools.cs");

            Assert.That(source, Does.Contain("session.Mode == AppInteractionMode.Office"));
            Assert.That(source, Does.Contain("return null;"));
            Assert.That(source, Does.Contain("InternalToolThreadContextBuilder.BuildForSession"));
            Assert.That(source, Does.Not.Contain("OfficialOfficeCapabilityEnabled"));
        }

        [Test]
        public void SendPath_UsesEffectiveModelForOfficeCollaborationMode()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs") +
                ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.MessageSending.cs");

            Assert.That(source, Does.Contain("BuildCollaborationModeForSend(targetSession, targetWorkspace, modelId, reasoningEffort)"));
            Assert.That(source, Does.Contain("client.SendSubAgentMessageAsync(session.ThreadId, request.InputItems"));
        }

        [Test]
        public void MainSend_RemainsBoundToCapturedSessionAcrossTabSelectionChanges()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.MessageSending.cs");
            var send = ExtractMethod(source, "private async Task SendCurrentInputAsync(string overrideText, bool includePendingAttachments)");
            var abortStart = source.IndexOf("private bool AbortSendIfContextChanged", StringComparison.Ordinal);
            Assert.That(abortStart, Is.GreaterThanOrEqualTo(0));
            var abort = source.Substring(abortStart);

            Assert.That(send, Does.Contain("var targetSession = _currentSession;"));
            Assert.That(send, Does.Contain("var targetWorkspace = _currentWorkspace;"));
            Assert.That(send, Does.Contain("EnsurePendingAttachmentsReadyForSendAsync(attachmentsSnapshot)"));
            Assert.That(send, Does.Contain("IsWorkspaceAvailable(targetWorkspace)"));
            Assert.That(send, Does.Contain("BuildCollaborationModeForSend(targetSession, targetWorkspace"));
            Assert.That(send, Does.Contain("BuildThreadToolContext(targetSession, targetWorkspace)"));
            Assert.That(send, Does.Contain("IsThreadCurrentlyRunning(targetSession.ThreadId)"));
            Assert.That(send, Does.Contain("ApplySessionSendCompletionPlanAsync(completionPlan, targetSession)"));
            Assert.That(send, Does.Contain("ClearSendTargetAttachmentsAfterSuccess(targetSession, attachmentsSnapshot, includePendingAttachments)"));
            Assert.That(send, Does.Contain("ApplySendTargetRefresh(targetSession)"));
            Assert.That(abort, Does.Contain("sendContext.MatchesRuntime("));
            Assert.That(abort, Does.Not.Contain("_currentWorkspace"));
            Assert.That(abort, Does.Not.Contain("_currentSession"));
        }

        [Test]
        public void MainSend_ShowsAndClearsAcceptedInputBeforeCreatingMissingThread()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.MessageSending.cs");
            var send = ExtractMethod(source, "private async Task SendCurrentInputAsync(string overrideText, bool includePendingAttachments)");
            var showIndex = send.IndexOf("AddSendTargetHistoryItem(targetSession, ChatHistoryItemFactory.GetUserSenderText(), previewText);", StringComparison.Ordinal);
            var clearIndex = send.IndexOf("ClearSendTargetInputBeforeDispatch(targetSession, rawText, attachmentsSnapshot);", StringComparison.Ordinal);
            var recreateIndex = send.IndexOf("_sessionThreadService.RecreateThreadAsync(", StringComparison.Ordinal);

            Assert.That(showIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(clearIndex, Is.GreaterThan(showIndex));
            Assert.That(recreateIndex, Is.GreaterThan(clearIndex));
        }

        [Test]
        public void MainSend_DefersMetadataUntilSendAcknowledgement()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.MessageSending.cs");
            var send = ExtractMethod(source, "private async Task SendCurrentInputAsync(string overrideText, bool includePendingAttachments)");
            Assert.That(send, Does.Not.Contain("TryUpdateThreadWorkspaceMetadataAsync"));
            var sendIndex = send.IndexOf("await _sessionThreadService.SendUserMessageAsync(", StringComparison.Ordinal);
            var completionIndex = send.IndexOf("ApplySessionSendCompletionPlanAsync(completionPlan, targetSession)", StringComparison.Ordinal);
            Assert.That(sendIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(completionIndex, Is.GreaterThan(sendIndex));
            var completion = ExtractMethod(ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs"),
                "private Task ApplySessionSendCompletionPlanAsync");
            Assert.That(completion, Does.Contain("if (acknowledged) ScheduleSendMetadataUpdate(session)"));
        }

        [Test]
        public void CollaborationMode_UsesCapturedSessionModeAndCachedOfficeRoot()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var collaboration = ExtractMethod(source, "private AppServerCollaborationMode BuildCollaborationModeForSend");
            var officeDetection = ExtractMethod(source, "private static bool IsOfficeSessionOrWorkspace");

            Assert.That(collaboration, Does.Contain("IsOfficeSessionOrWorkspace(session, workspace)"));
            Assert.That(collaboration, Does.Contain("ResolveOfficeRootPathForSession(session, workspace)"));
            Assert.That(collaboration, Does.Not.Contain("_currentInteractionMode"));
            Assert.That(collaboration, Does.Not.Contain("_appConfigSnapshot.OfficeRootPath"));
            Assert.That(officeDetection, Does.Contain("session.Mode == AppInteractionMode.Office"));
            Assert.That(officeDetection, Does.Not.Contain("_currentInteractionMode"));
            Assert.That(source, Does.Not.Contain("Mode = \"office\""));
            Assert.That(source, Does.Not.Contain("Mode = \"coding\""));
            Assert.That(source, Does.Not.Contain("Mode = \"daily\""));
        }

        [Test]
        public void DetachedSessionWorkbench_SeparatesRuntimeProfileFromOfficeDraftScope()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.SessionWorkbenches.cs");
            var open = ExtractMethod(source, "private void OpenSessionWorkbench");

            Assert.That(open, Does.Contain("SessionRuntimeIdentityResolver.ResolveRuntimeProfileId("));
            Assert.That(open, Does.Contain("GetComposeDraftScopeKey(session)"));
            Assert.That(open, Does.Contain("session.Mode"));
            Assert.That(open, Does.Not.Contain("_currentInteractionMode"));
            Assert.That(open, Does.Not.Contain("session.OfficeRuntimeScopeId"));
            Assert.That(source, Does.Not.Contain("GetSessionRuntimeScopeId"));
            Assert.That(source, Does.Not.Contain("BuildDetachedComposeDraftScopeKey"));
        }

        [Test]
        public void ModeTabActivation_DoesNotClearBackgroundThreadState()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var activation = ExtractMethod(source, "private void ActivateSelectedModeTab");

            Assert.That(activation, Does.Not.Contain("_busyThreadIds"));
            Assert.That(activation, Does.Not.Contain("ClearAllThreadBusy"));
            Assert.That(activation, Does.Not.Contain("ClearAllInFlightThreadStates"));
            Assert.That(activation, Does.Not.Contain("ResetThreadVolatileState"));
        }

        [Test]
        public void ModeTabActivation_RestoresIndependentMessageHistoryAndViewport()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var sessionsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Sessions.cs");
            var activation = ExtractMethod(source, "private void ActivateSelectedModeTab");
            var capture = ExtractMethod(source, "private void CaptureCurrentModeHistoryViewState");
            var restore = ExtractMethod(source, "private bool RestoreCurrentModeHistoryViewState");
            var matches = ExtractMethod(source, "private bool MatchesCurrentModeHistoryViewState");
            var openSelectedSession = ExtractMethod(sessionsSource, "private async Task OpenSelectedSessionFromListAsync");

            Assert.That(activation, Does.Contain("CaptureCurrentModeHistoryViewState();"));
            Assert.That(
                activation.IndexOf("CaptureCurrentModeHistoryViewState();", StringComparison.Ordinal),
                Is.LessThan(activation.IndexOf("_currentInteractionMode = nextMode;", StringComparison.Ordinal)));
            Assert.That(activation, Does.Contain("if (!RestoreCurrentModeHistoryViewState())"));
            Assert.That(capture, Does.Contain("_allChatHistoryItems.ToArray()"));
            Assert.That(capture, Does.Contain("ChatHistorySelectionSnapshot.Capture("));
            Assert.That(capture, Does.Contain("_chatHistoryListBox.TopIndex"));
            Assert.That(restore, Does.Contain("ReplaceChatHistoryItems(state.HistoryItems, false);"));
            Assert.That(restore, Does.Contain("state.ResolveSelectedIndex(visibleItems)"));
            Assert.That(restore, Does.Contain("state.ResolveTopIndex(visibleItems)"));
            Assert.That(matches, Does.Contain("_currentSession == null || state.HistoryItems.Length > 0"));
            Assert.That(activation, Does.Contain("MarkPendingHistoryReload(_currentSession);"));
            Assert.That(openSelectedSession, Does.Contain("HasPendingHistoryReload(_currentSession) || _allChatHistoryItems.Count == 0"));
        }

        [Test]
        public void ModeTabActivation_UsesOnlyCachedInMemoryWorkspaceProjectionTransitively()
        {
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var workspacesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var activation = ExtractMethod(modesSource, "private void ActivateSelectedModeTab");
            var projectionRefresh = ExtractMethod(modesSource, "private void RefreshModeProjectionFromCache");
            var restore = ExtractMethod(modesSource, "private void RestoreSelectionForCurrentMode");
            var workspaceRefresh = ExtractMethod(workspacesSource, "private void RefreshWorkspaceView");
            var cachedAvailability = ExtractMethod(workspacesSource, "private bool IsWorkspaceAvailableFromCache");

            Assert.That(activation, Does.Contain("RefreshModeProjectionFromCache();"));
            Assert.That(activation, Does.Not.Contain("ApplyMainFormRefreshPreset"));
            Assert.That(projectionRefresh, Does.Contain("RefreshWorkspaceView();"));
            Assert.That(projectionRefresh, Does.Contain("RefreshInputStateFromCache();"));
            Assert.That(projectionRefresh, Does.Not.Contain("PersistState"));
            Assert.That(restore, Does.Contain("GetFirstVisibleWorkspaceFromCache();"));
            Assert.That(restore, Does.Not.Contain("GetFirstVisibleWorkspace();"));
            Assert.That(workspaceRefresh, Does.Contain("ModeWorkspaceProjection.Build("));
            Assert.That(workspaceRefresh, Does.Contain("IsWorkspaceAvailableFromCache"));
            Assert.That(workspaceRefresh, Does.Not.Contain("EnsureOfficeCategoriesDiscovered"));
            Assert.That(workspaceRefresh, Does.Not.Contain("NormalizeAndPruneOfficeCategories"));
            Assert.That(workspaceRefresh, Does.Not.Contain("IsWorkspaceAvailable("));
            Assert.That(cachedAvailability, Does.Not.Contain("Directory.Exists"));
            Assert.That(cachedAvailability, Does.Not.Contain("_officeRuntimeFileSystem"));
            Assert.That(mainFormSource, Does.Contain("private void RefreshInputStateFromCache()"));
        }

        [Test]
        public void InitialWorkspaceRefresh_CannotCreateAWindowsOfficeDefaultForUnixRuntime()
        {
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var workspacesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");
            var workspaceRefresh = ExtractMethod(workspacesSource, "private void RefreshWorkspaceView");

            Assert.That(mainFormSource, Does.Contain("RefreshWorkspaceView();"));
            Assert.That(workspaceRefresh, Does.Not.Contain("EnsureDefaultOfficeWorkspace"));
            Assert.That(workspaceRefresh, Does.Not.Contain("EnsureOfficeRootDirectory"));
            Assert.That(workspaceRefresh, Does.Not.Contain("Directory.CreateDirectory"));
            Assert.That(workspaceRefresh, Does.Not.Contain("ResolveOfficeRootPath(_appConfigSnapshot.OfficeRootPath)"));
        }

        [Test]
        public void OfficeRootProbeError_GuardsWorkspaceActionsWithoutPruningState()
        {
            var workspacesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");
            var officeRuntimeSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");
            var availability = ExtractMethod(workspacesSource, "private bool IsWorkspaceAvailable(WorkspaceInfo workspace)");
            var refresh = ExtractMethod(officeRuntimeSource, "private async Task RefreshActiveOfficeRuntimeRootAsync()");

            Assert.That(availability, Does.Contain("OfficeWorkspaceAvailabilityPlanner.IsUnavailable("));
            Assert.That(availability, Does.Contain("_officeRuntimeRootError"));
            Assert.That(refresh, Does.Contain("RefreshInputState();"));
            Assert.That(refresh, Does.Not.Contain("RemoveSessionsForRemovedOfficeWorkspaces"));
            Assert.That(refresh, Does.Not.Contain("NormalizeAndPruneOfficeCategories"));
        }

        [Test]
        public void OfficeWorkspaceRoutes_UseRuntimeFileSystemWithoutCategoryDiscovery()
        {
            var workspacesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var open = ExtractMethod(workspacesSource, "private void OpenOfficeWorkspaceDirectory");
            var compatibility = ExtractMethod(workspacesSource, "private bool IsWorkspaceCompatibleWithCurrentRuntime");

            Assert.That(open, Does.Contain("target.UsesUnixPaths"));
            Assert.That(open, Does.Contain("ShowRuntimeFileBrowser("));
            Assert.That(open, Does.Contain("Process.Start(_resolvedOfficeRootPath)"));
            Assert.That(compatibility, Does.Contain("IsOfficeWorkspaceCompatibleWithActiveRuntime"));
            Assert.That(workspacesSource, Does.Not.Contain("RefreshOfficeCategoriesAsync"));
            Assert.That(workspacesSource, Does.Not.Contain("CreateOfficeCategoryAsync"));
            Assert.That(modesSource, Does.Not.Contain("Directory.CreateDirectory"));
            Assert.That(modesSource, Does.Not.Contain("_appConfigSnapshot.OfficeRootPath"));
        }

        [Test]
        public void OfficeImportAutomationAndCacheRoutes_UseTargetIdentity()
        {
            var mainSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var workspacesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");
            var threadSyncSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.ThreadSync.cs");
            var sessionsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Sessions.cs");
            var historyImport = ExtractMethod(threadSyncSource, "private WorkspaceInfo CreateWorkspaceForHistoryImport");

            Assert.That(mainSource, Does.Contain("StringComparer.Ordinal);"));
            Assert.That(workspacesSource, Does.Contain("WorkspaceRuntimeCacheKeyBuilder.Build("));
            Assert.That(historyImport, Does.Contain("target.RuntimeScopeId"));
            Assert.That(historyImport, Does.Contain("target.HostPlatform"));
            Assert.That(workspacesSource, Does.Not.Contain("CreateAutomationFromOfficeWorkspace"));
            Assert.That(sessionsSource, Does.Contain("_currentSession.OfficeRuntimeScopeId"));
            Assert.That(mainSource, Does.Not.Contain("ResolveOfficeRootPath(_appConfigSnapshot.OfficeRootPath)"));
        }

        [Test]
        public void SendCancellationStatusMentionsOnlyRuntimeOrAuthenticationChanges()
        {
            var sendingSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.MessageSending.cs");
            var englishResources = ReadSource("src", "MyCodex.WinForms", "Resources", "Strings.resx");
            var chineseResources = ReadSource("src", "MyCodex.WinForms", "Resources", "Strings.zh-CN.resx");

            Assert.That(sendingSource, Does.Contain("Send cancelled because the active runtime or authentication source changed."));
            Assert.That(sendingSource, Does.Not.Contain("active runtime or session changed"));
            Assert.That(englishResources, Does.Contain("Send cancelled because the active runtime or authentication source changed."));
            Assert.That(chineseResources, Does.Contain("由于活动运行时或身份验证来源已更改，发送已取消。"));
            Assert.That(chineseResources, Does.Not.Contain("由于当前运行源或会话已切换，发送已取消。"));
        }

        [Test]
        public void SendPath_UsesEffectiveModelAndReasoningForTurnStartOverrides()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.MessageSending.cs");

            Assert.That(source, Does.Contain("var turnModelId = modelId;"));
            Assert.That(source, Does.Contain("var turnReasoningEffort = reasoningEffort;"));
            Assert.That(source, Does.Not.Contain("var turnModelId = string.IsNullOrWhiteSpace(targetSession.ModelId) ? null : targetSession.ModelId;"));
            Assert.That(source, Does.Not.Contain("var turnReasoningEffort = string.IsNullOrWhiteSpace(targetSession.ModelId) ? null : targetSession.ReasoningEffort;"));
            Assert.That(source, Does.Not.Contain("var turnModelId = string.IsNullOrWhiteSpace(session.ModelId) ? null : session.ModelId;"));
            Assert.That(source, Does.Not.Contain("var turnReasoningEffort = string.IsNullOrWhiteSpace(session.ModelId) ? null : session.ReasoningEffort;"));
        }

        [Test]
        public void SendPath_UsesOfficeWorkspaceIdentityForOfficeCollaborationMode()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");

            Assert.That(source, Does.Contain("IsOfficeSessionOrWorkspace(ChatSession session, WorkspaceInfo workspace)"));
            Assert.That(source, Does.Contain("workspace.Id.StartsWith(\"office:\", StringComparison.OrdinalIgnoreCase)"));
            Assert.That(source, Does.Contain("session.WorkspaceId.StartsWith(\"office:\", StringComparison.OrdinalIgnoreCase)"));
            Assert.That(source, Does.Contain("IsOfficeSessionOrWorkspace(session, workspace)"));
        }

        [Test]
        public void OfficeMode_MapsToAppServerDefaultOrPlanCollaborationMode()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");

            Assert.That(source, Does.Contain("session.IsPlanModeEnabled ? \"plan\" : \"default\""));
            Assert.That(source, Does.Contain("CreateOfficeCollaborationMode("));
            Assert.That(source, Does.Contain("DeveloperInstructions = BuildOfficeDeveloperInstructions("));
            Assert.That(source, Does.Not.Contain("Mode = \"office\""));
        }

        [Test]
        public void MainForm_HasModePartialForSwitchFlow()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");

            Assert.That(source, Does.Contain("SelectInteractionModeTab"));
            Assert.That(source, Does.Contain("ModeTabControl_SelectedIndexChanged"));
            Assert.That(source, Does.Contain("ActivateSelectedModeTab"));
            Assert.That(source, Does.Contain("CaptureCurrentModeSelection"));
            Assert.That(source, Does.Contain("ApplyModeVisibility"));
            Assert.That(source, Does.Contain("EnsureOfficeWorkspace"));
            Assert.That(source, Does.Contain("RunStartupModeOnboardingIfNeeded"));
        }

        [Test]
        public void MainForm_UsesTwoAccessibleModeTabsAroundOneSharedWorkbench()
        {
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var layoutSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Layout.cs");

            Assert.That(mainFormSource, Does.Contain("private TabControl _modeTabControl;"));
            Assert.That(mainFormSource, Does.Contain("private TabPage _codingTabPage;"));
            Assert.That(mainFormSource, Does.Contain("private TabPage _officeTabPage;"));
            Assert.That(layoutSource, Does.Contain("MainForm.ModeTab.Coding"));
            Assert.That(layoutSource, Does.Contain("MainForm.ModeTab.Office"));
            Assert.That(layoutSource, Does.Not.Contain("MainForm.Accessible.ModeTabs"));
            Assert.That(layoutSource, Does.Not.Contain("Coding (Ctrl+1)"));
            Assert.That(layoutSource, Does.Not.Contain("Office (Ctrl+2)"));
            Assert.That(layoutSource, Does.Contain("MainForm.Accessible.CodingMode"));
            Assert.That(layoutSource, Does.Contain("MainForm.Accessible.OfficeMode"));
            Assert.That(layoutSource, Does.Contain("_codingTabPage.Controls.Add(_mainSplitContainer);"));
            Assert.That(layoutSource, Does.Not.Contain("_officeTabPage.Controls.Add(_mainSplitContainer);"));
            Assert.That(layoutSource, Does.Contain("Controls.Add(_modeTabControl);"));
            Assert.That(layoutSource, Does.Contain("_modeTabControl.TabStop = true"));
            Assert.That(layoutSource, Does.Not.Contain("DrawMode"));
            Assert.That(layoutSource, Does.Not.Contain("OwnerDraw"));
            Assert.That(CountOccurrences(mainFormSource, "_workspaceList = CreateWorkspaceListView"), Is.EqualTo(1));
            Assert.That(CountOccurrences(mainFormSource, "_sessionList = CreateSessionListView"), Is.EqualTo(1));
            Assert.That(CountOccurrences(mainFormSource, "_chatHistoryListBox = CreateChatHistoryListBox"), Is.EqualTo(1));
            Assert.That(CountOccurrences(mainFormSource, "_inputBox = CreateInputBox"), Is.EqualTo(1));
            Assert.That(CountOccurrences(mainFormSource, "_activityBrowseControl = CreateActivityBrowseControl"), Is.EqualTo(1));
        }

        [Test]
        public void ModeTabs_AttachActivationAfterConfigAndStateInitialization()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var loadConfigIndex = source.IndexOf("LoadAppConfig();", StringComparison.Ordinal);
            var loadStateIndex = source.IndexOf("LoadPersistedState(false);", StringComparison.Ordinal);
            var initializeTabsIndex = source.IndexOf("InitializeModeTabsAfterStateLoad();", StringComparison.Ordinal);

            Assert.That(loadConfigIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(loadStateIndex, Is.GreaterThan(loadConfigIndex));
            Assert.That(initializeTabsIndex, Is.GreaterThan(loadStateIndex));
        }

        [Test]
        public void ModeTabSelection_IsTheOnlyInMemoryActivationPath()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var handler = ExtractMethod(source, "private void ModeTabControl_SelectedIndexChanged");
            var activation = ExtractMethod(source, "private void ActivateSelectedModeTab");

            Assert.That(handler, Does.Contain("ActivateSelectedModeTab();"));
            Assert.That(handler, Does.Not.Contain("ScheduleOfficeCliOnboardingPromptIfNeeded"));
            Assert.That(activation, Does.Contain("SaveCurrentComposeState();"));
            Assert.That(activation, Does.Contain("CaptureCurrentModeSelection();"));
            Assert.That(activation, Does.Contain("RestoreSelectionForCurrentMode();"));
            Assert.That(activation, Does.Contain("RefreshModeProjectionFromCache();"));
            Assert.That(activation, Does.Contain("_modeTabControl.Focus();"));
            Assert.That(activation, Does.Not.Contain("PersistState"));
            Assert.That(activation, Does.Not.Contain("ReloadLocalStateForCurrentRuntimeProfile"));
            Assert.That(activation, Does.Not.Contain("PrepareRuntimeWindowsForTransition"));
            Assert.That(activation, Does.Not.Contain("EnsureOffice"));
            Assert.That(activation, Does.Not.Contain("RefreshActiveOfficeRuntimeRoot"));
            Assert.That(activation, Does.Not.Contain("RestartAsync"));
        }

        [Test]
        public void ModeTabs_UseDefaultStartupModeButRuntimeReloadPreservesCurrentMode()
        {
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs").Replace("\r\n", "\n");
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var onShown = ExtractMethod(mainFormSource, "private async void OnShown");

            Assert.That(mainFormSource, Does.Contain("preserveCurrentInteractionMode\n                ? preservedInteractionMode\n                : _activeModeSettings.DefaultStartupMode"));
            Assert.That(onShown, Does.Contain("SelectInteractionModeTab(_activeModeSettings.DefaultStartupMode);"));
            Assert.That(onShown, Does.Not.Contain("_currentInteractionMode = _activeModeSettings.DefaultStartupMode;"));
            Assert.That(modesSource, Does.Contain("_modeTabControl.SelectedTab = GetModeTabPage(_currentInteractionMode);"));
            Assert.That(modesSource, Does.Contain("_modeTabControl.SelectedIndexChanged += ModeTabControl_SelectedIndexChanged;"));
            Assert.That(modesSource, Does.Not.Contain("_officeTabPage.Enabled = false"));
            Assert.That(modesSource, Does.Not.Contain("_codingTabPage.Enabled = false"));
            var initialization = ExtractMethod(modesSource, "private void InitializeModeTabsAfterStateLoad");
            Assert.That(initialization, Does.Not.Contain("CodexRuntimeSource"));
            Assert.That(initialization, Does.Not.Contain("RuntimeProfile"));
        }

        [Test]
        public void RuntimeTargetTransition_ClearsOnlyTransientActivityAndSuppressesOldTargetRecovery()
        {
            var runtimeWindows = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.RuntimeWindows.cs");
            var appServerEvents = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.AppServerEvents.cs");
            var finalize = ExtractMethod(runtimeWindows, "private void FinalizeRuntimeTransitionState");
            var disconnected = ExtractMethod(appServerEvents, "private void HandleAppServerDisconnected");

            Assert.That(finalize, Does.Contain("ClearAllThreadBusy();"));
            Assert.That(finalize, Does.Contain("ClearAllInFlightThreadStates();"));
            Assert.That(finalize, Does.Contain("FinalizeAllActiveTurnSummaries(reason);"));
            Assert.That(finalize, Does.Contain("_pendingExecutablePlans.Clear();"));
            Assert.That(finalize, Does.Contain("_pendingRemotePlanExecutionPrompts.Clear();"));
            Assert.That(finalize, Does.Contain("_pendingInvalidEncryptedContentRetries.Clear();"));
            Assert.That(finalize, Does.Contain("_bufferedCommandOutputState.Reset();"));
            Assert.That(finalize, Does.Contain("_bufferedTurnSummaryEventState.Reset();"));
            Assert.That(finalize, Does.Contain("ClearModeHistoryViewStates();"));
            Assert.That(finalize, Does.Not.Contain("_sessions.Clear();"));
            Assert.That(finalize, Does.Not.Contain("_recentWorkspaces.Clear();"));
            Assert.That(finalize, Does.Not.Contain("_pendingAttachments.Clear();"));
            Assert.That(disconnected, Does.Contain("if (_isRuntimeWindowTransitionInProgress)"));
            Assert.That(disconnected.IndexOf("if (_isRuntimeWindowTransitionInProgress)", StringComparison.Ordinal),
                Is.LessThan(disconnected.IndexOf("TryRecoverAppServerConnectionAsync", StringComparison.Ordinal)));
        }

        [Test]
        public void ModeTabSelection_DoesNotRunBehindOwnedModalDialogs()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var selection = ExtractMethod(source, "private bool SelectInteractionModeTab");
            var handler = ExtractMethod(source, "private void ModeTabControl_SelectedIndexChanged");

            Assert.That(selection, Does.Contain("if (!Enabled || _modeTabControl == null)"));
            Assert.That(handler, Does.Contain("if (!Enabled)"));
        }

        [Test]
        public void ModeTabs_KeepFocusOnTheTabControlAfterActivation()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var activation = ExtractMethod(source, "private void ActivateSelectedModeTab");

            Assert.That(activation, Does.Contain("_modeTabControl.Focus();"));
            Assert.That(source, Does.Not.Contain("ModeFocusRole"));
            Assert.That(source, Does.Not.Contain("RestoreCurrentModeFocusRole"));
        }

        [Test]
        public void OfficeMode_SchedulesOfficeCliOnboardingPrompt()
        {
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");

            Assert.That(mainFormSource, Does.Contain("HasCompletedOfficeCliOnboarding"));
            Assert.That(mainFormSource, Does.Contain("ScheduleOfficeCliOnboardingPromptIfNeeded();"));
            Assert.That(modesSource, Does.Contain("PromptForOfficeCliIntegrationIfNeededAsync"));
            Assert.That(modesSource, Does.Contain("OfficeCliInstallationService"));
            Assert.That(modesSource, Does.Contain("InstallLatestAsync"));
            Assert.That(modesSource, Does.Contain("RestartAppServerAfterOfficeCliEnvironmentChangeAsync"));
        }

        [Test]
        public void ManualsAndRepositoryGuidance_DescribeRuntimeAwareCoexistingModeTabs()
        {
            var englishManual = ReadSource("help", "UserManual.en.html");
            var chineseManual = ReadSource("help", "用户手册.html");
            var guidance = ReadSource("AGENTS.md");

            Assert.That(englishManual, Does.Contain("two accessible tabs in the same main window"));
            Assert.That(englishManual, Does.Contain("Ctrl+1"));
            Assert.That(englishManual, Does.Contain("Ctrl+2"));
            Assert.That(englishManual, Does.Contain("$HOME/MyCodex-office"));
            Assert.That(englishManual, Does.Contain(
                "Responses, commands, detached workbenches, and automations in the other tab continue running"));
            Assert.That(englishManual, Does.Contain(
                "opening another target, or switching phone devices does not stop work in other targets"));
            Assert.That(englishManual, Does.Contain("active Coding and Office responses, commands, and automations together"));
            Assert.That(chineseManual, Does.Contain("两个无障碍标签页"));
            Assert.That(chineseManual, Does.Contain("$HOME/MyCodex-office"));
            Assert.That(chineseManual, Does.Contain(
                "另一个标签页中的响应、命令、独立工作台和自动化会继续运行"));
            Assert.That(chineseManual, Does.Contain(
                "打开其他目标或切换手机设备都不会停止其他目标的工作"));
            Assert.That(chineseManual, Does.Contain("统一提示编程与办公模式中的活动响应、命令和自动化"));
            Assert.That(guidance, Does.Contain("Coding and Office are coexisting interaction contexts"));
            Assert.That(guidance, Does.Contain("scoped by execution target"));
            var staleEnglishManualClaim = "Office Mode always " + "uses the local Office directory";
            var staleChineseManualClaim = "办公模式固定使用" + "本机办公目录";
            var staleGuidanceClaim = "Office Mode always " + "uses the local Windows Office directory";
            Assert.That(englishManual, Does.Not.Contain(staleEnglishManualClaim));
            Assert.That(chineseManual, Does.Not.Contain(staleChineseManualClaim));
            Assert.That(guidance, Does.Not.Contain(staleGuidanceClaim));
        }

        [Test]
        public void OfficeCliOnboarding_IsWindowsTargetOnlyAndNeverGatesOfficeTab()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var schedule = ExtractMethod(source, "private void ScheduleOfficeCliOnboardingPromptIfNeeded");
            var prompt = ExtractMethod(source, "private async Task PromptForOfficeCliIntegrationIfNeededAsync");
            var relevance = ExtractMethod(source, "private bool IsWindowsOfficeCliIntegrationRelevant");

            Assert.That(schedule, Does.Contain("IsWindowsOfficeCliIntegrationRelevant()"));
            Assert.That(prompt, Does.Contain("IsWindowsOfficeCliIntegrationRelevant()"));
            Assert.That(relevance, Does.Contain("GetActiveOfficeRuntimeTarget().HostPlatform == AppServerHostPlatform.Windows"));
            Assert.That(source, Does.Not.Contain("_officeTabPage.Enabled = false"));
            Assert.That(source, Does.Not.Contain("_officeTabPage.Visible = false"));
        }

        [Test]
        public void ModeSwitch_UpdatesWindowTitle()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");

            Assert.That(source, Does.Contain("RefreshWindowTitle();"));
            Assert.That(source, Does.Contain("MainForm.Title.Mode.Coding"));
            Assert.That(source, Does.Contain("MainForm.Title.Mode.Office"));
        }

        [Test]
        public void FileMenu_DisablesWorkspaceManagementInOfficeMode()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");

            Assert.That(source, Does.Contain("var isOfficeMode = _currentInteractionMode == AppInteractionMode.Office"));
            Assert.That(source, Does.Contain("_openWorkspaceFileMenuItem.Enabled = !isOfficeMode"));
        }

        [Test]
        public void ModeVisibility_AppliesOfficeActivityPaneSetting()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");

            Assert.That(source, Does.Contain("_contentSplitContainer.Panel2Collapsed = isOfficeMode && !_appConfigSnapshot.OfficeShowActivityPane"));
        }

        [Test]
        public void ModeVisibility_HidesWorkspaceManagementActionsInOfficeMode()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");

            Assert.That(source, Does.Contain("_workspaceNewMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_workspaceList.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_workspaceList.TabStop = !isOfficeMode"));
            Assert.That(source, Does.Not.Contain("_officeCategory"));
            Assert.That(source, Does.Contain("_workspaceImportSyncMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_workspaceRemoveMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_workspaceArchiveMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionSubAgentsMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionAdvancedSeparatorMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionArchiveMenuItem.Visible = !isOfficeMode"));
        }

        [Test]
        public void ModeVisibility_HidesCodingOnlyActionsInOfficeMode()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");

            Assert.That(source, Does.Contain("_gitMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_commandMenuButton.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_diffWorkbenchButton.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_workspaceProjectInstructionsMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_forkMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Not.Contain("_deriveMenuItem"));
            Assert.That(source, Does.Contain("_reviewChatHistoryMenuItem.Visible = !isOfficeMode"));
        }

        [Test]
        public void ModeSwitch_PreservesComposeDraftsByModeRuntimeIdentity()
        {
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var attachmentsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Attachments.cs");
            var officeRuntimeSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");

            Assert.That(mainFormSource, Does.Contain("UnboundComposeDraftSessionId"));
            Assert.That(mainFormSource, Does.Not.Contain("_officeUnboundComposeDraft"));
            Assert.That(modesSource, Does.Contain("SaveCurrentModeUnboundComposeState"));
            Assert.That(modesSource, Does.Contain("_composeDraftStore.Save(GetCurrentModeDraftScopeKey(), UnboundComposeDraftSessionId"));
            Assert.That(modesSource, Does.Contain("_composeDraftStore.Get(GetCurrentModeDraftScopeKey(), UnboundComposeDraftSessionId)"));
            Assert.That(modesSource, Does.Contain("_composeDraftStore.Remove(GetCurrentModeDraftScopeKey(), UnboundComposeDraftSessionId"));
            Assert.That(attachmentsSource, Does.Contain("GetCurrentModeUnboundComposeState"));
            Assert.That(attachmentsSource, Does.Contain("session.Mode == AppInteractionMode.Office"));
            Assert.That(attachmentsSource, Does.Contain("SessionRuntimeIdentityResolver.ResolveDraftScopeId("));
            Assert.That(attachmentsSource, Does.Contain("GetActiveOfficeRuntimeTarget().RuntimeScopeId"));
            Assert.That(attachmentsSource, Does.Contain("GetConfiguredRuntimeProfileId()"));
            Assert.That(officeRuntimeSource, Does.Contain("GetCurrentModeDraftScopeKey"));
        }

        [Test]
        public void OfficeMode_DoesNotProjectAWorkspaceList()
        {
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var workspacesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");

            Assert.That(modesSource, Does.Contain("_workspaceList.Visible = !isOfficeMode;"));
            Assert.That(modesSource, Does.Contain("_workspaceList.TabStop = !isOfficeMode;"));
            Assert.That(workspacesSource, Does.Contain("if (_currentInteractionMode == AppInteractionMode.Office)"));
            Assert.That(workspacesSource, Does.Not.Contain("categoryNameOnly"));
        }

        [Test]
        public void SendPath_SkipsGitMetadataUpdatesForOfficeSessions()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");

            Assert.That(source, Does.Contain("TryUpdateThreadWorkspaceMetadataAsync"));
            Assert.That(source, Does.Contain("session.Mode == AppInteractionMode.Office"));
            Assert.That(source, Does.Contain("return Task.CompletedTask"));
        }

        [Test]
        public void AppServerFileBrowser_DefaultsToOfficeRootInOfficeMode()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.SessionSettings.cs");

            Assert.That(source, Does.Contain("_currentInteractionMode == AppInteractionMode.Office"));
            Assert.That(source, Does.Contain("var officeWorkspace = EnsureOfficeWorkspace();"));
            Assert.That(source, Does.Contain("_resolvedOfficeRootPath ?? string.Empty"));
        }

        [Test]
        public void OfficeMode_RemovesOfficeCategoryLabels()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Layout.cs");

            Assert.That(source, Does.Contain("GetWorkspaceListAccessibleName"));
            Assert.That(source, Does.Contain("GetWorkspaceSectionTitleText"));
            Assert.That(source, Does.Not.Contain("MainForm.Accessible.OfficeCategories"));
            Assert.That(source, Does.Not.Contain("MainForm.Label.OfficeCategory"));
        }

        [Test]
        public void OfficeMode_UsesLocalWindowsWorkspaceCompatibility()
        {
            var workspacesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");
            var officeRuntimeSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");

            Assert.That(workspacesSource, Does.Contain("_currentInteractionMode == AppInteractionMode.Office && workspace.Kind == WorkspaceKind.Office"));
            Assert.That(workspacesSource, Does.Contain("WorkspacePathUtilities.LooksLikeWindowsDriveAbsolutePath(workspace.RootPath)"));
            Assert.That(officeRuntimeSource, Does.Contain("workspace.Kind == WorkspaceKind.Office"));
            Assert.That(officeRuntimeSource, Does.Contain("!IsWorkspacePathCompatibleWithCurrentRuntime(workspace.RootPath)"));
        }

        [Test]
        public void OfficeMode_PreservesOfficeSummaryInCompactHeader()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Status.cs");

            Assert.That(source, Does.Contain("_currentInteractionMode == AppInteractionMode.Office"));
            Assert.That(source, Does.Contain("BuildOfficeHeaderSummary(notSelectedText, notSyncedText)"));
            Assert.That(source, Does.Contain("_workspaceNameValueLabel.Text = summary.TitleText"));
            Assert.That(source, Does.Contain("_workspacePathValueLabel.Text = SessionHeaderSummaryBuilder.CombineDetails("));
            Assert.That(source, Does.Contain("_branchValueLabel.Text = summary.StatusText"));
        }

        [Test]
        public void OfficeWorkspace_DoesNotDiscoverChildDirectories()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");

            Assert.That(source, Does.Not.Contain("_officeRuntimeFileSystem.ListDirectoryAsync("));
            Assert.That(source, Does.Not.Contain("Directory.GetDirectories("));
            Assert.That(source, Does.Not.Contain("RefreshOfficeCategoriesAsync"));
        }

        [Test]
        public void OfficeWorkspace_HasNoManagedCategoryPruning()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");

            Assert.That(source, Does.Not.Contain("ManagedOfficeCategory"));
            Assert.That(source, Does.Not.Contain("RefreshOfficeCategoriesAsync"));
        }

        [Test]
        public void OfficeHistoryImport_UsesSingleWorkspaceMapping()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Automation.cs");

            Assert.That(source, Does.Contain("ResolveOfficeWorkspaceForImport"));
            Assert.That(source, Does.Not.Contain("CreateOfficeCategoryWorkspace(ResolveOfficeRootPathForHistorySync(), path)"));
        }

        [Test]
        public void OfficeStateRestore_CollapsesToSingleWorkspace()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");

            Assert.That(source, Does.Contain("_recentWorkspaces.RemoveAll(item => item != null && item.Kind == WorkspaceKind.Office);"));
            Assert.That(source, Does.Contain("_recentWorkspaces.Add(workspace);"));
        }

        [Test]
        public void OfficeMode_RespectsActivityPaneVisibilityOption()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");

            Assert.That(source, Does.Contain("_contentSplitContainer.Panel2Collapsed = isOfficeMode && !_appConfigSnapshot.OfficeShowActivityPane"));
            Assert.That(source, Does.Not.Contain("_contentSplitContainer.Panel2Collapsed = false"));
        }

        [Test]
        public void InputBoxCtrlEnter_SuppressesKeyBeforeAwaitingSend()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var methodStart = source.IndexOf("private async void InputBox_KeyDown", StringComparison.Ordinal);
            var methodEnd = source.IndexOf("private void InputBox_TextChanged", StringComparison.Ordinal);
            var method = source.Substring(methodStart, methodEnd - methodStart);
            var dispatchStart = method.IndexOf("var action = SendMessageShortcutPlanner.ResolveAction", StringComparison.Ordinal);
            var dispatchBranch = method.Substring(dispatchStart);

            Assert.That(dispatchBranch.IndexOf("e.SuppressKeyPress = true;", StringComparison.Ordinal), Is.LessThan(dispatchBranch.IndexOf("await SendCurrentInputAsync();", StringComparison.Ordinal)));
            Assert.That(dispatchBranch.IndexOf("e.Handled = true;", StringComparison.Ordinal), Is.LessThan(dispatchBranch.IndexOf("await SendCurrentInputAsync();", StringComparison.Ordinal)));
        }

        [Test]
        public void SendBootstrap_ClearsUnboundDraftAfterRestoringItToNewSession()
        {
            var messageSendingSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.MessageSending.cs");
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var sendStart = messageSendingSource.IndexOf("RestoreComposeDraftState(pendingDraftState);", StringComparison.Ordinal);
            var sendEnd = messageSendingSource.IndexOf("TraceSendDiagnostic(\"Bootstrap\"", sendStart, StringComparison.Ordinal);
            var sendBootstrap = messageSendingSource.Substring(sendStart, sendEnd - sendStart);

            Assert.That(modesSource, Does.Contain("ClearCurrentModeUnboundComposeState"));
            Assert.That(sendBootstrap, Does.Contain("ClearCurrentModeUnboundComposeState();"));
        }

        [Test]
        public void SessionCreationAndActivation_UpdatePerModeSessionSelection()
        {
            var sessionsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Sessions.cs");
            var chatHistoryInteractionSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.ChatHistoryInteraction.cs");

            Assert.That(sessionsSource, Does.Contain("UpdateCurrentModeSessionSelection(session);"));
            Assert.That(sessionsSource, Does.Contain("UpdateCurrentModeSessionSelection(_currentSession);"));
            Assert.That(chatHistoryInteractionSource, Does.Contain("UpdateCurrentModeSessionSelection(session);"));
        }

        [Test]
        public void OfficeModeSelectionPersistence_RejectsCodingWorkspaceIds()
        {
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var runtimeConnectionSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.RuntimeConnection.cs");
            var officeRuntimeSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");

            Assert.That(modesSource, Does.Contain("_currentWorkspace.Kind == WorkspaceKind.Office"));
            Assert.That(modesSource, Does.Contain("target.RuntimeScopeId"));
            Assert.That(modesSource, Does.Contain("target.HostPlatform"));
            Assert.That(modesSource, Does.Not.Contain("Directory.CreateDirectory"));
            Assert.That(modesSource, Does.Contain("_currentOfficeWorkspaceId = officeWorkspace != null && officeWorkspace.Kind == WorkspaceKind.Office"));
            Assert.That(mainFormSource, Does.Contain("activeOfficeState.CurrentWorkspaceId"));
            Assert.That(officeRuntimeSource, Does.Contain("RecentWorkspaces = CloneOfficeWorkspaces(_recentWorkspaces)"));
            Assert.That(mainFormSource, Does.Contain("RestoreOfficeSelectionAfterStateLoad();"));
            Assert.That(mainFormSource, Does.Contain("if (_currentInteractionMode == AppInteractionMode.Office)"));
            Assert.That(runtimeConnectionSource, Does.Contain("await LoadWorkspaceAsync(startupWorkspacePath, false, true);"));
        }

        [Test]
        public void OfficeMode_RestoresSavedSessionWhenItStillExists()
        {
            var modesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var sessionsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Sessions.cs");
            var workspacesSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var restoreStart = sessionsSource.IndexOf("private void RestoreOfficeSelectionAfterStateLoad()", StringComparison.Ordinal);
            var restoreEnd = sessionsSource.IndexOf("private void EnsureCurrentSessionMatchesInteractionMode()", restoreStart, StringComparison.Ordinal);
            var restoreSource = sessionsSource.Substring(restoreStart, restoreEnd - restoreStart);

            Assert.That(sessionsSource, Does.Contain("ResetOfficeSessionForNewTask"));
            Assert.That(sessionsSource, Does.Contain("_currentOfficeSessionId = null"));
            Assert.That(sessionsSource, Does.Contain("RestoreOfficeSelectionAfterStateLoad"));
            Assert.That(restoreSource, Does.Contain("FindSessionById(_currentOfficeSessionId)"));
            Assert.That(restoreSource.IndexOf("FindSessionById(_currentOfficeSessionId)", StringComparison.Ordinal),
                Is.LessThan(restoreSource.IndexOf("ResetOfficeSessionForNewTask();", StringComparison.Ordinal)));
            Assert.That(modesSource, Does.Not.Contain("InvalidateAvailableModelsForModeSwitch"));
            Assert.That(modesSource, Does.Contain("SessionSelectionResolver.RestoreOfficeSelection("));
            var modeRestoreStart = modesSource.IndexOf("private void RestoreSelectionForCurrentMode()", StringComparison.Ordinal);
            var modeRestoreEnd = modesSource.IndexOf("private WorkspaceInfo EnsureOfficeWorkspace()", modeRestoreStart, StringComparison.Ordinal);
            var modeRestoreSource = modesSource.Substring(modeRestoreStart, modeRestoreEnd - modeRestoreStart);
            Assert.That(modeRestoreSource, Does.Not.Contain("ResetOfficeSessionForNewTask();"));
            Assert.That(modeRestoreSource, Does.Not.Contain("EnsureOfficeWorkspace();"));
            Assert.That(workspacesSource, Does.Contain("ResetOfficeSessionForNewTask();"));
            Assert.That(mainFormSource, Does.Contain("MainForm.Message.OfficeNewTaskReady"));
        }

        [Test]
        public void OfficeRuntimeRoot_IsResolvedAsynchronouslyAndCachedOutsideConstruction()
        {
            var mainFormSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.cs");
            var runtimeConnectionSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.RuntimeConnection.cs");
            var officeRuntimeSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");

            Assert.That(officeRuntimeSource, Does.Contain("private readonly OfficeRuntimeScopeService _officeRuntimeScopeService"));
            Assert.That(officeRuntimeSource, Does.Contain("private readonly OfficeRuntimeStateService _officeRuntimeStateService"));
            Assert.That(officeRuntimeSource, Does.Contain("private readonly IOfficeRuntimeFileSystem _officeRuntimeFileSystem"));
            Assert.That(officeRuntimeSource, Does.Contain("private string _resolvedOfficeRootPath"));
            Assert.That(officeRuntimeSource, Does.Contain("private string _officeRuntimeRootError"));
            Assert.That(officeRuntimeSource, Does.Contain("private async Task RefreshActiveOfficeRuntimeRootAsync()"));
            Assert.That(officeRuntimeSource, Does.Contain("RequiresOfficeHomeResolution(configuredRootPath)"));
            Assert.That(runtimeConnectionSource, Does.Contain("await RefreshActiveOfficeRuntimeRootAsync();"));
            Assert.That(mainFormSource, Does.Not.Contain("RefreshActiveOfficeRuntimeRootAsync().Wait"));
            Assert.That(mainFormSource, Does.Not.Contain("RefreshActiveOfficeRuntimeRootAsync().GetAwaiter"));
        }

        [Test]
        public void OfficeSessionDeletion_DefaultsLocalHistoryDeletionOn()
        {
            var sessionsSource = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Sessions.cs");

            Assert.That(sessionsSource, Does.Contain("var defaultDeleteLocalHistory = session.Mode == AppInteractionMode.Office"));
            Assert.That(sessionsSource, Does.Contain("new LocalSessionDeleteConfirmationForm("));
            Assert.That(sessionsSource, Does.Contain("defaultDeleteLocalHistory"));
        }

        [Test]
        public void OfficeDirectoryOpenMessage_UsesOfficeWording()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");

            Assert.That(source, Does.Contain("BuildWorkspaceOpenedMessage"));
            Assert.That(source, Does.Contain("MainForm.Message.OfficeDirectoryOpened.Template"));
        }

        [Test]
        public void OfficeMode_UsesSessionOnlyNavigationAndAltOneFocusesSessions()
        {
            var modes = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Modes.cs");
            var workspaces = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");

            Assert.That(modes, Does.Contain("_workspaceSectionTitleLabel.Visible = !isOfficeMode;"));
            Assert.That(modes, Does.Contain("_workspaceList.Visible = !isOfficeMode;"));
            Assert.That(modes, Does.Contain("_workspaceList.TabStop = !isOfficeMode;"));
            Assert.That(workspaces, Does.Contain("if (_currentInteractionMode == AppInteractionMode.Office)"));
            Assert.That(workspaces, Does.Contain("FocusSessionList();"));
        }

        [Test]
        public void OfficeRootChange_ValidatesBeforeSaveAndDoesNotRefreshCategories()
        {
            var options = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OptionsSettings.cs");
            var runtime = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.OfficeRuntime.cs");
            var save = ExtractMethod(options, "private async Task<Tuple<bool, bool>> SaveModeSettingsForCurrentRuntimeAsync(");

            Assert.That(save.IndexOf("await ResolveAndEnsureOfficeRootAsync(", StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0));
            Assert.That(save.IndexOf("await SaveModeSettingsForCurrentRuntimeAsync(new ModeSettingsSnapshot", StringComparison.Ordinal),
                Is.GreaterThan(save.IndexOf("await ResolveAndEnsureOfficeRootAsync(", StringComparison.Ordinal)));
            Assert.That(runtime, Does.Not.Contain("RefreshOfficeCategoriesAsync"));
            Assert.That(options, Does.Contain("MainForm.Status.OfficeDirectoryChangedFilesNotMoved"));
        }

        [Test]
        public void OfficeWorkspaceCompatibility_UsesExactRuntimeSingletonIdentity()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Workspaces.cs");
            var compatibility = ExtractMethod(source, "private bool IsOfficeWorkspaceCompatibleWithActiveRuntime");

            Assert.That(compatibility, Does.Contain("string.Equals("));
            Assert.That(compatibility, Does.Contain("\"office:\" + target.RuntimeScopeId"));
            Assert.That(compatibility, Does.Not.Contain("StartsWith("));
        }

        [Test]
        public void GoalSet_StartsGoalExecutionAfterSuccessfulReplacement()
        {
            var source = ReadSource("src", "MyCodex.WinForms", "Forms", "MainForm.Goals.cs").Replace("\r\n", "\n");

            Assert.That(source, Does.Contain("await StartSessionGoalExecutionAsync(session);"));
            Assert.That(source, Does.Contain("private async Task StartSessionGoalExecutionAsync(ChatSession session)"));
            Assert.That(source, Does.Contain("MainForm.Goal.StartPrompt"));
            Assert.That(
                source,
                Does.Contain("await SendCurrentInputAsync(\n                UiLocalization.Text(\"MainForm.Goal.StartPrompt\", \"Please start working on the current goal.\"),\n                false);"));
        }

        private static string ReadSource(params string[] parts)
        {
            return File.ReadAllText(Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                Path.Combine(parts))));
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature + " should exist.");
            var braceStart = source.IndexOf('{', start);
            Assert.That(braceStart, Is.GreaterThanOrEqualTo(0), signature + " should have a body.");
            var depth = 0;
            for (var i = braceStart; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}' && --depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }

            Assert.Fail(signature + " body should be complete.");
            return string.Empty;
        }

        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }

            return count;
        }
    }
}
