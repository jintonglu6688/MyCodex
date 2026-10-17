using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormStructureSourceTests
    {
        [Test]
        public void RemovedTurnChangesViewer_LeavesGitWorkbenchAndEncodingChecksAvailable()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.DiffWorkbench.cs"));
            Assert.That(File.Exists(GetMainFormPath("DiffWorkbenchForm.cs")), Is.False);
            Assert.That(source, Does.Not.Contain("_turnDiffSnapshots"));
            Assert.That(source, Does.Contain("new GitWorkbenchForm("));
            Assert.That(source, Does.Contain("CheckEncodingSafetyForTurn(threadId, turnId, snapshot)"));
            Assert.That(File.ReadAllText(GetMainFormPath("MainForm.Layout.cs")), Does.Not.Contain("_viewSelectedTurnChangesMenuItem"));
            Assert.That(File.ReadAllText(GetMainFormPath("MainForm.AppServerEvents.cs")),
                Does.Contain("CheckTurnEncodingSafety(e.ThreadId, state.TurnId, state.DiffText, state.ChangedFiles)"));
        }

        [Test]
        public void CompactLayout_PreservesAccessibleNamesAndCommandPlacement()
        {
            var layout = File.ReadAllText(GetMainFormPath("MainForm.Layout.cs"));
            var main = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            foreach (var removedField in new[] { "_navigationSectionTitleLabel", "_activitySectionTitleLabel", "_sessionInfoSectionTitleLabel" })
            {
                Assert.That(layout + main, Does.Not.Contain(removedField));
            }

            Assert.That(layout, Does.Contain("_workspaceList.AccessibleName = GetWorkspaceListAccessibleName()"));
            Assert.That(layout, Does.Contain("_sessionList.AccessibleName = UiLocalization.Text(\"MainForm.Accessible.Sessions\", \"Sessions\")"));
            Assert.That(layout, Does.Contain("_activityBrowseControl.AccessibleName = UiLocalization.Text(\"MainForm.Accessible.Activity\", \"Activity\")"));
            Assert.That(layout, Does.Contain("_commandMenuButton.TabIndex = 0"));
            Assert.That(layout, Does.Contain("_diffWorkbenchButton.TabIndex = 1"));
            Assert.That(layout, Does.Contain("panel.Controls.Add(CreateCommandPanel(), 0, 1)"));
            Assert.That(main, Does.Contain("Screen.FromRectangle(defaultBounds).WorkingArea"));
            Assert.That(main, Does.Contain("MainFormLayoutPlanner.ConstrainWindowBounds(Bounds, workingArea)"));
        }

        private sealed class MainFormSourceMetric
        {
            public MainFormSourceMetric(string fileName, int targetLines, int baselineLines)
            {
                FileName = fileName;
                TargetLines = targetLines;
                BaselineLines = baselineLines;
            }

            public string FileName { get; }

            public int TargetLines { get; }

            public int BaselineLines { get; }
        }

        private const int DefaultMainFormPartialTargetLines = 1200;
        private const int DefaultMainFormHelperTargetLines = 800;

        private static readonly MainFormSourceMetric[] MainFormSourceMetrics =
        {
            new MainFormSourceMetric("MainForm.cs", 4000, 3963),
            new MainFormSourceMetric("MainForm.Activity.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.AppServerEvents.cs", 1200, 1775),
            new MainFormSourceMetric("MainForm.Attachments.cs", 1200, 1613),
            new MainFormSourceMetric("MainForm.Workspaces.cs", 1200, 1354),
            new MainFormSourceMetric("MainForm.SessionSettings.cs", 1200, 1169),
            new MainFormSourceMetric("MainForm.Layout.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.Sessions.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.ScreenReader.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.DiffWorkbench.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.CommandTerminal.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.Modes.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.RuntimeSettings.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.GitHosting.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.MessageSending.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.UpdatesTray.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.Models.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.SessionWorkflows.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.ThreadSync.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.ProjectInstructions.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.RuntimeConnection.cs", 1200, 1200),
            new MainFormSourceMetric("MainForm.Status.cs", 1200, 1200),
            new MainFormSourceMetric("MainFormStatusPresenter.cs", 800, 800),
            new MainFormSourceMetric("SessionListItem.cs", 800, 800),
            new MainFormSourceMetric("WorkspaceListItem.cs", 800, 800)
        };

        private static readonly string[] Phase1GoalPlanMethodNames =
        {
            "CanToggleSessionPlanMode",
            "CanManageSessionGoal",
            "SetSessionGoalAsync",
            "StartSessionGoalExecutionAsync",
            "ViewSessionGoalAsync",
            "ClearSessionGoalAsync",
            "EnsureSessionThreadCreatedForGoalAsync",
            "RefreshSessionGoalAsync",
            "MarkPausedGoalActiveForRunningTurn",
            "RefreshGoalAfterTurnTerminal",
            "ApplyThreadGoalToSession",
            "ClearThreadGoalFromSession",
            "SetSessionPlanMode",
            "MarkCollaborationModeSent",
            "RegisterExecutablePlanCandidate",
            "PromptForPlanExecutionIfReadyAsync",
            "ForwardRemotePlanExecutionRequestedAsync",
            "ForwardRemotePlanExecutionCompletedAsync",
            "RespondRemotePlanExecutionAsync",
            "ExecuteAcceptedPlanAsync"
        };

        private static readonly string[] Phase2ThreadSyncMethodNames =
        {
            "OnSyncThreadsMenuItemClick",
            "AutomaticThreadSyncTimer_Tick",
            "AutomaticThreadSyncFollowUpTimer_Tick",
            "ScheduleAutomaticThreadSyncFollowUp",
            "ScheduleAutomaticThreadSyncFollowUpTimer",
            "RefreshLocalThreadSessionWatchersAsync",
            "ConfigureLocalThreadSessionWatchers",
            "DisposeLocalThreadSessionWatchers",
            "LocalThreadSessionFileChanged",
            "LocalThreadSessionFileRenamed",
            "LocalThreadSessionWatcherError",
            "ShowThreadImportDialogAsync",
            "RefreshAfterImportedThreadsAsync",
            "ShowArchivedThreadRestoreDialogAsync",
            "RestoreArchivedThreadsAsync",
            "BelongsToKnownWorkspace",
            "FilterThreadsForCurrentInteractionMode",
            "ShouldShowThreadForCurrentInteractionMode",
            "ShouldBootstrapRuntimeEndpointHistorySync",
            "ImportThreads",
            "BuildAutomationThreadTitleMap",
            "FilterImportedProjectInstructionHelperThreads",
            "RemoveProjectInstructionsHelperSessionLocally",
            "PreloadThreadWorkspaceGitInfoAsync",
            "RefreshKnownUnixWorkspaceGitInfoAsync",
            "RefreshUnixWorkspaceGitInfoCacheAsync",
            "UpdateWorkspaceGitBranchFromSnapshot"
        };

        private static readonly string[] Phase3ChatHistoryMethodNames =
        {
            "ActivateSessionAsync",
            "LoadSessionHistoryAsync",
            "LoadActivatedSessionHistoryAsync",
            "ApplySessionHistoryLoadResult",
            "IsCurrentSessionHistoryLoadTarget",
            "ChatHistoryListBox_KeyDown",
            "ChatHistoryListBox_MouseDown",
            "ChatHistoryListBox_Enter",
            "ChatHistoryListBox_SelectedIndexChanged",
            "FocusChatHistoryList",
            "FocusInputBox",
            "FocusLogTextBox",
            "ChatHistoryContextMenu_Popup",
            "UpdateChatHistoryPlanModeMenuState",
            "UpdateChatHistoryGoalMenuState",
            "UpdateChatHistoryForkMenuState",
            "UpdateChatHistoryCompactionMenuState",
            "UpdateChatHistoryReviewMenuState",
            "CopySelectedChatItemToClipboard",
            "ScheduleRealtimeCurrentSessionHistoryReload",
            "ReloadCurrentSessionHistoryAfterRealtimeEventAsync",
            "ReloadCurrentSessionHistorySilentlyAsync",
            "ReloadCurrentSessionHistoryAsync",
            "ReplaceChatHistoryItems",
            "ApplyChatHistoryFilters",
            "ShouldDisplayChatHistoryItem"
        };

        private static readonly string[] Phase4RuntimeConnectionMethodNames =
        {
            "InitializeAppAsync",
            "EnsureAppServerConnectedAsync",
            "EnsureRuntimeEndpointReadyAsync",
            "RequiresRuntimeEndpointStorage",
            "TryRecoverAppServerConnectionAsync",
            "EnsureAppServerAvailableForActionAsync",
            "RefreshAuthenticationStateAsync",
            "RefreshAuthenticationUi",
            "UpdateAutomaticThreadSyncState"
        };

        private static readonly string[] Phase5SessionBranchingMethodNames =
        {
            "GenerateProjectInstructionsContentAsync",
            "CleanupProjectInstructionsHelperThreadAsync",
            "SendProjectInstructionsPromptAsync",
            "StartCurrentSessionCompactionAsync",
            "StartForkFromSelectedHistoryItemAsync",
            "StartFullForkFromCurrentSessionAsync",
            "TryGetForkTrimTurnsFromHistoryItem",
            "TryMoveChatHistorySelectionByTurn",
            "CreateForkSessionAsync",
            "GetNextForkIndex",
            "BuildUniqueSessionTitle",
        };

        private static readonly string[] Phase6ActivityLogMethodNames =
        {
            "AppendLog",
            "LogFlushTimer_Tick",
            "FlushPendingLogText",
            "ClearRenderedLog",
            "ClearAllLogBuffers",
            "HasRecordedLogContent",
            "HasActivityTimelineContent",
            "EnsurePendingLogFlushScheduled",
            "RefreshActivityDisplay",
            "RenderActivityDisplay",
            "BuildCurrentActivityTimelineItems",
            "ShouldShowActivityLog"
        };

        private static readonly string[] Phase6StatusTitleMethodNames =
        {
            "SetStatus",
            "SetLowPriorityStatus",
            "SetHighPriorityStatus",
            "DetermineStatusType",
            "RefreshHeader",
            "BuildOfficeHeaderSummary",
            "RefreshDisplayedSessionTitle",
            "UpdateThreadSyncSummary",
            "RefreshWindowTitle",
            "ComposeWindowTitle",
            "BuildRuntimeFileWindowTitle",
            "GetCurrentRuntimeDisplayName",
            "GetWindowStatusText"
        };

        [Test]
        public void MainFormSourceMetrics_ReportSizeTargetsAndBaselines()
        {
            var report = BuildMainFormSourceMetricsReport();
            TestContext.Out.WriteLine(report);

            var fileNames = GetCurrentMainFormSourceFileNames();
            Assert.That(
                fileNames,
                Is.Not.Empty,
                "MainForm source metrics should continue to report large-file size targets and baselines.");
        }

        [Test]
        public void MainFormCoreFile_DoesNotRegainExtractedWorkflowImplementations()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.cs"));

            Assert.That(source, Does.Not.Contain("private async Task SendCurrentInputAsync(string overrideText, bool includePendingAttachments)"));
            Assert.That(source, Does.Not.Contain("private async Task<ChatWorkbenchSendResult> SendSubAgentInputAsync("));
            Assert.That(source, Does.Not.Contain("private SendOperationContext CaptureSendOperationContext()"));
            Assert.That(source, Does.Not.Contain("private async Task OpenAppServerRuntimeSettingsAsync()"));
            Assert.That(source, Does.Not.Contain("private async Task OpenModelSettingsAsync("));
            Assert.That(source, Does.Not.Contain("private async Task OpenPermissionSettingsAsync("));
            Assert.That(source, Does.Not.Contain("private async Task OpenOptionsAsync()"));
            Assert.That(source, Does.Not.Contain("private async Task RunLocalCurrentSubAgentThreadSyncAsync()"));
            Assert.That(source, Does.Not.Contain("private async Task RunAutomaticThreadSyncAsync("));
            Assert.That(source, Does.Not.Contain("private async Task LoadAvailableModelsAsync("));
            Assert.That(source, Does.Not.Contain("private void RebuildModelMenu()"));
            Assert.That(source, Does.Not.Contain("private async Task RunWorkspaceCommandAsync("));
        }

        [Test]
        public void MainForm_OfficeRuntimeStateAndRootLogicLivesInDedicatedPartial()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var officeRuntimePath = GetMainFormPath("MainForm.OfficeRuntime.cs");
            var officeRuntimeSource = File.Exists(officeRuntimePath)
                ? File.ReadAllText(officeRuntimePath)
                : string.Empty;

            Assert.That(File.Exists(officeRuntimePath), Is.True);
            Assert.That(officeRuntimeSource, Does.Contain("partial class MainForm"));
            Assert.That(officeRuntimeSource, Does.Contain("RefreshActiveOfficeRuntimeRootAsync"));
            Assert.That(officeRuntimeSource, Does.Contain("BuildOfficeRuntimeStates"));
            Assert.That(mainSource, Does.Not.Contain("private async Task RefreshActiveOfficeRuntimeRootAsync()"));
        }

        [Test]
        public void MainForm_ModeActivationAndRuntimeTransitionRemainInBoundedPartials()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var modesSource = File.ReadAllText(GetMainFormPath("MainForm.Modes.cs"));
            var runtimeWindowsSource = File.ReadAllText(GetMainFormPath("MainForm.RuntimeWindows.cs"));

            Assert.That(modesSource, Does.Contain("private void ActivateSelectedModeTab()"));
            Assert.That(modesSource, Does.Contain("private void CaptureCurrentModeSelection()"));
            Assert.That(runtimeWindowsSource, Does.Contain("private bool PreflightRuntimeWindowsForTransition()"));
            Assert.That(runtimeWindowsSource, Does.Contain("private async Task StopCurrentRuntimeActivityAsync(string reason)"));
            Assert.That(mainSource, Does.Not.Contain("private void ActivateSelectedModeTab()"));
            Assert.That(mainSource, Does.Not.Contain("private async Task StopCurrentRuntimeActivityAsync(string reason)"));
        }

        [Test]
        public void MainFormCoreFile_PhaseCandidateResponsibilitiesDoNotExpandBeforeExtraction()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.cs"));

            Assert.That(
                CountPrivateMethodDeclarations(source, Phase1GoalPlanMethodNames),
                Is.EqualTo(0),
                "Phase 1 Goal/Plan/Remote Plan Execution methods should live outside MainForm.cs after Phase 1.");
            Assert.That(
                CountPrivateMethodDeclarations(source, Phase2ThreadSyncMethodNames),
                Is.EqualTo(0),
                "Phase 2 Thread Import/Sync/Watcher methods should live outside MainForm.cs after Phase 2.");
            Assert.That(
                CountPrivateMethodDeclarations(source, Phase3ChatHistoryMethodNames),
                Is.EqualTo(0),
                "Phase 3 Chat History Interaction/Reload methods should live outside MainForm.cs after Phase 3.");
            Assert.That(
                CountPrivateMethodDeclarations(source, Phase4RuntimeConnectionMethodNames),
                Is.EqualTo(0),
                "Phase 4 Runtime Startup/Connection/Gateway Lifecycle methods should live outside MainForm.cs after Phase 4.");
            Assert.That(
                CountPrivateMethodDeclarations(source, Phase5SessionBranchingMethodNames),
                Is.EqualTo(0),
                "Phase 5 Project Instructions / Session Branching methods should live outside MainForm.cs after Phase 5.");
            Assert.That(
                CountPrivateMethodDeclarations(source, Phase6ActivityLogMethodNames.Concat(Phase6StatusTitleMethodNames).ToArray()),
                Is.EqualTo(0),
                "Phase 6 Activity/Log/Status/Title methods should live outside MainForm.cs after Phase 6.");
        }

        [Test]
        public void MainForm_GoalPlanWorkflowsLiveInDedicatedPartialAndUsePlanner()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var goalsPath = GetMainFormPath("MainForm.Goals.cs");
            var goalsSource = File.Exists(goalsPath) ? File.ReadAllText(goalsPath) : string.Empty;
            var plannerPath = GetMainFormPath("SessionPlanModePlanner.cs");
            var plannerSource = File.Exists(plannerPath) ? File.ReadAllText(plannerPath) : string.Empty;

            Assert.That(File.Exists(goalsPath), Is.True, "MainForm.Goals.cs should exist after Phase 1.");
            Assert.That(goalsSource, Does.Contain("partial class MainForm"));
            Assert.That(File.Exists(plannerPath), Is.True, "SessionPlanModePlanner.cs should exist after Phase 1.");
            Assert.That(plannerSource, Does.Contain("internal static class SessionPlanModePlanner"));
            Assert.That(goalsSource, Does.Contain("SessionPlanModePlanner.CanChangePlanOrGoalState"));
            Assert.That(CountPrivateMethodDeclarations(mainSource, Phase1GoalPlanMethodNames), Is.EqualTo(0));
            Assert.That(CountPrivateMethodDeclarations(goalsSource, Phase1GoalPlanMethodNames), Is.GreaterThanOrEqualTo(20));
        }

        [Test]
        public void MainForm_ThreadSyncWorkflowsLiveInDedicatedPartialAndUsePlanner()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var threadSyncPath = GetMainFormPath("MainForm.ThreadSync.cs");
            var threadSyncSource = File.Exists(threadSyncPath) ? File.ReadAllText(threadSyncPath) : string.Empty;
            var plannerPath = GetMainFormPath("ThreadImportFilterPlanner.cs");
            var plannerSource = File.Exists(plannerPath) ? File.ReadAllText(plannerPath) : string.Empty;

            Assert.That(File.Exists(threadSyncPath), Is.True, "MainForm.ThreadSync.cs should exist after Phase 2.");
            Assert.That(threadSyncSource, Does.Contain("partial class MainForm"));
            Assert.That(File.Exists(plannerPath), Is.True, "ThreadImportFilterPlanner.cs should exist after Phase 2.");
            Assert.That(plannerSource, Does.Contain("internal static class ThreadImportFilterPlanner"));
            Assert.That(threadSyncSource, Does.Contain("ThreadImportFilterPlanner.BelongsToKnownWorkspace"));
            Assert.That(threadSyncSource, Does.Contain("ThreadImportFilterPlanner.FilterProjectInstructionHelperThreads"));
            Assert.That(CountPrivateMethodDeclarations(mainSource, Phase2ThreadSyncMethodNames), Is.EqualTo(0));
            Assert.That(CountPrivateMethodDeclarations(threadSyncSource, Phase2ThreadSyncMethodNames), Is.GreaterThanOrEqualTo(27));
        }

        [Test]
        public void MainForm_ChatHistoryInteractionLivesInDedicatedPartialAndUsesPlanner()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var chatHistoryPath = GetMainFormPath("MainForm.ChatHistoryInteraction.cs");
            var chatHistorySource = File.Exists(chatHistoryPath) ? File.ReadAllText(chatHistoryPath) : string.Empty;
            var plannerPath = GetMainFormPath("ChatHistoryContextMenuPlanner.cs");
            var plannerSource = File.Exists(plannerPath) ? File.ReadAllText(plannerPath) : string.Empty;

            Assert.That(File.Exists(chatHistoryPath), Is.True, "MainForm.ChatHistoryInteraction.cs should exist after Phase 3.");
            Assert.That(chatHistorySource, Does.Contain("partial class MainForm"));
            Assert.That(File.Exists(plannerPath), Is.True, "ChatHistoryContextMenuPlanner.cs should exist after Phase 3.");
            Assert.That(plannerSource, Does.Contain("internal static class ChatHistoryContextMenuPlanner"));
            Assert.That(chatHistorySource, Does.Contain("ChatHistoryContextMenuPlanner.BuildBasicState"));
            Assert.That(chatHistorySource, Does.Contain("ChatHistoryContextMenuPlanner.BuildPlanModeState"));
            Assert.That(chatHistorySource, Does.Contain("ChatHistoryContextMenuPlanner.BuildGoalState"));
            Assert.That(CountPrivateMethodDeclarations(mainSource, Phase3ChatHistoryMethodNames), Is.EqualTo(0));
            Assert.That(CountPrivateMethodDeclarations(chatHistorySource, Phase3ChatHistoryMethodNames), Is.GreaterThanOrEqualTo(28));
        }

        [Test]
        public void MainForm_RuntimeConnectionLifecycleLivesInDedicatedPartial()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var runtimeConnectionPath = GetMainFormPath("MainForm.RuntimeConnection.cs");
            var runtimeConnectionSource = File.Exists(runtimeConnectionPath) ? File.ReadAllText(runtimeConnectionPath) : string.Empty;

            Assert.That(File.Exists(runtimeConnectionPath), Is.True, "MainForm.RuntimeConnection.cs should exist after Phase 4.");
            Assert.That(runtimeConnectionSource, Does.Contain("partial class MainForm"));
            Assert.That(CountPrivateMethodDeclarations(mainSource, Phase4RuntimeConnectionMethodNames), Is.EqualTo(0));
            Assert.That(CountPrivateMethodDeclarations(runtimeConnectionSource, Phase4RuntimeConnectionMethodNames), Is.EqualTo(Phase4RuntimeConnectionMethodNames.Length));
            Assert.That(runtimeConnectionSource, Does.Contain("IsAppServerConnectivityFailure"));
            Assert.That(mainSource, Does.Not.Contain("private static bool IsAppServerConnectivityFailure("));
        }

        [Test]
        public void MainForm_ActivityLogAndStatusTitleLiveInDedicatedPartials()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var activityPath = GetMainFormPath("MainForm.Activity.cs");
            var statusPath = GetMainFormPath("MainForm.Status.cs");
            var presenterPath = GetMainFormPath("MainFormStatusPresenter.cs");
            var activitySource = File.Exists(activityPath) ? File.ReadAllText(activityPath) : string.Empty;
            var statusSource = File.Exists(statusPath) ? File.ReadAllText(statusPath) : string.Empty;
            var presenterSource = File.Exists(presenterPath) ? File.ReadAllText(presenterPath) : string.Empty;

            Assert.That(File.Exists(activityPath), Is.True, "MainForm.Activity.cs should exist after Phase 6.");
            Assert.That(File.Exists(statusPath), Is.True, "MainForm.Status.cs should exist after Phase 6.");
            Assert.That(File.Exists(presenterPath), Is.True, "MainFormStatusPresenter.cs should exist after Phase 6.");
            Assert.That(activitySource, Does.Contain("partial class MainForm"));
            Assert.That(statusSource, Does.Contain("partial class MainForm"));
            Assert.That(presenterSource, Does.Contain("internal static class MainFormStatusPresenter"));
            Assert.That(CountPrivateMethodDeclarations(mainSource, Phase6ActivityLogMethodNames.Concat(Phase6StatusTitleMethodNames).ToArray()), Is.EqualTo(0));
            Assert.That(CountPrivateMethodDeclarations(activitySource, Phase6ActivityLogMethodNames), Is.EqualTo(Phase6ActivityLogMethodNames.Length));
            Assert.That(CountPrivateMethodDeclarations(statusSource, Phase6StatusTitleMethodNames), Is.GreaterThanOrEqualTo(Phase6StatusTitleMethodNames.Length));
            Assert.That(statusSource, Does.Contain("MainFormStatusPresenter.DetermineStatusType"));
            Assert.That(statusSource, Does.Contain("MainFormWindowTitleComposer."));
            Assert.That(activitySource, Does.Contain("_activityBrowseControl.SetItems(items)"));
        }

        [Test]
        public void Phase7ListItems_LiveOutsideMainFormCoreFile()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var sessionListItemPath = GetMainFormPath("SessionListItem.cs");
            var workspaceListItemPath = GetMainFormPath("WorkspaceListItem.cs");
            var sessionListItemSource = File.Exists(sessionListItemPath) ? File.ReadAllText(sessionListItemPath) : string.Empty;
            var workspaceListItemSource = File.Exists(workspaceListItemPath) ? File.ReadAllText(workspaceListItemPath) : string.Empty;

            Assert.That(mainSource, Does.Not.Contain("class SessionListItem"));
            Assert.That(mainSource, Does.Not.Contain("class WorkspaceListItem"));
            Assert.That(File.Exists(sessionListItemPath), Is.True, "SessionListItem.cs should exist after Phase 7.");
            Assert.That(File.Exists(workspaceListItemPath), Is.True, "WorkspaceListItem.cs should exist after Phase 7.");
            Assert.That(sessionListItemSource, Does.Contain("internal sealed class SessionListItem"));
            Assert.That(sessionListItemSource, Does.Contain("public ListViewItem CreateListViewItem()"));
            Assert.That(workspaceListItemSource, Does.Contain("internal sealed class WorkspaceListItem"));
            Assert.That(workspaceListItemSource, Does.Contain("public ListViewItem CreateListViewItem(bool showSource)"));
            Assert.That(workspaceListItemSource, Does.Not.Contain("categoryNameOnly"));
        }

        [Test]
        public void ExistingMainFormPartials_DeclarePartialClass()
        {
            var expectedFiles = new[]
            {
                "MainForm.cs",
                "MainForm.ChatSearch.cs",
                "MainForm.DiffWorkbench.cs",
                "MainForm.GitHosting.cs",
                "MainForm.ScreenReader.cs",
                "MainForm.SessionSettings.cs"
            };

            for (var i = 0; i < expectedFiles.Length; i++)
            {
                var path = GetMainFormPath(expectedFiles[i]);
                Assert.That(File.Exists(path), Is.True, expectedFiles[i] + " should exist.");
                Assert.That(File.ReadAllText(path), Does.Contain("partial class MainForm"));
            }
        }

        [Test]
        public void FutureMainFormPartials_ShouldExistAfterPhase2()
        {
            var futureFiles = new[]
            {
                "MainForm.Layout.cs",
                "MainForm.Attachments.cs",
                "MainForm.Workspaces.cs",
                "MainForm.Sessions.cs",
                "MainForm.AppServerEvents.cs",
                "MainForm.CommandTerminal.cs",
                "MainForm.UpdatesTray.cs"
            };

            for (var i = 0; i < futureFiles.Length; i++)
            {
                var path = GetMainFormPath(futureFiles[i]);
                Assert.That(File.Exists(path), Is.True, futureFiles[i] + " should exist.");
                Assert.That(File.ReadAllText(path), Does.Contain("partial class MainForm"));
            }
        }

        [Test]
        public void Phase5BMainFormHelpers_ExistWithExpectedTypes()
        {
            var helpers = new[]
            {
                new[] { "MainFormModelOptionComposer.cs", "internal static class MainFormModelOptionComposer" },
                new[] { "ChatHistoryItemFactory.cs", "internal static class ChatHistoryItemFactory" },
                new[] { "MainFormWindowTitleComposer.cs", "internal static class MainFormWindowTitleComposer" },
                new[] { "MainFormStatusPresenter.cs", "internal static class MainFormStatusPresenter" },
                new[] { "MainFormInputStatePlanner.cs", "internal static class MainFormInputStatePlanner" },
                new[] { "MainFormAutomationNavigationPlanner.cs", "internal static class MainFormAutomationNavigationPlanner" },
                new[] { "MainFormSessionWorkspacePlanner.cs", "internal static class MainFormSessionWorkspacePlanner" },
                new[] { "MainFormWorkbenchStatePlanner.cs", "internal static class MainFormWorkbenchStatePlanner" }
            };

            for (var i = 0; i < helpers.Length; i++)
            {
                var path = GetMainFormPath(helpers[i][0]);
                Assert.That(File.Exists(path), Is.True, helpers[i][0] + " should exist.");
                Assert.That(File.ReadAllText(path), Does.Contain(helpers[i][1]));
            }
        }

        [Test]
        public void Phase6AuthenticationPresentationHelper_ExistsAndSessionSettingsDelegatesToIt()
        {
            var helperPath = GetMainFormPath("MainFormAuthenticationStatePlanner.cs");
            var helperSource = File.Exists(helperPath) ? File.ReadAllText(helperPath) : string.Empty;
            var sessionSettingsSource = File.ReadAllText(GetMainFormPath("MainForm.SessionSettings.cs"));

            Assert.That(File.Exists(helperPath), Is.True, "MainFormAuthenticationStatePlanner.cs should exist.");
            Assert.That(helperSource, Does.Contain("internal static class MainFormAuthenticationStatePlanner"));
            Assert.That(sessionSettingsSource, Does.Contain("MainFormAuthenticationStatePlanner."));
            Assert.That(helperSource, Does.Not.Contain("AuthenticationCenterForm"));
            Assert.That(helperSource, Does.Not.Contain("AuthenticationProfileStore"));
            Assert.That(helperSource, Does.Not.Contain("OpenRouterAccountStore"));
            Assert.That(helperSource, Does.Not.Contain("RuntimeSessionDefaultsStore"));
        }

        [Test]
        public void MainFormCoreFile_DelegatesPhase5BPureResponsibilities()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var statusSource = File.ReadAllText(GetMainFormPath("MainForm.Status.cs"));
            var automationSource = File.ReadAllText(GetMainFormPath("MainForm.Automation.cs"));

            Assert.That(automationSource, Does.Contain("MainFormModelOptionComposer."));
            Assert.That(source, Does.Contain("ChatHistoryItemFactory."));
            Assert.That(statusSource, Does.Contain("MainFormWindowTitleComposer."));
            Assert.That(source, Does.Contain("MainFormInputStatePlanner.Build"));
            Assert.That(source, Does.Not.Contain("private static int CompareModelOptions("));
            Assert.That(source, Does.Not.Contain("private static ModelSortKey BuildModelSortKey("));
            Assert.That(source, Does.Not.Contain("private static string GetReasoningEffortText("));
            Assert.That(source, Does.Not.Contain("private ChatHistoryItem CreateChatHistoryItem("));
            Assert.That(source, Does.Not.Contain("private string BuildWindowTitle("));
        }

        [Test]
        public void MainForm_AutomationIntegration_UsesDedicatedRuntimeHost()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.cs"));

            Assert.That(source, Does.Contain("AutomationRuntimeHost"));
            Assert.That(source, Does.Not.Contain("BuildDueRunIds("));
            Assert.That(source, Does.Not.Contain("AutomationRunStatus.Running"));
        }

        [Test]
        public void MainForm_DelegatesAutomationNavigationDecisions()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.Automation.cs"));

            Assert.That(source, Does.Contain("MainFormAutomationNavigationPlanner."));
            Assert.That(source, Does.Contain("MainFormAutomationNavigationPlanner.CanOpenThread(run)"));
            Assert.That(source, Does.Contain("MainFormAutomationNavigationPlanner.BuildPathDecision("));
            Assert.That(source, Does.Contain("MainFormAutomationNavigationPlanner.IsRemotePath(path)"));
            Assert.That(source, Does.Not.Contain("private bool IsAutomationRemotePath(string path)"));
        }

        [Test]
        public void MainForm_DelegatesSessionWorkspacePlanning()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.Automation.cs"));
            var navigationScopeBlock = ExtractBlock(
                source,
                "private void ApplyAutomationRunNavigationScope",
                "private bool IsAutomationOfficePath");

            Assert.That(navigationScopeBlock, Does.Contain("MainFormSessionWorkspacePlanner.ResolveWorkspace("));
            Assert.That(navigationScopeBlock, Does.Contain("MainFormSessionWorkspacePlanner.ResolveInteractionMode("));
            Assert.That(navigationScopeBlock, Does.Contain("MainFormSessionWorkspacePlanner.FindWorkspaceByPath(_recentWorkspaces, path)"));
            Assert.That(navigationScopeBlock, Does.Contain("FindWorkspaceById"));
            Assert.That(navigationScopeBlock, Does.Contain("IsAutomationOfficePath"));
            Assert.That(source, Does.Not.Contain("private WorkspaceInfo ResolveAutomationRunWorkspace("));
            Assert.That(source, Does.Not.Contain("private AppInteractionMode ResolveAutomationRunInteractionMode("));
            Assert.That(source, Does.Not.Contain("private string ResolveAutomationRunWorkspacePath("));
            Assert.That(source, Does.Not.Contain("private WorkspaceInfo FindWorkspaceByPath("));
            Assert.That(source, Does.Not.Contain("private static bool AreAutomationPathsEqual("));
        }

        [Test]
        public void AppServerEvents_AutomationTerminalEventsRefreshOpenAutomationsForm()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.AppServerEvents.cs"));
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.Automation.cs"));
            var recoverySource = File.ReadAllText(GetMainFormPath("MainForm.PersistenceRecovery.cs"));
            var block = ExtractBlock(
                source,
                "if (AppServerEventRouting.IsTurnTerminal(route))",
                "HandleReasoningSummaryChatMessageEvent(e, route);");

            Assert.That(mainSource, Does.Contain("CreateAutomationRunActivityCallbacks()"));
            Assert.That(mainSource, Does.Contain("SubscribeAutomationRunActivityUpdates"));
            Assert.That(mainSource, Does.Contain("NotifyAutomationRunActivitySubscribers"));
            Assert.That(block, Does.Contain("bool automationUpdated;"));
            Assert.That(block, Does.Contain("TryHandleAutomationTurnTerminal(e, completion, out automationUpdated)"));
            Assert.That(block, Does.Contain("MainFormWorkbenchStatePlanner."));
            Assert.That(block, Does.Contain("UpdateTurnProgressSummaryState(e);"));
            Assert.That(block, Does.Contain("AutomationRunCompletionBuilder.Build("));
            Assert.That(block, Does.Contain("EnsureThreadTurnSummaryState(e.ThreadId)"));
            Assert.That(recoverySource, Does.Contain("HandleTurnTerminal(e.ThreadId, e.TurnId, completion)"));
            Assert.That(block, Does.Contain("_automationsForm.RefreshAutomationState();"));
            Assert.That(block, Does.Not.Contain("HandleAutomationRunCompletedNotification"));
            Assert.That(
                block.IndexOf("UpdateTurnProgressSummaryState(e);", System.StringComparison.Ordinal),
                Is.LessThan(block.IndexOf("AutomationRunCompletionBuilder.Build(", System.StringComparison.Ordinal)));
            Assert.That(
                block.IndexOf("TryHandleAutomationTurnTerminal(e, completion, out automationUpdated)", System.StringComparison.Ordinal),
                Is.LessThan(block.IndexOf("_automationsForm.RefreshAutomationState();", System.StringComparison.Ordinal)));
        }

        [Test]
        public void MainForm_AutomationsFormReceivesRunActionCallbacks()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.Automation.cs"));

            Assert.That(source, Does.Contain("CreateAutomationRunActionCallbacks()"));
            Assert.That(source, Does.Contain("CreateAutomationRunActivityCallbacks()"));
            Assert.That(source, Does.Contain("private AutomationRunActivityCallbacks CreateAutomationRunActivityCallbacks()"));
            Assert.That(source, Does.Contain("GetActivityItems = GetAutomationRunActivityItems"));
            Assert.That(source, Does.Contain("SubscribeActivityUpdates = SubscribeAutomationRunActivityUpdates"));
            Assert.That(source, Does.Contain("StopAutomationRunAsync"));
            Assert.That(source, Does.Contain("_appServerClient.InterruptAsync(run.ThreadId"));
        }

        [Test]
        public void MainForm_AutomationsFormReceivesModelSelectionCallbacks()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.Automation.cs"));

            Assert.That(source, Does.Contain("CreateAutomationModelSelectionCallbacks()"));
            Assert.That(source, Does.Contain("LoadAutomationModelOptionsAsync"));
            Assert.That(source, Does.Contain("EnsureAvailableModelsLoadedAsync()"));
            Assert.That(source, Does.Contain("MainFormModelOptionComposer.BuildOrderedModelOptions(_availableModels)"));
            Assert.That(source, Does.Contain("SupportsReasoningEffortForModel = SupportsReasoningEffortForModel"));
        }

        [Test]
        public void MainForm_MessageSendingWorkflowLivesInDedicatedPartial()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var messageSource = File.ReadAllText(GetMainFormPath("MainForm.MessageSending.cs"));

            Assert.That(File.Exists(GetMainFormPath("MainForm.MessageSending.cs")), Is.True);
            Assert.That(messageSource, Does.Contain("partial class MainForm"));
            Assert.That(messageSource, Does.Contain("private async Task SendCurrentInputAsync(string overrideText, bool includePendingAttachments)"));
            Assert.That(messageSource, Does.Contain("private async Task<ChatWorkbenchSendResult> SendSubAgentInputAsync("));
            Assert.That(messageSource, Does.Contain("private SendOperationContext CaptureSendOperationContext()"));
            Assert.That(mainSource, Does.Not.Contain("private async Task SendCurrentInputAsync(string overrideText, bool includePendingAttachments)"));
            Assert.That(mainSource, Does.Not.Contain("private async Task<ChatWorkbenchSendResult> SendSubAgentInputAsync("));
            Assert.That(mainSource, Does.Not.Contain("private SendOperationContext CaptureSendOperationContext()"));
        }

        [Test]
        public void MainForm_SessionDialogWorkflowsLiveInDedicatedPartial()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var workflowSource = File.ReadAllText(GetMainFormPath("MainForm.SessionWorkflows.cs"));

            Assert.That(File.Exists(GetMainFormPath("MainForm.SessionWorkflows.cs")), Is.True);
            Assert.That(workflowSource, Does.Contain("partial class MainForm"));
            Assert.That(workflowSource, Does.Contain("private async Task ShowReviewStartDialogAsync("));
            Assert.That(mainSource, Does.Not.Contain("private async Task ShowReviewStartDialogAsync("));
        }

        [Test]
        public void MainForm_ForkWorkflowsPassSelectedTurnIdToSegmentedThreadWorkflow()
        {
            var mainWorkflowSource = File.ReadAllText(GetMainFormPath("MainForm.SessionWorkflows.cs"));
            var threadWorkflowSource = File.ReadAllText(GetMainFormPath("SessionThreadOperationWorkflow.cs"));

            Assert.That(threadWorkflowSource, Does.Contain("IAppServerThreadClient appServerClient"));
            Assert.That(threadWorkflowSource, Does.Contain("string lastTurnId"));
            Assert.That(mainWorkflowSource, Does.Contain("ShowForkSessionDialogAsync(_currentSession, forkTipText, trimTurnsAfterAnchor, selectedItem.TurnId)"));
            Assert.That(mainWorkflowSource, Does.Contain("await ShowForkSessionDialogAsync(_currentSession);"));
        }

        [Test]
        public void MainForm_Phase5ProjectInstructionsAndBranchingLiveOutsideCoreFile()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var workflowSource = File.ReadAllText(GetMainFormPath("MainForm.SessionWorkflows.cs"));
            var projectInstructionsSource = File.ReadAllText(GetMainFormPath("MainForm.ProjectInstructions.cs"));

            Assert.That(File.Exists(GetMainFormPath("MainForm.ProjectInstructions.cs")), Is.True);
            Assert.That(projectInstructionsSource, Does.Contain("partial class MainForm"));
            Assert.That(projectInstructionsSource, Does.Contain("private async Task<string> GenerateProjectInstructionsContentAsync("));
            Assert.That(projectInstructionsSource, Does.Contain("private async Task CleanupProjectInstructionsHelperThreadAsync("));
            Assert.That(projectInstructionsSource, Does.Contain("private async Task<string> SendProjectInstructionsPromptAsync("));
            Assert.That(projectInstructionsSource, Does.Contain("private static string BuildProjectInstructionsBasicTemplateFallback()"));

            Assert.That(workflowSource, Does.Contain("private async Task StartCurrentSessionCompactionAsync()"));
            Assert.That(workflowSource, Does.Contain("private async Task CreateForkSessionAsync("));

            Assert.That(mainSource, Does.Not.Contain("private async Task<string> GenerateProjectInstructionsContentAsync("));
            Assert.That(mainSource, Does.Not.Contain("private async Task StartCurrentSessionCompactionAsync()"));
            Assert.That(mainSource, Does.Not.Contain("private async Task CreateForkSessionAsync("));
        }

        [Test]
        public void MainForm_ThreadSyncWorkflowLivesInDedicatedPartial()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var syncSource = File.ReadAllText(GetMainFormPath("MainForm.ThreadSync.cs"));

            Assert.That(File.Exists(GetMainFormPath("MainForm.ThreadSync.cs")), Is.True);
            Assert.That(syncSource, Does.Contain("partial class MainForm"));
            Assert.That(File.ReadAllText(GetMainFormPath("MainForm.SubAgents.cs")), Does.Contain("private async Task RunLocalCurrentSubAgentThreadSyncAsync()"));
            Assert.That(syncSource, Does.Contain("private async Task RunAutomaticThreadSyncAsync("));
            Assert.That(syncSource, Does.Contain("private async Task ShowThreadAssociationDialogAsync("));
            Assert.That(mainSource, Does.Not.Contain("private async Task RunLocalCurrentSubAgentThreadSyncAsync()"));
            Assert.That(mainSource, Does.Not.Contain("private async Task RunAutomaticThreadSyncAsync("));
            Assert.That(mainSource, Does.Not.Contain("private async Task ShowThreadAssociationDialogAsync("));
        }

        [Test]
        public void MainForm_ModelAndWorkspaceCommandWorkflowsLiveInDedicatedPartials()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var modelSource = File.ReadAllText(GetMainFormPath("MainForm.Models.cs"));
            var commandSource = File.ReadAllText(GetMainFormPath("MainForm.CommandTerminal.cs"));

            Assert.That(File.Exists(GetMainFormPath("MainForm.Models.cs")), Is.True);
            Assert.That(modelSource, Does.Contain("partial class MainForm"));
            Assert.That(modelSource, Does.Contain("private async Task LoadAvailableModelsAsync("));
            Assert.That(modelSource, Does.Not.Contain("private void RebuildModelMenu()"));
            Assert.That(commandSource, Does.Contain("private async Task RunWorkspaceCommandAsync("));
            Assert.That(mainSource, Does.Not.Contain("private async Task LoadAvailableModelsAsync("));
            Assert.That(mainSource, Does.Not.Contain("private void RebuildModelMenu()"));
            Assert.That(mainSource, Does.Not.Contain("private async Task RunWorkspaceCommandAsync("));
        }

        [Test]
        public void MainForm_ConstructorDelegatesPostReadonlyUiInitialization()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var layoutSource = File.ReadAllText(GetMainFormPath("MainForm.Layout.cs"));
            var constructorBlock = ExtractBlock(mainSource, "public MainForm(", "public void ApplyStartupArguments");

            Assert.That(constructorBlock, Does.Contain("ConfigureConstructedMainFormUi();"));
            Assert.That(layoutSource, Does.Contain("private void ConfigureConstructedMainFormUi()"));
            Assert.That(layoutSource, Does.Contain("_workspaceList.TabIndex = 0;"));
            Assert.That(layoutSource, Does.Contain("ConfigureSessionSettingsUi();"));
            Assert.That(layoutSource, Does.Contain("_automaticThreadSyncTimer.Tick += AutomaticThreadSyncTimer_Tick;"));
            Assert.That(constructorBlock, Does.Not.Contain("_workspaceList.TabIndex = 0;"));
            Assert.That(constructorBlock, Does.Not.Contain("_automaticThreadSyncTimer.Tick += AutomaticThreadSyncTimer_Tick;"));
        }

        [Test]
        public void MainForm_SessionSettingsSubflowsLiveInFocusedPartials()
        {
            var sessionSettingsSource = File.ReadAllText(GetMainFormPath("MainForm.SessionSettings.cs"));
            var runtimeSource = File.ReadAllText(GetMainFormPath("MainForm.RuntimeSettings.cs"));
            var modelSource = File.ReadAllText(GetMainFormPath("MainForm.ModelSettings.cs"));
            var permissionSource = File.ReadAllText(GetMainFormPath("MainForm.PermissionSettings.cs"));
            var optionsSource = File.ReadAllText(GetMainFormPath("MainForm.OptionsSettings.cs"));

            Assert.That(runtimeSource, Does.Contain("private async Task OpenAppServerRuntimeSettingsAsync()"));
            Assert.That(modelSource, Does.Contain("private async Task OpenModelSettingsAsync("));
            Assert.That(permissionSource, Does.Contain("private Task OpenPermissionSettingsAsync("));
            Assert.That(optionsSource, Does.Contain("private async Task OpenOptionsAsync()"));
            Assert.That(sessionSettingsSource, Does.Not.Contain("private async Task OpenAppServerRuntimeSettingsAsync()"));
            Assert.That(sessionSettingsSource, Does.Not.Contain("private async Task OpenModelSettingsAsync("));
            Assert.That(sessionSettingsSource, Does.Not.Contain("private Task OpenPermissionSettingsAsync("));
            Assert.That(sessionSettingsSource, Does.Not.Contain("private async Task OpenOptionsAsync()"));
        }

        [Test]
        public void AutomationsForm_EditContextMenuIsWiredToUpdateExistingAutomation()
        {
            var source = File.ReadAllText(GetMainFormPath("AutomationsForm.cs"));

            Assert.That(source, Does.Contain("_editAutomationMenuItem.Click += EditAutomationMenuItem_Click;"));
            Assert.That(source, Does.Contain("private async void EditAutomationMenuItem_Click"));
            Assert.That(source, Does.Contain("private async Task EditSelectedAutomationAsync()"));
            Assert.That(source, Does.Contain("GetSelectedAutomation()"));
            Assert.That(source, Does.Contain("_automationService.Update("));
            Assert.That(source, Does.Contain("BuildUpdateRequest("));
        }

        [Test]
        public void MainForm_AutomationRunTargetsCanOpenThreadAndLocalPaths()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.Automation.cs"));
            var englishResources = File.ReadAllText(GetWinFormsResourcePath("Strings.resx"));
            var chineseResources = File.ReadAllText(GetWinFormsResourcePath("Strings.zh-CN.resx"));

            var callbackBlock = ExtractBlock(
                source,
                "private AutomationRunActionCallbacks CreateAutomationRunActionCallbacks()",
                "private bool CanStopAutomationRun");
            var openThreadBlock = ExtractBlock(
                source,
                "private async Task OpenAutomationThreadAsync(AutomationRunSnapshot run)",
                "private Task OpenAutomationPathAsync(string path)");
            var openPathBlock = ExtractBlock(
                source,
                "private Task OpenAutomationPathAsync(string path)",
                "private void ApplyAutomationRunNavigationScope");

            Assert.That(callbackBlock, Does.Contain("CanOpenThread = CanOpenAutomationThread"));
            Assert.That(callbackBlock, Does.Contain("OpenThreadAsync = OpenAutomationThreadAsync"));
            Assert.That(callbackBlock, Does.Contain("OpenPathAsync = OpenAutomationPathAsync"));
            Assert.That(source, Does.Contain("private bool CanOpenAutomationThread(AutomationRunSnapshot run)"));

            Assert.That(openThreadBlock, Does.Contain("FindSessionByThreadId(run.ThreadId)"));
            Assert.That(openThreadBlock, Does.Contain("await ActivateSessionAsync(session, true);"));
            Assert.That(openThreadBlock, Does.Contain("await _appServerClient.ListThreadsAsync()"));
            Assert.That(openThreadBlock, Does.Contain("ImportThreads(new List<AppServerThreadSummary> { thread }, true);"));
            Assert.That(openThreadBlock, Does.Contain("AutomationsForm.Status.ThreadNotFound"));
            Assert.That(openThreadBlock, Does.Contain("AnnounceAutomationRunNavigationStatus(message);"));

            Assert.That(openPathBlock, Does.Contain("MainFormAutomationNavigationPlanner.BuildPathDecision(path, File.Exists, Directory.Exists)"));
            Assert.That(openPathBlock, Does.Contain("AutomationsForm.Status.RemotePathOpenUnsupported"));
            Assert.That(openPathBlock, Does.Contain("decision.Kind == MainFormAutomationNavigationDecisionKind.OpenLocalPath"));
            Assert.That(openPathBlock, Does.Contain("Process.Start(decision.Path);"));
            Assert.That(openPathBlock, Does.Contain("AutomationsForm.Status.PathNotFound"));
            Assert.That(openPathBlock.IndexOf("MainFormAutomationNavigationDecisionKind.RemotePathUnsupported", System.StringComparison.Ordinal), Is.LessThan(openPathBlock.IndexOf("Process.Start(decision.Path);", System.StringComparison.Ordinal)));
            Assert.That(source, Does.Contain("MainFormAutomationNavigationPlanner.IsRemotePath(path)"));

            Assert.That(englishResources, Does.Contain("name=\"AutomationsForm.Status.ThreadNotFound\""));
            Assert.That(englishResources, Does.Contain("name=\"AutomationsForm.Status.PathNotFound\""));
            Assert.That(englishResources, Does.Contain("name=\"AutomationsForm.Status.RemotePathOpenUnsupported\""));
            Assert.That(chineseResources, Does.Contain("name=\"AutomationsForm.Status.ThreadNotFound\""));
            Assert.That(chineseResources, Does.Contain("name=\"AutomationsForm.Status.PathNotFound\""));
            Assert.That(chineseResources, Does.Contain("name=\"AutomationsForm.Status.RemotePathOpenUnsupported\""));
        }

        [Test]
        public void MainForm_WorkspaceAutomationEntriesAreWired()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var layoutSource = File.ReadAllText(GetMainFormPath("MainForm.Layout.cs"));
            var workspaceSource = File.ReadAllText(GetMainFormPath("MainForm.Workspaces.cs"));
            var sessionsSource = File.ReadAllText(GetMainFormPath("MainForm.Sessions.cs"));
            var modesSource = File.ReadAllText(GetMainFormPath("MainForm.Modes.cs"));
            var englishResources = File.ReadAllText(GetWinFormsResourcePath("Strings.resx"));

            Assert.That(mainSource, Does.Contain("private readonly MenuItem _workspaceCreateAutomationMenuItem;"));
            Assert.That(mainSource, Does.Contain("MainForm.Workspace.CreateAutomation"));
            Assert.That(mainSource, Does.Contain("_workspaceCreateAutomationMenuItem.Click += (sender, e) => CreateAutomationFromSelectedWorkspace();"));
            Assert.That(mainSource, Does.Contain("_workspaceContextMenu.MenuItems.Add(_workspaceCreateAutomationMenuItem);"));

            Assert.That(layoutSource, Does.Contain("_workspaceCreateAutomationMenuItem.Text = UiLocalization.Text(\"MainForm.Workspace.CreateAutomation\""));
            Assert.That(workspaceSource, Does.Contain("private void CreateAutomationFromSelectedWorkspace()"));
            Assert.That(workspaceSource, Does.Contain("private void CreateAutomationFromWorkspace(WorkspaceInfo workspace)"));
            Assert.That(workspaceSource, Does.Contain("AutomationCreationContextBuilder.ForProjectWorkspace"));
            Assert.That(workspaceSource, Does.Contain("_workspaceCreateAutomationMenuItem.Enabled"));
            Assert.That(sessionsSource, Does.Contain("AutomationCreationContextBuilder.ForThread"));
            Assert.That(modesSource, Does.Contain("_workspaceCreateAutomationMenuItem.Visible = !isOfficeMode;"));
            Assert.That(englishResources, Does.Contain("name=\"MainForm.Workspace.CreateAutomation\""));
            Assert.That(englishResources, Does.Contain("Create Project Automation(&amp;A)..."));
        }

        [Test]
        public void AppServerEvents_CommandOutputWritesTerminalButNotActivityLog()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.AppServerEvents.cs"));
            var block = ExtractBlock(
                source,
                "case AppServerEventRoute.CommandOutput:",
                "case AppServerEventRoute.CommandTerminalInteraction:");

            Assert.That(block, Does.Contain("AppendCommandTerminalEntry("));
            Assert.That(block, Does.Not.Contain("AppendLog(e.Content)"));
        }

        [Test]
        public void ActivityArea_UsesStableListBoxInsteadOfTextBoxCaret()
        {
            var mainFormSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var activitySource = File.ReadAllText(GetMainFormPath("MainForm.Activity.cs"));
            var layoutSource = File.ReadAllText(GetMainFormPath("MainForm.Layout.cs"));
            var combinedSource = mainFormSource + activitySource + layoutSource;
            var renderBlock = ExtractBlock(
                activitySource,
                "private void RenderActivityDisplay()",
                "private bool ShouldShowActivityLog()");

            Assert.That(combinedSource, Does.Contain("private readonly ActivityVirtualBrowseControl _activityBrowseControl;"));
            Assert.That(combinedSource, Does.Contain("private readonly Label _activityCurrentStatusLabel;"));
            Assert.That(combinedSource, Does.Contain("CreateActivityBrowseControl()"));
            Assert.That(combinedSource, Does.Contain("CreateActivityCurrentStatusLabel()"));
            Assert.That(combinedSource, Does.Not.Contain("_activityTextBox"));
            Assert.That(combinedSource, Does.Not.Contain("_activityListBox"));
            Assert.That(renderBlock, Does.Not.Contain("SelectionStart ="));
            Assert.That(renderBlock, Does.Not.Contain("ScrollToCaret()"));
            Assert.That(renderBlock, Does.Not.Contain("deferWhileFocused"));
            Assert.That(renderBlock, Does.Contain("BuildCurrentStatus("));
            Assert.That(renderBlock, Does.Contain("BuildStableItems("));
            Assert.That(renderBlock, Does.Contain("BuildCurrentActivityTimelineItems("));
            Assert.That(renderBlock, Does.Contain("_activityBrowseControl.SetItems(items)"));
            Assert.That(renderBlock, Does.Not.Contain("BeginUpdate()"));
        }

        [Test]
        public void ActivityArea_ReliesOnAccessibilityNotificationsInsteadOfManualSpeech()
        {
            var mainFormSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var activitySource = File.ReadAllText(GetMainFormPath("MainForm.Activity.cs"));
            var activityControlSource = File.ReadAllText(GetControlPath("ActivityVirtualBrowseControl.cs"));
            var combinedSource = mainFormSource + activitySource + activityControlSource;

            Assert.That(activityControlSource, Does.Contain("AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);"));
            Assert.That(activityControlSource, Does.Contain("public override string Value"));
            Assert.That(activityControlSource, Does.Contain("public override int GetChildCount()"));
            Assert.That(activityControlSource, Does.Not.Contain("AccessibilityNotifyClients(AccessibleEvents.Selection"));
            Assert.That(activityControlSource, Does.Not.Contain("AccessibilityNotifyClients(AccessibleEvents.Focus"));
            Assert.That(activityControlSource, Does.Not.Contain("ActivityVirtualBrowseItemAccessibleObject"));
            Assert.That(combinedSource, Does.Not.Contain("ActivityRecordReadRequested"));
            Assert.That(combinedSource, Does.Not.Contain("SpeakLine(e.Text)"));
        }

        [Test]
        public void ManualBrowse_DelegatesMainWindowStateToTheSharedContext()
        {
            var mainFormSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var screenReaderSource = File.ReadAllText(GetMainFormPath("MainForm.ScreenReader.cs"));
            var hotkeyMethod = ExtractBlock(
                screenReaderSource,
                "private bool HandleScreenReaderHotkey(Keys keyData)",
                "private void ToggleAutomaticChatReading()");

            Assert.That(mainFormSource, Does.Contain("private ManualBrowseContext _mainManualBrowseContext;"));
            Assert.That(mainFormSource, Does.Not.Contain("_manualReadChatHistoryIndex"));
            Assert.That(screenReaderSource, Does.Contain("_mainManualBrowseContext.HandleHotkey(keyData)"));
            Assert.That(hotkeyMethod, Does.Not.Contain("ManualReadTarget.Activity"));
        }

        [Test]
        public void ManualBrowse_ReservesAltEnterForTheVirtualCursor()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.cs"));

            Assert.That(source, Does.Contain("_mainManualBrowseContext != null && keyData == (Keys.Alt | Keys.Enter)"));
        }

        [Test]
        public void ActivityManualReadTarget_SupportsBoundaryAndPagingHotkeys()
        {
            var source = File.ReadAllText(GetMainFormPath("ManualBrowseContext.cs"));
            var activityBlock = ExtractBlock(
                source,
                "private bool HandleActivityKey(Keys keyData)",
                "private void ToggleTarget()");

            Assert.That(activityBlock, Does.Contain("keyData == (Keys.Alt | Keys.Up)"));
            Assert.That(activityBlock, Does.Contain("keyData == (Keys.Alt | Keys.Down)"));
            Assert.That(activityBlock, Does.Contain("keyData == (Keys.Alt | Keys.Home)"));
            Assert.That(activityBlock, Does.Contain("ReadBoundaryActivityItem(true)"));
            Assert.That(activityBlock, Does.Contain("keyData == (Keys.Alt | Keys.End)"));
            Assert.That(activityBlock, Does.Contain("ReadBoundaryActivityItem(false)"));
            Assert.That(activityBlock, Does.Contain("keyData == (Keys.Alt | Keys.PageUp)"));
            Assert.That(activityBlock, Does.Contain("ReadRelativeActivityItem(-1, PageSize)"));
            Assert.That(activityBlock, Does.Contain("keyData == (Keys.Alt | Keys.PageDown)"));
            Assert.That(activityBlock, Does.Contain("ReadRelativeActivityItem(1, PageSize)"));
        }

        [Test]
        public void MessageViewerOpenCommandsUseSeparateListAndManualReadCursors()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.ScreenReader.cs"));
            var contextSource = File.ReadAllText(GetMainFormPath("ManualBrowseContext.cs"));
            var listMethod = ExtractBlock(
                source,
                "private void OpenCurrentChatMessageViewer()",
                "private void OpenChatMessageDetail(ChatHistoryItem item)");
            var hotkeyBlock = ExtractBlock(
                contextSource,
                "if (keyData == (Keys.Alt | Keys.Enter))",
                "if (keyData == (Keys.Alt | Keys.Left))");

            Assert.That(listMethod, Does.Contain("GetPrimaryChatHistoryItem()"));
            Assert.That(listMethod, Does.Not.Contain("_messageIndex"));
            Assert.That(hotkeyBlock, Does.Contain("OpenCurrentMessageDetail();"));
            Assert.That(hotkeyBlock, Does.Not.Contain("GetPrimaryChatHistoryItem()"));
        }

        [Test]
        public void ActivityArea_ContextMenuUsesSettingsDialogAndClearActivity()
        {
            var mainFormSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var layoutSource = File.ReadAllText(GetMainFormPath("MainForm.Layout.cs"));
            var contextMenuBlock = ExtractBlock(
                layoutSource,
                "private void ConfigureLogContextMenu()",
                "private void ConfigureMainMenu()");

            Assert.That(mainFormSource, Does.Contain("private readonly MenuItem _activitySettingsMenuItem;"));
            Assert.That(mainFormSource, Does.Contain("private readonly MenuItem _clearActivityMenuItem;"));
            Assert.That(mainFormSource, Does.Contain("ActivitySettingsMenuItem_Click"));
            Assert.That(mainFormSource, Does.Contain("ClearActivityMenuItem_Click"));
            Assert.That(contextMenuBlock, Does.Contain("_activitySettingsMenuItem"));
            Assert.That(contextMenuBlock, Does.Contain("_clearActivityMenuItem"));
            Assert.That(contextMenuBlock, Does.Not.Contain("_showActivityLogMenuItem"));
            Assert.That(contextMenuBlock, Does.Not.Contain("_clearLogMenuItem"));
        }

        [Test]
        public void ChatHistory_ContextMenuDoesNotExposeMessageFilters()
        {
            var mainFormSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var layoutSource = File.ReadAllText(GetMainFormPath("MainForm.Layout.cs"));

            Assert.That(mainFormSource, Does.Not.Contain("_chatFiltersMenuItem"));
            Assert.That(mainFormSource, Does.Not.Contain("ChatFilterMenuItem_Click"));
            Assert.That(mainFormSource, Does.Not.Contain("CreateChatFilterMenuItem"));
            Assert.That(layoutSource, Does.Not.Contain("MainForm.Context.MessageFilter"));
            Assert.That(layoutSource, Does.Not.Contain("MainForm.Context.Filter."));
        }

        [Test]
        public void MainForm_CommandLineWorkspaceOpensAndCreatesSessionWhenNoneExists()
        {
            var mainFormSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var runtimeConnectionSource = File.ReadAllText(GetMainFormPath("MainForm.RuntimeConnection.cs"));
            var traySource = File.ReadAllText(GetMainFormPath("MainForm.UpdatesTray.cs"));
            var workspaceSource = File.ReadAllText(GetMainFormPath("MainForm.Workspaces.cs"));

            Assert.That(mainFormSource, Does.Contain("private StartupCommandLineOptions _startupCommandLineOptions;"));
            Assert.That(mainFormSource, Does.Contain("ApplyStartupArguments(string[] args)"));
            Assert.That(runtimeConnectionSource, Does.Contain("LoadCommandLineWorkspaceAsync"));
            Assert.That(workspaceSource, Does.Contain("createSessionIfNoRestoredSession"));
            Assert.That(workspaceSource, Does.Contain("await CreateNewSessionAsync();"));
            Assert.That(traySource, Does.Contain("StartupCommandLineOptions.Parse(args)"));
            Assert.That(traySource, Does.Contain("LoadCommandLineWorkspaceAsync(options.WorkspacePath)"));
        }

        [Test]
        public void MainForm_ForkResolvesRuntimeSettingsBeforeCreatingAndKeepsOverridesInWorkflow()
        {
            var sessionWorkflowSource = File.ReadAllText(GetMainFormPath("MainForm.SessionWorkflows.cs"));
            var forkBlock = ExtractBlock(
                sessionWorkflowSource,
                "private async Task CreateForkSessionAsync(",
                "private int GetNextForkIndex(");
            Assert.That(forkBlock, Does.Contain("await ReadSessionModelSettingsAsync(sourceSession, workspace)"));
            var operationSource = File.ReadAllText(GetMainFormPath("SessionThreadOperationWorkflow.cs"));
            Assert.That(operationSource, Does.Contain("sourceSession.ModelId, sourceSession.ReasoningEffort"));
            Assert.That(forkBlock, Does.Not.Contain("sourceSession.ModelId"));
            Assert.That(forkBlock, Does.Not.Contain("sourceSession.ReasoningEffort"));

        }

        [Test]
        public void MainForm_ReviewOwnsFreshSettingsAndPreservesSessionOverrides()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.Review.cs"));
            Assert.That(source, Does.Contain("await ReadSessionModelSettingsAsync(session, workspace)"));
            Assert.That(source, Does.Contain("observed.WorkspaceConfig.ReviewModelId"));
            Assert.That(source, Does.Contain("sourceSession.ApprovalMode, sourceSession.ModelId, sourceSession.ReasoningEffort"));
            Assert.That(File.ReadAllText(GetMainFormPath("MainForm.cs")), Does.Not.Contain("private async Task StartDetachedReviewAsync("));
        }

        [Test]
        public void MainForm_LocalDesktopTurnsDoNotApplyRealtimeHistoryReload()
        {
            var mainFormSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var chatHistorySource = File.ReadAllText(GetMainFormPath("MainForm.ChatHistoryInteraction.cs"));
            var appServerEventsSource = File.ReadAllText(GetMainFormPath("MainForm.AppServerEvents.cs"));
            var messageSendingSource = File.ReadAllText(GetMainFormPath("MainForm.MessageSending.cs"));
            var eventBlock = ExtractBlock(
                appServerEventsSource,
                "private void AppServerClientOnEventReceived(object sender, AppServerEventArgs e)",
                "private void ForwardAppServerEventToSubAgentWorkbenches(AppServerEventArgs e)");
            var scheduleBlock = ExtractBlock(
                chatHistorySource,
                "private void ScheduleRealtimeCurrentSessionHistoryReload(AppServerEventArgs e)",
                "private async Task ReloadCurrentSessionHistoryAfterRealtimeEventAsync(");
            var sendBlock = ExtractBlock(
                messageSendingSource,
                "private async Task SendCurrentInputAsync(string overrideText, bool includePendingAttachments)",
                "private bool AbortSendIfContextChanged(");

            Assert.That(mainFormSource, Does.Contain("private readonly HashSet<string> _localDesktopRealtimeTurnThreadIds;"));
            Assert.That(mainFormSource, Does.Contain("_localDesktopRealtimeTurnThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);"));
            Assert.That(scheduleBlock, Does.Contain("IsLocalDesktopRealtimeTurn(e.ThreadId)"));
            Assert.That(scheduleBlock, Does.Contain("SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(e, currentThreadId, IsLocalDesktopRealtimeTurn(e.ThreadId))"));
            Assert.That(sendBlock, Does.Contain("MarkLocalDesktopRealtimeTurn(targetSession.ThreadId);"));
            Assert.That(sendBlock, Does.Contain("ClearLocalDesktopRealtimeTurn(targetSession != null ? targetSession.ThreadId : null);"));
            Assert.That(eventBlock, Does.Contain("ClearLocalDesktopRealtimeTurn(e.ThreadId);"));
        }

        [Test]
        public void AppServerEvents_UpdateHiddenSessionThreadStateBeforeVisibleUiFiltering()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.AppServerEvents.cs"));
            var eventBlock = ExtractBlock(
                source,
                "private void AppServerClientOnEventReceived(object sender, AppServerEventArgs e)",
                "private void ForwardAppServerEventToSubAgentWorkbenches(AppServerEventArgs e)");
            var markBusyIndex = eventBlock.IndexOf("MarkThreadBusy(e.ThreadId);", StringComparison.Ordinal);
            var clearBusyIndex = eventBlock.IndexOf("ClearThreadBusy(e.ThreadId);", StringComparison.Ordinal);
            var visibleFilterIndex = eventBlock.IndexOf("ShouldDisplayThreadEvent(e)", StringComparison.Ordinal);

            Assert.That(markBusyIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(clearBusyIndex, Is.GreaterThan(markBusyIndex));
            Assert.That(visibleFilterIndex, Is.GreaterThan(clearBusyIndex));
            Assert.That(source, Does.Contain("var session = FindSessionByThreadId(e.ThreadId);"));
            Assert.That(source, Does.Contain("ApplyThreadTokenUsageToSession(session, e.ThreadTokenUsage)"));
            Assert.That(source, Does.Contain("RefreshLiveWorkStatus();"));
            Assert.That(source, Does.Contain("RefreshSessionWorkbenches();"));
        }

        [TestCase("ProtocolWarning", "ProtocolNotice")]
        [TestCase("ProtocolNotice", "ThreadArchivedChanged")]
        public void GlobalProtocolMessages_AreRecordedInCurrentActivityWithoutDuplicatingThreadEvents(string route, string nextRoute)
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.AppServerEvents.cs"));
            var block = ExtractBlock(source, "case AppServerEventRoute." + route + ":", "case AppServerEventRoute." + nextRoute + ":");
            Assert.That(block, Does.Contain("if (string.IsNullOrWhiteSpace(e.ThreadId)) ApplyCurrentActivityTimelineEvent(e);"));
            Assert.That(block, Does.Not.Contain(".Focus(").And.Not.Contain(".Select("));
        }

        [Test]
        public void MainForm_McpMessageOnlyRequestsRequireConfirmationAndUnsupportedRequestsAreCanceled()
        {
            var source = File.ReadAllText(GetMainFormPath("MainForm.ToolUserInput.cs"));
            var toolInputBlock = ExtractBlock(
                source,
                "private async void ShowToolUserInputDialog(AppServerEventArgs request)",
                "private async Task ShowMcpElicitationConfirmationDialogAsync(AppServerEventArgs request)");
            var confirmationBlock = ExtractBlock(
                source,
                "private async Task ShowMcpElicitationConfirmationDialogAsync(AppServerEventArgs request)",
                "private async void ShowMcpElicitationUrlDialog(AppServerEventArgs request)");

            Assert.That(toolInputBlock, Does.Contain("request.IsMcpElicitationConfirmation"));
            Assert.That(toolInputBlock, Does.Contain("CancelToolUserInputRequestAsync(request.RequestId)"));
            Assert.That(confirmationBlock, Does.Contain("MessageBoxDefaultButton.Button2"));
            Assert.That(confirmationBlock, Does.Contain("ResolveToolUserInputRequestAsync("));
            Assert.That(confirmationBlock, Does.Contain("CancelToolUserInputRequestAsync(request.RequestId)"));
        }

        [Test]
        public void MainForm_UsesModelessAppServerFileWindowManager()
        {
            var mainSource = File.ReadAllText(GetMainFormPath("MainForm.cs"));
            var sessionSettingsSource = File.ReadAllText(GetMainFormPath("MainForm.SessionSettings.cs"));
            var eventSource = File.ReadAllText(GetMainFormPath("MainForm.AppServerEvents.cs"));
            var fileWindowsSource = File.ReadAllText(GetMainFormPath("MainForm.AppServerFileWindows.cs"));
            var showFilesBlock = ExtractBlock(
                sessionSettingsSource,
                "private async Task ShowAppServerFilesAsync(",
                "private string GetDefaultAppServerFileBrowserPath()");

            Assert.That(mainSource, Does.Contain("private readonly AppServerFileWindowManager _appServerFileWindowManager;"));
            Assert.That(showFilesBlock, Does.Contain("_appServerFileWindowManager.OpenBrowser("));
            Assert.That(showFilesBlock, Does.Not.Contain("new AppServerFileBrowserForm("));
            Assert.That(showFilesBlock, Does.Not.Contain("ShowDialog("));
            Assert.That(eventSource, Does.Contain("ForwardAppServerFileWindowEvent(e);"));
            Assert.That(eventSource, Does.Contain("ClearAppServerFileWindowWatches();"));
            Assert.That(fileWindowsSource, Does.Contain("_appServerFileWindowManager.HandleFileSystemChangedAsync("));
            Assert.That(fileWindowsSource, Does.Not.Contain("SpeakManualTextWithScreenReader"));
            Assert.That(fileWindowsSource, Does.Not.Contain("readFileColumn"));
        }

        private static string BuildMainFormSourceMetricsReport()
        {
            var lines = GetCurrentMainFormSourceFileNames()
                .Select(GetMainFormSourceMetric)
                .Select(metric => new
                {
                    metric.FileName,
                    metric.TargetLines,
                    metric.BaselineLines,
                    ActualLines = CountMeasuredSourceLines(GetMainFormPath(metric.FileName))
                })
                .OrderByDescending(metric => metric.ActualLines)
                .ThenBy(metric => metric.FileName, StringComparer.Ordinal)
                .Select(metric =>
                    metric.FileName + ": " + metric.ActualLines +
                    " lines; target " + metric.TargetLines +
                    "; baseline " + metric.BaselineLines +
                    (metric.ActualLines > metric.TargetLines ? " (currently above target)" : string.Empty) +
                    (metric.ActualLines > metric.BaselineLines ? " (above baseline; justify necessary scoped growth)" : string.Empty));

            return "MainForm source size metrics:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
        }

        private static MainFormSourceMetric GetMainFormSourceMetric(string fileName)
        {
            var metric = MainFormSourceMetrics.FirstOrDefault(item =>
                string.Equals(item.FileName, fileName, StringComparison.Ordinal));
            if (metric != null)
            {
                return metric;
            }

            var targetLines = fileName.StartsWith("MainForm.", StringComparison.Ordinal)
                ? DefaultMainFormPartialTargetLines
                : DefaultMainFormHelperTargetLines;
            return new MainFormSourceMetric(fileName, targetLines, targetLines);
        }

        private static string[] GetCurrentMainFormSourceFileNames()
        {
            var mainFormFileNames = Directory
                .GetFiles(GetMainFormDirectoryPath(), "MainForm*.cs")
                .Select(Path.GetFileName)
                .Concat(MainFormSourceMetrics.Select(metric => metric.FileName))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(fileName => fileName, StringComparer.Ordinal)
                .ToArray();

            return mainFormFileNames;
        }

        private static int CountPrivateMethodDeclarations(string source, IReadOnlyCollection<string> methodNames)
        {
            var count = 0;
            using (var reader = new StringReader(source))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmedLine = line.TrimStart();
                    if (!trimmedLine.StartsWith("private ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var methodName in methodNames)
                    {
                        if (trimmedLine.IndexOf(" " + methodName + "(", StringComparison.Ordinal) >= 0)
                        {
                            count++;
                            break;
                        }
                    }
                }
            }

            return count;
        }

        private static int CountMeasuredSourceLines(string path)
        {
            return File.ReadLines(path).Count(line => line.Length > 0);
        }

        private static string GetMainFormPath(string fileName)
        {
            return Path.Combine(GetMainFormDirectoryPath(), fileName);
        }

        private static string GetMainFormDirectoryPath()
        {
            return Path.Combine(GetRepositoryRoot(), "src", "MyCodex.WinForms", "Forms");
        }

        private static string GetControlPath(string fileName)
        {
            return Path.Combine(GetRepositoryRoot(), "src", "MyCodex.WinForms", "Controls", fileName);
        }

        private static string GetWinFormsResourcePath(string fileName)
        {
            return Path.Combine(GetRepositoryRoot(), "src", "MyCodex.WinForms", "Resources", fileName);
        }

        private static string GetRepositoryRoot()
        {
            var current = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(Path.Combine(current, "src", "MyCodex.WinForms")) &&
                    Directory.Exists(Path.Combine(current, "tests", "MyCodex.Application.Tests")))
                {
                    return current;
                }

                var parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }

            return Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                ".."));
        }

        private static string ExtractBlock(string source, string startMarker, string endMarker)
        {
            var startIndex = source.IndexOf(startMarker, System.StringComparison.Ordinal);
            Assert.That(startIndex, Is.GreaterThanOrEqualTo(0), startMarker + " should exist.");

            var endIndex = source.IndexOf(endMarker, startIndex + startMarker.Length, System.StringComparison.Ordinal);
            Assert.That(endIndex, Is.GreaterThan(startIndex), endMarker + " should follow " + startMarker + ".");

            return source.Substring(startIndex, endIndex - startIndex);
        }
    }
}
