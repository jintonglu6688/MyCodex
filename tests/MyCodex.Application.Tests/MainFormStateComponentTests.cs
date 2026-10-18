using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormStateComponentTests
    {
        [Test]
        public void AppServerEventRouting_HandlesAppServerFileEventsQuietly()
        {
            Assert.That(
                AppServerEventRouting.Resolve(new AppServerEventArgs("file_system_changed", "changed")),
                Is.EqualTo(AppServerEventRoute.SystemNotice));
            Assert.That(
                AppServerEventRouting.Resolve(new AppServerEventArgs("fuzzy_file_search_session_updated", "updated")),
                Is.EqualTo(AppServerEventRoute.Ignore));
            Assert.That(
                AppServerEventRouting.Resolve(new AppServerEventArgs("fuzzy_file_search_session_completed", "done")),
                Is.EqualTo(AppServerEventRoute.Ignore));
        }

        [Test]
        public void MainFormStatusPresenter_DetermineStatusTypePreservesExistingKeywordMapping()
        {
            Assert.That(MainFormStatusPresenter.DetermineStatusType("Save config failed"), Is.EqualTo(UiTheme.StatusType.Error));
            Assert.That(MainFormStatusPresenter.DetermineStatusType("保存失败"), Is.EqualTo(UiTheme.StatusType.Error));
            Assert.That(MainFormStatusPresenter.DetermineStatusType("Loading skills..."), Is.EqualTo(UiTheme.StatusType.Processing));
            Assert.That(MainFormStatusPresenter.DetermineStatusType("正在处理"), Is.EqualTo(UiTheme.StatusType.Processing));
            Assert.That(MainFormStatusPresenter.DetermineStatusType("Ready"), Is.EqualTo(UiTheme.StatusType.Ready));
        }

        [Test]
        public void AppServerEventRouting_BuffersHighVolumeActivitySummaryEvents()
        {
            var highVolumeEventTypes = new[]
            {
                "plan_delta",
                "turn_plan_updated",
                "turn_diff_updated",
                "reasoning_summary_delta",
                "reasoning_text_delta",
                "file_change_progress",
                "file_change_completed",
                "mcp_tool_progress",
                "mcp_tool_completed",
                "network_activity_started",
                "network_activity_completed"
            };

            foreach (var eventType in highVolumeEventTypes)
            {
                Assert.That(
                    AppServerEventRouting.IsBufferedTurnSummaryEvent(new AppServerEventArgs(eventType, "content")),
                    Is.True,
                    eventType);
            }
        }

        [Test]
        public void AutomationRunActionPlanner_CanStopRequiresStoppableRun()
        {
            var running = new AutomationRunSnapshot
            {
                Status = AutomationRunStatus.Running,
                ThreadId = "thread-1",
                TurnId = "turn-1"
            };
            var completed = new AutomationRunSnapshot
            {
                Status = AutomationRunStatus.Completed,
                ThreadId = "thread-1",
                TurnId = "turn-1"
            };
            var missingThread = new AutomationRunSnapshot
            {
                Status = AutomationRunStatus.Running,
                TurnId = "turn-1"
            };

            Assert.That(AutomationRunActionPlanner.CanStop(running, _ => false), Is.True);
            Assert.That(AutomationRunActionPlanner.CanStop(completed, _ => true), Is.False);
            Assert.That(AutomationRunActionPlanner.CanStop(missingThread, _ => true), Is.False);
        }

        [Test]
        public void ActivityDisplayRefreshState_CoalescesRequestsAndSkipsUnchangedText()
        {
            var state = new ActivityDisplayRefreshState();

            Assert.That(state.RequestRefresh(), Is.True);
            Assert.That(state.RequestRefresh(), Is.False);
            Assert.That(state.ConsumeRefreshRequest(), Is.True);
            Assert.That(state.ConsumeRefreshRequest(), Is.False);

            Assert.That(state.ShouldRender("first"), Is.True);
            state.MarkRendered("first");
            Assert.That(state.ShouldRender("first"), Is.False);
            Assert.That(state.ShouldRender("second"), Is.True);
        }

        [Test]
        public void ComposeAttachmentState_SynchronizeWithInputTextRemovesMissingMarkersAndSortsRemaining()
        {
            var attachments = new List<PendingAttachmentItem>
            {
                Attachment("b.txt", "[B]"),
                Attachment("c.txt", "[C]"),
                Attachment("a.txt", "[A]")
            };

            var result = ComposeAttachmentState.SynchronizeWithInputText(attachments, "[A] body [B]");

            Assert.That(result.Changed, Is.True);
            Assert.That(result.RemovedAttachments.Select(item => item.MarkerText).ToArray(), Is.EqualTo(new[] { "[C]" }));
            Assert.That(attachments.Select(item => item.MarkerText).ToArray(), Is.EqualTo(new[] { "[A]", "[B]" }));
        }

        [Test]
        public void ComposeAttachmentState_BuildAttachmentMarkerTextAddsIndexWhenMarkerAlreadyExists()
        {
            using (UseCulture("en-US"))
            {
                var attachments = new List<PendingAttachmentItem>
                {
                    Attachment("first.txt", "[File: first.txt]")
                };

                var marker = ComposeAttachmentState.BuildAttachmentMarkerText(attachments, AttachmentKind.File, "first.txt", "first.txt");

                Assert.That(marker, Is.EqualTo("[File: first.txt #2]"));
            }
        }

        [Test]
        public void ComposeDraftStore_SaveGetAndRemoveCloneDraftState()
        {
            var store = new ComposeDraftStore();
            var originalAttachments = new List<PendingAttachmentItem>
            {
                Attachment("one.txt", "[One]"),
                Attachment("two.txt", "[Two]")
            };

            store.Save("session-1", "draft text", originalAttachments);
            originalAttachments.Clear();

            var draftState = store.Get("session-1");

            Assert.That(draftState, Is.Not.Null);
            Assert.That(draftState.DraftText, Is.EqualTo("draft text"));
            Assert.That(draftState.Attachments.Count, Is.EqualTo(2));
            Assert.That(draftState.Attachments[0].MarkerText, Is.EqualTo("[One]"));

            var removedMarkers = new List<string>();
            store.Remove("session-1", true, item => removedMarkers.Add(item.MarkerText));

            Assert.That(removedMarkers, Is.EqualTo(new[] { "[One]", "[Two]" }));
            Assert.That(store.Get("session-1"), Is.Null);
        }

        [Test]
        public void ComposeDraftStore_ScopesDraftsByRuntimeProfile()
        {
            var store = new ComposeDraftStore();

            store.Save("remote-unix|linux-dev", "session-1", "remote draft", new[]
            {
                new PendingAttachmentItem(
                    "/home/demo/.codex/skills/apple-news",
                    "Apple 24H News",
                    false,
                    AttachmentKind.Skill,
                    "[Skill: Apple 24H News]",
                    null,
                    "apple-news",
                    true,
                    true)
            });
            store.Save("windows", "session-1", "windows draft", Array.Empty<PendingAttachmentItem>());

            var remoteDraft = store.Get("remote-unix|linux-dev", "session-1");
            var windowsDraft = store.Get("windows", "session-1");

            Assert.That(remoteDraft.DraftText, Is.EqualTo("remote draft"));
            Assert.That(remoteDraft.Attachments.Select(item => item.DisplayName).ToArray(), Is.EqualTo(new[] { "Apple 24H News" }));
            Assert.That(windowsDraft.DraftText, Is.EqualTo("windows draft"));
            Assert.That(windowsDraft.Attachments, Is.Empty);
        }

        [Test]
        public void SessionCollectionQueries_SelectsCodingAndOfficeByIndependentRuntimeIdentities()
        {
            var coding = new ChatSession
            {
                Id = "coding",
                Mode = AppInteractionMode.Coding,
                RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId
            };
            var office = new ChatSession
            {
                Id = "office",
                Mode = AppInteractionMode.Office,
                RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                OfficeRuntimeScopeId = "wsl|ubuntu"
            };
            var otherOffice = new ChatSession
            {
                Id = "other-office",
                Mode = AppInteractionMode.Office,
                RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                OfficeRuntimeScopeId = "wsl|debian"
            };

            var selected = SessionCollectionQueries.GetSessionsForActiveRuntimeContexts(
                new[] { coding, office, otherOffice },
                RuntimeProfileService.WindowsRuntimeProfileId,
                "wsl|ubuntu");

            Assert.That(selected.Select(session => session.Id).ToArray(),
                Is.EqualTo(new[] { "coding", "office" }));
        }

        [Test]
        public void ModeWorkspaceProjection_EmptyOfficeStateDoesNotFabricateDefaultWorkspace()
        {
            var availabilityChecks = 0;

            var result = ModeWorkspaceProjection.Build(
                Array.Empty<WorkspaceInfo>(),
                AppInteractionMode.Office,
                false,
                workspace => true,
                workspace =>
                {
                    availabilityChecks++;
                    return true;
                });

            Assert.That(result, Is.Empty);
            Assert.That(availabilityChecks, Is.Zero);
        }

        [Test]
        public void ModeWorkspaceProjection_UsesOnlySuppliedCachedAvailability()
        {
            var coding = new WorkspaceInfo { Id = "coding", Kind = WorkspaceKind.Coding, RootPath = @"C:\Coding" };
            var office = new WorkspaceInfo { Id = "office:wsl|ubuntu", Kind = WorkspaceKind.Office, RootPath = "/home/alice/office" };

            var result = ModeWorkspaceProjection.Build(
                new[] { coding, office },
                AppInteractionMode.Office,
                false,
                workspace => true,
                workspace => workspace.Id != office.Id);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void OfficeWorkspaceAvailabilityPlanner_ActiveScopeErrorOverridesUnknownUnixAvailability()
        {
            var activeOffice = new WorkspaceInfo
            {
                Id = "office:wsl|ubuntu",
                Kind = WorkspaceKind.Office,
                RootPath = "/home/alice/MyCodex-office"
            };
            var otherOffice = new WorkspaceInfo
            {
                Id = "office:wsl|debian",
                Kind = WorkspaceKind.Office,
                RootPath = "/home/deb/MyCodex-office"
            };
            var coding = new WorkspaceInfo
            {
                Id = "coding",
                Kind = WorkspaceKind.Coding,
                RootPath = "/home/alice/code"
            };

            Assert.That(OfficeWorkspaceAvailabilityPlanner.ResolveForProjection(
                activeOffice,
                "wsl|ubuntu",
                "unavailable",
                null), Is.False);
            Assert.That(OfficeWorkspaceAvailabilityPlanner.ResolveForProjection(
                otherOffice,
                "wsl|ubuntu",
                "unavailable",
                null), Is.True);
            Assert.That(OfficeWorkspaceAvailabilityPlanner.ResolveForProjection(
                coding,
                "wsl|ubuntu",
                "unavailable",
                null), Is.True);
            Assert.That(OfficeWorkspaceAvailabilityPlanner.ResolveForProjection(
                activeOffice,
                "wsl|ubuntu",
                null,
                false), Is.False);
        }

        [Test]
        public void SessionRuntimeIdentityResolver_SeparatesSharedWindowsRuntimeFromWslOfficeDraftScope()
        {
            var session = new ChatSession
            {
                Id = "office-session",
                Mode = AppInteractionMode.Office,
                RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                OfficeRuntimeScopeId = "wsl|ubuntu"
            };

            Assert.That(SessionRuntimeIdentityResolver.ResolveRuntimeProfileId(
                session,
                "fallback-profile"), Is.EqualTo(RuntimeProfileService.WindowsRuntimeProfileId));
            Assert.That(SessionRuntimeIdentityResolver.ResolveDraftScopeId(
                session,
                "fallback-profile",
                "fallback-office"), Is.EqualTo("wsl|ubuntu"));
        }

        [Test]
        public void WorkspaceRuntimeCacheKeyBuilder_FoldsWindowsCaseButPreservesUnixCaseAndScope()
        {
            Assert.That(
                WorkspaceRuntimeCacheKeyBuilder.Build("windows", @"C:\Office\Docs"),
                Is.EqualTo(WorkspaceRuntimeCacheKeyBuilder.Build("windows", @"c:\office\docs")));
            Assert.That(
                WorkspaceRuntimeCacheKeyBuilder.Build("wsl|ubuntu", "/Office/Docs"),
                Is.Not.EqualTo(WorkspaceRuntimeCacheKeyBuilder.Build("wsl|ubuntu", "/Office/docs")));
            Assert.That(
                WorkspaceRuntimeCacheKeyBuilder.Build("wsl|ubuntu", "/Office/Docs"),
                Is.Not.EqualTo(WorkspaceRuntimeCacheKeyBuilder.Build("wsl|debian", "/Office/Docs")));
        }

        [Test]
        public void ThreadOperationStateTracker_TracksCompactionLifecycleAndCompletionDeduplication()
        {
            var tracker = new ThreadOperationStateTracker(2, TimeSpan.FromSeconds(1));

            Assert.That(tracker.TryBeginCompaction("thread-1"), Is.True);
            Assert.That(tracker.IsThreadPendingCompaction("thread-1"), Is.True);
            Assert.That(tracker.TryMarkCompactionStarted("thread-1", "turn-1"), Is.True);
            Assert.That(tracker.IsThreadPendingCompaction("thread-1"), Is.False);
            Assert.That(tracker.IsThreadActiveCompaction("thread-1", "turn-1"), Is.True);

            Assert.That(tracker.TryRegisterCompactionCompletion("turn-1"), Is.True);
            Assert.That(tracker.TryRegisterCompactionCompletion("turn-1"), Is.False);
            Assert.That(tracker.HasRecentCompactionCompletionTurn("turn-1"), Is.True);
            Assert.That(tracker.TryRegisterCompactionCompletion("turn-2"), Is.True);
            Assert.That(tracker.TryRegisterCompactionCompletion("turn-3"), Is.True);
            Assert.That(tracker.HasRecentCompactionCompletionTurn("turn-1"), Is.False);

            tracker.ClearThreadCompactionState("thread-1");
            Assert.That(tracker.IsThreadCompacting("thread-1"), Is.False);
        }

        [Test]
        public void BufferedDispatchState_QueuesUntilDrainedAndCanReset()
        {
            var state = new BufferedDispatchState<string>();

            Assert.That(state.Enqueue("one"), Is.True);
            Assert.That(state.Enqueue("two"), Is.False);

            List<string> items;
            Assert.That(state.TryDequeueAll(out items), Is.True);
            Assert.That(items, Is.EqualTo(new[] { "one", "two" }));
            Assert.That(state.TryDequeueAll(out items), Is.False);

            Assert.That(state.Enqueue("three"), Is.True);
            state.Reset();
            Assert.That(state.TryDequeueAll(out items), Is.False);
        }

        [Test]
        public void BufferedDispatchState_DropsOldestItemsWhenCapacityIsExceeded()
        {
            var state = new BufferedDispatchState<string>(3);

            Assert.That(state.Enqueue("one"), Is.True);
            Assert.That(state.Enqueue("two"), Is.False);
            Assert.That(state.Enqueue("three"), Is.False);
            Assert.That(state.Enqueue("four"), Is.False);

            List<string> items;
            Assert.That(state.TryDequeueAll(out items), Is.True);
            Assert.That(items, Is.EqualTo(new[] { "two", "three", "four" }));
        }

        [Test]
        public void BufferedDispatchState_DequeuesBatchesAndKeepsDispatchQueuedWhenItemsRemain()
        {
            var state = new BufferedDispatchState<string>();

            Assert.That(state.Enqueue("one"), Is.True);
            Assert.That(state.Enqueue("two"), Is.False);
            Assert.That(state.Enqueue("three"), Is.False);

            List<string> items;
            bool hasMore;
            Assert.That(state.TryDequeueBatch(2, out items, out hasMore), Is.True);
            Assert.That(items, Is.EqualTo(new[] { "one", "two" }));
            Assert.That(hasMore, Is.True);
            Assert.That(state.Enqueue("four"), Is.False);

            Assert.That(state.TryDequeueBatch(10, out items, out hasMore), Is.True);
            Assert.That(items, Is.EqualTo(new[] { "three", "four" }));
            Assert.That(hasMore, Is.False);
            Assert.That(state.Enqueue("five"), Is.True);
        }

        [Test]
        public void RemoteHostMutationRefreshPlanner_WorkspaceRemovalReloadsLocalState()
        {
            var plan = RemoteHostMutationRefreshPlanner.Build(
                new RemoteHostMutation
                {
                    MutationType = RemoteHostMutationTypes.WorkspaceDeleted,
                    WorkspaceId = "workspace-a",
                    StatusText = "Removed workspace."
                });

            Assert.That(plan.ReloadLocalStateForCurrentRuntime, Is.True);
            Assert.That(plan.StatusText, Is.EqualTo("Removed workspace."));
        }

        [Test]
        public void RemoteHostMutationRefreshPlanner_SessionRemovalReloadsLocalState()
        {
            var plan = RemoteHostMutationRefreshPlanner.Build(
                new RemoteHostMutation
                {
                    MutationType = RemoteHostMutationTypes.SessionDeleted,
                    WorkspaceId = "workspace-a",
                    SessionId = "session-a",
                    StatusText = "Deleted session."
                });

            Assert.That(plan.ReloadLocalStateForCurrentRuntime, Is.True);
            Assert.That(plan.StatusText, Is.EqualTo("Deleted session."));
        }

        [Test]
        public void RemoteHostMutationRefreshPlanner_SessionCreatedReloadsLocalState()
        {
            var plan = RemoteHostMutationRefreshPlanner.Build(
                new RemoteHostMutation
                {
                    MutationType = RemoteHostMutationTypes.SessionCreated,
                    WorkspaceId = "workspace-a",
                    SessionId = "session-a",
                    StatusText = "Session created."
                });

            Assert.That(plan.ReloadLocalStateForCurrentRuntime, Is.True);
            Assert.That(plan.StatusText, Is.EqualTo("Session created."));
        }

        [Test]
        public void RemoteHostMutationRefreshPlanner_SessionThreadUpdatedReloadsLocalState()
        {
            var plan = RemoteHostMutationRefreshPlanner.Build(
                new RemoteHostMutation
                {
                    MutationType = RemoteHostMutationTypes.SessionThreadUpdated,
                    WorkspaceId = "workspace-a",
                    SessionId = "session-a",
                    StatusText = "Session thread updated."
                });

            Assert.That(plan.ReloadLocalStateForCurrentRuntime, Is.True);
            Assert.That(plan.StatusText, Is.EqualTo("Session thread updated."));
        }

        [Test]
        public void RemoteHostMutationRefreshPlanner_CompactBeginsCompactionTracking()
        {
            var plan = RemoteHostMutationRefreshPlanner.Build(
                new RemoteHostMutation
                {
                    MutationType = RemoteHostMutationTypes.SessionCompactRequested,
                    WorkspaceId = "workspace-1",
                    SessionId = "session-1",
                    StatusText = "Context compaction started."
                });

            Assert.That(plan.ReloadLocalStateForCurrentRuntime, Is.True);
            Assert.That(plan.StatusText, Is.EqualTo("Context compaction started."));
            Assert.That(plan.ShouldBeginCompaction, Is.True);
            Assert.That(plan.BeginCompactionSessionId, Is.EqualTo("session-1"));
        }

        [Test]
        public void RemoteHostMutationRefreshPlanner_SessionSettingsUpdateReloadsLocalState()
        {
            var plan = RemoteHostMutationRefreshPlanner.Build(
                new RemoteHostMutation
                {
                    MutationType = RemoteHostMutationTypes.SessionSettingsUpdated,
                    WorkspaceId = "workspace-a",
                    SessionId = "session-a",
                    StatusText = "Session settings updated."
                });

            Assert.That(plan.ReloadLocalStateForCurrentRuntime, Is.True);
            Assert.That(plan.StatusText, Is.EqualTo("Session settings updated."));
        }

        [Test]
        public void RemoteHostMutationRefreshPlanner_HostSettingsUpdateRefreshesHostSettings()
        {
            var plan = RemoteHostMutationRefreshPlanner.Build(
                new RemoteHostMutation
                {
                    MutationType = RemoteHostMutationTypes.HostSettingsUpdated
                });

            Assert.That(plan.RefreshHostSettings, Is.True);
            Assert.That(plan.ReloadLocalStateForCurrentRuntime, Is.False);
        }

        [Test]
        public void CoalescingBufferedDispatchState_ReplacesItemsWithSameKey()
        {
            var state = new CoalescingBufferedDispatchState<string>(item => item.Substring(0, 1));

            Assert.That(state.Enqueue("a1"), Is.True);
            Assert.That(state.Enqueue("a2"), Is.False);
            Assert.That(state.Enqueue("b1"), Is.False);
            Assert.That(state.Enqueue("a3"), Is.False);

            List<string> items;
            Assert.That(state.TryDequeueAll(out items), Is.True);
            Assert.That(items, Is.EqualTo(new[] { "a3", "b1" }));
            Assert.That(state.Enqueue("c1"), Is.True);
        }

        [Test]
        public void ChatHistorySpeechFormatter_BuildAutoReadTextUsesPreviewForLongMessages()
        {
            using (UseCulture("en-US"))
            {
                var item = new ChatHistoryItem
                {
                    DisplayText = "codex: short preview",
                    FullText = "codex: " + new string('x', 5000)
                };

                var text = ChatHistorySpeechFormatter.BuildAutoReadText(item);

                Assert.That(text.Length, Is.LessThan(1000));
                Assert.That(text, Does.Contain("codex:"));
                Assert.That(text, Does.Contain("Content is long"));
                Assert.That(text, Does.Not.Contain(new string('x', 1000)));
            }
        }

        [Test]
        public void ChatHistorySpeechFormatter_BuildAutoReadTextCanKeepCompleteLongMessages()
        {
            using (UseCulture("en-US"))
            {
                var content = new string('x', 5000);
                var item = new ChatHistoryItem
                {
                    DisplayText = "codex: short preview",
                    FullText = "codex: " + content
                };

                var text = ChatHistorySpeechFormatter.BuildAutoReadText(item, true);

                Assert.That(text, Is.EqualTo("codex: " + content));
                Assert.That(text, Does.Not.Contain("Content is long"));
            }
        }

        [Test]
        public void ChatHistorySpeechFormatter_BuildBrowseTextCanKeepCompleteLongMessages()
        {
            using (UseCulture("en-US"))
            {
                var content = new string('x', 5000);
                var item = new ChatHistoryItem
                {
                    DisplayText = "codex: short preview",
                    FullText = "codex: " + content
                };

                var text = ChatHistorySpeechFormatter.BuildBrowseText(item, true);

                Assert.That(text, Is.EqualTo("codex: " + content));
                Assert.That(text, Does.Not.Contain("Content is long"));
            }
        }

        [Test]
        public void ChatHistorySpeechFormatter_BuildAutoReadTextDoesNotPreviewThousandChineseCharacters()
        {
            using (UseCulture("en-US"))
            {
                var content = new string('\u6D4B', 1000);
                var item = new ChatHistoryItem
                {
                    DisplayText = "codex: short preview",
                    FullText = "codex: " + content
                };

                var text = ChatHistorySpeechFormatter.BuildAutoReadText(item);

                Assert.That(text, Does.Not.Contain("Content is long"));
                Assert.That(text, Does.Contain(content));
            }
        }

        [Test]
        public void ChatHistorySpeechFormatter_BuildAutoReadTextPreservesSanitizedLineBreaksForCodexMessages()
        {
            var item = ChatHistoryItemFactory.Create(
                "codex",
                "# Title " + char.ConvertFromUtf32(0x2705) + Environment.NewLine + "**Bold**",
                ChatHistoryCategory.Conversation);
            var prefix = UiLocalization.Text("MainForm.Chat.Prefix.Codex", "codex:");

            var text = ChatHistorySpeechFormatter.BuildAutoReadText(item);

            Assert.That(text, Is.EqualTo(prefix + " Title" + Environment.NewLine + "Bold"));
        }

        [Test]
        public void VisibleLogBuffer_AppendsLinesAndTruncatesQueuedText()
        {
            var buffer = new VisibleLogBuffer(12, 8);

            buffer.QueueText("1234");
            Assert.That(buffer.FlushPendingText(), Is.EqualTo("1234"));

            buffer.AppendLogLine("next");
            Assert.That(buffer.FlushPendingText(), Is.EqualTo("1234" + Environment.NewLine + "next" + Environment.NewLine));

            buffer.QueueText("123456789012345");
            Assert.That(buffer.FlushPendingText(), Is.EqualTo("89012345"));
        }

        [Test]
        public void VisibleLogBuffer_GetSnapshotTextIncludesVisibleAndPendingText()
        {
            var buffer = new VisibleLogBuffer(64, 48);

            buffer.QueueText("head");
            Assert.That(buffer.FlushPendingText(), Is.EqualTo("head"));

            buffer.QueueText("-tail");

            Assert.That(buffer.GetSnapshotText(), Is.EqualTo("head-tail"));
        }

        [Test]
        public void ActivityDisplayState_BuildsSingleReadableActivityText()
        {
            var text = ActivityDisplayState.BuildText(
                "Running command",
                "Started: ping 127.0.0.1",
                true,
                "Summary",
                "Log",
                "No summary currently.",
                "No log entries currently.");

            Assert.That(text, Is.EqualTo(
                "[Summary]" + Environment.NewLine +
                "Running command" + Environment.NewLine +
                Environment.NewLine +
                "[Log]" + Environment.NewLine +
                "Started: ping 127.0.0.1"));
        }

        [Test]
        public void ActivityDisplayState_UsesEmptyFallbacks()
        {
            var text = ActivityDisplayState.BuildText(
                null,
                " ",
                true,
                "Summary",
                "Log",
                "No summary currently.",
                "No log entries currently.");

            Assert.That(text, Is.EqualTo(
                "[Summary]" + Environment.NewLine +
                "No summary currently." + Environment.NewLine +
                Environment.NewLine +
                "[Log]" + Environment.NewLine +
                "No log entries currently."));
        }

        [Test]
        public void ActivityDisplayState_HidesLogSectionWhenDisabled()
        {
            var text = ActivityDisplayState.BuildText(
                "Running command",
                "Started: ping 127.0.0.1",
                false,
                "Summary",
                "Log",
                "No summary currently.",
                "No log entries currently.");

            Assert.That(text, Is.EqualTo(
                "[Summary]" + Environment.NewLine +
                "Running command"));
        }

        [Test]
        public void ActivityDisplayState_BuildsStableListItems()
        {
            var items = ActivityDisplayState.BuildItems(
                "Running command" + Environment.NewLine + "Waiting for result",
                "Started: ping 127.0.0.1" + Environment.NewLine + "Exit code: 0",
                true,
                "Summary",
                "Log",
                "No summary currently.",
                "No log entries currently.");

            Assert.That(items, Is.EqualTo(new[]
            {
                "Summary: Running command",
                "Summary: Waiting for result",
                "Log (2): Exit code: 0"
            }));
        }

        [Test]
        public void ActivityDisplayState_UsesSummarySectionNamesAsListItemPrefixes()
        {
            var items = ActivityDisplayState.BuildItems(
                "Current step: Checking project" + Environment.NewLine +
                Environment.NewLine +
                "Thinking:" + Environment.NewLine +
                "Current: Reading files" + Environment.NewLine +
                "Completed: Found entry point" + Environment.NewLine +
                Environment.NewLine +
                "Plan:" + Environment.NewLine +
                "Completed: Build project",
                string.Empty,
                false,
                "Summary",
                "Log",
                "No summary currently.",
                "No log entries currently.");

            Assert.That(items, Is.EqualTo(new[]
            {
                "Current step: Checking project",
                "Thinking: Current: Reading files",
                "Thinking: Completed: Found entry point",
                "Plan: Completed: Build project"
            }));
        }

        [Test]
        public void ActivityDisplayState_SplitsLiveStatusFromStableActivityItems()
        {
            var summaryText =
                "Current step: Running tests" + Environment.NewLine +
                Environment.NewLine +
                "Thinking:" + Environment.NewLine +
                "Current: Reading project metadata" + Environment.NewLine +
                "Completed: Found project entry points" + Environment.NewLine +
                Environment.NewLine +
                "Plan:" + Environment.NewLine +
                "In Progress: Run focused tests" + Environment.NewLine +
                "Completed: Inspect activity list rendering" + Environment.NewLine +
                Environment.NewLine +
                "Changes:" + Environment.NewLine +
                "Modified 2 file(s)";

            Assert.That(
                ActivityDisplayState.BuildCurrentStatus(
                    summaryText,
                    "No activity currently."),
                Is.EqualTo("Current step: Running tests"));

            Assert.That(
                ActivityDisplayState.BuildStableItems(summaryText, "No activity records currently."),
                Is.EqualTo(new[]
                {
                    "Thinking: Completed: Found project entry points",
                    "Plan: Completed: Inspect activity list rendering",
                    "Changes: Modified 2 file(s)"
                }));
        }

        [Test]
        public void ActivityTimelineState_AppendsLogicalActivityItemsInEventOrder()
        {
            using (UseCulture("en-US"))
            {
                var state = new ActivityTimelineState();

                state.ApplyEvent(new AppServerEventArgs(
                    "turn_plan_updated",
                    "Plan updated.",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    planSteps: new[]
                    {
                        new AppServerTurnPlanStep { Step = "Inspect event routing", Status = "in_progress" }
                    }));
                state.ApplyEvent(new AppServerEventArgs(
                    "mcp_tool_completed",
                    "Completed",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "tool-1",
                    mcpServerName: "shell",
                    mcpToolName: "rg",
                    itemStatus: "completed"));
                state.ApplyEvent(new AppServerEventArgs(
                    "network_activity_completed",
                    "Web search completed: OpenAI homepage",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "web-1",
                    details: "OpenAI homepage"));
                state.ApplyEvent(new AppServerEventArgs(
                    "reasoning_completed",
                    "**Tracing activity flow** Confirmed events should stay chronological.",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "reason-1"));
                state.ApplyEvent(new AppServerEventArgs(
                    "file_change_completed",
                    "File changes completed",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "file-1",
                    changedFiles: new[] { "src/MainForm.cs", "src/ActivityTimelineState.cs" }));
                state.ApplyEvent(new AppServerEventArgs(
                    "command_completed",
                    "Command finished: dotnet test, status: completed, exit code: 0",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "command-1",
                    commandText: "dotnet test"));

                Assert.That(state.BuildDisplayItems("No activity records currently."), Is.EqualTo(new[]
                {
                    "Plan: In Progress: Inspect event routing",
                    "Tool: shell / rg: Completed",
                    "Network: Web search completed: OpenAI homepage",
                    "Thinking: Tracing activity flow: Confirmed events should stay chronological.",
                    "Changes: Modified 2 file(s): src/MainForm.cs, src/ActivityTimelineState.cs",
                    "Command: Command finished: dotnet test, status: completed, exit code: 0"
                }));
            }
        }

        [Test]
        public void ActivityTimelineState_RecordsCompletedImagesAsToolsAndSubAgentsAsNotices()
        {
            using (UseCulture("en-US"))
            {
                var state = new ActivityTimelineState();

                Assert.That(state.ApplyEvent(new AppServerEventArgs(
                    "image_generation_started",
                    "Image generation started: draft",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "image-1")), Is.False);
                Assert.That(state.ApplyEvent(new AppServerEventArgs(
                    "image_generation_completed",
                    "Image generation completed: final",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "image-1")), Is.True);
                Assert.That(state.ApplyEvent(new AppServerEventArgs(
                    "subagent_activity",
                    "Subagent activity (interacted): reviewer",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "activity-1")), Is.True);

                Assert.That(state.BuildDisplayItems("No activity records currently."), Is.EqualTo(new[]
                {
                    "Tool: Image generation completed: final",
                    "Notice: Subagent activity (interacted): reviewer"
                }));

                var hideTools = new AppConfigSnapshot
                {
                    ShowActivityToolMessages = false
                };
                Assert.That(state.BuildDisplayItems("No activity records currently.", hideTools), Is.EqualTo(new[]
                {
                    "Notice: Subagent activity (interacted): reviewer"
                }));

                var hideNotices = new AppConfigSnapshot
                {
                    ShowActivityNoticeMessages = false
                };
                Assert.That(state.BuildDisplayItems("No activity records currently.", hideNotices), Is.EqualTo(new[]
                {
                    "Tool: Image generation completed: final"
                }));
            }
        }

        [Test]
        public void ActivityTimelineState_UpdatesTurnCompletionWithTokenUsageSummary()
        {
            using (UseCulture("en-US"))
            {
                var state = new ActivityTimelineState();

                Assert.That(state.ApplyEvent(new AppServerEventArgs(
                    "turn_completed",
                    "Current turn completed",
                    threadId: "thread-1",
                    turnId: "turn-1")), Is.True);

                Assert.That(state.UpsertTurnSummary("turn-1", "Current turn completed | This turn 2.35\u4e07 | Total 1.23\u4ebf"), Is.True);

                Assert.That(state.BuildDisplayItems("No activity records currently."), Is.EqualTo(new[]
                {
                    "Notice: Current turn completed | This turn 2.35\u4e07 | Total 1.23\u4ebf"
                }));
            }
        }

        [Test]
        public void ActivityTimelineState_FiltersVisibleItemsByActivitySettings()
        {
            using (UseCulture("en-US"))
            {
                var state = new ActivityTimelineState();
                state.ApplyEvent(new AppServerEventArgs(
                    "reasoning_completed",
                    "Finished tracing.",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "reason-1"));
                state.ApplyEvent(new AppServerEventArgs(
                    "mcp_tool_completed",
                    "Completed",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "tool-1",
                    mcpToolName: "rg"));
                state.ApplyEvent(new AppServerEventArgs(
                    "command_completed",
                    "Command finished: dotnet test",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "command-1"));

                var config = new AppConfigSnapshot
                {
                    ShowActivityToolMessages = false,
                    ShowActivityCommandMessages = false
                };

                Assert.That(state.BuildDisplayItems("No activity records currently.", config), Is.EqualTo(new[]
                {
                    "Thinking: Finished tracing."
                }));

                config.ShowActivityThinkingMessages = false;
                Assert.That(state.BuildDisplayItems("No activity records currently.", config), Is.EqualTo(new[]
                {
                    "No activity records currently."
                }));
            }
        }

        [TestCase(null)]
        [TestCase("thread-1")]
        public void ActivityTimelineState_AppendsMigratedChatFilterCategories(string threadId)
        {
            using (UseCulture("en-US"))
            {
                var state = new ActivityTimelineState();
                state.ApplyEvent(new AppServerEventArgs(
                    "approval_resolved",
                    "Approval accepted.",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "approval-1"));
                state.ApplyEvent(new AppServerEventArgs(
                    "auth_changed",
                    "Authentication updated.",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "auth-1"));
                state.ApplyEvent(new AppServerEventArgs(
                    "protocol_notice",
                    "Protocol notice.",
                    threadId: threadId,
                    turnId: "turn-1",
                    itemId: "notice-1"));
                state.ApplyEvent(new AppServerEventArgs(
                    "protocol_warning",
                    "Protocol warning.",
                    threadId: threadId,
                    turnId: "turn-1",
                    itemId: "warning-1"));

                Assert.That(state.BuildDisplayItems("No activity records currently."), Is.EqualTo(new[]
                {
                    "Approval: Approval accepted.",
                    "Account: Authentication updated.",
                    "Protocol Notice: Protocol notice.",
                    "Protocol Warning: Protocol warning."
                }));

                var config = new AppConfigSnapshot
                {
                    ShowActivityApprovalMessages = false,
                    ShowActivityAccountMessages = false,
                    ShowActivityProtocolNoticeMessages = false,
                    ShowActivityProtocolWarningMessages = false
                };

                Assert.That(state.BuildDisplayItems("No activity records currently.", config), Is.EqualTo(new[]
                {
                    "No activity records currently."
                }));
            }
        }

        [Test]
        public void ActivityTimelineState_AddsTurnBoundaryAndHonorsAutoClearSetting()
        {
            using (UseCulture("en-US"))
            {
                var state = new ActivityTimelineState();
                var keepHistoryConfig = new AppConfigSnapshot
                {
                    ClearActivityOnNewTurn = false
                };

                state.ApplyEvent(new AppServerEventArgs(
                    "reasoning_completed",
                    "Previous turn summary.",
                    threadId: "thread-1",
                    turnId: "turn-1",
                    itemId: "reason-1"),
                    keepHistoryConfig);
                state.ApplyEvent(new AppServerEventArgs(
                    "turn_started",
                    "Turn started.",
                    threadId: "thread-1",
                    turnId: "turn-2"),
                    keepHistoryConfig);

                Assert.That(state.BuildDisplayItems("No activity records currently.", keepHistoryConfig), Is.EqualTo(new[]
                {
                    "Thinking: Previous turn summary.",
                    "Turn: Starting a new conversation turn"
                }));

                state.ApplyEvent(new AppServerEventArgs(
                    "reasoning_completed",
                    "Current turn summary.",
                    threadId: "thread-1",
                    turnId: "turn-2",
                    itemId: "reason-2"),
                    keepHistoryConfig);

                var clearHistoryConfig = new AppConfigSnapshot
                {
                    ClearActivityOnNewTurn = true
                };
                state.ApplyEvent(new AppServerEventArgs(
                    "turn_started",
                    "Turn started.",
                    threadId: "thread-1",
                    turnId: "turn-3"),
                    clearHistoryConfig);

                Assert.That(state.BuildDisplayItems("No activity records currently.", clearHistoryConfig), Is.EqualTo(new[]
                {
                    "Turn: Starting a new conversation turn"
                }));
            }
        }

        [Test]
        public void ActivityDisplayState_ResolvesPreservedSelectionByTextBeforeIndex()
        {
            var items = new[]
            {
                "Summary: Updated",
                "Summary: Extra line",
                "Log: Started",
                "Log: Exit code: 0"
            };

            Assert.That(ActivityDisplayState.ResolvePreservedIndex(items, "Log: Started", 1), Is.EqualTo(2));
            Assert.That(ActivityDisplayState.ResolvePreservedIndex(items, "Missing", 1), Is.EqualTo(1));
            Assert.That(ActivityDisplayState.ResolvePreservedIndex(items, "Missing", 8), Is.EqualTo(-1));
        }

        [Test]
        public void ActivityVirtualBrowseState_UpdatesItemsWithoutMovingManualSelection()
        {
            var state = new ActivityVirtualBrowseState();

            state.SetItems(new[] { "思考：确认项目结构", "计划：完成分析" });
            Assert.That(state.SelectedIndex, Is.EqualTo(1));
            Assert.That(state.CurrentText, Is.EqualTo("计划：完成分析"));
            Assert.That(state.IsFollowingLatest, Is.True);

            Assert.That(state.MoveBy(-1), Is.EqualTo("思考：确认项目结构"));
            Assert.That(state.SelectedIndex, Is.EqualTo(0));
            Assert.That(state.CurrentText, Is.EqualTo("思考：确认项目结构"));
            Assert.That(state.IsFollowingLatest, Is.False);

            state.SetItems(new[] { "思考：确认项目结构", "计划：完成分析", "变更：修改 2 个文件" });
            Assert.That(state.SelectedIndex, Is.EqualTo(0));
            Assert.That(state.CurrentText, Is.EqualTo("思考：确认项目结构"));
            Assert.That(state.IsFollowingLatest, Is.False);

            Assert.That(state.MoveToEnd(), Is.EqualTo("变更：修改 2 个文件"));
            Assert.That(state.SelectedIndex, Is.EqualTo(2));
            Assert.That(state.IsFollowingLatest, Is.False);
        }

        [Test]
        public void ActivityVirtualBrowseState_DoesNotSpeakWhenNavigationStaysAtBoundary()
        {
            var state = new ActivityVirtualBrowseState();
            state.SetItems(new[] { "Thinking: first item", "Plan: last item" });

            Assert.That(state.MoveToEnd(), Is.EqualTo("Plan: last item"));
            Assert.That(state.SelectedIndex, Is.EqualTo(1));
            Assert.That(state.MoveBy(1), Is.Empty);
            Assert.That(state.SelectedIndex, Is.EqualTo(1));

            Assert.That(state.MoveToStart(), Is.EqualTo("Thinking: first item"));
            Assert.That(state.SelectedIndex, Is.EqualTo(0));
            Assert.That(state.MoveBy(-1), Is.Empty);
            Assert.That(state.SelectedIndex, Is.EqualTo(0));
        }

        [Test]
        public void ActivityVirtualBrowseState_PreservesLastItemAfterManualNavigationWhenNewItemsArrive()
        {
            var state = new ActivityVirtualBrowseState();
            state.SetItems(new[] { "Thinking: first item", "Plan: previous last item" });

            Assert.That(state.SelectedIndex, Is.EqualTo(1));
            Assert.That(state.IsFollowingLatest, Is.True);

            Assert.That(state.MoveToEnd(), Is.EqualTo("Plan: previous last item"));
            Assert.That(state.SelectedIndex, Is.EqualTo(1));
            Assert.That(state.IsFollowingLatest, Is.False);

            state.SetItems(new[] { "Thinking: first item", "Plan: previous last item", "Thinking: new item" });

            Assert.That(state.SelectedIndex, Is.EqualTo(1));
            Assert.That(state.CurrentText, Is.EqualTo("Plan: previous last item"));
            Assert.That(state.IsFollowingLatest, Is.False);
        }

        [Test]
        public void CommandOutputAccumulator_AppendsBoundedOutputAndSkipsBufferedDuplicatesAfterStreamingWithoutLoggingRawOutput()
        {
            var task = new CommandTask();
            var visibleLogBuffer = new VisibleLogBuffer(64, 48);
            var recordedLogBuffer = new VisibleLogBuffer(64, 48);

            CommandOutputAccumulator.Append(task, "abcdef", "stdout", true, 4);

            Assert.That(task.StdOut, Is.EqualTo("cdef"));
            Assert.That(task.HasStreamedStdOut, Is.True);
            Assert.That(visibleLogBuffer.FlushPendingText(), Is.Null);
            Assert.That(recordedLogBuffer.FlushPendingText(), Is.Null);

            CommandOutputAccumulator.AppendBuffered(task, "ignored", "stdout", 4);
            Assert.That(task.StdOut, Is.EqualTo("cdef"));
            Assert.That(visibleLogBuffer.FlushPendingText(), Is.Null);
            Assert.That(recordedLogBuffer.FlushPendingText(), Is.Null);

            task = new CommandTask { StdErr = "seed" };
            visibleLogBuffer = new VisibleLogBuffer(64, 48);
            recordedLogBuffer = new VisibleLogBuffer(64, 48);
            CommandOutputAccumulator.AppendBuffered(task, "tail", "stderr", 16);

            Assert.That(task.StdErr, Is.EqualTo("seedtail"));
            Assert.That(visibleLogBuffer.FlushPendingText(), Is.Null);
            Assert.That(recordedLogBuffer.FlushPendingText(), Is.Null);
        }

        [Test]
        public void CommandOutputAccumulator_UpdatesCommandTaskWhenVisibleBufferIsMissing()
        {
            var task = new CommandTask();
            var recordedLogBuffer = new VisibleLogBuffer(64, 48);

            CommandOutputAccumulator.Append(task, "abcdef", "stdout", true, 8);

            Assert.That(task.StdOut, Is.EqualTo("abcdef"));
            Assert.That(recordedLogBuffer.FlushPendingText(), Is.Null);
        }

        [Test]
        public void CommandTerminalInteractionState_MarksMatchingCommandAsWaitingForInput()
        {
            var task = new CommandTask
            {
                ProcessId = "process-1",
                IsWaitingForInput = false
            };
            var now = new DateTime(2026, 5, 9, 10, 30, 0);

            var applied = CommandTerminalInteractionState.TryMarkWaitingForInput(
                task,
                new AppServerEventArgs("command_terminal_interaction", "Password:", processId: "process-1"),
                now);

            Assert.That(applied, Is.True);
            Assert.That(task.IsWaitingForInput, Is.True);
            Assert.That(task.LastInteractionText, Is.EqualTo("Password:"));
            Assert.That(task.LastInteractionAt, Is.EqualTo(now));
        }

        [Test]
        public void CommandTerminalInteractionState_IgnoresMismatchedProcessId()
        {
            var task = new CommandTask
            {
                ProcessId = "process-1",
                LastInteractionText = "seed"
            };
            var now = new DateTime(2026, 5, 9, 10, 30, 0);

            var applied = CommandTerminalInteractionState.TryMarkWaitingForInput(
                task,
                new AppServerEventArgs("command_terminal_interaction", "Password:", processId: "process-2"),
                now);

            Assert.That(applied, Is.False);
            Assert.That(task.IsWaitingForInput, Is.False);
            Assert.That(task.LastInteractionText, Is.EqualTo("seed"));
            Assert.That(task.LastInteractionAt, Is.Null);
        }

        [Test]
        public void CommandTerminalInteractionState_BuildsUnmatchedNotice()
        {
            var notice = CommandTerminalInteractionState.BuildUnmatchedNotice(
                new AppServerEventArgs("command_terminal_interaction", "Password:", processId: "process-2"),
                "Command interaction was not attached to the active terminal: {0}");

            Assert.That(notice, Is.EqualTo("Command interaction was not attached to the active terminal: Password:"));
            Assert.That(CommandTerminalInteractionState.BuildUnmatchedNotice(null, "Template {0}"), Is.Null);
        }

        [Test]
        public void CommandTerminalBuffer_AppendsBoundedEntriesAndReadableLines()
        {
            var buffer = new CommandTerminalBuffer(24);

            buffer.Append(CommandTerminalEntryKind.System, "Started");
            buffer.Append(CommandTerminalEntryKind.StandardOutput, "alpha");
            buffer.Append(CommandTerminalEntryKind.StandardError, "warning");
            buffer.Append(CommandTerminalEntryKind.Interaction, "Password:");

            var snapshot = buffer.GetSnapshotText();
            Assert.That(snapshot, Does.Contain("warning"));
            Assert.That(snapshot, Does.Contain("Password:"));
            Assert.That(snapshot, Does.Not.Contain("Started"));

            var lines = buffer.GetReadableLines();
            Assert.That(lines.Last(), Is.EqualTo("Password:"));
        }

        [Test]
        public void CommandTerminalSelectionState_UsesLatestCommandWhenNoActiveCommand()
        {
            var latest = new CommandTask
            {
                ProcessId = "process-2",
                Status = "completed"
            };
            var older = new CommandTask
            {
                ProcessId = "process-1",
                Status = "completed"
            };

            var selected = CommandTerminalSelectionState.GetTerminalCommand(null, new[] { latest, older });

            Assert.That(selected, Is.SameAs(latest));
        }

        [Test]
        public void CommandTerminalSelectionState_SubmitInputRequiresRunningActiveCommand()
        {
            var completed = new CommandTask
            {
                ProcessId = "process-1",
                Status = "completed"
            };
            var running = new CommandTask
            {
                ProcessId = "process-2",
                Status = "running"
            };

            Assert.That(CommandTerminalSelectionState.CanSubmitInput(completed, completed), Is.False);
            Assert.That(CommandTerminalSelectionState.CanSubmitInput(running, running), Is.True);
            Assert.That(CommandTerminalSelectionState.CanSubmitInput(running, completed), Is.False);
        }

        [Test]
        public void CommandTerminalSelectionState_FindsCommandByProcessId()
        {
            var first = new CommandTask { ProcessId = "process-1", Status = "completed" };
            var second = new CommandTask { ProcessId = "process-2", Status = "running" };

            var match = CommandTerminalSelectionState.FindByProcessId("PROCESS-2", new[] { first, second });

            Assert.That(match, Is.SameAs(second));
            Assert.That(CommandTerminalSelectionState.FindByProcessId("missing", new[] { first, second }), Is.Null);
            Assert.That(CommandTerminalSelectionState.FindByProcessId(null, new[] { first, second }), Is.Null);
        }

        [Test]
        public void ManualReadTargetState_TogglesBetweenMessageTerminalAndActivity()
        {
            Assert.That(ManualReadTargetState.Toggle(ManualReadTarget.Message), Is.EqualTo(ManualReadTarget.Terminal));
            Assert.That(ManualReadTargetState.Toggle(ManualReadTarget.Terminal), Is.EqualTo(ManualReadTarget.Activity));
            Assert.That(ManualReadTargetState.Toggle(ManualReadTarget.Activity), Is.EqualTo(ManualReadTarget.Message));
        }

        [Test]
        public void ManualReadTargetState_GetsTerminalLinesFromTerminalBuffer()
        {
            var buffer = new CommandTerminalBuffer(128);
            buffer.Append(CommandTerminalEntryKind.System, "ping 127.0.0.1");
            buffer.Append(CommandTerminalEntryKind.StandardOutput, "Reply from 127.0.0.1\nPackets: Sent = 1");

            var lines = ManualReadTargetState.GetReadableTerminalLines(buffer);

            Assert.That(lines, Is.EqualTo(new[]
            {
                "ping 127.0.0.1",
                "Reply from 127.0.0.1",
                "Packets: Sent = 1"
            }));
            Assert.That(ManualReadTargetState.GetReadableTerminalLines(null), Is.Empty);
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ActivityVirtualBrowseControl_ExposesReadableItemsForManualRead()
        {
            using (var control = new ActivityVirtualBrowseControl())
            {
                control.SetItems(new[] { " first ", "", "second" });

                Assert.That(control.GetReadableItems(), Is.EqualTo(new[] { "first", "second" }));
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ActivityVirtualBrowseControl_ExposesSingleAccessibleListValueForScreenReaderPosition()
        {
            using (var control = new ActivityVirtualBrowseControl())
            {
                control.AccessibleName = "Activity";
                control.Size = new Size(320, 120);
                control.SetItems(new[] { "first", "second", "third" });

                var root = control.AccessibilityObject;

                Assert.That(root.Role, Is.EqualTo(AccessibleRole.List));
                Assert.That(root.GetChildCount(), Is.EqualTo(0));
                Assert.That(root.Value, Is.EqualTo("third"));
            }
        }

        [Test]
        public void OutgoingInputItemBuilder_BuildInterleavesTextAndAttachmentItems()
        {
            var attachments = new List<PendingAttachmentItem>
            {
                new PendingAttachmentItem("image.png", "diagram", false, AttachmentKind.Image, "[img]", new byte[] { 1, 2, 3 }, null),
                new PendingAttachmentItem("skill/path", "Skill Display", false, AttachmentKind.Skill, "[skill]", null, "skill-name"),
                new PendingAttachmentItem("plugin/path", "Plugin Display", false, AttachmentKind.Plugin, "[plugin]", null, "plugin-name"),
                new PendingAttachmentItem("notes.txt", "notes.txt", false, AttachmentKind.File, "[file]", null, null)
            };

            var items = OutgoingInputItemBuilder.Build("A [img] B [skill] C [plugin] D [file] E", attachments, item => item.InMemoryContent);

            Assert.That(items, Has.Count.EqualTo(9));
            Assert.That(items[0].Type, Is.EqualTo("text"));
            Assert.That(items[0].Text, Is.EqualTo("A "));
            Assert.That(items[1].Type, Is.EqualTo("image"));
            Assert.That(items[1].Url, Is.EqualTo("data:image/png;base64,AQID"));
            Assert.That(items[2].Text, Is.EqualTo(" B "));
            Assert.That(items[3].Type, Is.EqualTo("skill"));
            Assert.That(items[3].Name, Is.EqualTo("skill-name"));
            Assert.That(items[3].Path, Is.EqualTo("skill/path"));
            Assert.That(items[4].Text, Is.EqualTo(" C "));
            Assert.That(items[5].Type, Is.EqualTo("mention"));
            Assert.That(items[5].Name, Is.EqualTo("plugin-name"));
            Assert.That(items[5].Path, Is.EqualTo("plugin/path"));
            Assert.That(items[6].Text, Is.EqualTo(" D "));
            Assert.That(items[7].Text, Is.EqualTo("notes.txt"));
            Assert.That(items[8].Text, Is.EqualTo(" E"));
        }

        [Test]
        public void OutgoingInputItemBuilder_BuildUsesImageMimeTypeFromAttachmentPath()
        {
            var attachments = new List<PendingAttachmentItem>
            {
                new PendingAttachmentItem("photo.jpg", "photo", false, AttachmentKind.Image, "[jpg]", new byte[] { 1 }, null),
                new PendingAttachmentItem("diagram.webp", "diagram", false, AttachmentKind.Image, "[webp]", new byte[] { 2 }, null),
                new PendingAttachmentItem("anim.gif", "anim", false, AttachmentKind.Image, "[gif]", new byte[] { 3 }, null),
                new PendingAttachmentItem("scan.bmp", "scan", false, AttachmentKind.Image, "[bmp]", new byte[] { 4 }, null)
            };

            var items = OutgoingInputItemBuilder.Build("[jpg] [webp] [gif] [bmp]", attachments, item => item.InMemoryContent);

            Assert.That(items.Where(item => item.Type == "image").Select(item => item.Url).ToArray(), Is.EqualTo(new[]
            {
                "data:image/jpeg;base64,AQ==",
                "data:image/webp;base64,Ag==",
                "data:image/gif;base64,Aw==",
                "data:image/bmp;base64,BA=="
            }));
        }

        [Test]
        public void OutgoingInputItemBuilder_BuildPreviewTextFallsBackToAttachmentMarkers()
        {
            var attachments = new List<PendingAttachmentItem>
            {
                Attachment("one.txt", "[One]"),
                Attachment("two.txt", "[Two]")
            };

            Assert.That(OutgoingInputItemBuilder.BuildPreviewText("  hello  ", attachments), Is.EqualTo("hello"));
            Assert.That(OutgoingInputItemBuilder.BuildPreviewText("   ", attachments), Is.EqualTo("[One] [Two]"));
            Assert.That(OutgoingInputItemBuilder.BuildPreviewText(string.Empty, Array.Empty<PendingAttachmentItem>()), Is.Empty);
        }

        [Test]
        public void OutgoingInputItemBuilder_BuildThrowsWhenImageAttachmentIsUnreadable()
        {
            using (UseCulture("en-US"))
            {
                var attachments = new[]
                {
                    new PendingAttachmentItem("broken.png", "Broken", false, AttachmentKind.Image, "[img]", null, null)
                };

                var exception = Assert.Throws<InvalidOperationException>(
                    () => OutgoingInputItemBuilder.Build("[img]", attachments, item => null));

                Assert.That(exception.Message, Is.EqualTo("Image \"Broken\" is currently unreadable. Please re-add it before sending."));
            }
        }

        [Test]
        public void SessionHistoryStateStore_StoresInvalidatesAndTracksPendingReload()
        {
            var store = new SessionHistoryStateStore();
            var items = new[] { new ChatHistoryItem { DisplayText = "entry" } };

            store.Store("thread-1", items);

            ChatHistoryItem[] cached;
            Assert.That(store.TryGetCached("thread-1", out cached), Is.True);
            Assert.That(cached[0].DisplayText, Is.EqualTo("entry"));

            store.MarkPendingReload("thread-1");
            Assert.That(store.HasPendingReload("thread-1"), Is.True);
            store.ClearPendingReload("thread-1");
            Assert.That(store.HasPendingReload("thread-1"), Is.False);

            store.Invalidate("thread-1");
            Assert.That(store.TryGetCached("thread-1", out cached), Is.False);
        }

        [Test]
        public void SessionHistoryEventPolicy_MatchesExpectedInvalidationEvents()
        {
            Assert.That(SessionHistoryEventPolicy.ShouldInvalidateForEvent(new AppServerEventArgs("turn_completed", "done", threadId: "thread-1")), Is.True);
            Assert.That(SessionHistoryEventPolicy.ShouldInvalidateForEvent(new AppServerEventArgs("context_compaction_completed", "done", threadId: "thread-1")), Is.True);
            Assert.That(SessionHistoryEventPolicy.ShouldInvalidateForEvent(new AppServerEventArgs("assistant", "reply", threadId: "thread-1")), Is.True);
            Assert.That(SessionHistoryEventPolicy.ShouldInvalidateForEvent(new AppServerEventArgs("system_notice", "note", threadId: "thread-1")), Is.False);
            Assert.That(SessionHistoryEventPolicy.ShouldInvalidateForEvent(new AppServerEventArgs("assistant", "reply")), Is.False);
        }

        [Test]
        public void SessionHistoryRealtimeRefreshPolicy_ReloadsOnlyCurrentThreadForTurnBoundaries()
        {
            Assert.That(SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(
                new AppServerEventArgs("turn_started", "started", threadId: "thread-1"),
                "THREAD-1"), Is.True);
            Assert.That(SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(
                new AppServerEventArgs("turn_completed", "done", threadId: "thread-1"),
                "THREAD-1"), Is.True);
            Assert.That(SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(
                new AppServerEventArgs("turn_failed", "failed", threadId: "thread-1"),
                "THREAD-1"), Is.True);
            Assert.That(SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(
                new AppServerEventArgs("context_compaction_completed", "done", threadId: "thread-1"),
                "THREAD-1"), Is.False);
            Assert.That(SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(
                new AppServerEventArgs("turn_started", "started", threadId: "thread-2"),
                "thread-1"), Is.False);
            Assert.That(SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(
                new AppServerEventArgs("turn_completed", "done"),
                "thread-1"), Is.False);
            Assert.That(SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(
                new AppServerEventArgs("system_notice", "note", threadId: "thread-1"),
                "thread-1"), Is.False);
        }

        [Test]
        public void WorkspaceGitMetadataRefreshPlanner_UpdatesCurrentAndRecentWorkspaceBranchInPlace()
        {
            var currentWorkspace = new WorkspaceInfo
            {
                Id = "workspace-a",
                Name = "Project",
                RootPath = @"E:\Project",
                GitBranch = "(Not a Git repository)"
            };
            var otherWorkspace = new WorkspaceInfo
            {
                Id = "workspace-b",
                Name = "Other",
                RootPath = @"E:\Other",
                GitBranch = "main"
            };
            var recentWorkspaces = new List<WorkspaceInfo>
            {
                currentWorkspace,
                otherWorkspace
            };
            var refreshedWorkspace = new WorkspaceInfo
            {
                Id = "WORKSPACE-A",
                Name = "Project",
                RootPath = @"E:\Project",
                GitBranch = "main"
            };

            var changed = WorkspaceGitMetadataRefreshPlanner.Apply(recentWorkspaces, currentWorkspace, refreshedWorkspace);

            Assert.That(changed, Is.True);
            Assert.That(currentWorkspace.GitBranch, Is.EqualTo("main"));
            Assert.That(recentWorkspaces[0], Is.SameAs(currentWorkspace));
            Assert.That(recentWorkspaces[0].GitBranch, Is.EqualTo("main"));
            Assert.That(recentWorkspaces[1], Is.SameAs(otherWorkspace));
        }

        [Test]
        public void WorkspaceGitMetadataRefreshPlanner_ReturnsFalseWhenMetadataIsUnchanged()
        {
            var currentWorkspace = new WorkspaceInfo
            {
                Id = "workspace-a",
                Name = "Project",
                RootPath = @"E:\Project",
                GitBranch = "main"
            };
            var recentWorkspaces = new List<WorkspaceInfo>
            {
                currentWorkspace
            };
            var refreshedWorkspace = new WorkspaceInfo
            {
                Id = "workspace-a",
                Name = "Project",
                RootPath = @"E:\Project",
                GitBranch = "main"
            };

            var changed = WorkspaceGitMetadataRefreshPlanner.Apply(recentWorkspaces, currentWorkspace, refreshedWorkspace);

            Assert.That(changed, Is.False);
            Assert.That(recentWorkspaces[0], Is.SameAs(currentWorkspace));
        }

        [Test]
        public void SessionHistorySilentReloadSelectionPolicy_PreservesSelectionOnlyWhenHistoryIsFocused()
        {
            Assert.That(SessionHistorySilentReloadSelectionPolicy.ShouldPreserveSelection(true, 4), Is.True);
            Assert.That(SessionHistorySilentReloadSelectionPolicy.ShouldPreserveSelection(false, 4), Is.False);
            Assert.That(SessionHistorySilentReloadSelectionPolicy.ShouldPreserveSelection(true, -1), Is.False);
        }

        [Test]
        public void SessionHistoryOptimisticMerge_PreservesLocalUserMessageWhenRemoteReloadLags()
        {
            var localItems = new[]
            {
                ChatHistoryItemFactory.Create("System", "Thread ID: thread-1", ChatHistoryCategory.System),
                ChatHistoryItemFactory.Create("User", "message sent before thread/read catches up", ChatHistoryCategory.Conversation)
            };
            var remoteItems = new[]
            {
                ChatHistoryItemFactory.Create("System", "Thread ID: thread-1", ChatHistoryCategory.System),
                ChatHistoryItemFactory.Create("Codex", "older assistant response", ChatHistoryCategory.Conversation)
            };

            var merged = SessionHistoryOptimisticMerge.PreserveLocalConversationItems(localItems, remoteItems);

            Assert.That(
                merged.Any(item =>
                    ChatHistoryItemFactory.IsUserSender(item.Sender) &&
                    item.FullText.Contains("message sent before thread/read catches up")),
                Is.True);
            Assert.That(
                merged.Any(item =>
                    string.Equals(item.Sender, "Codex", StringComparison.OrdinalIgnoreCase) &&
                    item.FullText.Contains("older assistant response")),
                Is.True);
        }

        [Test]
        public void SessionHistoryOptimisticMerge_DoesNotAppendOldLocalUserMessageMissingFromRemoteReload()
        {
            var oldUserMessage = "old image message";
            var localItems = new[]
            {
                ChatHistoryItemFactory.Create("System", "Thread ID: thread-1", ChatHistoryCategory.System),
                ChatHistoryItemFactory.Create("User", oldUserMessage, ChatHistoryCategory.Conversation),
                ChatHistoryItemFactory.Create("Codex", "assistant after old message", ChatHistoryCategory.Conversation),
                ChatHistoryItemFactory.Create("User", "latest question", ChatHistoryCategory.Conversation),
                ChatHistoryItemFactory.Create("Codex", "latest answer", ChatHistoryCategory.Conversation)
            };
            var remoteItems = new[]
            {
                ChatHistoryItemFactory.Create("System", "Thread ID: thread-1", ChatHistoryCategory.System),
                ChatHistoryItemFactory.Create("Codex", "assistant after old message", ChatHistoryCategory.Conversation),
                ChatHistoryItemFactory.Create("User", "latest question", ChatHistoryCategory.Conversation),
                ChatHistoryItemFactory.Create("Codex", "latest answer", ChatHistoryCategory.Conversation)
            };

            var merged = SessionHistoryOptimisticMerge.PreserveLocalConversationItems(localItems, remoteItems);

            Assert.That(
                merged.Count(item =>
                    ChatHistoryItemFactory.IsUserSender(item.Sender) &&
                    item.FullText.Contains(oldUserMessage)),
                Is.EqualTo(0));
        }

        [Test]
        public void SessionHistoryOptimisticMerge_PreservesOptimisticUserMessageWhenTextMatchesOlderMessage()
        {
            var repeatedMessage = "same message text";
            var localItems = new[]
            {
                ChatHistoryItemFactory.Create("System", "Thread ID: thread-1", ChatHistoryCategory.System),
                ChatHistoryItemFactory.Create("User", repeatedMessage, ChatHistoryCategory.Conversation),
                ChatHistoryItemFactory.Create("Codex", "older assistant response", ChatHistoryCategory.Conversation),
                ChatHistoryItemFactory.Create("User", repeatedMessage, ChatHistoryCategory.Conversation)
            };
            var remoteItems = new[]
            {
                ChatHistoryItemFactory.Create("System", "Thread ID: thread-1", ChatHistoryCategory.System),
                ChatHistoryItemFactory.Create("User", repeatedMessage, ChatHistoryCategory.Conversation),
                ChatHistoryItemFactory.Create("Codex", "older assistant response", ChatHistoryCategory.Conversation)
            };

            var merged = SessionHistoryOptimisticMerge.PreserveLocalConversationItems(localItems, remoteItems);

            Assert.That(
                merged.Count(item =>
                    ChatHistoryItemFactory.IsUserSender(item.Sender) &&
                    item.FullText.Contains(repeatedMessage)),
                Is.EqualTo(2));
        }

        [Test]
        public void ChatHistoryTurnNavigationPlanner_MovesByUserMessagesInDisplayOrder()
        {
            var items = new[]
            {
                new ChatHistoryItem { Sender = "System", FullText = "header" },
                new ChatHistoryItem { Sender = "Me", FullText = "first", TurnIndex = 0 },
                new ChatHistoryItem { Sender = "codex", FullText = "first reply", TurnIndex = 0 },
                new ChatHistoryItem { Sender = "Me", FullText = "second", TurnIndex = 0 },
                new ChatHistoryItem { Sender = "codex", FullText = "second reply", TurnIndex = 0 },
                new ChatHistoryItem { Sender = "Me", FullText = "third", TurnIndex = 0 }
            };

            Assert.That(
                ChatHistoryTurnNavigationPlanner.ResolveNextUserMessageIndex(
                    items,
                    2,
                    1,
                    ChatHistoryItemFactory.IsUserSender),
                Is.EqualTo(3));
            Assert.That(
                ChatHistoryTurnNavigationPlanner.ResolveNextUserMessageIndex(
                    items,
                    4,
                    -1,
                    ChatHistoryItemFactory.IsUserSender),
                Is.EqualTo(3));
            Assert.That(
                ChatHistoryTurnNavigationPlanner.ResolveNextUserMessageIndex(
                    items,
                    -1,
                    1,
                    ChatHistoryItemFactory.IsUserSender),
                Is.EqualTo(1));
            Assert.That(
                ChatHistoryTurnNavigationPlanner.ResolveNextUserMessageIndex(
                    items,
                    -1,
                    -1,
                    ChatHistoryItemFactory.IsUserSender),
                Is.EqualTo(5));
        }

        [Test]
        public void SessionActivationWorkflowPlanner_BuildActivationTouchesSessionAndSkipsExplicitSelection()
        {
            var session = new ChatSession
            {
                Id = "session-1",
                LastActiveAt = new DateTime(2026, 4, 1, 8, 0, 0)
            };
            var now = new DateTime(2026, 4, 19, 14, 30, 0);

            var plan = SessionActivationWorkflowPlanner.BuildActivation(session, true);
            plan.ApplyActivityTimestamp(now);

            Assert.That(plan.ShouldActivate, Is.True);
            Assert.That(plan.Session, Is.SameAs(session));
            Assert.That(plan.IncludeSystemHeader, Is.True);
            Assert.That(session.LastActiveAt, Is.EqualTo(now));
            Assert.That(
                SessionActivationWorkflowPlanner.ShouldSkipExplicitActivation(
                    new ChatSession { Id = "SESSION-1" },
                    session),
                Is.True);
            Assert.That(
                SessionActivationWorkflowPlanner.ShouldSkipExplicitActivation(
                    new ChatSession { Id = "session-2" },
                    session),
                Is.False);
        }

        [Test]
        public void ChatHistoryContextMenuPlanner_BuildBasicStateEnablesItemCommandsFromVisibleItems()
        {
            var selected = ChatHistoryContextMenuPlanner.BuildBasicState(true, 3);
            var empty = ChatHistoryContextMenuPlanner.BuildBasicState(false, 0);

            Assert.That(selected.CopySelectedEnabled, Is.True);
            Assert.That(selected.FindEnabled, Is.True);
            Assert.That(empty.CopySelectedEnabled, Is.False);
            Assert.That(empty.FindEnabled, Is.False);
        }

        [Test]
        public void ChatHistoryContextMenuPlanner_BuildPlanModeStatePreservesEnabledAndCheckedSeparately()
        {
            var state = ChatHistoryContextMenuPlanner.BuildPlanModeState(false, true);

            Assert.That(state.Enabled, Is.False);
            Assert.That(state.Checked, Is.True);
        }

        [Test]
        public void ChatHistoryContextMenuPlanner_BuildGoalStateMatchesExistingMenuRules()
        {
            var draftState = ChatHistoryContextMenuPlanner.BuildGoalState(true, false);
            var threadedState = ChatHistoryContextMenuPlanner.BuildGoalState(true, true);
            var readOnlyThreadState = ChatHistoryContextMenuPlanner.BuildGoalState(false, true);
            var unavailableState = ChatHistoryContextMenuPlanner.BuildGoalState(false, false);

            Assert.That(draftState.SetEnabled, Is.True);
            Assert.That(draftState.ViewEnabled, Is.False);
            Assert.That(draftState.ClearEnabled, Is.False);
            Assert.That(draftState.MenuEnabled, Is.True);

            Assert.That(threadedState.SetEnabled, Is.True);
            Assert.That(threadedState.ViewEnabled, Is.True);
            Assert.That(threadedState.ClearEnabled, Is.True);
            Assert.That(threadedState.MenuEnabled, Is.True);

            Assert.That(readOnlyThreadState.SetEnabled, Is.False);
            Assert.That(readOnlyThreadState.ViewEnabled, Is.True);
            Assert.That(readOnlyThreadState.ClearEnabled, Is.False);
            Assert.That(readOnlyThreadState.MenuEnabled, Is.True);

            Assert.That(unavailableState.SetEnabled, Is.False);
            Assert.That(unavailableState.ViewEnabled, Is.False);
            Assert.That(unavailableState.ClearEnabled, Is.False);
            Assert.That(unavailableState.MenuEnabled, Is.False);
        }

        [Test]
        public void SessionHistoryAnchorPlanner_AllowsAssistantMessageWithValidTurn()
        {
            var session = new ChatSession { ThreadId = "thread-1" };
            var item = new ChatHistoryItem
            {
                ThreadId = "thread-1",
                TurnId = "turn-2",
                TurnIndex = 2,
                TurnCount = 4,
                Sender = "codex"
            };

            var canFork = SessionHistoryAnchorPlanner.TryGetForkTrimTurns(
                session,
                item,
                sender => string.Equals(sender, "user", StringComparison.OrdinalIgnoreCase),
                out var trimTurns);

            Assert.That(canFork, Is.True);
            Assert.That(trimTurns, Is.EqualTo(2));
        }

        [Test]
        public void SessionHistoryLoadWorkflow_BuildRequestHandlesDraftAndCachedHistory()
        {
            using (UseCulture("en-US"))
            {
                var store = new SessionHistoryStateStore();
                var cachedItems = new[] { CreateHistoryItem("codex", "cached reply", ChatHistoryCategory.Conversation) };
                store.Store("thread-1", cachedItems);
                store.MarkPendingReload("thread-1");
                var workflow = CreateSessionHistoryLoadWorkflow(
                    store,
                    threadId => throw new InvalidOperationException("remote read should not run"));

                var draftRequest = workflow.BuildRequest(new ChatSession { Title = "Draft" }, true);
                var cachedRequest = workflow.BuildRequest(
                    new ChatSession { Title = "Cached", ThreadId = "thread-1" },
                    true);

                Assert.That(draftRequest.RequiresRemoteRead, Is.False);
                Assert.That(draftRequest.ImmediateResult.Kind, Is.EqualTo(SessionHistoryLoadResultKind.DraftSession));
                Assert.That(
                    draftRequest.ImmediateResult.HistoryItems.Select(item => item.MarkdownText).ToArray(),
                    Does.Contain("This session has no history messages yet. You can continue typing, and a remote thread will be created automatically when you send the first message."));

                Assert.That(cachedRequest.RequiresRemoteRead, Is.False);
                Assert.That(cachedRequest.ImmediateResult.Kind, Is.EqualTo(SessionHistoryLoadResultKind.CachedHistory));
                Assert.That(
                    cachedRequest.ImmediateResult.HistoryItems.Select(item => item.MarkdownText).ToArray(),
                    Does.Contain("cached reply"));
                Assert.That(store.HasPendingReload("thread-1"), Is.False);
            }
        }

        [Test]
        public void SessionHistoryLoadWorkflow_CompleteRemoteLoadStoresHistoryAndSessionPath()
        {
            using (UseCulture("en-US"))
            {
                var store = new SessionHistoryStateStore();
                var session = new ChatSession
                {
                    Id = "session-1",
                    Title = "Loaded",
                    ThreadId = "thread-1"
                };
                var workflow = CreateSessionHistoryLoadWorkflow(
                    store,
                    threadId => Task.FromResult(new AppServerThreadDetails
                    {
                        ThreadId = threadId,
                        SessionPath = "session-path.json",
                        TurnCount = 1,
                        Messages =
                        {
                            new AppServerThreadMessage
                            {
                                ThreadId = threadId,
                                TurnId = "turn-1",
                                TurnIndex = 1,
                                Sender = "codex",
                                Content = "remote reply"
                            }
                        }
                    }));

                var request = workflow.BuildRequest(session, true);
                var result = workflow.CompleteRemoteLoadAsync(request, (target, threadId) => true, null)
                    .GetAwaiter()
                    .GetResult();

                Assert.That(request.RequiresRemoteRead, Is.True);
                Assert.That(request.ImmediateResult.Kind, Is.EqualTo(SessionHistoryLoadResultKind.LoadingRemoteHistory));
                Assert.That(result.Kind, Is.EqualTo(SessionHistoryLoadResultKind.LoadedRemoteHistory));
                Assert.That(session.SessionPath, Is.EqualTo("session-path.json"));
                Assert.That(result.HistoryItems.Select(item => item.MarkdownText).ToArray(), Does.Contain("remote reply"));

                ChatHistoryItem[] cachedItems;
                Assert.That(store.TryGetCached("thread-1", out cachedItems), Is.True);
                Assert.That(cachedItems.Select(item => item.MarkdownText).ToArray(), Is.EqualTo(new[] { "remote reply" }));
            }
        }

        [Test]
        public void SessionHistoryLoadWorkflow_CompleteRemoteLoadWaitsBeforeBuildingDisplayItems()
        {
            using (UseCulture("en-US"))
            {
                var session = new ChatSession
                {
                    Id = "session-1",
                    Title = "Loaded",
                    ThreadId = "thread-1"
                };
                var beforeBuildCompleted = new TaskCompletionSource<bool>();
                var workflow = CreateSessionHistoryLoadWorkflow(
                    new SessionHistoryStateStore(),
                    threadId => Task.FromResult(new AppServerThreadDetails
                    {
                        ThreadId = threadId,
                        TurnCount = 1,
                        Messages =
                        {
                            new AppServerThreadMessage
                            {
                                ThreadId = threadId,
                                TurnId = "turn-1",
                                TurnIndex = 1,
                                Sender = "codex",
                                Content = "remote reply"
                            }
                        }
                    }));

                var request = workflow.BuildRequest(session, true);
                var resultTask = workflow.CompleteRemoteLoadAsync(
                    request,
                    (target, threadId) => true,
                    null,
                    () => beforeBuildCompleted.Task);

                Assert.That(resultTask.IsCompleted, Is.False);

                session.ThreadGoalObjective = "Fresh goal";
                session.ThreadGoalStatus = "active";
                beforeBuildCompleted.SetResult(true);

                var result = resultTask.GetAwaiter().GetResult();

                Assert.That(result.Kind, Is.EqualTo(SessionHistoryLoadResultKind.LoadedRemoteHistory));
                Assert.That(
                    result.HistoryItems.Select(item => item.MarkdownText).ToArray(),
                    Does.Contain("Goal: Fresh goal | status: Active"));
                Assert.That(
                    result.HistoryItems.Select(item => item.MarkdownText).ToArray(),
                    Does.Contain("remote reply"));
            }
        }

        [Test]
        public void SessionHistoryLoadWorkflow_CompleteRemoteLoadIgnoresStaleResult()
        {
            var session = new ChatSession
            {
                Id = "session-1",
                ThreadId = "thread-1"
            };
            var workflow = CreateSessionHistoryLoadWorkflow(
                new SessionHistoryStateStore(),
                threadId => Task.FromResult(new AppServerThreadDetails
                {
                    SessionPath = "stale-path.json"
                }));

            var request = workflow.BuildRequest(session, false);
            var result = workflow.CompleteRemoteLoadAsync(request, (target, threadId) => false, null)
                .GetAwaiter()
                .GetResult();

            Assert.That(result.Kind, Is.EqualTo(SessionHistoryLoadResultKind.Stale));
            Assert.That(result.ShouldReplaceHistory, Is.False);
            Assert.That(session.SessionPath, Is.Null);
        }

        [Test]
        public void SessionHistoryLoadWorkflow_CompleteRemoteLoadRecoversMissingThreadAsDraft()
        {
            using (UseCulture("en-US"))
            {
                var session = new ChatSession
                {
                    Id = "session-1",
                    Title = "Recovered",
                    ThreadId = "thread-1"
                };
                var workflow = CreateSessionHistoryLoadWorkflow(
                    new SessionHistoryStateStore(),
                    threadId => throw new InvalidOperationException("failed to locate rollout for thread"));

                var request = workflow.BuildRequest(session, true);
                var result = workflow.CompleteRemoteLoadAsync(
                        request,
                        (target, threadId) => true,
                        target =>
                        {
                            target.ThreadId = string.Empty;
                            target.SessionPath = null;
                            return true;
                        })
                    .GetAwaiter()
                    .GetResult();

                Assert.That(result.Kind, Is.EqualTo(SessionHistoryLoadResultKind.MissingThreadRecoveredAsDraft));
                Assert.That(result.StatusPriority, Is.EqualTo(SessionHistoryLoadStatusPriority.Normal));
                Assert.That(session.ThreadId, Is.Empty);
                Assert.That(
                    result.HistoryItems.Select(item => item.MarkdownText).ToArray(),
                    Does.Contain("This session has no history messages yet. You can continue typing, and a remote thread will be created automatically when you send the first message."));
            }
        }

        [Test]
        public void SessionHistoryLoadWorkflow_CompleteRemoteLoadMarksConnectivityRetry()
        {
            using (UseCulture("en-US"))
            {
                var store = new SessionHistoryStateStore();
                var session = new ChatSession
                {
                    Id = "session-1",
                    Title = "Retry",
                    ThreadId = "thread-1"
                };
                var workflow = CreateSessionHistoryLoadWorkflow(
                    store,
                    threadId => throw new TimeoutException("connection lost"),
                    ex => false,
                    ex => ex is TimeoutException);

                var request = workflow.BuildRequest(session, true);
                var result = workflow.CompleteRemoteLoadAsync(request, (target, threadId) => true, null)
                    .GetAwaiter()
                    .GetResult();

                Assert.That(result.Kind, Is.EqualTo(SessionHistoryLoadResultKind.ConnectivityUnavailable));
                Assert.That(result.StatusPriority, Is.EqualTo(SessionHistoryLoadStatusPriority.High));
                Assert.That(store.HasPendingReload("thread-1"), Is.True);
                Assert.That(
                    result.HistoryItems.Select(item => item.MarkdownText).ToArray(),
                    Does.Contain("Unable to connect to App Server for now. Press Enter to retry loading full history."));
            }
        }

        [Test]
        public void AutomaticThreadSyncWorkflow_RunAsyncFiltersLoadedUnknownThreads()
        {
            using (UseCulture("en-US"))
            {
                var workflow = new AutomaticThreadSyncWorkflow(
                    () => Task.FromResult(new List<string> { "thread-2" }),
                    () => Task.FromResult(new List<AppServerThreadSummary>
                    {
                        new AppServerThreadSummary
                        {
                            ThreadId = "thread-1",
                            WorkingDirectory = @"E:\Repo"
                        },
                        new AppServerThreadSummary
                        {
                            ThreadId = "thread-2",
                            WorkingDirectory = @"E:\Repo"
                        },
                        new AppServerThreadSummary
                        {
                            ThreadId = "thread-3",
                            WorkingDirectory = @"E:\Repo"
                        }
                    }));

                var result = workflow.RunAsync(
                        new HashSet<string>(new[] { "thread-1" }, StringComparer.OrdinalIgnoreCase),
                        true,
                        _ => true,
                        _ => true,
                        _ => false,
                        true,
                        new DateTime(2026, 4, 19, 15, 0, 0))
                    .GetAwaiter()
                    .GetResult();

                Assert.That(result.HasFailure, Is.False);
                Assert.That(result.CompletedCheck, Is.True);
                Assert.That(result.ShouldReportNoNewThreads, Is.True);
                Assert.That(result.ThreadSummaries.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "thread-1", "thread-2", "thread-3" }));
                Assert.That(result.NewThreads.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "thread-2" }));
                Assert.That(result.CompletionPlan.StatusText, Is.EqualTo("Automatically synced 1 new thread(s)"));
            }
        }

        [Test]
        public void AutomaticThreadSyncWorkflow_RunAsyncKeepsKnownParentSubAgentsWhenLoadedThreadRestrictionIsEnabled()
        {
            using (UseCulture("en-US"))
            {
                var workflow = new AutomaticThreadSyncWorkflow(
                    () => Task.FromResult(new List<string> { "parent-thread" }),
                    () => Task.FromResult(new List<AppServerThreadSummary>
                    {
                        new AppServerThreadSummary
                        {
                            ThreadId = "child-thread",
                            WorkingDirectory = @"E:\Repo",
                            ThreadSource = "subagent",
                            ParentThreadId = "parent-thread"
                        }
                    }));

                var result = workflow.RunAsync(
                        new HashSet<string>(new[] { "parent-thread" }, StringComparer.OrdinalIgnoreCase),
                        true,
                        _ => true,
                        _ => true,
                        _ => false,
                        true,
                        new DateTime(2026, 4, 19, 15, 0, 0))
                    .GetAwaiter()
                    .GetResult();

                Assert.That(result.HasFailure, Is.False);
                Assert.That(result.CompletedCheck, Is.True);
                Assert.That(result.NewThreads.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "child-thread" }));
            }
        }

        [Test]
        public void AutomaticThreadSyncWorkflow_RunAsyncSkipsIgnoredProjectInstructionHelperThreads()
        {
            using (UseCulture("en-US"))
            {
                var workflow = new AutomaticThreadSyncWorkflow(
                    () => Task.FromResult(new List<string> { "thread-helper", "thread-fresh" }),
                    () => Task.FromResult(new List<AppServerThreadSummary>
                    {
                        new AppServerThreadSummary
                        {
                            ThreadId = "thread-helper",
                            WorkingDirectory = @"E:\Repo"
                        },
                        new AppServerThreadSummary
                        {
                            ThreadId = "thread-fresh",
                            WorkingDirectory = @"E:\Repo"
                        }
                    }));

                var result = workflow.RunAsync(
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        true,
                        _ => true,
                        _ => true,
                        _ => false,
                        true,
                        new DateTime(2026, 4, 19, 15, 0, 0),
                        false,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "thread-helper" })
                    .GetAwaiter()
                    .GetResult();

                Assert.That(result.HasFailure, Is.False);
                Assert.That(result.NewThreads.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "thread-fresh" }));
            }
        }

        [Test]
        public void AutomaticThreadSyncWorkflow_CanUseThreadClientWithoutFullAppServerClient()
        {
            using (UseCulture("en-US"))
            {
                var client = new RecordingThreadClient
                {
                    LoadedThreadIds = new List<string> { "thread-2" },
                    Threads = new List<AppServerThreadSummary>
                    {
                        new AppServerThreadSummary
                        {
                            ThreadId = "thread-2",
                            WorkingDirectory = @"E:\Repo"
                        }
                    }
                };
                var workflow = new AutomaticThreadSyncWorkflow(client);

                var result = workflow.RunAsync(
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                        true,
                        _ => true,
                        _ => true,
                        _ => false,
                        true,
                        new DateTime(2026, 4, 19, 15, 0, 0))
                    .GetAwaiter()
                    .GetResult();

                Assert.That(result.HasFailure, Is.False);
                Assert.That(result.NewThreads.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "thread-2" }));
            }
        }

        [Test]
        public void AutomaticThreadSyncRunCoordinator_QueuesRequestArrivingWhileSyncIsRunning()
        {
            var coordinator = new AutomaticThreadSyncRunCoordinator();

            Assert.That(coordinator.TryBegin(true, false, false), Is.True);
            Assert.That(coordinator.IsRunning, Is.True);

            Assert.That(coordinator.TryBegin(true, true, false), Is.False);

            bool reportNoNewThreads;
            bool forceFullThreadScan;
            Assert.That(coordinator.CompleteAndTryConsumePending(out reportNoNewThreads, out forceFullThreadScan), Is.True);
            Assert.That(reportNoNewThreads, Is.True);
            Assert.That(forceFullThreadScan, Is.False);
            Assert.That(coordinator.IsRunning, Is.False);
        }

        [Test]
        public void AutomaticThreadSyncRunCoordinator_DoesNotQueueWhenSyncCannotStart()
        {
            var coordinator = new AutomaticThreadSyncRunCoordinator();

            Assert.That(coordinator.TryBegin(false, true, true), Is.False);

            bool reportNoNewThreads;
            bool forceFullThreadScan;
            Assert.That(coordinator.CompleteAndTryConsumePending(out reportNoNewThreads, out forceFullThreadScan), Is.False);
            Assert.That(reportNoNewThreads, Is.False);
            Assert.That(forceFullThreadScan, Is.False);
        }

        [Test]
        public void AutomaticThreadSyncRunCoordinator_QueuesForcedFullScanRequestArrivingWhileSyncIsRunning()
        {
            var coordinator = new AutomaticThreadSyncRunCoordinator();

            Assert.That(coordinator.TryBegin(true, false, false), Is.True);
            Assert.That(coordinator.TryBegin(true, false, true), Is.False);

            bool reportNoNewThreads;
            bool forceFullThreadScan;
            Assert.That(coordinator.CompleteAndTryConsumePending(out reportNoNewThreads, out forceFullThreadScan), Is.True);
            Assert.That(reportNoNewThreads, Is.False);
            Assert.That(forceFullThreadScan, Is.True);
        }

        [Test]
        public void AutomaticThreadSyncFollowUpTracker_SchedulesShortRetryWindowAndResetsOnNewRequest()
        {
            var tracker = new AutomaticThreadSyncFollowUpTracker(3, 2000, 5000);

            tracker.RequestFollowUp();

            int delay;
            Assert.That(tracker.TryConsumeNextDelay(out delay), Is.True);
            Assert.That(delay, Is.EqualTo(2000));
            Assert.That(tracker.TryConsumeNextDelay(out delay), Is.True);
            Assert.That(delay, Is.EqualTo(5000));

            tracker.RequestFollowUp();

            Assert.That(tracker.TryConsumeNextDelay(out delay), Is.True);
            Assert.That(delay, Is.EqualTo(2000));
            Assert.That(tracker.TryConsumeNextDelay(out delay), Is.True);
            Assert.That(delay, Is.EqualTo(5000));
            Assert.That(tracker.TryConsumeNextDelay(out delay), Is.True);
            Assert.That(delay, Is.EqualTo(5000));
            Assert.That(tracker.TryConsumeNextDelay(out delay), Is.False);
        }

        [Test]
        public void AppServerEventRouting_SchedulesThreadRefreshFollowUpForThreadDiscoveryEvents()
        {
            Assert.That(AppServerEventRouting.ShouldScheduleThreadListRefreshFollowUp(AppServerEventRoute.ThreadStarted), Is.True);
            Assert.That(AppServerEventRouting.ShouldScheduleThreadListRefreshFollowUp(AppServerEventRoute.ThreadStatusChanged), Is.True);
            Assert.That(AppServerEventRouting.ShouldScheduleThreadListRefreshFollowUp(AppServerEventRoute.TurnStarted), Is.True);
            Assert.That(AppServerEventRouting.ShouldScheduleThreadListRefreshFollowUp(AppServerEventRoute.TurnCompleted), Is.False);
            Assert.That(AppServerEventRouting.ShouldScheduleThreadListRefreshFollowUp(AppServerEventRoute.Assistant), Is.False);
        }

        [Test]
        public void ThreadDiscoveryRefreshPlanner_RequestsRefreshForUnknownThreadSummaryEvent()
        {
            var route = AppServerEventRouting.Resolve(
                new AppServerEventArgs("reasoning_summary_delta", "thinking", threadId: "child-thread"));

            var shouldRefresh = ThreadDiscoveryRefreshPlanner.ShouldRequestThreadListRefresh(
                route,
                "child-thread",
                threadId => string.Equals(threadId, "parent-thread", StringComparison.OrdinalIgnoreCase));
            var shouldFollowUp = ThreadDiscoveryRefreshPlanner.ShouldScheduleFollowUp(
                route,
                "child-thread",
                threadId => string.Equals(threadId, "parent-thread", StringComparison.OrdinalIgnoreCase));

            Assert.That(route, Is.EqualTo(AppServerEventRoute.SummaryOnly));
            Assert.That(shouldRefresh, Is.True);
            Assert.That(shouldFollowUp, Is.True);
        }

        [Test]
        public void AppServerClient_MergeLoadedThreadIdsKeepsLocallyKnownIdsMissingFromRemoteList()
        {
            var merged = AppServerClient.MergeLoadedThreadIdsForSync(
                new[] { "remote-thread" },
                new[] { "event-thread", "REMOTE-THREAD" },
                null);

            Assert.That(merged, Is.EqualTo(new[] { "remote-thread", "event-thread" }));
        }

        [Test]
        public void AppServerClient_ShouldRememberLoadedThreadIdFromAnyThreadEvent()
        {
            Assert.That(
                AppServerClient.ShouldRememberLoadedThreadIdFromEvent(
                    new AppServerEventArgs("reasoning_summary_delta", "thinking", threadId: "child-thread")),
                Is.True);
            Assert.That(
                AppServerClient.ShouldRememberLoadedThreadIdFromEvent(
                    new AppServerEventArgs("system_notice", "notice")),
                Is.False);
            Assert.That(
                AppServerClient.ShouldRememberLoadedThreadIdFromEvent(
                    new AppServerEventArgs("thread_deleted", "deleted", threadId: "child-thread")),
                Is.False);
            Assert.That(
                AppServerClient.ShouldRememberLoadedThreadIdFromEvent(
                    new AppServerEventArgs("thread_closed", "closed", threadId: "child-thread")),
                Is.False);
        }

        [Test]
        public void ArchivedThreadRestoreWorkflow_RestoreAsyncMergesPartialFailure()
        {
            using (UseCulture("en-US"))
            {
                var selectedThreads = new List<AppServerThreadSummary>
                {
                    new AppServerThreadSummary
                    {
                        ThreadId = "thread-1",
                        Title = "Keep"
                    },
                    new AppServerThreadSummary
                    {
                        ThreadId = "thread-2",
                        Title = "Fail"
                    }
                };
                var workflow = new ArchivedThreadRestoreWorkflow(
                    threadId =>
                    {
                        if (string.Equals(threadId, "thread-2", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("restore failed");
                        }

                        return Task.CompletedTask;
                    },
                    () => Task.FromResult(new List<AppServerThreadSummary>
                    {
                        new AppServerThreadSummary
                        {
                            ThreadId = "thread-1",
                            Title = "Keep"
                        }
                    }));

                var result = workflow.RestoreAsync(selectedThreads, thread => thread.Title)
                    .GetAwaiter()
                    .GetResult();

                Assert.That(result.HasRestorableThreads, Is.True);
                Assert.That(result.RestorableThreads.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "thread-1" }));
                Assert.That(result.CompletionPlan.RestoredThreadCount, Is.EqualTo(1));
                Assert.That(result.CompletionPlan.FailureCount, Is.EqualTo(1));
                Assert.That(result.CompletionPlan.HasFailureDialogMessage, Is.True);
                Assert.That(result.CompletionPlan.FailureDialogMessage, Does.Contain("Fail"));
            }
        }

        private sealed class RecordingThreadClient : UnsupportedAppServerThreadClient
        {
            public List<string> LoadedThreadIds { get; set; } = new List<string>();

            public List<AppServerThreadSummary> Threads { get; set; } = new List<AppServerThreadSummary>();

            public Dictionary<string, AppServerThreadSummary> ForkResults { get; } = new Dictionary<string, AppServerThreadSummary>(StringComparer.OrdinalIgnoreCase);

            public List<string> ForkedThreadIds { get; } = new List<string>();

            public List<string> GitMetadataThreadIds { get; } = new List<string>();

            public override Task<string> CreateThreadAsync(
                string workspacePath,
                ApprovalMode? approvalMode,
                string modelId,
                string reasoningEffort,
                AppServerThreadToolContext toolContext = null)
            {
                throw new NotSupportedException();
            }

            public override Task SendUserMessageAsync(
                string threadId,
                string workspacePath,
                IReadOnlyCollection<AppServerUserInputItem> inputItems,
                ApprovalMode? approvalMode,
                string modelId,
                string reasoningEffort,
                AppServerCollaborationMode collaborationMode = null)
            {
                throw new NotSupportedException();
            }

            public override Task<List<AppServerThreadSummary>> ListThreadsAsync()
            {
                return Task.FromResult(Threads);
            }

            public override Task<List<AppServerThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(Threads);
            }

            public override Task<List<string>> ListLoadedThreadIdsAsync(int? limit = null)
            {
                return Task.FromResult(LoadedThreadIds);
            }

            public override Task<List<string>> ListLoadedThreadIdsAsync(int? limit, CancellationToken cancellationToken)
            {
                return Task.FromResult(LoadedThreadIds);
            }

            public override Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerConversationSummary> GetConversationSummaryAsync(AppServerConversationSummaryRequest request, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult<AppServerConversationSummary>(null);
            }

            public override Task<AppServerThreadDetails> ReadThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public override Task ArchiveThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task UnarchiveThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadSummary> ForkThreadAsync(string threadId)
            {
                ForkedThreadIds.Add(threadId);
                AppServerThreadSummary summary;
                if (!ForkResults.TryGetValue(threadId, out summary))
                {
                    summary = new AppServerThreadSummary
                    {
                        ThreadId = threadId + "-copy",
                        SessionPath = threadId + "-copy.jsonl"
                    };
                }

                return Task.FromResult(summary);
            }


            public override Task CompactThreadAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerTurnSteerResult> TrySteerTurnAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, string clientUserMessageId = null)
            {
                return Task.FromResult(AppServerTurnSteerResult.NotAttempted(null, "test-double"));
            }

            public override Task<AppServerThreadInjectItemsResult> InjectThreadItemsAsync(string threadId, IReadOnlyCollection<IDictionary<string, object>> items, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(new AppServerThreadInjectItemsResult { Succeeded = true });
            }

            public override Task ApproveGuardianDeniedActionAsync(AppServerGuardianDeniedActionApprovalRequest request, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.CompletedTask;
            }

            public override Task<AppServerReviewStartResult> StartReviewAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, AppServerReviewTarget target, bool runDetached, string reasoningEffort = null, string reviewModelId = null)
            {
                throw new NotSupportedException();
            }

            public override Task DeleteThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task UnsubscribeThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task DeleteThreadFileAsync(string sessionPath)
            {
                throw new NotSupportedException();
            }

            public override Task SetThreadNameAsync(string threadId, string name)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadGoal> SetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, string objective, long? tokenBudget = null)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
            {
                throw new NotSupportedException();
            }

            public override Task ClearThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
            {
                throw new NotSupportedException();
            }

            public override Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo)
            {
                GitMetadataThreadIds.Add(threadId);
                return Task.CompletedTask;
            }
        }

        [Test]
        public void AppServerEventRouting_ResolveMapsKnownRoutesAndBufferedOutput()
        {
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("auth_changed", "ok")), Is.EqualTo(AppServerEventRoute.AuthenticationChanged));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("rate_limits_changed", "skip")), Is.EqualTo(AppServerEventRoute.Ignore));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("turn_plan_updated", "plan")), Is.EqualTo(AppServerEventRoute.SummaryOnly));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("file_change_patch_updated", "patch")), Is.EqualTo(AppServerEventRoute.SummaryOnly));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("image_generation_started", "image")), Is.EqualTo(AppServerEventRoute.SummaryOnly));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("image_generation_completed", "image")), Is.EqualTo(AppServerEventRoute.SummaryOnly));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("subagent_activity", "agent")), Is.EqualTo(AppServerEventRoute.SummaryOnly));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("plan", "plan")), Is.EqualTo(AppServerEventRoute.Assistant));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("command_exec_output", "stdout")), Is.EqualTo(AppServerEventRoute.StandaloneCommandOutput));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("command_terminal_interaction", "input")), Is.EqualTo(AppServerEventRoute.CommandTerminalInteraction));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("thread_unarchived", "done")), Is.EqualTo(AppServerEventRoute.ThreadArchivedChanged));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("thread_deleted", "done")), Is.EqualTo(AppServerEventRoute.ThreadArchivedChanged));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("thread_closed", "done")), Is.EqualTo(AppServerEventRoute.ThreadArchivedChanged));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("thread_name_updated", "New title")), Is.EqualTo(AppServerEventRoute.ThreadNameChanged));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("external_agent_config_import_completed", "done")), Is.EqualTo(AppServerEventRoute.ExternalAgentConfigImportCompleted));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("windows_sandbox_setup_completed", "done")), Is.EqualTo(AppServerEventRoute.Ignore));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("error", "boom")), Is.EqualTo(AppServerEventRoute.Error));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("protocol_notice", "notice")), Is.EqualTo(AppServerEventRoute.ProtocolNotice));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("protocol_warning", "warning")), Is.EqualTo(AppServerEventRoute.ProtocolWarning));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("windows_world_writable_warning", "warning")), Is.EqualTo(AppServerEventRoute.ProtocolWarning));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("mcp_startup_status_updated", "ready")), Is.EqualTo(AppServerEventRoute.McpStartupStatusUpdated));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("internal_tool_running", "tool")), Is.EqualTo(AppServerEventRoute.InternalToolStatus));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("internal_tool_completed", "tool")), Is.EqualTo(AppServerEventRoute.InternalToolStatus));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("internal_tool_failed", "tool")), Is.EqualTo(AppServerEventRoute.InternalToolStatus));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("auto_approval_review_started", "review started")), Is.EqualTo(AppServerEventRoute.InternalToolStatus));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("auto_approval_review_completed", "review completed")), Is.EqualTo(AppServerEventRoute.InternalToolStatus));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("turn_moderation_metadata", "moderation metadata")), Is.EqualTo(AppServerEventRoute.ProtocolNotice));
            Assert.That(AppServerEventRouting.Resolve(new AppServerEventArgs("internal_tools_registered", "tools")), Is.EqualTo(AppServerEventRoute.SystemNotice));

            Assert.That(AppServerEventRouting.IsBufferedCommandOutputEvent(new AppServerEventArgs("command_output", "running")), Is.True);
            Assert.That(AppServerEventRouting.IsBufferedCommandOutputEvent(new AppServerEventArgs("command_exec_output", "running")), Is.True);
            Assert.That(AppServerEventRouting.IsBufferedCommandOutputEvent(new AppServerEventArgs("assistant", "reply")), Is.False);
            Assert.That(AppServerEventRouting.IsBufferedTurnSummaryEvent(new AppServerEventArgs("plan_delta", "plan")), Is.True);
            Assert.That(AppServerEventRouting.IsBufferedTurnSummaryEvent(new AppServerEventArgs("turn_plan_updated", "plan")), Is.True);
            Assert.That(AppServerEventRouting.IsBufferedTurnSummaryEvent(new AppServerEventArgs("file_change_patch_updated", "patch")), Is.True);
            Assert.That(AppServerEventRouting.IsBufferedTurnSummaryEvent(new AppServerEventArgs("assistant", "reply")), Is.False);
        }

        [Test]
        public void MainFormSource_OfficeInstructionsUseCapabilityFirstToolGuidance()
        {
            var mainFormSource = RepositorySource.ReadSourceFile("MainForm.cs");
            var instructions = MyCodex.Application.Capabilities.OfficeDeveloperInstructionComposer.Compose();

            Assert.That(mainFormSource, Does.Contain("OfficeDeveloperInstructionComposer.Compose"));
            Assert.That(instructions, Does.Contain("Use enabled Skills, plugins, MCP tools, and CLI tools"));
            Assert.That(instructions, Does.Contain("Do not prefer a specific Office plugin solely because it is installed"));
            Assert.That(instructions, Does.Contain("OfficeCLI"));
            Assert.That(instructions, Does.Not.Contain("office/create_document"));
            Assert.That(instructions, Does.Not.Contain("office/read_document"));
            Assert.That(instructions, Does.Not.Contain("office/edit_document"));
            Assert.That(instructions, Does.Not.Contain("Prefer mycodex-office"));
        }

        [Test]
        public void AppServerEventRouting_RequestsThreadListRefreshAtThreadDiscoveryBoundaries()
        {
            Assert.That(AppServerEventRouting.ShouldRequestThreadListRefresh(AppServerEventRoute.ThreadStarted), Is.True);
            Assert.That(AppServerEventRouting.ShouldRequestThreadListRefresh(AppServerEventRoute.ThreadStatusChanged), Is.True);
            Assert.That(AppServerEventRouting.ShouldRequestThreadListRefresh(AppServerEventRoute.TurnStarted), Is.True);
            Assert.That(AppServerEventRouting.ShouldRequestThreadListRefresh(AppServerEventRoute.TurnCompleted), Is.True);
            Assert.That(AppServerEventRouting.ShouldRequestThreadListRefresh(AppServerEventRoute.ThreadArchivedChanged), Is.True);
            Assert.That(AppServerEventRouting.ShouldRequestThreadListRefresh(AppServerEventRoute.Assistant), Is.False);
        }

        [Test]
        public void AppServerEventRouting_TerminalAndErrorFlagsMatchExpectedRoutes()
        {
            Assert.That(AppServerEventRouting.IsTurnCompleted(AppServerEventRoute.TurnCompleted), Is.True);
            Assert.That(AppServerEventRouting.IsTurnCompleted(AppServerEventRoute.TurnFailed), Is.False);

            Assert.That(AppServerEventRouting.IsTurnTerminal(AppServerEventRoute.TurnCompleted), Is.True);
            Assert.That(AppServerEventRouting.IsTurnTerminal(AppServerEventRoute.TurnInterrupted), Is.True);
            Assert.That(AppServerEventRouting.IsTurnTerminal(AppServerEventRoute.TurnFailed), Is.True);
            Assert.That(AppServerEventRouting.IsTurnTerminal(AppServerEventRoute.Error), Is.False);

            Assert.That(AppServerEventRouting.IsErrorDisplayEvent(AppServerEventRoute.TurnFailed), Is.True);
            Assert.That(AppServerEventRouting.IsErrorDisplayEvent(AppServerEventRoute.Error), Is.True);
            Assert.That(AppServerEventRouting.IsErrorDisplayEvent(AppServerEventRoute.TurnInterrupted), Is.False);
        }

        [Test]
        public void SessionHistoryRealtimeRefreshPolicy_SkipsCurrentLocalDesktopTurn()
        {
            var started = new AppServerEventArgs("turn_started", "started", threadId: "thread-1", turnId: "turn-1");
            var completed = new AppServerEventArgs("turn_completed", "done", threadId: "thread-1", turnId: "turn-1");

            Assert.That(SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(started, "thread-1", true), Is.False);
            Assert.That(SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(completed, "thread-1", true), Is.False);
            Assert.That(SessionHistoryRealtimeRefreshPolicy.ShouldReloadCurrentSessionForEvent(completed, "thread-1", false), Is.True);
        }

        [Test]
        public void AppServerEventRouting_GetFallbackCategoryMatchesExpectedCategories()
        {
            Assert.That(AppServerEventRouting.GetFallbackCategory(AppServerEventRoute.TurnStarted), Is.EqualTo(ChatHistoryCategory.Processing));
            Assert.That(AppServerEventRouting.GetFallbackCategory(AppServerEventRoute.CommandCompleted), Is.EqualTo(ChatHistoryCategory.Command));
            Assert.That(AppServerEventRouting.GetFallbackCategory(AppServerEventRoute.ApprovalResolved), Is.EqualTo(ChatHistoryCategory.Approval));
            Assert.That(AppServerEventRouting.GetFallbackCategory(AppServerEventRoute.AuthenticationChanged), Is.EqualTo(ChatHistoryCategory.Account));
            Assert.That(AppServerEventRouting.GetFallbackCategory(AppServerEventRoute.SkillFilesChanged), Is.EqualTo(ChatHistoryCategory.FileChange));
            Assert.That(AppServerEventRouting.GetFallbackCategory(AppServerEventRoute.ExternalAgentConfigImportCompleted), Is.EqualTo(ChatHistoryCategory.FileChange));
            Assert.That(AppServerEventRouting.GetFallbackCategory(AppServerEventRoute.ProtocolNotice), Is.EqualTo(ChatHistoryCategory.ProtocolNotice));
            Assert.That(AppServerEventRouting.GetFallbackCategory(AppServerEventRoute.ProtocolWarning), Is.EqualTo(ChatHistoryCategory.ProtocolWarning));
            Assert.That(AppServerEventRouting.GetFallbackCategory(AppServerEventRoute.Default), Is.EqualTo(ChatHistoryCategory.System));
        }

        [Test]
        public void ThreadCompactionEventPlanner_ShouldHandleTurnCompletedMatchesExpectedCases()
        {
            Assert.That(ThreadCompactionEventPlanner.ShouldHandleTurnCompleted(true, false, null, "turn-1"), Is.True);
            Assert.That(ThreadCompactionEventPlanner.ShouldHandleTurnCompleted(false, true, null, "turn-1"), Is.True);
            Assert.That(ThreadCompactionEventPlanner.ShouldHandleTurnCompleted(false, false, "turn-1", "turn-1"), Is.True);
            Assert.That(ThreadCompactionEventPlanner.ShouldHandleTurnCompleted(false, false, "turn-1", null), Is.True);
            Assert.That(ThreadCompactionEventPlanner.ShouldHandleTurnCompleted(false, false, "turn-1", "turn-2"), Is.False);
            Assert.That(ThreadCompactionEventPlanner.ShouldHandleTurnCompleted(false, false, null, "turn-1"), Is.False);
        }

        [Test]
        public void ThreadCompactionEventPlanner_ShouldHandleFailureWhenPendingOrActive()
        {
            Assert.That(ThreadCompactionEventPlanner.ShouldHandleFailure(true, false), Is.True);
            Assert.That(ThreadCompactionEventPlanner.ShouldHandleFailure(false, true), Is.True);
            Assert.That(ThreadCompactionEventPlanner.ShouldHandleFailure(false, false), Is.False);
        }

        [Test]
        public void ThreadCompactionEventPlanner_BuildCompletionPlanKeepsChatHistoryUnchanged()
        {
            var currentSessionPlan = ThreadCompactionEventPlanner.BuildCompletionPlan(true);
            var backgroundSessionPlan = ThreadCompactionEventPlanner.BuildCompletionPlan(false);

            Assert.That(currentSessionPlan.ShouldClearCompactionState, Is.True);
            Assert.That(currentSessionPlan.ShouldInvalidateHistoryCache, Is.True);
            Assert.That(currentSessionPlan.ShouldRefreshInputState, Is.True);
            Assert.That(currentSessionPlan.ShouldReloadHistory, Is.False);
            Assert.That(currentSessionPlan.ShouldAddChatHistoryMessage, Is.False);
            Assert.That(currentSessionPlan.ShouldAnnounceCompletion, Is.True);

            Assert.That(backgroundSessionPlan.ShouldClearCompactionState, Is.True);
            Assert.That(backgroundSessionPlan.ShouldInvalidateHistoryCache, Is.True);
            Assert.That(backgroundSessionPlan.ShouldRefreshInputState, Is.True);
            Assert.That(backgroundSessionPlan.ShouldReloadHistory, Is.False);
            Assert.That(backgroundSessionPlan.ShouldAddChatHistoryMessage, Is.False);
            Assert.That(backgroundSessionPlan.ShouldAnnounceCompletion, Is.False);
        }

        [Test]
        public void ThreadImportWorkflowPlanner_ShouldRunAutomaticSyncOnlyWhenReady()
        {
            Assert.That(ThreadImportWorkflowPlanner.ShouldRunAutomaticSync(false, true, true), Is.True);
            Assert.That(ThreadImportWorkflowPlanner.ShouldRunAutomaticSync(true, true, true), Is.False);
            Assert.That(ThreadImportWorkflowPlanner.ShouldRunAutomaticSync(false, false, true), Is.False);
            Assert.That(ThreadImportWorkflowPlanner.ShouldRunAutomaticSync(false, true, false), Is.False);
            Assert.That(ThreadImportWorkflowPlanner.ShouldRunAutomaticSync(false, false, true, true), Is.True);
            Assert.That(ThreadImportWorkflowPlanner.ShouldRunAutomaticSync(true, false, true, true), Is.False);
        }

        [Test]
        public void ThreadImportWorkflowPlanner_FilterAutomaticSyncThreadsKeepsOnlyImportableThreads()
        {
            var threads = new[]
            {
                Thread("known", @"C:\repo"),
                Thread("missing-path", @"C:\missing"),
                Thread("not-loaded", @"C:\repo2"),
                Thread("unknown-workspace", @"C:\other"),
                Thread("fresh", @"C:\repo")
            };
            var knownThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "known" };
            var loadedThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "known", "fresh", "unknown-workspace" };

            var result = ThreadImportWorkflowPlanner.FilterAutomaticSyncThreads(
                threads,
                knownThreadIds,
                loadedThreadIds,
                true,
                path => path != @"C:\missing",
                path => path == @"C:\repo");

            Assert.That(result.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "fresh" }));
        }

        [Test]
        public void ThreadImportWorkflowPlanner_FilterAutomaticSyncThreadsAllowsKnownParentSubAgentsOutsideLoadedThreads()
        {
            var child = Thread("child-thread", @"C:\repo");
            child.ThreadSource = "subagent";
            child.ParentThreadId = "parent-thread";
            var unrelatedChild = Thread("unrelated-child", @"C:\repo");
            unrelatedChild.ThreadSource = "subagent";
            unrelatedChild.ParentThreadId = "other-parent";

            var result = ThreadImportWorkflowPlanner.FilterAutomaticSyncThreads(
                new[] { child, unrelatedChild },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "parent-thread" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "parent-thread" },
                true,
                _ => true,
                _ => true);

            Assert.That(result.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "child-thread" }));
        }

        [Test]
        public void ThreadImportWorkflowPlanner_FilterAutomaticSyncThreadsAllowsKnownParentSubAgentsOutsideKnownWorkspaces()
        {
            var child = Thread("child-thread", @"C:\repo\child-worktree");
            child.ThreadSource = "subagent";
            child.ParentThreadId = "parent-thread";

            var result = ThreadImportWorkflowPlanner.FilterAutomaticSyncThreads(
                new[] { child },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "parent-thread" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "parent-thread" },
                true,
                _ => true,
                _ => false);

            Assert.That(result.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "child-thread" }));
        }

        [Test]
        public void ThreadImportWorkflowPlanner_FilterAutomaticSyncThreadsCanBootstrapUnknownWorkspaces()
        {
            var threads = new[]
            {
                Thread("known", @"/Users/demo/repo"),
                Thread("fresh", @"/Users/demo/new")
            };
            var knownThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "known" };

            var result = ThreadImportWorkflowPlanner.FilterAutomaticSyncThreads(
                threads,
                knownThreadIds,
                null,
                false,
                _ => true,
                _ => false,
                true);

            Assert.That(result.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "fresh" }));
        }

        [Test]
        public void ThreadImportWorkflowPlanner_FilterAutomaticSyncThreadsSkipsIgnoredThreadIds()
        {
            var threads = new[]
            {
                Thread("helper", @"C:\repo"),
                Thread("fresh", @"C:\repo")
            };

            var result = ThreadImportWorkflowPlanner.FilterAutomaticSyncThreads(
                threads,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "helper", "fresh" },
                true,
                _ => true,
                _ => true,
                false,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "helper" });

            Assert.That(result.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "fresh" }));
        }

        [Test]
        public void ThreadImportWorkflowPlanner_FilterAutomaticSyncThreadsRejectsOfficeRootInCodingMode()
        {
            var officeRoot = Path.GetFullPath(@"C:\Users\demo\Documents");
            var threads = new[]
            {
                Thread("office", officeRoot),
                Thread("coding", @"C:\repo")
            };

            var result = ThreadImportWorkflowPlanner.FilterAutomaticSyncThreads(
                threads,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                null,
                false,
                _ => true,
                _ => true,
                false,
                null,
                AppInteractionMode.Coding,
                officeRoot);

            Assert.That(result.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "coding" }));
        }

        [Test]
        public void ThreadImportWorkflowPlanner_FilterAutomaticSyncThreadsAcceptsOnlyOfficeRootInOfficeMode()
        {
            var officeRoot = Path.GetFullPath(@"C:\Users\demo\Documents");
            var nestedOfficeDirectory = Path.Combine(officeRoot, "Reports");
            var threads = new[]
            {
                Thread("office-root", officeRoot),
                Thread("office-nested", nestedOfficeDirectory),
                Thread("coding", @"C:\repo")
            };

            var result = ThreadImportWorkflowPlanner.FilterAutomaticSyncThreads(
                threads,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                null,
                false,
                _ => true,
                _ => false,
                true,
                null,
                AppInteractionMode.Office,
                officeRoot);

            Assert.That(result.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "office-root", "office-nested" }));
        }

        [Test]
        public void ThreadImportWorkflowPlanner_BuildsManualAndAutomaticStatusPlans()
        {
            using (UseCulture("en-US"))
            {
                var now = new DateTime(2026, 4, 19, 9, 5, 0);

                var noInitialSelection = ThreadImportWorkflowPlanner.BuildManualNoSelection(true);
                var manualSuccess = ThreadImportWorkflowPlanner.BuildManualSuccess(2, now);
                var autoSynced = ThreadImportWorkflowPlanner.BuildAutomaticSynced(3, now);
                var noNew = ThreadImportWorkflowPlanner.BuildAutomaticNoNew(now);

                Assert.That(noInitialSelection.StatusText, Is.EqualTo("No history sessions were synced"));
                Assert.That(manualSuccess.SummaryText, Is.EqualTo("09:05 Manual sync 2 history session(s)"));
                Assert.That(manualSuccess.StatusText, Is.EqualTo("Synced 2 history session(s)"));
                Assert.That(autoSynced.SummaryText, Is.EqualTo("09:05 Auto-synced 3 new history session(s)"));
                Assert.That(autoSynced.StatusText, Is.EqualTo("Automatically synced 3 new thread(s)"));
                Assert.That(noNew.SummaryText, Is.EqualTo("09:05 Auto check completed, no new sessions"));
                Assert.That(noNew.StatusText, Is.Empty);
            }
        }

        [Test]
        public void ThreadImportWorkflowPlanner_BuildsAutomaticFailurePlans()
        {
            using (UseCulture("en-US"))
            {
                var now = new DateTime(2026, 4, 19, 9, 5, 0);

                var paused = ThreadImportWorkflowPlanner.BuildAutomaticConnectivityPause("offline", true, now);
                var failed = ThreadImportWorkflowPlanner.BuildAutomaticCheckFailed("bad json", now);

                Assert.That(paused.SummaryText, Is.EqualTo("09:05 Auto sync paused, waiting for connection recovery"));
                Assert.That(paused.LogText, Is.EqualTo("Auto sync paused: offline"));
                Assert.That(paused.StatusText, Is.EqualTo("App Server is not connected. Automatic sync paused."));
                Assert.That(failed.SummaryText, Is.EqualTo("09:05 Auto check failed"));
                Assert.That(failed.LogText, Is.EqualTo("Auto sync check failed: bad json"));
                Assert.That(failed.HasStatusText, Is.False);
            }
        }

        [Test]
        public void ThreadImportFilterPlanner_BelongsToKnownWorkspaceMatchesCodingWorkspaceIds()
        {
            var workspaces = new[]
            {
                new WorkspaceInfo
                {
                    Id = @"c:\repo",
                    RootPath = @"C:\repo",
                    Kind = WorkspaceKind.Coding
                }
            };

            Assert.That(
                ThreadImportFilterPlanner.BelongsToKnownWorkspace(
                    @"C:\repo",
                    @"c:\repo",
                    workspaces,
                    AppInteractionMode.Coding,
                    null,
                    (candidate, root) => false),
                Is.True);
            Assert.That(
                ThreadImportFilterPlanner.BelongsToKnownWorkspace(
                    @"C:\other",
                    @"c:\other",
                    workspaces,
                    AppInteractionMode.Coding,
                    null,
                    (candidate, root) => false),
                Is.False);
        }

        [Test]
        public void ThreadImportFilterPlanner_BelongsToKnownWorkspaceKeepsOfficeRootBehavior()
        {
            var workspaces = new[]
            {
                new WorkspaceInfo
                {
                    Id = "office",
                    RootPath = @"C:\Users\demo\Documents",
                    Kind = WorkspaceKind.Office
                }
            };

            Assert.That(
                ThreadImportFilterPlanner.BelongsToKnownWorkspace(
                    @"C:\Users\demo\Documents\Reports",
                    "office-child",
                    workspaces,
                    AppInteractionMode.Office,
                    @"C:\Users\demo\Documents",
                    (candidate, root) => candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)),
                Is.True);
            Assert.That(
                ThreadImportFilterPlanner.BelongsToKnownWorkspace(
                    @"C:\Users\demo\Documents\Reports",
                    "office-child",
                    Array.Empty<WorkspaceInfo>(),
                    AppInteractionMode.Office,
                    @"C:\Users\demo\Documents",
                    (candidate, root) => candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)),
                Is.False);
        }

        [Test]
        public void ThreadImportFilterPlanner_FilterProjectInstructionHelperThreadsSkipsHelpersAndInvalidThreads()
        {
            var threads = new List<AppServerThreadSummary>
            {
                Thread("helper", @"C:\repo"),
                Thread("fresh", @"C:\repo"),
                new AppServerThreadSummary { WorkingDirectory = @"C:\repo" },
                null
            };

            var result = ThreadImportFilterPlanner.FilterProjectInstructionHelperThreads(
                threads,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "helper" });

            Assert.That(result.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "fresh" }));
        }

        [Test]
        public void ThreadImportFilterPlanner_FilterProjectInstructionHelperThreadsPreservesThreadsWhenNoHelpersAreTracked()
        {
            var threads = new List<AppServerThreadSummary>
            {
                Thread("fresh", @"C:\repo"),
                null
            };

            var result = ThreadImportFilterPlanner.FilterProjectInstructionHelperThreads(
                threads,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            Assert.That(result, Is.EqualTo(threads));
        }

        [Test]
        public void ArchivedThreadRestorePlanner_ResolveAfterActiveRefreshKeepsActiveSelectedThreadsAndClearsFailures()
        {
            var selectedThreadIds = ArchivedThreadRestorePlanner.BuildSelectedThreadIdSet(new[]
            {
                Thread("one", @"C:\repo"),
                Thread("two", @"C:\repo"),
                Thread("two", @"C:\repo"),
                null
            });
            var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["one"] = "one failed",
                ["two"] = "two failed",
                ["missing"] = "missing failed"
            };

            var resolution = ArchivedThreadRestorePlanner.ResolveAfterActiveRefresh(
                new[] { Thread("one", @"C:\repo"), Thread("other", @"C:\repo") },
                selectedThreadIds,
                failures);

            Assert.That(selectedThreadIds.ToArray(), Is.EquivalentTo(new[] { "one", "two" }));
            Assert.That(resolution.RestorableThreads.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "one" }));
            Assert.That(resolution.FailureMessages.Keys.ToArray(), Is.EquivalentTo(new[] { "two", "missing" }));
        }

        [Test]
        public void ArchivedThreadRestorePlanner_ResolveFromSuccessfulSelectionFallsBackToUnarchivedSelections()
        {
            var successfulThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "two" };

            var resolution = ArchivedThreadRestorePlanner.ResolveFromSuccessfulSelection(
                new[] { Thread("one", @"C:\repo"), Thread("two", @"C:\repo") },
                successfulThreadIds,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["one"] = "one failed"
                });

            Assert.That(resolution.RestorableThreads.Select(thread => thread.ThreadId).ToArray(), Is.EqualTo(new[] { "two" }));
            Assert.That(resolution.FailureMessages.Keys.ToArray(), Is.EqualTo(new[] { "one" }));
        }

        [Test]
        public void ArchivedThreadRestorePlanner_BuildCompletionPlanFormatsSuccessAndFailureMessages()
        {
            using (UseCulture("en-US"))
            {
                var now = new DateTime(2026, 4, 19, 9, 5, 0);
                var failures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["one"] = "Thread one: denied"
                };

                var partialSuccess = ArchivedThreadRestorePlanner.BuildCompletionPlan(2, failures, now);
                var emptyRestore = ArchivedThreadRestorePlanner.BuildCompletionPlan(0, failures, now);

                Assert.That(partialSuccess.SummaryText, Is.EqualTo("09:05 Restored 2 archived session(s)"));
                Assert.That(partialSuccess.StatusText, Is.EqualTo("Restored 2 archived session(s), 1 failed"));
                Assert.That(partialSuccess.FailureDialogMessage, Does.Contain("The following sessions failed to restore:"));
                Assert.That(partialSuccess.FailureDialogMessage, Does.Contain("Thread one: denied"));
                Assert.That(emptyRestore.StatusText, Is.EqualTo("Restore archived sessions failed"));
                Assert.That(emptyRestore.EmptyRestoreMessage, Does.Contain("No archived sessions were restored successfully."));
                Assert.That(emptyRestore.EmptyRestoreMessage, Does.Contain("Thread one: denied"));
            }
        }

        [Test]
        public void ArchivedThreadRestorePlanner_BuildFailureLineUsesThreadTitle()
        {
            using (UseCulture("en-US"))
            {
                var failureLine = ArchivedThreadRestorePlanner.BuildFailureLine(
                    Thread("thread-1", @"C:\repo", "Saved title"),
                    "network error",
                    thread => thread.Title);

                Assert.That(failureLine, Is.EqualTo("Saved title: network error"));
            }
        }

        [Test]
        public void ThreadImportSessionMerger_ImportsNewThreadAndUpsertsWorkspace()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var sessions = new List<ChatSession>();
                var upsertedWorkspaces = new List<WorkspaceInfo>();
                var createdAt = new DateTime(2026, 4, 18, 8, 0, 0);
                var updatedAt = new DateTime(2026, 4, 19, 9, 5, 0);

                var result = ThreadImportSessionMerger.ImportThreads(
                    new[]
                    {
                        new AppServerThreadSummary
                        {
                            ThreadId = "thread-1",
                            Title = "Imported title",
                            Preview = "preview",
                            Source = "local",
                            WorkingDirectory = tempDirectory,
                            CreatedAt = createdAt,
                            UpdatedAt = updatedAt,
                            SessionPath = "session.json"
                        }
                    },
                    sessions,
                    new WorkspaceService(),
                    new SessionService(),
                    Directory.Exists,
                    upsertedWorkspaces.Add,
                    false);

                Assert.That(result.ImportedThreadCount, Is.EqualTo(1));
                Assert.That(result.HasPreferredSelection, Is.True);
                Assert.That(upsertedWorkspaces, Has.Count.EqualTo(1));
                Assert.That(upsertedWorkspaces[0].LastOpenedAt, Is.EqualTo(updatedAt));
                Assert.That(sessions, Has.Count.EqualTo(1));
                Assert.That(sessions[0].ThreadId, Is.EqualTo("thread-1"));
                Assert.That(sessions[0].Title, Is.EqualTo("Imported title"));
                Assert.That(sessions[0].Preview, Is.EqualTo("preview"));
                Assert.That(sessions[0].SessionPath, Is.EqualTo("session.json"));
                Assert.That(sessions[0].CreatedAt, Is.EqualTo(createdAt));
                Assert.That(sessions[0].LastActiveAt, Is.EqualTo(updatedAt));
                Assert.That(sessions[0].IsImported, Is.True);
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void ThreadImportSessionMerger_ApplyThreadTitlesUpdatesKnownSessionsOnly()
        {
            var knownSession = new ChatSession
            {
                ThreadId = "thread-1",
                Title = "Old title"
            };
            var untouchedSession = new ChatSession
            {
                ThreadId = "thread-2",
                Title = "Keep title"
            };
            var sessions = new List<ChatSession> { knownSession, untouchedSession };

            var updatedSessions = ThreadImportSessionMerger.ApplyThreadTitles(
                new[]
                {
                    new AppServerThreadSummary
                    {
                        ThreadId = "thread-1",
                        Title = " New title "
                    },
                    new AppServerThreadSummary
                    {
                        ThreadId = "thread-2",
                        Title = null
                    },
                    new AppServerThreadSummary
                    {
                        ThreadId = "thread-3",
                        Title = "Unknown title"
                    }
                },
                sessions);

            Assert.That(updatedSessions, Is.EqualTo(new[] { knownSession }));
            Assert.That(knownSession.Title, Is.EqualTo("New title"));
            Assert.That(untouchedSession.Title, Is.EqualTo("Keep title"));
        }

        [Test]
        public void ThreadImportSessionMerger_UsesTitleFallbackWhenThreadSummaryHasNoTitle()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var sessions = new List<ChatSession>();

                ThreadImportSessionMerger.ImportThreads(
                    new[]
                    {
                        new AppServerThreadSummary
                        {
                            ThreadId = "thread-automation",
                            WorkingDirectory = tempDirectory
                        }
                    },
                    sessions,
                    new WorkspaceService(),
                    new SessionService(),
                    Directory.Exists,
                    _ => { },
                    false,
                    null,
                    AppInteractionMode.Coding,
                    null,
                    thread => string.Equals(thread.ThreadId, "thread-automation", StringComparison.OrdinalIgnoreCase)
                        ? "Apple news"
                        : null);

                Assert.That(sessions, Has.Count.EqualTo(1));
                Assert.That(sessions[0].Title, Is.EqualTo("Apple news"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void ThreadImportSessionMerger_UpdatesExistingThreadAndSkipsUnavailablePath()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var missingDirectory = Path.Combine(tempDirectory, "missing");
                var sessions = new List<ChatSession>
                {
                    new ChatSession
                    {
                        Id = "existing",
                        ThreadId = "THREAD-1",
                        Title = "Old title"
                    }
                };

                var result = ThreadImportSessionMerger.ImportThreads(
                    new[]
                    {
                        Thread("thread-1", tempDirectory, "Updated title"),
                        Thread("thread-2", missingDirectory, "Skipped")
                    },
                    sessions,
                    new WorkspaceService(),
                    new SessionService(),
                    Directory.Exists,
                    _ => { },
                    false);

                Assert.That(result.ImportedThreadCount, Is.EqualTo(1));
                Assert.That(sessions, Has.Count.EqualTo(1));
                Assert.That(sessions[0].Id, Is.EqualTo("existing"));
                Assert.That(sessions[0].Title, Is.EqualTo("Updated title"));
                Assert.That(sessions[0].IsImported, Is.True);
                Assert.That(result.PreferredSession, Is.SameAs(sessions[0]));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void ThreadImportSessionMerger_BindsImportedThreadToMatchingEmptyLocalSession()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var workspace = new WorkspaceService().CreateFromPath(tempDirectory, true);
                var sessions = new List<ChatSession>
                {
                    new ChatSession
                    {
                        Id = "placeholder",
                        WorkspaceId = workspace.Id,
                        Mode = AppInteractionMode.Coding,
                        ThreadId = string.Empty,
                        Title = "Mobile title"
                    }
                };

                var result = ThreadImportSessionMerger.ImportThreads(
                    new[] { Thread("thread-created", tempDirectory, "Mobile title") },
                    sessions,
                    new WorkspaceService(),
                    new SessionService(),
                    Directory.Exists,
                    _ => { },
                    false);

                Assert.That(result.ImportedThreadCount, Is.EqualTo(1));
                Assert.That(sessions, Has.Count.EqualTo(1));
                Assert.That(sessions[0].Id, Is.EqualTo("placeholder"));
                Assert.That(sessions[0].ThreadId, Is.EqualTo("thread-created"));
                Assert.That(sessions[0].Title, Is.EqualTo("Mobile title"));
                Assert.That(sessions[0].IsImported, Is.True);
                Assert.That(result.PreferredSession, Is.SameAs(sessions[0]));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void ThreadImportSessionMerger_UpdatesExistingThreadSubAgentMetadata()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var sessions = new List<ChatSession>
                {
                    new ChatSession
                    {
                        Id = "existing-child",
                        ThreadId = "child-thread",
                        Title = "Old child title"
                    }
                };
                var thread = Thread("child-thread", tempDirectory, "Updated child");
                thread.ThreadSource = "subagent";
                thread.ParentThreadId = "parent-thread";
                thread.SubagentDepth = 1;
                thread.AgentNickname = "Ampere";
                thread.AgentRole = "default";

                ThreadImportSessionMerger.ImportThreads(
                    new[] { thread },
                    sessions,
                    new WorkspaceService(),
                    new SessionService(),
                    Directory.Exists,
                    _ => { },
                    false);

                Assert.That(sessions, Has.Count.EqualTo(1));
                Assert.That(sessions[0].Id, Is.EqualTo("existing-child"));
                Assert.That(sessions[0].IsSubAgent, Is.True);
                Assert.That(sessions[0].ParentThreadId, Is.EqualTo("parent-thread"));
                Assert.That(sessions[0].SubAgentDepth, Is.EqualTo(1));
                Assert.That(sessions[0].SubAgentNickname, Is.EqualTo("Ampere"));
                Assert.That(sessions[0].SubAgentRole, Is.EqualTo("default"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void ThreadImportSessionMerger_AllowsMissingWorkspaceWhenRequested()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var missingDirectory = Path.Combine(tempDirectory, "missing");
                var sessions = new List<ChatSession>();

                var result = ThreadImportSessionMerger.ImportThreads(
                    new[] { Thread("thread-1", missingDirectory, "Historical title") },
                    sessions,
                    new WorkspaceService(),
                    new SessionService(),
                    _ => false,
                    _ => { },
                    true);

                Assert.That(result.ImportedThreadCount, Is.EqualTo(1));
                Assert.That(sessions, Has.Count.EqualTo(1));
                Assert.That(sessions[0].Title, Is.EqualTo("Historical title"));
                Assert.That(result.PreferredWorkspace.RootPath, Is.EqualTo(Path.GetFullPath(missingDirectory)));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void ThreadImportSessionMerger_UsesInjectedWorkspaceFactoryForRuntimeSpecificPaths()
        {
            var sessions = new List<ChatSession>();
            var workspaceService = new WorkspaceService();

            var result = ThreadImportSessionMerger.ImportThreads(
                new[] { Thread("thread-wsl", "/home/tester/repo", "WSL title") },
                sessions,
                workspaceService,
                new SessionService(),
                path => path == "/home/tester/repo",
                _ => { },
                false,
                (path, requireExisting) => workspaceService.CreateFromPath(
                    path,
                    requireExisting,
                    candidate => candidate == "/home/tester/repo",
                    candidate => new MyCodex.Application.DTOs.WorkspaceGitInfoSnapshot
                    {
                        IsGitRepository = true,
                        Branch = "main"
                    }));

            Assert.That(result.ImportedThreadCount, Is.EqualTo(1));
            Assert.That(result.PreferredWorkspace.RootPath, Is.EqualTo("/home/tester/repo"));
            Assert.That(result.PreferredWorkspace.GitBranch, Is.EqualTo("main"));
            Assert.That(sessions[0].WorkspaceId, Is.EqualTo(result.PreferredWorkspace.Id));
        }

        [Test]
        public void ThreadImportSessionMerger_OfficeImportUsesOfficeWorkspaceAndOfficeSessionMode()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var sessions = new List<ChatSession>();
                var upsertedWorkspaces = new List<WorkspaceInfo>();
                var officeWorkspaceService = new OfficeWorkspaceService(
                    () => tempDirectory,
                    () => Path.GetTempPath());
                var officeWorkspace = officeWorkspaceService.CreateOfficeWorkspace(tempDirectory);

                var result = ThreadImportSessionMerger.ImportThreads(
                    new[] { Thread("office-thread", tempDirectory, "Office history") },
                    sessions,
                    new WorkspaceService(),
                    new SessionService(),
                    Directory.Exists,
                    upsertedWorkspaces.Add,
                    false,
                    (path, requireExisting) => officeWorkspaceService.CreateOfficeWorkspace(path),
                    AppInteractionMode.Office);

                Assert.That(result.ImportedThreadCount, Is.EqualTo(1));
                Assert.That(result.PreferredWorkspace.Id, Is.EqualTo(officeWorkspace.Id));
                Assert.That(upsertedWorkspaces[0].Kind, Is.EqualTo(WorkspaceKind.Office));
                Assert.That(sessions, Has.Count.EqualTo(1));
                Assert.That(sessions[0].WorkspaceId, Is.EqualTo(officeWorkspace.Id));
                Assert.That(sessions[0].Mode, Is.EqualTo(AppInteractionMode.Office));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void ThreadImportSessionMerger_OfficeImportMapsDescendantPathToSingleWorkspace()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var categoryPath = Path.Combine(tempDirectory, "表格");
                var descendantPath = Path.Combine(categoryPath, "2026", "Q2");
                Directory.CreateDirectory(descendantPath);
                var sessions = new List<ChatSession>();
                var upsertedWorkspaces = new List<WorkspaceInfo>();
                var officeWorkspaceService = new OfficeWorkspaceService(
                    () => tempDirectory,
                    () => Path.GetTempPath());

                var result = ThreadImportSessionMerger.ImportThreads(
                    new[] { Thread("office-category-thread", descendantPath, "Office category history") },
                    sessions,
                    new WorkspaceService(),
                    new SessionService(),
                    Directory.Exists,
                    upsertedWorkspaces.Add,
                    false,
                    (path, requireExisting) => officeWorkspaceService.ResolveOfficeWorkspaceForImport(
                        tempDirectory,
                        path,
                        upsertedWorkspaces),
                    AppInteractionMode.Office,
                    tempDirectory);

                Assert.That(result.ImportedThreadCount, Is.EqualTo(1));
                Assert.That(result.PreferredWorkspace.RootPath, Is.EqualTo(Path.GetFullPath(tempDirectory)));
                Assert.That(upsertedWorkspaces[0].RootPath, Is.EqualTo(Path.GetFullPath(tempDirectory)));
                Assert.That(sessions, Has.Count.EqualTo(1));
                Assert.That(sessions[0].WorkspaceId, Is.EqualTo(result.PreferredWorkspace.Id));
                Assert.That(sessions[0].Mode, Is.EqualTo(AppInteractionMode.Office));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void ThreadImportSessionMerger_DoesNotConvertExistingCodingSessionDuringOfficeImport()
        {
            var tempDirectory = CreateTempDirectory();
            try
            {
                var codingSession = new ChatSession
                {
                    Id = "coding-existing",
                    WorkspaceId = "coding-workspace",
                    ThreadId = "shared-thread",
                    Mode = AppInteractionMode.Coding,
                    Title = "Coding"
                };
                var sessions = new List<ChatSession> { codingSession };
                var officeWorkspaceService = new OfficeWorkspaceService(
                    () => tempDirectory,
                    () => Path.GetTempPath());

                var result = ThreadImportSessionMerger.ImportThreads(
                    new[] { Thread("shared-thread", tempDirectory, "Office history") },
                    sessions,
                    new WorkspaceService(),
                    new SessionService(),
                    Directory.Exists,
                    _ => { },
                    false,
                    (path, requireExisting) => officeWorkspaceService.CreateOfficeWorkspace(path),
                    AppInteractionMode.Office);

                Assert.That(result.ImportedThreadCount, Is.EqualTo(1));
                Assert.That(sessions, Has.Count.EqualTo(2));
                Assert.That(codingSession.WorkspaceId, Is.EqualTo("coding-workspace"));
                Assert.That(codingSession.Mode, Is.EqualTo(AppInteractionMode.Coding));
                Assert.That(sessions.Single(session => session.Mode == AppInteractionMode.Office).ThreadId, Is.EqualTo("shared-thread"));
            }
            finally
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Test]
        public void ThreadImportSessionMerger_RejectsOfficeRootWhenImportingCodingSessions()
        {
            var rootDirectory = CreateTempDirectory();
            try
            {
                var officeRoot = Path.Combine(rootDirectory, "office");
                var codingRoot = Path.Combine(rootDirectory, "repo");
                Directory.CreateDirectory(officeRoot);
                Directory.CreateDirectory(codingRoot);
                var sessions = new List<ChatSession>();

                var result = ThreadImportSessionMerger.ImportThreads(
                    new[]
                    {
                        Thread("office-thread", officeRoot, "Office history"),
                        Thread("coding-thread", codingRoot, "Coding history")
                    },
                    sessions,
                    new WorkspaceService(),
                    new SessionService(),
                    Directory.Exists,
                    _ => { },
                    false,
                    null,
                    AppInteractionMode.Coding,
                    officeRoot);

                Assert.That(result.ImportedThreadCount, Is.EqualTo(1));
                Assert.That(sessions, Has.Count.EqualTo(1));
                Assert.That(sessions[0].ThreadId, Is.EqualTo("coding-thread"));
                Assert.That(sessions[0].Mode, Is.EqualTo(AppInteractionMode.Coding));
            }
            finally
            {
                Directory.Delete(rootDirectory, true);
            }
        }

        [Test]
        public void ThreadAssociationPlanner_ClassifiesAndFiltersCandidates()
        {
            var targetWorkspace = Workspace("target");
            var otherWorkspace = Workspace("other");
            var sessions = new[]
            {
                new ChatSession { WorkspaceId = targetWorkspace.Id, ThreadId = "current" },
                new ChatSession { WorkspaceId = otherWorkspace.Id, ThreadId = "elsewhere" }
            };
            var candidates = ThreadAssociationPlanner.BuildCandidates(
                new[]
                {
                    Thread("fresh", @"E:\Old", "Fresh"),
                    Thread("elsewhere", @"E:\Old", "Elsewhere"),
                    Thread("current", @"E:\Old", "Current"),
                    Thread("ignored", @"E:\Old", "Ignored")
                },
                sessions,
                targetWorkspace.Id,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ignored" });

            Assert.That(candidates.Select(candidate => candidate.Thread.ThreadId).ToArray(), Is.EqualTo(new[] { "fresh", "elsewhere", "current" }));
            Assert.That(candidates[0].Status, Is.EqualTo(ThreadAssociationStatus.Unassociated));
            Assert.That(candidates[1].Status, Is.EqualTo(ThreadAssociationStatus.AssociatedElsewhere));
            Assert.That(candidates[2].Status, Is.EqualTo(ThreadAssociationStatus.AssociatedCurrentWorkspace));
            Assert.That(
                candidates.Where(candidate => ThreadAssociationPlanner.IsVisible(candidate, ThreadAssociationFilterMode.Linkable))
                    .Select(candidate => candidate.Thread.ThreadId)
                    .ToArray(),
                Is.EqualTo(new[] { "fresh", "elsewhere" }));
            Assert.That(
                candidates.Where(candidate => ThreadAssociationPlanner.IsVisible(candidate, ThreadAssociationFilterMode.AssociatedCurrentWorkspace))
                    .Select(candidate => candidate.Thread.ThreadId)
                    .ToArray(),
                Is.EqualTo(new[] { "current" }));
        }

        [Test]
        public async Task ThreadAssociationSessionMerger_ImportsSelectedThreadsByForkingCopiesToTargetWorkspace()
        {
            var targetWorkspace = Workspace("target");
            var otherWorkspace = Workspace("other");
            targetWorkspace.RootPath = @"E:\TargetRepo";
            var existingElsewhere = new ChatSession
            {
                Id = "existing",
                WorkspaceId = otherWorkspace.Id,
                ThreadId = "thread-1",
                Title = "Old"
            };
            var sessions = new List<ChatSession>
            {
                existingElsewhere,
                new ChatSession
                {
                    Id = "current",
                    WorkspaceId = targetWorkspace.Id,
                    ThreadId = "thread-current"
                }
            };
            var thread = Thread("thread-1", @"E:\OldRepo", "Associated title");
            thread.Preview = "preview";
            thread.SessionPath = @"E:\sessions\thread-1.jsonl";
            thread.ParentThreadId = "parent-thread";
            thread.SubagentDepth = 1;
            var threadClient = new RecordingThreadClient();
            threadClient.ForkResults["thread-1"] = new AppServerThreadSummary
            {
                ThreadId = "thread-1-copy",
                Title = "Associated title",
                Preview = "preview",
                WorkingDirectory = @"E:\OldRepo",
                Source = "local",
                SessionPath = @"E:\sessions\thread-1-copy.jsonl",
                CreatedAt = thread.CreatedAt,
                UpdatedAt = thread.UpdatedAt
            };

            var result = await ThreadAssociationSessionMerger.ImportThreadsAsync(
                new[]
                {
                    thread,
                    Thread("thread-current", @"E:\OldRepo", "Skip current")
                },
                sessions,
                targetWorkspace,
                new SessionService(),
                threadClient,
                new SessionThreadService(
                    threadClient,
                    new WorkspaceService(),
                    _ => new WorkspaceGitInfoSnapshot
                    {
                        IsGitRepository = true,
                        OriginUrl = "https://example.test/repo.git",
                        Branch = "main",
                        Sha = "abcdef"
                    }));

            Assert.That(result.AssociatedThreadCount, Is.EqualTo(1));
            Assert.That(result.SkippedThreadCount, Is.EqualTo(1));
            Assert.That(sessions, Has.Count.EqualTo(3));
            Assert.That(sessions[0], Is.SameAs(existingElsewhere));
            Assert.That(result.FirstAssociatedSession.WorkspaceId, Is.EqualTo(targetWorkspace.Id));
            Assert.That(result.FirstAssociatedSession.ThreadId, Is.EqualTo("thread-1-copy"));
            Assert.That(result.FirstAssociatedSession.Title, Is.EqualTo("Associated title"));
            Assert.That(result.FirstAssociatedSession.Preview, Is.EqualTo("preview"));
            Assert.That(result.FirstAssociatedSession.SessionPath, Is.EqualTo(@"E:\sessions\thread-1-copy.jsonl"));
            Assert.That(result.FirstAssociatedSession.ParentThreadId, Is.EqualTo("parent-thread"));
            Assert.That(result.FirstAssociatedSession.SubAgentDepth, Is.EqualTo(1));
            Assert.That(result.FirstAssociatedSession.ForkSourceThreadId, Is.EqualTo("thread-1"));
            Assert.That(threadClient.ForkedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-1" }));
            Assert.That(threadClient.GitMetadataThreadIds.ToArray(), Is.EqualTo(new[] { "thread-1-copy" }));
        }

        [Test]
        public void SessionSelectionResolver_ResolveCurrentSessionClearsSessionWithoutWorkspace()
        {
            var session = new ChatSession
            {
                Id = "session-1",
                WorkspaceId = "workspace-1"
            };

            var resolved = SessionSelectionResolver.ResolveCurrentSession(null, session, new[] { session });

            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void SessionSelectionResolver_ResolveCurrentSessionRehydratesPreferredSessionInWorkspace()
        {
            var workspace = Workspace("workspace-1");
            var staleSession = new ChatSession
            {
                Id = "session-1",
                WorkspaceId = "workspace-1",
                Title = "stale"
            };
            var currentSession = new ChatSession
            {
                Id = "session-1",
                WorkspaceId = "workspace-1",
                Title = "current"
            };

            var resolved = SessionSelectionResolver.ResolveCurrentSession(workspace, staleSession, new[] { currentSession });

            Assert.That(resolved, Is.SameAs(currentSession));
        }

        [Test]
        public void SessionSelectionResolver_ResolveCurrentSessionFallsBackToFirstSessionInWorkspace()
        {
            var workspace = Workspace("workspace-1");
            var otherWorkspaceSession = new ChatSession
            {
                Id = "other",
                WorkspaceId = "workspace-2"
            };
            var firstWorkspaceSession = new ChatSession
            {
                Id = "first",
                WorkspaceId = "workspace-1"
            };
            var secondWorkspaceSession = new ChatSession
            {
                Id = "second",
                WorkspaceId = "workspace-1"
            };

            var resolved = SessionSelectionResolver.ResolveCurrentSession(
                workspace,
                new ChatSession { Id = "missing", WorkspaceId = "workspace-1" },
                new[] { otherWorkspaceSession, firstWorkspaceSession, secondWorkspaceSession });

            Assert.That(resolved, Is.SameAs(firstWorkspaceSession));
        }

        [Test]
        public void SessionSelectionResolver_ResolveCurrentSessionMapsPreferredSubAgentToParentSession()
        {
            var workspace = Workspace("workspace-1");
            var parentSession = new ChatSession
            {
                Id = "parent",
                WorkspaceId = "workspace-1",
                ThreadId = "parent-thread",
                Mode = AppInteractionMode.Coding
            };
            var childSession = new ChatSession
            {
                Id = "child",
                WorkspaceId = "workspace-1",
                ThreadId = "child-thread",
                Mode = AppInteractionMode.Coding,
                IsSubAgent = true,
                ParentThreadId = "parent-thread"
            };

            var resolved = SessionSelectionResolver.ResolveCurrentSession(
                workspace,
                childSession,
                new[] { childSession, parentSession },
                AppInteractionMode.Coding);

            Assert.That(resolved, Is.SameAs(parentSession));
        }

        [Test]
        public void SessionSelectionResolver_ResolveCurrentSessionFallbackSkipsSubAgentWhenParentExists()
        {
            var workspace = Workspace("workspace-1");
            var childSession = new ChatSession
            {
                Id = "child",
                WorkspaceId = "workspace-1",
                ThreadId = "child-thread",
                Mode = AppInteractionMode.Coding,
                IsSubAgent = true,
                ParentThreadId = "parent-thread"
            };
            var parentSession = new ChatSession
            {
                Id = "parent",
                WorkspaceId = "workspace-1",
                ThreadId = "parent-thread",
                Mode = AppInteractionMode.Coding
            };

            var resolved = SessionSelectionResolver.ResolveCurrentSession(
                workspace,
                new ChatSession { Id = "missing", WorkspaceId = "workspace-1", Mode = AppInteractionMode.Coding },
                new[] { childSession, parentSession },
                AppInteractionMode.Coding);

            Assert.That(resolved, Is.SameAs(parentSession));
        }

        [Test]
        public void SessionSelectionResolver_ResolveCurrentSessionFiltersByInteractionMode()
        {
            var workspace = Workspace("workspace-1");
            var codingSession = new ChatSession
            {
                Id = "coding",
                WorkspaceId = "workspace-1",
                Mode = AppInteractionMode.Coding
            };
            var officeSession = new ChatSession
            {
                Id = "office",
                WorkspaceId = "workspace-1",
                Mode = AppInteractionMode.Office
            };

            var resolved = SessionSelectionResolver.ResolveCurrentSession(
                workspace,
                codingSession,
                new[] { codingSession, officeSession },
                AppInteractionMode.Office);

            Assert.That(resolved, Is.SameAs(officeSession));
        }

        [Test]
        public void SessionSelectionResolver_RestoreOfficeSelectionKeepsSavedSession()
        {
            var workspace = new WorkspaceInfo
            {
                Id = "office-workspace",
                Kind = WorkspaceKind.Office
            };
            var session = new ChatSession
            {
                Id = "office-session",
                WorkspaceId = workspace.Id,
                Mode = AppInteractionMode.Office
            };

            var selection = SessionSelectionResolver.RestoreOfficeSelection(
                new[] { workspace },
                new[] { session },
                workspace.Id,
                session.Id);

            Assert.That(selection.Workspace, Is.SameAs(workspace));
            Assert.That(selection.Session, Is.SameAs(session));
        }

        [Test]
        public void SessionCreationWorkspaceResolver_OfficeModeUsesOfficeWorkspaceEvenWhenCurrentWorkspaceIsCoding()
        {
            var codingWorkspace = new WorkspaceInfo
            {
                Id = "d:\\documents",
                Name = "documents",
                RootPath = "D:\\Documents",
                Kind = WorkspaceKind.Coding
            };
            var officeWorkspace = new WorkspaceInfo
            {
                Id = "office:D:\\Documents",
                Name = "Office",
                RootPath = "D:\\Documents",
                Kind = WorkspaceKind.Office
            };

            var resolved = SessionCreationWorkspaceResolver.Resolve(
                AppInteractionMode.Office,
                codingWorkspace,
                () => officeWorkspace);

            Assert.That(resolved, Is.SameAs(officeWorkspace));
        }

        [Test]
        public void SessionCreationWorkspaceResolver_OfficeModeNormalizesStaleOfficeWorkspaceKind()
        {
            var staleOfficeWorkspace = new WorkspaceInfo
            {
                Id = "office:D:\\Documents",
                Name = "Office",
                RootPath = "D:\\Documents",
                Kind = WorkspaceKind.Coding
            };

            var resolved = SessionCreationWorkspaceResolver.Resolve(
                AppInteractionMode.Office,
                staleOfficeWorkspace,
                () => staleOfficeWorkspace);

            Assert.That(resolved, Is.SameAs(staleOfficeWorkspace));
            Assert.That(resolved.Kind, Is.EqualTo(WorkspaceKind.Office));
        }

        [Test]
        public void SessionModeConsistencyResolver_ClearsCodingSessionWhenCurrentModeIsOffice()
        {
            var codingSession = new ChatSession
            {
                Id = "coding",
                Mode = AppInteractionMode.Coding
            };

            var resolved = SessionModeConsistencyResolver.Resolve(
                codingSession,
                AppInteractionMode.Office);

            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void SessionCollectionQueries_CountSessionsInWorkspaceFiltersByInteractionMode()
        {
            var sessions = new[]
            {
                new ChatSession { Id = "coding", WorkspaceId = "workspace-1", Mode = AppInteractionMode.Coding },
                new ChatSession { Id = "office", WorkspaceId = "workspace-1", Mode = AppInteractionMode.Office },
                new ChatSession { Id = "other", WorkspaceId = "workspace-2", Mode = AppInteractionMode.Office }
            };

            Assert.That(
                SessionCollectionQueries.CountSessionsInWorkspace(sessions, "workspace-1", AppInteractionMode.Office),
                Is.EqualTo(1));
        }

        [Test]
        public void SessionSelectionResolver_RestoreSelectionUsesRequestedWorkspaceAndSession()
        {
            var workspaces = new[]
            {
                Workspace("workspace-1"),
                Workspace("workspace-2")
            };
            var sessions = new[]
            {
                new ChatSession { Id = "session-1", WorkspaceId = "workspace-1" },
                new ChatSession { Id = "session-2", WorkspaceId = "workspace-2" }
            };

            var selection = SessionSelectionResolver.RestoreSelection(
                workspaces,
                sessions,
                null,
                null,
                "WORKSPACE-2",
                "SESSION-2");

            Assert.That(selection.Workspace, Is.SameAs(workspaces[1]));
            Assert.That(selection.Session, Is.SameAs(sessions[1]));
        }

        [Test]
        public void SessionSelectionResolver_RestoreSelectionFallsBackToFirstWorkspaceAndWorkspaceSession()
        {
            var workspaces = new[]
            {
                Workspace("workspace-1"),
                Workspace("workspace-2")
            };
            var sessions = new[]
            {
                new ChatSession { Id = "session-1", WorkspaceId = "workspace-1" }
            };

            var selection = SessionSelectionResolver.RestoreSelection(
                workspaces,
                sessions,
                null,
                null,
                "missing-workspace",
                "missing-session");

            Assert.That(selection.Workspace, Is.SameAs(workspaces[0]));
            Assert.That(selection.Session, Is.SameAs(sessions[0]));
        }

        [Test]
        public void SessionCollectionQueries_FindSessionsAndWorkspacesCaseInsensitively()
        {
            var sessions = new[]
            {
                new ChatSession { Id = "session-1", ThreadId = "thread-1", WorkspaceId = "workspace-1" },
                new ChatSession { Id = "session-2", ThreadId = "thread-2", WorkspaceId = "workspace-2" }
            };
            var workspaces = new[]
            {
                Workspace("workspace-1"),
                Workspace("workspace-2")
            };

            Assert.That(SessionCollectionQueries.FindSessionById(sessions, "SESSION-2"), Is.SameAs(sessions[1]));
            Assert.That(SessionCollectionQueries.FindSessionByThreadId(sessions, "THREAD-1"), Is.SameAs(sessions[0]));
            Assert.That(SessionCollectionQueries.FindWorkspaceById(workspaces, "WORKSPACE-2"), Is.SameAs(workspaces[1]));
            Assert.That(SessionCollectionQueries.CountSessionsInWorkspace(sessions, "WORKSPACE-1"), Is.EqualTo(1));
            Assert.That(SessionCollectionQueries.GetSessionsForWorkspace(sessions, "WORKSPACE-2").ToArray(), Is.EqualTo(new[] { sessions[1] }));
        }

        [Test]
        public void SessionCollectionQueries_GetThreadIdsIncludesAllThreadBackedSessions()
        {
            var sessions = new[]
            {
                new ChatSession { ThreadId = "thread-1", IsImported = false },
                new ChatSession { ThreadId = "thread-2", IsImported = true },
                new ChatSession { ThreadId = " " }
            };

            var threadIds = SessionCollectionQueries.GetThreadIds(sessions);

            Assert.That(threadIds.ToArray(), Is.EqualTo(new[] { "thread-1", "thread-2" }));
        }

        [Test]
        public void SessionCollectionQueries_HasImportedSessionsRequiresImportedThreadId()
        {
            var noImportedThreads = new[]
            {
                new ChatSession { ThreadId = "thread-1", IsImported = false },
                new ChatSession { ThreadId = " ", IsImported = true }
            };
            var importedThreads = new[]
            {
                new ChatSession { ThreadId = "thread-1", IsImported = false },
                new ChatSession { ThreadId = "thread-2", IsImported = true }
            };

            Assert.That(SessionCollectionQueries.HasImportedSessions(noImportedThreads), Is.False);
            Assert.That(SessionCollectionQueries.HasImportedSessions(importedThreads), Is.True);
        }

        [Test]
        public void LocalSessionRemovalPlanner_WorkspaceRemovalDropsWorkspaceSessionsAndRestoresSelection()
        {
            var retainedWorkspace = Workspace("workspace-1");
            var removedWorkspace = Workspace("workspace-2");
            var retainedSession = new ChatSession
            {
                Id = "session-1",
                WorkspaceId = "workspace-1"
            };
            var removedSession = new ChatSession
            {
                Id = "session-2",
                WorkspaceId = "workspace-2"
            };
            var removedSessionTwo = new ChatSession
            {
                Id = "session-3",
                WorkspaceId = "workspace-2"
            };
            var inactiveRemovedSession = new ChatSession
            {
                Id = "inactive-1",
                WorkspaceId = "workspace-2"
            };

            var plan = LocalSessionRemovalPlanner.BuildWorkspaceRemoval(
                removedWorkspace,
                new[] { retainedWorkspace, removedWorkspace },
                new[] { retainedSession, removedSession, removedSessionTwo },
                new[] { inactiveRemovedSession },
                removedWorkspace,
                removedSession);

            Assert.That(plan.HasChanges, Is.True);
            Assert.That(plan.RetainedWorkspaces.ToArray(), Is.EqualTo(new[] { retainedWorkspace }));
            Assert.That(plan.RetainedSessions.ToArray(), Is.EqualTo(new[] { retainedSession }));
            Assert.That(plan.RetainedInactiveRuntimeSessions, Is.Empty);
            Assert.That(plan.SessionsToClear.ToArray(), Is.EqualTo(new[] { removedSession, removedSessionTwo }));
            Assert.That(plan.CurrentWorkspace, Is.SameAs(retainedWorkspace));
            Assert.That(plan.CurrentSession, Is.SameAs(retainedSession));
        }

        [Test]
        public void LocalSessionRemovalPlanner_SessionRemovalSelectsNextSessionInCurrentWorkspace()
        {
            var workspace = Workspace("workspace-1");
            var removedSession = new ChatSession
            {
                Id = "session-1",
                WorkspaceId = "workspace-1"
            };
            var nextSession = new ChatSession
            {
                Id = "session-2",
                WorkspaceId = "workspace-1"
            };

            var plan = LocalSessionRemovalPlanner.BuildSessionRemoval(
                removedSession,
                new[] { workspace },
                new[] { removedSession, nextSession },
                Array.Empty<ChatSession>(),
                workspace,
                removedSession);

            Assert.That(plan.HasChanges, Is.True);
            Assert.That(plan.RetainedSessions.ToArray(), Is.EqualTo(new[] { nextSession }));
            Assert.That(plan.SessionsToClear.ToArray(), Is.EqualTo(new[] { removedSession }));
            Assert.That(plan.CurrentWorkspace, Is.SameAs(workspace));
            Assert.That(plan.CurrentSession, Is.SameAs(nextSession));
        }

        [Test]
        public void LocalSessionRemovalPlanner_SessionRemovalAlsoRemovesSubAgents()
        {
            var workspace = Workspace("workspace-1");
            var removedSession = new ChatSession
            {
                Id = "session-1",
                WorkspaceId = "workspace-1",
                ThreadId = "parent-thread"
            };
            var childSession = new ChatSession
            {
                Id = "child-1",
                WorkspaceId = "workspace-1",
                IsSubAgent = true,
                ThreadId = "child-thread",
                ParentThreadId = "parent-thread"
            };
            var grandchildSession = new ChatSession
            {
                Id = "grandchild-1",
                WorkspaceId = "workspace-1",
                IsSubAgent = true,
                ThreadId = "grandchild-thread",
                ParentThreadId = "child-thread"
            };
            var nextSession = new ChatSession
            {
                Id = "session-2",
                WorkspaceId = "workspace-1"
            };

            var plan = LocalSessionRemovalPlanner.BuildSessionRemoval(
                removedSession,
                new[] { workspace },
                new[] { removedSession, grandchildSession, childSession, nextSession },
                Array.Empty<ChatSession>(),
                workspace,
                removedSession);

            Assert.That(plan.RetainedSessions.ToArray(), Is.EqualTo(new[] { nextSession }));
            Assert.That(plan.SessionsToClear.ToArray(), Is.EqualTo(new[] { removedSession, childSession, grandchildSession }));
            Assert.That(plan.CurrentSession, Is.SameAs(nextSession));
        }

        [Test]
        public void LocalSessionRemovalPlanner_SessionRemovalOutsideCurrentWorkspacePreservesSelection()
        {
            var currentWorkspace = Workspace("workspace-1");
            var otherWorkspace = Workspace("workspace-2");
            var currentSession = new ChatSession
            {
                Id = "session-1",
                WorkspaceId = "workspace-1"
            };
            var removedSession = new ChatSession
            {
                Id = "session-2",
                WorkspaceId = "workspace-2"
            };

            var plan = LocalSessionRemovalPlanner.BuildSessionRemoval(
                removedSession,
                new[] { currentWorkspace, otherWorkspace },
                new[] { currentSession, removedSession },
                Array.Empty<ChatSession>(),
                currentWorkspace,
                currentSession);

            Assert.That(plan.RetainedSessions.ToArray(), Is.EqualTo(new[] { currentSession }));
            Assert.That(plan.CurrentWorkspace, Is.SameAs(currentWorkspace));
            Assert.That(plan.CurrentSession, Is.SameAs(currentSession));
        }

        [Test]
        public void LocalSessionRemovalTextPlanner_BuildsConfirmationIcons()
        {
            Assert.That(
                LocalSessionRemovalTextPlanner.GetConfirmationIcon(LocalSessionRemovalMode.LocalOnly),
                Is.EqualTo(MessageBoxIcon.Question));
            Assert.That(
                LocalSessionRemovalTextPlanner.GetConfirmationIcon(LocalSessionRemovalMode.Archive),
                Is.EqualTo(MessageBoxIcon.Warning));
            Assert.That(
                LocalSessionRemovalTextPlanner.GetConfirmationIcon(LocalSessionRemovalMode.PermanentDelete),
                Is.EqualTo(MessageBoxIcon.Warning));
        }

        [Test]
        public void LocalSessionRemovalTextPlanner_BuildsWorkspaceRemovalText()
        {
            using (UseCulture("en-US"))
            {
                var workspace = Workspace("workspace-1");
                workspace.Name = "Repo";

                var localConfirmation = LocalSessionRemovalTextPlanner.BuildWorkspaceConfirmation(
                    workspace,
                    2,
                    LocalSessionRemovalMode.LocalOnly);
                var archiveProgress = LocalSessionRemovalTextPlanner.BuildWorkspaceProgressText(
                    LocalSessionRemovalMode.Archive,
                    "Repo");
                var deleteCompletion = LocalSessionRemovalTextPlanner.BuildWorkspaceCompletionStatus(
                    LocalSessionRemovalMode.PermanentDelete,
                    "Repo",
                    3);

                Assert.That(localConfirmation, Does.Contain("Remove workspace \"Repo\" and its 2 session(s) from MyCodex only?"));
                Assert.That(localConfirmation, Does.Contain("This does not affect Codex CLI"));
                Assert.That(archiveProgress, Is.EqualTo("Archiving sessions under workspace \"Repo\"..."));
                Assert.That(deleteCompletion, Is.EqualTo("Permanently deleted 3 session file(s) under workspace \"Repo\"."));
            }
        }

        [Test]
        public void LocalSessionRemovalTextPlanner_BuildsSessionRemovalText()
        {
            using (UseCulture("en-US"))
            {
                var session = new ChatSession
                {
                    Title = "Fix startup"
                };

                var archiveConfirmation = LocalSessionRemovalTextPlanner.BuildSessionConfirmation(
                    session,
                    LocalSessionRemovalMode.Archive);
                var deleteProgress = LocalSessionRemovalTextPlanner.BuildSessionProgressText(
                    LocalSessionRemovalMode.PermanentDelete,
                    "Fix startup");
                var localCompletion = LocalSessionRemovalTextPlanner.BuildSessionCompletionStatus(
                    LocalSessionRemovalMode.LocalOnly,
                    "Fix startup");

                Assert.That(archiveConfirmation, Does.Contain("Archive session \"Fix startup\"?"));
                Assert.That(archiveConfirmation, Does.Contain("can still be restored"));
                Assert.That(deleteProgress, Is.EqualTo("Permanently deleting session \"Fix startup\"..."));
                Assert.That(localCompletion, Is.EqualTo("Removed session \"Fix startup\" from MyCodex."));
            }
        }

        [Test]
        public void LocalSessionRemovalFlowOrchestrator_BuildWorkspacePlanMapsModesAndProcessedCount()
        {
            var localPlan = LocalSessionRemovalFlowOrchestrator.BuildWorkspacePlan(LocalSessionRemovalMode.LocalOnly, 3);
            var archivePlan = LocalSessionRemovalFlowOrchestrator.BuildWorkspacePlan(LocalSessionRemovalMode.Archive, 3);
            var deletePlan = LocalSessionRemovalFlowOrchestrator.BuildWorkspacePlan(LocalSessionRemovalMode.PermanentDelete, 3);

            Assert.That(localPlan.OperationKind, Is.EqualTo(LocalSessionRemovalOperationKind.None));
            Assert.That(localPlan.DefaultProcessedCount, Is.EqualTo(3));
            Assert.That(localPlan.RequiresRemoteOperation, Is.False);
            Assert.That(
                LocalSessionRemovalFlowOrchestrator.ResolveWorkspaceProcessedCount(localPlan, 9),
                Is.EqualTo(3));

            Assert.That(archivePlan.OperationKind, Is.EqualTo(LocalSessionRemovalOperationKind.ArchiveSessions));
            Assert.That(archivePlan.DefaultProcessedCount, Is.EqualTo(0));
            Assert.That(archivePlan.RequiresRemoteOperation, Is.True);
            Assert.That(
                LocalSessionRemovalFlowOrchestrator.ResolveWorkspaceProcessedCount(archivePlan, 2),
                Is.EqualTo(2));

            Assert.That(deletePlan.OperationKind, Is.EqualTo(LocalSessionRemovalOperationKind.DeleteSessionFiles));
            Assert.That(deletePlan.DefaultProcessedCount, Is.EqualTo(0));
            Assert.That(deletePlan.RequiresRemoteOperation, Is.True);
            Assert.That(
                LocalSessionRemovalFlowOrchestrator.ResolveWorkspaceProcessedCount(deletePlan, 1),
                Is.EqualTo(1));
        }

        [Test]
        public void LocalSessionRemovalFlowOrchestrator_BuildSessionPlanMapsModes()
        {
            var localPlan = LocalSessionRemovalFlowOrchestrator.BuildSessionPlan(LocalSessionRemovalMode.LocalOnly);
            var archivePlan = LocalSessionRemovalFlowOrchestrator.BuildSessionPlan(LocalSessionRemovalMode.Archive);
            var deletePlan = LocalSessionRemovalFlowOrchestrator.BuildSessionPlan(LocalSessionRemovalMode.PermanentDelete);

            Assert.That(localPlan.OperationKind, Is.EqualTo(LocalSessionRemovalOperationKind.None));
            Assert.That(localPlan.DefaultProcessedCount, Is.EqualTo(1));
            Assert.That(localPlan.RequiresRemoteOperation, Is.False);

            Assert.That(archivePlan.OperationKind, Is.EqualTo(LocalSessionRemovalOperationKind.ArchiveSession));
            Assert.That(archivePlan.DefaultProcessedCount, Is.EqualTo(1));
            Assert.That(archivePlan.RequiresRemoteOperation, Is.True);

            Assert.That(deletePlan.OperationKind, Is.EqualTo(LocalSessionRemovalOperationKind.DeleteSessionFile));
            Assert.That(deletePlan.DefaultProcessedCount, Is.EqualTo(1));
            Assert.That(deletePlan.RequiresRemoteOperation, Is.True);
        }

        [Test]
        public void SessionRemovalInterruptPlanner_BuildTargetsSelectsInterruptibleThreadsOnce()
        {
            var sessions = new[]
            {
                new ChatSession { ThreadId = "thread-a" },
                new ChatSession { ThreadId = "thread-a" },
                new ChatSession { ThreadId = "thread-b" },
                new ChatSession { ThreadId = "thread-c" },
                new ChatSession { ThreadId = string.Empty },
                null
            };
            var interruptible = new HashSet<string>(StringComparer.Ordinal)
            {
                "thread-a",
                "thread-c"
            };

            var targets = SessionRemovalInterruptPlanner.BuildTargets(
                sessions,
                threadId => interruptible.Contains(threadId),
                threadId => threadId == "thread-a" ? "turn-a" : string.Empty);

            Assert.That(targets.Select(target => target.ThreadId).ToArray(), Is.EqualTo(new[] { "thread-a", "thread-c" }));
            Assert.That(targets.Select(target => target.TurnId).ToArray(), Is.EqualTo(new[] { "turn-a", string.Empty }));
        }

        [Test]
        public void ThreadGoalRuntimeStatePlanner_ShowsPausedGoalAsActiveWhileThreadRuns()
        {
            Assert.That(
                ThreadGoalRuntimeStatePlanner.ResolveEffectiveStatus("paused", true),
                Is.EqualTo("active"));
            Assert.That(
                ThreadGoalRuntimeStatePlanner.ResolveEffectiveStatus("paused", false),
                Is.EqualTo("paused"));
            Assert.That(
                ThreadGoalRuntimeStatePlanner.ResolveEffectiveStatus("complete", true),
                Is.EqualTo("complete"));
        }

        [Test]
        public void SessionPlanModePlanner_AllowsPlanAndGoalChangesForAvailableSessionWithoutThread()
        {
            var context = new SessionPlanModeAvailabilityContext
            {
                HasSession = true,
                IsWorkspaceAvailable = true,
                HasThread = false
            };

            Assert.That(SessionPlanModePlanner.CanChangePlanOrGoalState(context), Is.True);
        }

        [Test]
        public void SessionPlanModePlanner_BlocksWhenSessionOrWorkspaceIsUnavailable()
        {
            Assert.That(
                SessionPlanModePlanner.CanChangePlanOrGoalState(null),
                Is.False);
            Assert.That(
                SessionPlanModePlanner.CanChangePlanOrGoalState(new SessionPlanModeAvailabilityContext
                {
                    HasSession = false,
                    IsWorkspaceAvailable = true
                }),
                Is.False);
            Assert.That(
                SessionPlanModePlanner.CanChangePlanOrGoalState(new SessionPlanModeAvailabilityContext
                {
                    HasSession = true,
                    IsWorkspaceAvailable = false
                }),
                Is.False);
        }

        [Test]
        public void SessionPlanModePlanner_BlocksWhenExistingThreadHasActiveOperation()
        {
            Assert.That(CanChangeWithExistingThread(isThreadBusy: true), Is.False);
            Assert.That(CanChangeWithExistingThread(canInterruptThread: true), Is.False);
            Assert.That(CanChangeWithExistingThread(isThreadCompacting: true), Is.False);
            Assert.That(CanChangeWithExistingThread(), Is.True);
        }

        [Test]
        public void TurnInputStateRefreshPlanner_RefreshesWhenTurnStartsOrStops()
        {
            Assert.That(TurnInputStateRefreshPlanner.ShouldRefresh(AppServerEventRoute.TurnStarted), Is.True);
            Assert.That(TurnInputStateRefreshPlanner.ShouldRefresh(AppServerEventRoute.TurnCompleted), Is.True);
            Assert.That(TurnInputStateRefreshPlanner.ShouldRefresh(AppServerEventRoute.TurnInterrupted), Is.True);
            Assert.That(TurnInputStateRefreshPlanner.ShouldRefresh(AppServerEventRoute.Error), Is.True);
            Assert.That(TurnInputStateRefreshPlanner.ShouldRefresh(AppServerEventRoute.SummaryOnly), Is.False);
        }

        [Test]
        public void MainFormRefreshPlanner_BuildMapsPresetFlags()
        {
            var workspacePreset = MainFormRefreshPlanner.Build(MainFormRefreshPreset.WorkspaceSessionHeaderInputPersist);
            var modeActivationPreset = MainFormRefreshPlanner.Build(MainFormRefreshPreset.WorkspaceSessionHeaderInput);
            var sessionWithInputPreset = MainFormRefreshPlanner.Build(MainFormRefreshPreset.SessionHeaderInputPersist);
            var sessionPreset = MainFormRefreshPlanner.Build(MainFormRefreshPreset.SessionHeaderPersist);

            Assert.That(workspacePreset.RefreshWorkspaceView, Is.True);
            Assert.That(workspacePreset.RefreshSessionList, Is.True);
            Assert.That(workspacePreset.RefreshHeader, Is.True);
            Assert.That(workspacePreset.RefreshInputState, Is.True);
            Assert.That(workspacePreset.PersistState, Is.True);

            Assert.That(modeActivationPreset.RefreshWorkspaceView, Is.True);
            Assert.That(modeActivationPreset.RefreshSessionList, Is.True);
            Assert.That(modeActivationPreset.RefreshHeader, Is.True);
            Assert.That(modeActivationPreset.RefreshInputState, Is.True);
            Assert.That(modeActivationPreset.PersistState, Is.False);

            Assert.That(sessionWithInputPreset.RefreshWorkspaceView, Is.False);
            Assert.That(sessionWithInputPreset.RefreshSessionList, Is.True);
            Assert.That(sessionWithInputPreset.RefreshHeader, Is.True);
            Assert.That(sessionWithInputPreset.RefreshInputState, Is.True);
            Assert.That(sessionWithInputPreset.PersistState, Is.True);

            Assert.That(sessionPreset.RefreshWorkspaceView, Is.False);
            Assert.That(sessionPreset.RefreshSessionList, Is.True);
            Assert.That(sessionPreset.RefreshHeader, Is.True);
            Assert.That(sessionPreset.RefreshInputState, Is.False);
            Assert.That(sessionPreset.PersistState, Is.True);
        }

        [Test]
        public void MainFormLayoutPlanner_UsesRoomierDefaultWindowWithoutMaximizing()
        {
            Assert.That(MainFormLayoutPlanner.DefaultWindowSize.Width, Is.EqualTo(1500));
            Assert.That(MainFormLayoutPlanner.DefaultWindowSize.Height, Is.EqualTo(950));
            Assert.That(MainFormLayoutPlanner.MinimumWindowSize.Width, Is.EqualTo(1024));
            Assert.That(MainFormLayoutPlanner.MinimumWindowSize.Height, Is.EqualTo(640));
            Assert.That(MainFormLayoutPlanner.DefaultWindowState, Is.EqualTo(FormWindowState.Normal));
        }

        [Test]
        public void MainFormLayoutPlanner_ClampsSavedSplittersToKeepConversationReadable()
        {
            var mainWidth = 1400;
            var navigationWidth = MainFormLayoutPlanner.ResolveMainSplitterDistance(mainWidth, null);
            var contentWidth = mainWidth - navigationWidth - MainFormLayoutPlanner.SplitterWidth;

            Assert.That(navigationWidth, Is.EqualTo(MainFormLayoutPlanner.DefaultNavigationWidth));
            Assert.That(
                MainFormLayoutPlanner.ResolveContentSplitterDistance(contentWidth, null),
                Is.EqualTo(contentWidth - MainFormLayoutPlanner.SplitterWidth - MainFormLayoutPlanner.DefaultActivityWidth));
            Assert.That(MainFormLayoutPlanner.DefaultActivityWidth, Is.EqualTo(470));
            Assert.That(
                MainFormLayoutPlanner.ResolveContentSplitterDistance(contentWidth, 420),
                Is.EqualTo(contentWidth - MainFormLayoutPlanner.SplitterWidth - MainFormLayoutPlanner.MaximumActivityWidth));
            Assert.That(
                contentWidth - MainFormLayoutPlanner.SplitterWidth - MainFormLayoutPlanner.ResolveContentSplitterDistance(contentWidth, 420),
                Is.LessThanOrEqualTo(MainFormLayoutPlanner.MaximumActivityWidth));
        }

        [Test]
        public void MainFormLayoutPlanner_GivesWorkspaceNameColumnPriority()
        {
            var localColumns = MainFormLayoutPlanner.ResolveWorkspaceListColumnWidths(248, false);
            var remoteColumns = MainFormLayoutPlanner.ResolveWorkspaceListColumnWidths(248, true);

            Assert.That(localColumns.NameWidth, Is.GreaterThan(localColumns.BranchWidth));
            Assert.That(localColumns.SourceWidth, Is.EqualTo(0));
            Assert.That(localColumns.NameWidth + localColumns.BranchWidth, Is.LessThanOrEqualTo(248));
            Assert.That(remoteColumns.NameWidth, Is.GreaterThan(remoteColumns.BranchWidth));
            Assert.That(remoteColumns.SourceWidth, Is.EqualTo(MainFormLayoutPlanner.WorkspaceSourceColumnWidth));
        }

        [Test]
        public void MainFormLayoutPlanner_UsesTallerComposeAndCompactSummaryHeader()
        {
            Assert.That(MainFormLayoutPlanner.ComposePanelHeight, Is.EqualTo(150));
            Assert.That(MainFormLayoutPlanner.HeaderPanelHeight, Is.EqualTo(64));
            Assert.That(MainFormLayoutPlanner.HeaderSummaryVerticalPadding, Is.EqualTo(4));
        }

        [Test]
        public void SessionHeaderSummaryBuilder_BuildsCompactSummaryWithoutPathOrThreadId()
        {
            using (UseCulture("en-US"))
            {
                var workspace = new WorkspaceInfo
                {
                    Name = "tts",
                    RootPath = @"F:\tts",
                    GitBranch = "main"
                };
                var session = new ChatSession
                {
                    Title = "Read current project",
                    ThreadId = "019e4500-41f4-7bf0-8ae6-6f3b74f3ff93"
                };

                var summary = SessionHeaderSummaryBuilder.Build(
                    session,
                    workspace,
                    "5.5 (High)",
                    "20:39 auto checked, no new sessions",
                    "(Not selected)",
                    "Not synced yet");

                Assert.That(SessionHeaderSummaryBuilder.CombineDetails(summary.ContextText, summary.StatusText, "  |  "),
                    Is.EqualTo("tts  |  main  |  5.5 (High)  |  Sync 20:39 auto checked, no new sessions"));
                Assert.That(summary.TitleText, Does.Contain("Read current project"));
                Assert.That(summary.ContextText, Does.Contain("tts"));
                Assert.That(summary.ContextText, Does.Contain("main"));
                Assert.That(summary.ContextText, Does.Contain("5.5 (High)"));
                Assert.That(summary.StatusText, Is.EqualTo("Sync 20:39 auto checked, no new sessions"));
                Assert.That(summary.ContextText, Does.Not.Contain(@"F:\tts"));
                Assert.That(summary.TitleText + summary.ContextText + summary.StatusText, Does.Not.Contain("019e4500"));
            }
        }

        [Test]
        public void SessionStorageOperationPlanner_SelectArchivableAndCountSessions()
        {
            var sessions = new ChatSession[]
            {
                new ChatSession { Id = "a", ThreadId = "thread-a" },
                new ChatSession { Id = "b", ThreadId = string.Empty },
                null
            };

            var archivable = SessionStorageOperationPlanner.SelectArchivableSessions(sessions);
            var nonNullCount = SessionStorageOperationPlanner.CountNonNullSessions(sessions);

            Assert.That(archivable.Select(session => session.Id).ToArray(), Is.EqualTo(new[] { "a" }));
            Assert.That(nonNullCount, Is.EqualTo(2));
        }

        [TestCase("windows")]
        [TestCase("wsl:Ubuntu")]
        [TestCase("remote-mac:test")]
        [TestCase("remote-unix:test")]
        public void SessionStorageOperationPlanner_FileDeletionIgnoresUnusedSessions(string runtime)
        {
            var workspace = new WorkspaceInfo { Id = "workspace", Name = "Workspace" };
            var blank = new SessionService().CreateSession(workspace, 1);
            blank.RuntimeProfileId = runtime;
            var thread = new ChatSession
            {
                WorkspaceId = workspace.Id,
                RuntimeProfileId = runtime,
                ThreadId = "thread",
                SessionPath = "thread.jsonl"
            };
            var file = new ChatSession
            {
                WorkspaceId = workspace.Id,
                RuntimeProfileId = runtime,
                SessionPath = "file.jsonl"
            };
            var sessions = new[]
            {
                blank, thread, file, null,
                new ChatSession { ThreadId = " ", SessionPath = " " }
            };

            Assert.That(LocalSessionRemovalEligibility.CanPermanentlyDeleteWorkspace(sessions, workspace.Id), Is.True);
            var files = SessionStorageOperationPlanner.SelectFileDeletionSessions(sessions);
            Assert.That(files, Is.EqualTo(new[] { file }));
            Assert.That(files.Any(session => string.IsNullOrWhiteSpace(session.SessionPath)), Is.False);
            Assert.That(SessionStorageOperationPlanner.SelectFileDeletionSessions(new[] { blank }), Is.Empty);
            Assert.That(SessionStorageOperationPlanner.SelectFileDeletionSessions(null), Is.Empty);
            Assert.That(blank.ThreadId, Is.Empty);
            Assert.That(blank.SessionPath, Is.Null);
        }

        [Test]
        public void SessionStorageOperationPlanner_ResolveSessionPathFromSummariesAndDetails()
        {
            var summaries = new[]
            {
                new AppServerThreadSummary { ThreadId = "thread-a", SessionPath = string.Empty },
                new AppServerThreadSummary { ThreadId = "thread-b", SessionPath = "session-b.json" }
            };

            var summaryPath = SessionStorageOperationPlanner.ResolveSessionPathFromThreadSummaries(summaries, "THREAD-B");
            var detailsPath = SessionStorageOperationPlanner.ResolveSessionPathFromThreadDetails(
                new AppServerThreadDetails { SessionPath = "session-c.json" });
            var emptyDetailsPath = SessionStorageOperationPlanner.ResolveSessionPathFromThreadDetails(
                new AppServerThreadDetails());

            Assert.That(summaryPath, Is.EqualTo("session-b.json"));
            Assert.That(detailsPath, Is.EqualTo("session-c.json"));
            Assert.That(emptyDetailsPath, Is.Null);
        }

        [Test]
        public void SessionStorageOperationPlanner_ShouldIgnoreReadThreadInvalidOperationMatchesKeywords()
        {
            var ignored = new InvalidOperationException("failed to locate rollout for this thread");
            var ignoredByMaterialized = new InvalidOperationException("thread not materialized");
            var notIgnored = new InvalidOperationException("permission denied");

            Assert.That(SessionStorageOperationPlanner.ShouldIgnoreThreadReadInvalidOperation(ignored), Is.True);
            Assert.That(SessionStorageOperationPlanner.ShouldIgnoreThreadReadInvalidOperation(ignoredByMaterialized), Is.True);
            Assert.That(SessionStorageOperationPlanner.ShouldIgnoreThreadReadInvalidOperation(notIgnored), Is.False);
        }

        [Test]
        public void SessionPathResolutionExecutor_ResolveAsyncUsesSummaryAndDetailsFallback()
        {
            var summarySession = new ChatSession
            {
                ThreadId = "thread-a"
            };
            var summaryReadCalls = 0;

            var summaryPath = SessionPathResolutionExecutor.ResolveAsync(
                summarySession,
                () => Task.FromResult(new List<AppServerThreadSummary>
                {
                    new AppServerThreadSummary { ThreadId = "thread-a", SessionPath = "summary-path.json" }
                }),
                threadId =>
                {
                    summaryReadCalls++;
                    return Task.FromResult(new AppServerThreadDetails());
                }).GetAwaiter().GetResult();

            Assert.That(summaryPath, Is.EqualTo("summary-path.json"));
            Assert.That(summarySession.SessionPath, Is.EqualTo("summary-path.json"));
            Assert.That(summaryReadCalls, Is.EqualTo(0));

            var detailSession = new ChatSession
            {
                ThreadId = "thread-b"
            };
            var detailReadCalls = 0;
            var detailPath = SessionPathResolutionExecutor.ResolveAsync(
                detailSession,
                () => Task.FromResult(new List<AppServerThreadSummary>()),
                threadId =>
                {
                    detailReadCalls++;
                    return Task.FromResult(new AppServerThreadDetails { SessionPath = "details-path.json" });
                }).GetAwaiter().GetResult();

            Assert.That(detailPath, Is.EqualTo("details-path.json"));
            Assert.That(detailSession.SessionPath, Is.EqualTo("details-path.json"));
            Assert.That(detailReadCalls, Is.EqualTo(1));
        }

        [Test]
        public void SessionPathResolutionExecutor_ResolveAsyncHandlesInvalidOperationByKeyword()
        {
            var ignoredSession = new ChatSession
            {
                ThreadId = "thread-a"
            };

            var ignoredPath = SessionPathResolutionExecutor.ResolveAsync(
                ignoredSession,
                () => Task.FromResult(new List<AppServerThreadSummary>()),
                threadId => throw new InvalidOperationException("failed to locate rollout for thread"))
                .GetAwaiter()
                .GetResult();

            Assert.That(ignoredPath, Is.Null);
            Assert.That(ignoredSession.SessionPath, Is.Null);

            var rejectedSession = new ChatSession
            {
                ThreadId = "thread-b"
            };

            Assert.Throws<InvalidOperationException>(() =>
                SessionPathResolutionExecutor.ResolveAsync(
                    rejectedSession,
                    () => Task.FromResult(new List<AppServerThreadSummary>()),
                    threadId => throw new InvalidOperationException("permission denied"))
                    .GetAwaiter()
                    .GetResult());
        }

        [Test]
        public void DeletionUiStatePlanner_BuildClearsHistoryWhenWorkspaceMissing()
        {
            var plan = DeletionUiStatePlanner.Build(
                new ChatSession { Title = "Keep" },
                null);

            Assert.That(plan.ShouldClearHistory, Is.True);
            Assert.That(plan.ShouldMarkPendingReload, Is.False);
            Assert.That(plan.HasPrimaryMessage, Is.False);
            Assert.That(plan.HasSecondaryMessage, Is.False);
        }

        [Test]
        public void DeletionUiStatePlanner_BuildShowsNoSessionMessageWhenSessionMissing()
        {
            using (UseCulture("en-US"))
            {
                var plan = DeletionUiStatePlanner.Build(
                    null,
                    Workspace("workspace-1"));

                Assert.That(plan.ShouldClearHistory, Is.True);
                Assert.That(plan.ShouldMarkPendingReload, Is.False);
                Assert.That(plan.PrimaryMessage, Does.Contain("There are no sessions in the current workspace."));
                Assert.That(plan.HasSecondaryMessage, Is.False);
            }
        }

        [Test]
        public void DeletionUiStatePlanner_BuildShowsSwitchPromptWhenSessionAvailable()
        {
            using (UseCulture("en-US"))
            {
                var session = new ChatSession { Title = "Fix startup" };

                var plan = DeletionUiStatePlanner.Build(
                    session,
                    Workspace("workspace-1"));

                Assert.That(plan.ShouldClearHistory, Is.False);
                Assert.That(plan.ShouldMarkPendingReload, Is.True);
                Assert.That(plan.PrimaryMessage, Is.EqualTo("Switched to session: Fix startup"));
                Assert.That(plan.SecondaryMessage, Is.EqualTo("Press Enter to reopen full history."));
            }
        }

        [Test]
        public void LocalSessionRemovalEligibility_HasThreadSessionsMatchesWorkspaceMembership()
        {
            var sessions = new[]
            {
                new ChatSession { WorkspaceId = "workspace-1", ThreadId = string.Empty },
                new ChatSession { WorkspaceId = "workspace-2", ThreadId = "thread-2" }
            };

            Assert.That(LocalSessionRemovalEligibility.HasThreadSessions(sessions, "workspace-1"), Is.False);
            Assert.That(LocalSessionRemovalEligibility.HasThreadSessions(sessions, "workspace-2"), Is.True);
        }

        [Test]
        public void LocalSessionRemovalEligibility_CanPermanentlyDeleteSessionAndWorkspace()
        {
            var workspaceSessions = new[]
            {
                new ChatSession { WorkspaceId = "workspace-1", SessionPath = string.Empty, ThreadId = string.Empty },
                new ChatSession { WorkspaceId = "workspace-1", SessionPath = "session.json", ThreadId = string.Empty }
            };

            Assert.That(
                LocalSessionRemovalEligibility.CanPermanentlyDeleteSession(
                    new ChatSession { SessionPath = string.Empty, ThreadId = "thread-1" }),
                Is.True);
            Assert.That(
                LocalSessionRemovalEligibility.CanPermanentlyDeleteSession(
                    new ChatSession { SessionPath = string.Empty, ThreadId = string.Empty }),
                Is.False);
            Assert.That(
                LocalSessionRemovalEligibility.CanPermanentlyDeleteWorkspace(workspaceSessions, "workspace-1"),
                Is.True);
            Assert.That(
                LocalSessionRemovalEligibility.CanPermanentlyDeleteWorkspace(workspaceSessions, "workspace-2"),
                Is.False);
        }

        [Test]
        public void SessionHistoryDisplayBuilder_BuildSessionHeaderItemsIncludesSessionContext()
        {
            using (UseCulture("en-US"))
            {
                var builder = CreateSessionHistoryDisplayBuilder();
                var session = new ChatSession
                {
                    Title = "Fix startup",
                    IsFork = true,
                    ForkIndex = 2,
                    ForkSourceTitle = "Mainline | Seed session",
                    ThreadId = "thread-42"
                };

                var items = builder.BuildSessionHeaderItems(session, true);

                Assert.That(items.Select(item => item.MarkdownText).ToArray(), Is.EqualTo(new[]
                {
                    "Created new session: Fork: Fix startup",
                    "Current branch: Fork",
                    "Forked from: Mainline | Seed session",
                    "Thread ID: thread-42",
                    "Current model: model-summary"
                }));
                Assert.That(items.All(item => item.Sender == "System"), Is.True);
                Assert.That(items.All(item => item.Category == ChatHistoryCategory.System), Is.True);
            }
        }

        [Test]
        public void SessionHistoryDisplayBuilder_BuildDisplayChatHistoryItemsAddsHeaderAndEmptyFallback()
        {
            using (UseCulture("en-US"))
            {
                var builder = CreateSessionHistoryDisplayBuilder();
                var session = new ChatSession
                {
                    Title = "Empty thread"
                };

                var items = builder.BuildDisplayChatHistoryItems(session, true, Array.Empty<ChatHistoryItem>());

                Assert.That(items.Select(item => item.MarkdownText).ToArray(), Is.EqualTo(new[]
                {
                    "Switched session: Mainline: Empty thread",
                    "Current branch: Mainline",
                    "A remote thread will be created automatically when you send the first message.",
                    "Current model: model-summary",
                    "No history messages."
                }));
            }
        }

        [Test]
        public void SessionHistoryDisplayBuilder_BuildHistoryChatItemsMapsTurnMetadataAndFallback()
        {
            using (UseCulture("en-US"))
            {
                var builder = CreateSessionHistoryDisplayBuilder();
                var details = new AppServerThreadDetails
                {
                    TurnCount = 3
                };
                details.Messages.Add(new AppServerThreadMessage
                {
                    ThreadId = "thread-1",
                    TurnId = "turn-1",
                    TurnIndex = 1,
                    Sender = "Me",
                    Content = "ping"
                });
                details.Messages.Add(new AppServerThreadMessage
                {
                    ThreadId = "thread-1",
                    TurnId = "turn-2",
                    TurnIndex = 2,
                    Sender = "codex",
                    Content = "pong"
                });

                var items = builder.BuildHistoryChatItems(details);

                Assert.That(items, Has.Length.EqualTo(2));
                Assert.That(items[0].MarkdownText, Is.EqualTo("ping"));
                Assert.That(items[0].Category, Is.EqualTo(ChatHistoryCategory.Conversation));
                Assert.That(items[0].ThreadId, Is.EqualTo("thread-1"));
                Assert.That(items[0].TurnId, Is.EqualTo("turn-1"));
                Assert.That(items[0].TurnIndex, Is.EqualTo(1));
                Assert.That(items[0].TurnCount, Is.EqualTo(3));
                Assert.That(items[1].MarkdownText, Is.EqualTo("pong"));
                Assert.That(items[1].Category, Is.EqualTo(ChatHistoryCategory.Conversation));

                var emptyItems = builder.BuildHistoryChatItems(new AppServerThreadDetails());

                Assert.That(emptyItems.Select(item => item.MarkdownText).ToArray(), Is.EqualTo(new[] { "No history messages." }));
                Assert.That(emptyItems[0].Category, Is.EqualTo(ChatHistoryCategory.System));
            }
        }

        [Test]
        public void SessionHistoryDisplayBuilder_BuildDisplayedSessionHeaderTextPreservesHeaderIntent()
        {
            using (UseCulture("en-US"))
            {
                var builder = CreateSessionHistoryDisplayBuilder();
                var session = new ChatSession
                {
                    Title = "Updated title"
                };

                var createdHeader = builder.BuildDisplayedSessionHeaderText(
                    new ChatHistoryItem { FullText = "System: Created new session: Previous" },
                    session);
                var switchedToHeader = builder.BuildDisplayedSessionHeaderText(
                    new ChatHistoryItem { FullText = "System: Switched to session: Previous" },
                    session);
                var switchedHeader = builder.BuildDisplayedSessionHeaderText(
                    new ChatHistoryItem { FullText = "System: Other header" },
                    session);

                Assert.That(createdHeader, Is.EqualTo("Created new session: Mainline: Updated title"));
                Assert.That(switchedToHeader, Is.EqualTo("Switched to session: Mainline: Updated title"));
                Assert.That(switchedHeader, Is.EqualTo("Switched session: Mainline: Updated title"));
            }
        }

        [Test]
        public void ChatHistorySelectionSnapshot_ResolveSelectedIndexMatchesTurnIdentityAcrossReloadedItems()
        {
            var selectedItem = new ChatHistoryItem
            {
                ThreadId = "thread-1",
                TurnId = "turn-2",
                TurnIndex = 2,
                Sender = "codex",
                Category = ChatHistoryCategory.Conversation,
                MarkdownText = "reply",
                FullText = "codex: reply"
            };
            var snapshot = ChatHistorySelectionSnapshot.Capture(selectedItem, 6);
            var reloadedItems = new[]
            {
                new ChatHistoryItem { Sender = "System", Category = ChatHistoryCategory.System, FullText = "header" },
                new ChatHistoryItem
                {
                    ThreadId = "thread-1",
                    TurnId = "turn-1",
                    TurnIndex = 1,
                    Sender = "Me",
                    Category = ChatHistoryCategory.Conversation,
                    MarkdownText = "question",
                    FullText = "Me: question"
                },
                new ChatHistoryItem
                {
                    ThreadId = "thread-1",
                    TurnId = "turn-2",
                    TurnIndex = 2,
                    Sender = "codex",
                    Category = ChatHistoryCategory.Conversation,
                    MarkdownText = "reply",
                    FullText = "codex: reply"
                }
            };

            var selectedIndex = snapshot.ResolveSelectedIndex(reloadedItems);

            Assert.That(selectedIndex, Is.EqualTo(2));
        }

        [Test]
        public void ChatHistorySelectionSnapshot_ResolveSelectedIndexMatchesOptimisticUserMessageAfterFirstRemoteReload()
        {
            var selectedItem = ChatHistoryItemFactory.Create(
                ChatHistoryItemFactory.GetUserSenderText(),
                "first message",
                ChatHistoryCategory.Conversation);
            var snapshot = ChatHistorySelectionSnapshot.Capture(selectedItem, 1);
            var reloadedItems = new[]
            {
                ChatHistoryItemFactory.Create("System", "header", ChatHistoryCategory.System),
                ChatHistoryItemFactory.Create("System", "remote thread created", ChatHistoryCategory.System),
                ChatHistoryItemFactory.Create("User", "first message", ChatHistoryCategory.Conversation),
                ChatHistoryItemFactory.Create("codex", "reply", ChatHistoryCategory.Conversation)
            };

            var selectedIndex = snapshot.ResolveSelectedIndex(reloadedItems);

            Assert.That(selectedIndex, Is.EqualTo(2));
        }

        [Test]
        public void ChatHistorySelectionSnapshot_ResolveSelectedIndexFallsBackToLastAvailableIndexWhenOriginalItemIsMissing()
        {
            var selectedItem = new ChatHistoryItem
            {
                ThreadId = "thread-1",
                TurnId = "turn-9",
                TurnIndex = 9,
                Sender = "codex",
                Category = ChatHistoryCategory.Conversation,
                MarkdownText = "reply 9",
                FullText = "codex: reply 9"
            };
            var snapshot = ChatHistorySelectionSnapshot.Capture(selectedItem, 8);
            var reloadedItems = new[]
            {
                new ChatHistoryItem { Sender = "System", Category = ChatHistoryCategory.System, FullText = "header" },
                new ChatHistoryItem
                {
                    ThreadId = "thread-1",
                    TurnId = "turn-1",
                    TurnIndex = 1,
                    Sender = "Me",
                    Category = ChatHistoryCategory.Conversation,
                    MarkdownText = "question",
                    FullText = "Me: question"
                },
                new ChatHistoryItem
                {
                    ThreadId = "thread-1",
                    TurnId = "turn-2",
                    TurnIndex = 2,
                    Sender = "codex",
                    Category = ChatHistoryCategory.Conversation,
                    MarkdownText = "reply",
                    FullText = "codex: reply"
                }
            };

            var selectedIndex = snapshot.ResolveSelectedIndex(reloadedItems);

            Assert.That(selectedIndex, Is.EqualTo(2));
        }

        [Test]
        public void ChatHistorySelectionSnapshot_CaptureCanSkipSelectionRestoreForSilentReload()
        {
            var selectedItem = new ChatHistoryItem
            {
                ThreadId = "thread-1",
                TurnId = "turn-1",
                TurnIndex = 1,
                Sender = "Me",
                Category = ChatHistoryCategory.Conversation,
                FullText = "Me: hello"
            };

            var snapshot = ChatHistorySelectionSnapshot.Capture(selectedItem, 0, false);

            Assert.That(snapshot, Is.Null);
        }

        [Test]
        public void PendingAttachmentSendValidator_EnsureReadyRestoresTemporaryImageAndReturnsUnavailableItems()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            var restoredImagePath = Path.Combine(tempDirectory, "restored.png");
            var missingFilePath = Path.Combine(tempDirectory, "missing.txt");

            try
            {
                var restoredImage = new PendingAttachmentItem(
                    restoredImagePath,
                    "restored.png",
                    true,
                    AttachmentKind.Image,
                    "[img]",
                    new byte[] { 1, 2, 3 },
                    null);
                var missingFile = new PendingAttachmentItem(
                    missingFilePath,
                    "missing.txt",
                    false,
                    AttachmentKind.File,
                    "[file]",
                    null,
                    null);

                var unavailable = PendingAttachmentSendValidator.EnsureReady(new[] { restoredImage, missingFile });

                Assert.That(File.Exists(restoredImagePath), Is.True);
                Assert.That(File.ReadAllBytes(restoredImagePath), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(unavailable.Select(item => item.DisplayName).ToArray(), Is.EqualTo(new[] { "missing.txt" }));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
        }

        [Test]
        public void PendingAttachmentSendValidator_AllowsResolvableStalePluginSkillAttachment()
        {
            var root = Path.Combine(Path.GetTempPath(), "MyCodex.PendingAttachmentResolver", Guid.NewGuid().ToString("N"));
            try
            {
                var oldSkill = Path.Combine(root, ".codex", "plugins", "cache", "openai-curated", "superpowers", "oldhash", "skills", "writing-plans");
                var newSkill = Path.Combine(root, ".codex", "plugins", "cache", "openai-curated", "superpowers", "newhash", "skills", "writing-plans");
                Directory.CreateDirectory(newSkill);
                File.WriteAllText(Path.Combine(newSkill, "SKILL.md"), "---\nname: writing-plans\n---\n");

                var unavailable = PendingAttachmentSendValidator.EnsureReady(new[]
                {
                    new PendingAttachmentItem(oldSkill, "Writing Plans", false, AttachmentKind.Skill, "[Writing Plans]", null, "writing-plans")
                });

                Assert.That(unavailable, Is.Empty);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Test]
        public void PendingAttachmentSendValidator_EnsureReadySkipsRemotePathValidation()
        {
            var remoteFile = new PendingAttachmentItem(
                "/Users/demo/repo/README.md",
                "README.md",
                false,
                AttachmentKind.File,
                "[file]",
                null,
                null,
                false,
                true);
            var remoteFolder = new PendingAttachmentItem(
                "/Users/demo/repo/docs",
                "docs",
                false,
                AttachmentKind.File,
                "[folder]",
                null,
                null,
                true,
                true);

            var unavailable = PendingAttachmentSendValidator.EnsureReady(new[] { remoteFile, remoteFolder });

            Assert.That(unavailable, Is.Empty);
            Assert.That(ComposeAttachmentState.CountFileAttachments(new[] { remoteFile, remoteFolder }), Is.EqualTo(1));
            Assert.That(ComposeAttachmentState.CountFolderAttachments(new[] { remoteFile, remoteFolder }), Is.EqualTo(1));
        }

        [TestCase(CodexRuntimeSources.Wsl, "/home/demo/.codex/skills/apple-news", true)]
        [TestCase(CodexRuntimeSources.Wsl, "/home/demo/.codex/skills/apple-news/SKILL.md", false)]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, "/home/demo/.codex/skills/apple-news", true)]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, "/home/demo/.codex/skills/apple-news/SKILL.md", false)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, "/home/demo/.codex/skills/apple-news", true)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, "/home/demo/.codex/skills/apple-news/SKILL.md", false)]
        public void SkillAttachments_InUnixRuntimeClassifySkillPathForRemoteValidation(
            string runtimeSource,
            string skillPath,
            bool expectedDirectory)
        {
            using (var listBox = new ListBox())
            using (var attachmentPanel = new Panel())
            using (var composePanel = new Panel())
            {
                var form = (MainForm)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(MainForm));
                SetPrivateField(form, "_appConfigSnapshot", new AppConfigSnapshot { CodexRuntimeSource = runtimeSource });
                SetPrivateField(form, "_pendingAttachments", new List<PendingAttachmentItem>());
                SetPrivateField(form, "_pendingAttachmentListBox", listBox);
                SetPrivateField(form, "_attachmentPanel", attachmentPanel);
                SetPrivateField(form, "_composePanel", composePanel);
                SetPrivateField(form, "_removeSelectedAttachmentMenuItem", new MenuItem());
                SetPrivateField(form, "_clearAttachmentsMenuItem", new MenuItem());

                var skill = new AppServerSkillInfo
                {
                    Name = "apple-news",
                    DisplayName = "Apple 24H News",
                    Path = skillPath
                };

                var mainAttachment = (PendingAttachmentItem)InvokePrivateMethod(form, "AddSkillAttachment", skill);
                var subAgentAttachments = (IReadOnlyList<PendingAttachmentItem>)InvokePrivateMethod(form, "CreateSubAgentWorkbenchSkillAttachment", skill);

                Assert.That(mainAttachment.IsRemotePath, Is.True);
                Assert.That(mainAttachment.IsDirectory, Is.EqualTo(expectedDirectory));
                Assert.That(PendingAttachmentSendValidator.EnsureReady(new[] { mainAttachment }), Is.Empty);
                Assert.That(subAgentAttachments, Has.Count.EqualTo(1));
                Assert.That(subAgentAttachments[0].IsRemotePath, Is.True);
                Assert.That(subAgentAttachments[0].IsDirectory, Is.EqualTo(expectedDirectory));
                Assert.That(PendingAttachmentSendValidator.EnsureReady(subAgentAttachments), Is.Empty);
            }
        }

        [Test]
        public void SessionTitleStrategy_ShouldReplaceGeneratedTitleOnlyForDefaultTitles()
        {
            using (UseCulture("en-US"))
            {
                var workspace = new WorkspaceInfo { Name = "Repo" };

                Assert.That(SessionTitleStrategy.ShouldReplaceGeneratedTitle(
                    new ChatSession { Title = "Repo Session 3" },
                    workspace), Is.True);
                Assert.That(SessionTitleStrategy.ShouldReplaceGeneratedTitle(
                    new ChatSession { Title = "Custom title" },
                    workspace), Is.False);
                Assert.That(SessionTitleStrategy.ShouldReplaceGeneratedTitle(
                    new ChatSession { Title = "Repo Session 4", IsImported = true },
                    workspace), Is.False);
            }
        }

        [Test]
        public void SessionTitleStrategy_BuildTitleFromFirstMessageNormalizesWhitespaceAndTruncates()
        {
            var normalized = SessionTitleStrategy.BuildTitleFromFirstMessage("  Hello\r\n   world  ", string.Empty);
            var truncated = SessionTitleStrategy.BuildTitleFromFirstMessage(
                string.Empty,
                "12345678901234567890123456789012345678901");

            Assert.That(normalized, Is.EqualTo("Hello world"));
            Assert.That(truncated, Is.EqualTo("1234567890123456789012345678901234567890..."));
        }

        [Test]
        public void SendInputPreflight_EvaluateSessionBootstrapBlocksWithoutWorkspaceAndRequiresSessionOtherwise()
        {
            using (UseCulture("en-US"))
            {
                var blocked = SendInputPreflight.EvaluateSessionBootstrap(false, false);
                var requiresSession = SendInputPreflight.EvaluateSessionBootstrap(false, true);
                var existingSession = SendInputPreflight.EvaluateSessionBootstrap(true, true);

                Assert.That(blocked.IsBlocked, Is.True);
                Assert.That(blocked.StatusText, Is.EqualTo("Please create a workspace first"));
                Assert.That(requiresSession.RequiresNewSession, Is.True);
                Assert.That(existingSession.IsBlocked, Is.False);
                Assert.That(existingSession.RequiresNewSession, Is.False);
            }
        }

        [Test]
        public void SendInputPreflight_EvaluateAttachmentAvailabilityBuildsWarningText()
        {
            using (UseCulture("en-US"))
            {
                var result = SendInputPreflight.EvaluateAttachmentAvailability(new[]
                {
                    new PendingAttachmentItem("one.txt", "one.txt", false, AttachmentKind.File, "[one]", null, null),
                    new PendingAttachmentItem("two.txt", "two.txt", false, AttachmentKind.File, "[two]", null, null)
                });

                Assert.That(result.Outcome, Is.EqualTo(SendInputValidationOutcome.AttachmentsUnavailable));
                Assert.That(result.StatusText, Is.EqualTo("Attachments are unavailable. Please add them again"));
                Assert.That(result.WarningMessage, Is.EqualTo("The following attachments are unavailable. Re-add them before sending: one.txt, two.txt"));
            }
        }

        [Test]
        public void SendInputPreflight_EvaluateReadyToDispatchHandlesEmptyInputAndMissingWorkspacePath()
        {
            using (UseCulture("en-US"))
            {
                var emptyInput = SendInputPreflight.EvaluateReadyToDispatch(true, 0);
                var missingWorkspace = SendInputPreflight.EvaluateReadyToDispatch(false, 2);
                var ready = SendInputPreflight.EvaluateReadyToDispatch(true, 2);

                Assert.That(emptyInput.Outcome, Is.EqualTo(SendInputValidationOutcome.EmptyInput));
                Assert.That(missingWorkspace.Outcome, Is.EqualTo(SendInputValidationOutcome.WorkspaceUnavailable));
                Assert.That(missingWorkspace.StatusText, Is.EqualTo("Current workspace path is unavailable"));
                Assert.That(ready.CanContinue, Is.True);
            }
        }

        [Test]
        public void SessionSendCompletionPlanner_BuildReturnsRecreationFeedbackAndTitleUpdate()
        {
            using (UseCulture("en-US"))
            {
                var session = new ChatSession
                {
                    Title = "Repo Session 3"
                };
                var workspace = new WorkspaceInfo
                {
                    Name = "Repo"
                };
                var sendResult = new SessionSendResult
                {
                    RetryThreadRecreation = new SessionThreadRecreationResult
                    {
                        PreviousThreadId = "thread-old",
                        CurrentThreadId = "thread-new"
                    }
                };

                var plan = SessionSendCompletionPlanner.Build(sendResult, session, workspace, "  hello world  ", "ignored");

                Assert.That(plan.RequiresThreadRecreationUiRefresh, Is.True);
                Assert.That(plan.PreviousThreadIdToReset, Is.EqualTo("thread-old"));
                Assert.That(plan.HistoryMessage, Is.EqualTo("The remote thread for current session was missing. A new thread was created automatically and sending continued."));
                Assert.That(plan.StatusText, Is.EqualTo("Session thread was recreated and message was resent"));
                Assert.That(plan.HasSessionTitleUpdate, Is.True);
                Assert.That(plan.SessionTitle, Is.EqualTo("hello world"));
                Assert.That(plan.SessionPreview, Is.EqualTo("hello world"));

                Assert.That(plan.ApplyLocalSessionTitleUpdate(session), Is.True);
                Assert.That(session.Title, Is.EqualTo("hello world"));
                Assert.That(session.Preview, Is.EqualTo("hello world"));
            }
        }

        [Test]
        public void SessionSendCompletionPlanner_BuildReturnsRepairFeedback()
        {
            using (UseCulture("en-US"))
            {
                var session = new ChatSession
                {
                    Title = "Custom title"
                };
                var workspace = new WorkspaceInfo
                {
                    Name = "Repo"
                };
                var sendResult = new SessionSendResult
                {
                    InvalidEncryptedContentRepairResult = ThreadHistoryRepairResult.CreateRepaired(1)
                };

                var plan = SessionSendCompletionPlanner.Build(sendResult, session, workspace, "continue", "continue");

                Assert.That(plan.RequiresThreadRecreationUiRefresh, Is.False);
                Assert.That(plan.PreviousThreadIdToReset, Is.Empty);
                Assert.That(plan.HistoryMessage, Is.EqualTo("The current session history was repaired automatically and sending continued."));
                Assert.That(plan.StatusText, Is.EqualTo("Session history was repaired and message was resent"));
                Assert.That(plan.HasSessionTitleUpdate, Is.False);
            }
        }

        [Test]
        public void SessionSendCompletionPlanner_BuildUsesPreviewFallbackAndSkipsCustomTitles()
        {
            using (UseCulture("en-US"))
            {
                var generatedSession = new ChatSession
                {
                    Title = "Repo Session 7"
                };
                var customSession = new ChatSession
                {
                    Title = "Custom title"
                };
                var workspace = new WorkspaceInfo
                {
                    Name = "Repo"
                };

                var generatedPlan = SessionSendCompletionPlanner.Build(new SessionSendResult(), generatedSession, workspace, string.Empty, "[One] [Two]");
                var customPlan = SessionSendCompletionPlanner.Build(new SessionSendResult(), customSession, workspace, "hello", "hello");

                Assert.That(generatedPlan.HasSessionTitleUpdate, Is.True);
                Assert.That(generatedPlan.SessionTitle, Is.EqualTo("[One] [Two]"));
                Assert.That(generatedPlan.SessionPreview, Is.EqualTo("[One] [Two]"));
                Assert.That(customPlan.HasSessionTitleUpdate, Is.False);
                Assert.That(customPlan.HistoryMessage, Is.Empty);
                Assert.That(customPlan.StatusText, Is.Empty);
            }
        }

        private static PendingAttachmentItem Attachment(string localPath, string markerText)
        {
            return new PendingAttachmentItem(localPath, localPath, false, AttachmentKind.File, markerText, null, null);
        }

        private static AppServerThreadSummary Thread(string threadId, string workingDirectory, string title = null)
        {
            return new AppServerThreadSummary
            {
                ThreadId = threadId,
                WorkingDirectory = workingDirectory,
                Title = title
            };
        }

        private static WorkspaceInfo Workspace(string workspaceId, string rootPath = null)
        {
            return new WorkspaceInfo
            {
                Id = workspaceId,
                Name = workspaceId,
                RootPath = rootPath ?? workspaceId
            };
        }

        private static bool CanChangeWithExistingThread(
            bool isThreadBusy = false,
            bool canInterruptThread = false,
            bool isThreadCompacting = false)
        {
            return SessionPlanModePlanner.CanChangePlanOrGoalState(new SessionPlanModeAvailabilityContext
            {
                HasSession = true,
                IsWorkspaceAvailable = true,
                HasThread = true,
                IsThreadBusy = isThreadBusy,
                CanInterruptThread = canInterruptThread,
                IsThreadCompacting = isThreadCompacting
            });
        }

        private static string CreateTempDirectory()
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        private static SessionHistoryDisplayBuilder CreateSessionHistoryDisplayBuilder()
        {
            return new SessionHistoryDisplayBuilder(
                "System",
                "Error",
                _ => "model-summary",
                CreateHistoryItem,
                ResolveDefaultChatHistoryCategory);
        }

        private static SessionHistoryLoadWorkflow CreateSessionHistoryLoadWorkflow(
            SessionHistoryStateStore store,
            Func<string, Task<AppServerThreadDetails>> readThreadAsync,
            Func<Exception, bool> isMissingThreadRolloutError = null,
            Func<Exception, bool> isConnectivityFailure = null)
        {
            return new SessionHistoryLoadWorkflow(
                store,
                CreateSessionHistoryDisplayBuilder(),
                readThreadAsync,
                isMissingThreadRolloutError ?? SessionThreadService.IsMissingThreadRolloutError,
                isConnectivityFailure ?? (_ => false));
        }

        private static ChatHistoryItem CreateHistoryItem(string sender, string message, ChatHistoryCategory category)
        {
            return new ChatHistoryItem
            {
                Sender = sender,
                Category = category,
                DisplayText = message,
                FullText = string.Format("{0}: {1}", sender, message),
                MarkdownText = message
            };
        }

        private static ChatHistoryCategory ResolveDefaultChatHistoryCategory(string sender)
        {
            if (string.Equals(sender, "Me", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sender, "codex", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sender, "Review", StringComparison.OrdinalIgnoreCase))
            {
                return ChatHistoryCategory.Conversation;
            }

            return string.Equals(sender, "Error", StringComparison.OrdinalIgnoreCase)
                ? ChatHistoryCategory.Error
                : ChatHistoryCategory.System;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static object InvokePrivateMethod(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(
                methodName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            return method.Invoke(target, args);
        }

        private static IDisposable UseCulture(string cultureName)
        {
            return new CultureScope(cultureName);
        }

        private sealed class RecordingScreenReaderService : IScreenReaderService
        {
            private readonly List<string> _spokenTexts = new List<string>();

            public IReadOnlyList<string> SpokenTexts
            {
                get { return _spokenTexts; }
            }

            public bool IsAvailable
            {
                get { return true; }
            }

            public bool IsInitialized
            {
                get { return true; }
            }

            public string LastError
            {
                get { return null; }
            }

            public bool EnsureInitialized()
            {
                return true;
            }

            public bool IsReaderRunning()
            {
                return true;
            }

            public int GetReaderState()
            {
                return 4;
            }

            public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
            {
                _spokenTexts.Add(text);
                return true;
            }

            public bool Stop(bool useSlave)
            {
                return true;
            }

            public void Dispose()
            {
            }
        }

        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo _originalCulture;
            private readonly CultureInfo _originalUiCulture;
            private readonly CultureInfo _originalDefaultCulture;
            private readonly CultureInfo _originalDefaultUiCulture;

            public CultureScope(string cultureName)
            {
                _originalCulture = CultureInfo.CurrentCulture;
                _originalUiCulture = CultureInfo.CurrentUICulture;
                _originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
                _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = _originalCulture;
                CultureInfo.CurrentUICulture = _originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = _originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
            }
        }
    }
}
