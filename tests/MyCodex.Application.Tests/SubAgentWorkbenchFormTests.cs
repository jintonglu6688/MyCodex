using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class SubAgentWorkbenchFormTests
    {
        [Test]
        public async Task PollingAddsNewReplyWithoutMovingTheMessageBeingRead()
        {
            var append = false;
            var operations = CreateOperations(new List<string>(), id =>
            {
                var details = new AppServerThreadDetails { ThreadId = id };
                details.Messages.Add(new AppServerThreadMessage { Sender = "codex", Content = "first reply" });
                if (append) details.Messages.Add(new AppServerThreadMessage { Sender = "codex", Content = "second reply" });
                return Task.FromResult(details);
            });
            using (var form = new SubAgentWorkbenchForm(new ChatSession { Id = "parent" },
                new[] { CreateSubAgent("a", "a", "A") }, operations))
            {
                await ShowAndActivateSelectedSubAgentAsync(form);
                var history = FindSingleControl<ChatHistoryListBox>(form);
                history.SelectSingleIndex(history.Items.Count - 1);
                var selected = history.PrimaryChatHistoryItem;
                var selectionChanges = 0;
                history.SelectedIndexChanged += (sender, args) => selectionChanges++;
                append = true;
                await form.RefreshObservedSubAgentsAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(history.PrimaryChatHistoryItem, Is.SameAs(selected));
                    Assert.That(selectionChanges, Is.Zero);
                    Assert.That(history.Items.Cast<ChatHistoryItem>().Any(item => item.FullText.Contains("second reply")), Is.True);
                });
            }
        }

        [Test]
        public async Task UnchangedPollingDoesNotReselectHistoryOrRepeatAnnouncements()
        {
            var announcements = new List<string>();
            var operations = CreateOperations(new List<string>());
            operations.WorkbenchCallbacks = new ChatWorkbenchHostCallbacks { AnnounceStatus = announcements.Add };
            using (var form = new SubAgentWorkbenchForm(new ChatSession { Id = "parent" },
                new[] { CreateSubAgent("a", "a", "A") }, operations))
            {
                await ShowAndActivateSelectedSubAgentAsync(form);
                var history = FindSingleControl<ChatHistoryListBox>(form);
                history.SelectSingleIndex(history.Items.Count - 1);
                var selected = history.PrimaryChatHistoryItem;
                var selectionChanges = 0;
                history.SelectedIndexChanged += (sender, args) => selectionChanges++;
                announcements.Clear();
                await form.RefreshObservedSubAgentsAsync();
                await form.RefreshObservedSubAgentsAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(selectionChanges, Is.Zero, "Unchanged snapshots must not rebuild/reselect the message list.");
                    Assert.That(history.PrimaryChatHistoryItem, Is.SameAs(selected));
                    Assert.That(announcements, Is.Empty, "Background polling must not speak refresh instructions repeatedly.");
                });
            }
        }

        [TestCase(1100, 760)]
        [TestCase(980, 640)]
        public void DefaultAndMinimumLayoutKeepNavigationAndActivityUsable(int width, int height)
        {
            using (var form = new SubAgentWorkbenchForm(new ChatSession { Id = "parent" },
                new[] { CreateSubAgent("a", "a", "A") }, CreateOperations(new List<string>())))
            {
                form.Size = new System.Drawing.Size(width, height);
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.Multiple(() =>
                {
                    Assert.That(FindSingleControl<ListView>(form).Width, Is.GreaterThanOrEqualTo(260));
                    Assert.That(FindSingleControl<ListView>(form).Columns[3].DisplayIndex, Is.EqualTo(1), "Show status beside the agent name.");
                    Assert.That(FindSingleControl<ActivityVirtualBrowseControl>(form).Height, Is.GreaterThanOrEqualTo(90));
                });
            }
        }

        [Test]
        public async Task SwitchingAgentsRestoresSeparateInputAndAttachments()
        {
            using (var form = new SubAgentWorkbenchForm(new ChatSession { Id = "parent" },
                new[] { CreateSubAgent("a", "a", "A"), CreateSubAgent("b", "b", "B") }, CreateOperations(new List<string>())))
            {
                await ShowAndActivateSelectedSubAgentAsync(form);
                var workbench = FindSingleControl<ChatWorkbenchControl>(form);
                workbench.SetInputText("draft A");
                workbench.AddPendingAttachments(new[] { CreateFileAttachment() });
                var list = FindSingleControl<ListView>(form);
                list.Items[0].Selected = false;
                list.Items[1].Selected = true;
                await form.ActivateSelectedSubAgentAsync();
                Assert.That(workbench.InputText, Is.Empty);
                Assert.That(workbench.PendingAttachments, Is.Empty);
                workbench.SetInputText("draft B");
                list.Items[1].Selected = false;
                list.Items[0].Selected = true;
                await form.ActivateSelectedSubAgentAsync();
                Assert.That(workbench.InputText, Is.EqualTo("draft A" + CreateFileAttachment().MarkerText));
                Assert.That(workbench.PendingAttachments.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ParentActivityRefreshesSameAgentOnEachFollowupWithoutChildTurnEvents()
        {
            var reads = new List<string>();
            using (var form = new SubAgentWorkbenchForm(
                new ChatSession { Id = "parent", ThreadId = "parent-thread" },
                new[] { CreateSubAgent("child", "child-thread", "Agent") }, CreateOperations(reads)))
            {
                await ShowAndActivateSelectedSubAgentAsync(form);
                foreach (var kind in new[] { "interacted", "completed", "interacted", "completed" })
                    form.HandleAppServerEvent(new AppServerEventArgs("subagent_activity", kind, threadId: "parent-thread",
                        subAgentActivity: new AppServerSubAgentActivity { AgentThreadId = "child-thread", Kind = kind }));
                Assert.That(reads, Has.Count.EqualTo(5));
                Assert.That(form.ActiveSession.ThreadId, Is.EqualTo("child-thread"));
            }
        }

        [Test]
        public async Task CompletedHistoryWithStopCapabilityDoesNotKeepTargetBusy()
        {
            var working = false;
            var operations = CreateOperations(new List<string>());
            operations.CanStop = session => true;
            operations.IsWorking = session => working;
            using (var form = new SubAgentWorkbenchForm(
                new ChatSession { Id = "parent", ThreadId = "parent-thread" },
                new[] { CreateSubAgent("child", "child-thread", "Agent") }, operations))
            {
                await ShowAndActivateSelectedSubAgentAsync(form);
                Assert.That(form.IsBusy, Is.False);
                working = true;
                Assert.That(form.IsBusy, Is.True);
                working = false;
                Assert.That(form.IsBusy, Is.False);
            }
        }

        [Test]
        public async Task CompletedFollowupReloadsPreviouslyCachedHistory()
        {
            var reads = new List<string>();
            using (var form = new SubAgentWorkbenchForm(
                new ChatSession { Id = "parent", ThreadId = "parent-thread" },
                new[] { CreateSubAgent("child", "child-thread", "Agent") },
                CreateOperations(reads)))
            {
                await ShowAndActivateSelectedSubAgentAsync(form);
                form.HandleAppServerEvent(new AppServerEventArgs("turn_started", "started", threadId: "child-thread", turnId: "second"));
                form.HandleAppServerEvent(new AppServerEventArgs("turn_completed", "completed", threadId: "child-thread", turnId: "second"));
                Assert.That(reads, Has.Count.EqualTo(2), "The completed followup must read fresh history.");
            }
        }

        [Test]
        public async Task SwitchingAgentsDoesNotCarryRunningStateToIdleAgent()
        {
            using (UseCulture("en-US"))
            using (var form = new SubAgentWorkbenchForm(
                new ChatSession { Id = "parent", ThreadId = "parent-thread" },
                new[] { CreateSubAgent("a", "thread-a", "A"), CreateSubAgent("b", "thread-b", "B") },
                CreateOperations(new List<string>())))
            {
                await ShowAndActivateSelectedSubAgentAsync(form);
                form.HandleAppServerEvent(new AppServerEventArgs("turn_started", "started", threadId: "thread-a", turnId: "turn-a"));
                var list = FindSingleControl<ListView>(form);
                list.Items[0].Selected = false;
                list.Items[1].Selected = true;
                await form.ActivateSelectedSubAgentAsync();
                Assert.That(FindButton(form, "Stop").Enabled, Is.False);
                list.Items[1].Selected = false;
                list.Items[0].Selected = true;
                await form.ActivateSelectedSubAgentAsync();
                Assert.That(FindButton(form, "Stop").Enabled, Is.True);
            }
        }

        [Test]
        public async Task ExplicitRefreshKeepsDisplayedHistoryWhenReadFails()
        {
            var fail = false;
            var operations = CreateOperations(new List<string>(), threadId =>
            {
                if (fail)
                {
                    throw new InvalidOperationException("read failed");
                }
                return Task.FromResult(new AppServerThreadDetails
                {
                    ThreadId = threadId,
                    Messages = { new AppServerThreadMessage { Sender = "codex", Content = "retained history" } }
                });
            });
            using (var form = new SubAgentWorkbenchForm(new ChatSession { Id = "parent" },
                new[] { CreateSubAgent("child", "thread", "Agent") }, operations))
            {
                await ShowAndActivateSelectedSubAgentAsync(form);
                fail = true;
                await form.ActivateSelectedSubAgentAsync();
                Assert.That(FindSingleControl<ChatHistoryListBox>(form).Items.Cast<ChatHistoryItem>()
                    .Any(item => item.FullText.Contains("retained history")), Is.True);
            }
        }

        [Test]
        public async Task EventDuringReadDiscardsOldSnapshotAndCoalescesReload()
        {
            var pending = new TaskCompletionSource<AppServerThreadDetails>();
            var reads = 0;
            var operations = CreateOperations(new List<string>(), threadId =>
            {
                reads++;
                return reads == 1 ? pending.Task : Task.FromResult(new AppServerThreadDetails
                {
                    ThreadId = threadId,
                    Messages = { new AppServerThreadMessage { Sender = "codex", Content = "fresh followup" } }
                });
            });
            using (var form = new SubAgentWorkbenchForm(new ChatSession { Id = "parent" },
                new[] { CreateSubAgent("child", "thread", "Agent") }, operations))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var activation = form.ActivateSelectedSubAgentAsync();
                form.HandleAppServerEvent(new AppServerEventArgs("turn_completed", "done", threadId: "thread", turnId: "second"));
                Assert.That(reads, Is.EqualTo(1));
                pending.SetResult(new AppServerThreadDetails { ThreadId = "thread" });
                await activation;
                Assert.That(reads, Is.EqualTo(2));
                Assert.That(FindSingleControl<ChatHistoryListBox>(form).Items.Cast<ChatHistoryItem>()
                    .Any(item => item.FullText.Contains("fresh followup")), Is.True);
            }
        }

        [Test]
        public async Task OpeningSubAgentWorkbenchSelectsFirstSubAgentWithoutActivatingIt()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var firstSubAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");
                var secondSubAgent = CreateSubAgent("child-2", "child-thread-2", "Babbage");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { firstSubAgent, secondSubAgent },
                    operations))
                {
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(form.ActiveSession, Is.Null);
                    Assert.That(readThreadIds, Is.Empty);

                    var listView = FindSingleControl<ListView>(form);
                    Assert.That(listView.AccessibleName, Is.EqualTo("Subagents"));
                    Assert.That(listView.Items, Has.Count.EqualTo(2));
                    Assert.That(listView.SelectedItems, Has.Count.EqualTo(1));
                    Assert.That(listView.SelectedItems[0].Tag, Is.SameAs(firstSubAgent));
                    Assert.That(listView.TabIndex, Is.EqualTo(0));
                    Assert.That(
                        FindButtons(form).Any(button => string.Equals(button.AccessibleName, "Open", StringComparison.Ordinal)),
                        Is.False);

                    listView.Items[1].Selected = true;
                    listView.Items[1].Focused = true;
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(form.ActiveSession, Is.Null);
                    Assert.That(readThreadIds, Is.Empty);

                    await form.ActivateSelectedSubAgentAsync();

                    Assert.That(form.ActiveSession, Is.SameAs(secondSubAgent));
                    Assert.That(readThreadIds, Is.EqualTo(new[] { "child-thread-2" }));

                    var historyListBox = FindSingleControl<ChatHistoryListBox>(form);
                    Assert.That(historyListBox.Items.OfType<ChatHistoryItem>().Any(item => item.FullText.Contains("history child-thread-2")), Is.True);
                    historyListBox.SelectSingleIndex(historyListBox.Items
                        .Cast<ChatHistoryItem>()
                        .Select((item, index) => new { item, index })
                        .First(entry => entry.item.FullText.Contains("history child-thread-2"))
                        .index);
                    Assert.That(historyListBox.BuildClipboardTextForSelection(), Does.Contain("history child-thread-2"));
                }
            }
        }

        [Test]
        public async Task RefreshSubAgentsClearsRemovedActiveSessionWithoutLoadingReplacement()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var firstSubAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");
                var secondSubAgent = CreateSubAgent("child-2", "child-thread-2", "Babbage");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { firstSubAgent, secondSubAgent },
                    operations))
                {
                    await ShowAndActivateSelectedSubAgentAsync(form);

                    Assert.That(form.ActiveSession, Is.SameAs(firstSubAgent));
                    Assert.That(readThreadIds, Is.EqualTo(new[] { "child-thread-1" }));

                    form.RefreshSubAgents(new List<ChatSession> { secondSubAgent });
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(form.ActiveSession, Is.Null);
                    Assert.That(readThreadIds, Is.EqualTo(new[] { "child-thread-1" }));

                    await form.ActivateSelectedSubAgentAsync();

                    Assert.That(form.ActiveSession, Is.SameAs(secondSubAgent));
                    Assert.That(readThreadIds, Is.EqualTo(new[] { "child-thread-1", "child-thread-2" }));
                }
            }
        }

        [Test]
        public void RefreshSubAgentsClearsActivityWhenActiveSubAgentIsRemoved()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var firstSubAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");
                var secondSubAgent = CreateSubAgent("child-2", "child-thread-2", "Babbage");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { firstSubAgent, secondSubAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    var activity = FindSingleControl<ActivityVirtualBrowseControl>(form);
                    form.HandleAppServerEvent(new AppServerEventArgs("turn_started", "started", threadId: "child-thread-1", turnId: "turn-1"));
                    form.HandleAppServerEvent(new AppServerEventArgs("reasoning_completed", "**Removed task** Should disappear.", threadId: "child-thread-1", turnId: "turn-1", itemId: "reason-1"));
                    System.Windows.Forms.Application.DoEvents();
                    Assert.That(activity.GetReadableItems().Any(item => item.Contains("Removed task: Should disappear.")), Is.True);

                    form.RefreshSubAgents(new List<ChatSession> { secondSubAgent });
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(form.ActiveSession, Is.Null);
                    Assert.That(activity.GetReadableItems(), Is.EqualTo(new[] { "No activity records currently." }));
                }
            }
        }

        [Test]
        public void TurnStartedEventEnablesStopForActiveSubAgent()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var subAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { subAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    var stopButton = FindButton(form, "Stop");
                    Assert.That(stopButton.Enabled, Is.False);

                    form.HandleAppServerEvent(new AppServerEventArgs("turn_started", "started", threadId: "child-thread-1", turnId: "turn-1"));
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(stopButton.Enabled, Is.True);

                    form.HandleAppServerEvent(new AppServerEventArgs("turn_completed", "completed", threadId: "child-thread-1", turnId: "turn-1"));
                    WaitForCondition(() => !stopButton.Enabled);
                }
            }
        }

        [Test]
        public void ActiveSubAgentReasoningUpdatesActivityArea()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var subAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { subAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    var activity = FindSingleControl<ActivityVirtualBrowseControl>(form);
                    Assert.That(activity.AccessibleName, Is.EqualTo("Activity"));
                    Assert.That(activity.GetReadableItems(), Is.EqualTo(new[] { "No activity records currently." }));

                    form.HandleAppServerEvent(new AppServerEventArgs("turn_started", "started", threadId: "child-thread-1", turnId: "turn-1"));
                    form.HandleAppServerEvent(new AppServerEventArgs("reasoning_started", string.Empty, threadId: "child-thread-1", turnId: "turn-1", itemId: "reason-1"));
                    form.HandleAppServerEvent(new AppServerEventArgs("reasoning_summary_delta", "**Inspecting files** Reading subagent state.", threadId: "child-thread-1", turnId: "turn-1", itemId: "reason-1"));
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(GetLabels(form).Any(label => string.Equals(label.AccessibleName, "Current activity", StringComparison.Ordinal) &&
                                                             label.Text.Contains("Analyzing")), Is.True);
                    Assert.That(activity.GetReadableItems(), Is.EqualTo(new[] { "Turn: Starting a new conversation turn" }));

                    form.HandleAppServerEvent(new AppServerEventArgs("reasoning_completed", "**Inspecting files** Reading subagent state.", threadId: "child-thread-1", turnId: "turn-1", itemId: "reason-1"));
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(activity.GetReadableItems().Any(item => item.Contains("Inspecting files: Reading subagent state.")), Is.True);
                }
            }
        }

        [Test]
        public async Task SwitchingActiveSubAgentRestoresThatSubAgentsActivity()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var firstSubAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");
                var secondSubAgent = CreateSubAgent("child-2", "child-thread-2", "Babbage");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { firstSubAgent, secondSubAgent },
                    operations))
                {
                    await ShowAndActivateSelectedSubAgentAsync(form);

                    var activity = FindSingleControl<ActivityVirtualBrowseControl>(form);
                    form.HandleAppServerEvent(new AppServerEventArgs("turn_started", "started", threadId: "child-thread-1", turnId: "turn-1"));
                    form.HandleAppServerEvent(new AppServerEventArgs("reasoning_completed", "**First task** Checking desktop code.", threadId: "child-thread-1", turnId: "turn-1", itemId: "reason-1"));
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(activity.GetReadableItems().Any(item => item.Contains("First task: Checking desktop code.")), Is.True);

                    var listView = FindSingleControl<ListView>(form);
                    listView.Items[1].Selected = true;
                    listView.Items[1].Focused = true;
                    await form.ActivateSelectedSubAgentAsync();

                    form.HandleAppServerEvent(new AppServerEventArgs("turn_started", "started", threadId: "child-thread-2", turnId: "turn-2"));
                    form.HandleAppServerEvent(new AppServerEventArgs("reasoning_completed", "**Second task** Checking Android code.", threadId: "child-thread-2", turnId: "turn-2", itemId: "reason-2"));
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(activity.GetReadableItems().Any(item => item.Contains("Second task: Checking Android code.")), Is.True);
                    Assert.That(activity.GetReadableItems().Any(item => item.Contains("First task: Checking desktop code.")), Is.False);

                    listView.Items[0].Selected = true;
                    listView.Items[0].Focused = true;
                    await form.ActivateSelectedSubAgentAsync();

                    Assert.That(activity.GetReadableItems().Any(item => item.Contains("First task: Checking desktop code.")), Is.True);
                    Assert.That(activity.GetReadableItems().Any(item => item.Contains("Second task: Checking Android code.")), Is.False);
                }
            }
        }

        [Test]
        public async Task SendCurrentInputUsesAttachmentAwareWorkbenchRequest()
        {
            using (UseCulture("en-US"))
            {
                ChatWorkbenchSendRequest capturedRequest = null;
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                operations.SendAsync = (session, request) =>
                {
                    capturedRequest = request;
                    return Task.FromResult(new ChatWorkbenchSendResult(true, true, "sent"));
                };
                var subAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { subAgent },
                    operations))
                {
                    await ShowAndActivateSelectedSubAgentAsync(form);

                    var workbench = FindSingleControl<ChatWorkbenchControl>(form);
                    workbench.SetInputText("inspect ");
                    workbench.AddPendingAttachments(new[]
                    {
                        new PendingAttachmentItem(
                            @"E:\Repo\a.txt",
                            "a.txt",
                            false,
                            AttachmentKind.File,
                            "[File: a.txt]",
                            null,
                            null)
                    });
                    await workbench.SendCurrentInputAsync();

                    Assert.That(capturedRequest, Is.Not.Null);
                    Assert.That(capturedRequest.InputItems.Select(item => item.Type), Is.EqualTo(new[] { "text", "text" }));
                    Assert.That(capturedRequest.InputItems[1].Text, Is.EqualTo(@"E:\Repo\a.txt"));
                }
            }
        }

        [Test]
        public void RefreshSubAgentsWithSameSessionsDoesNotRebuildFocusedList()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var firstSubAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");
                var secondSubAgent = CreateSubAgent("child-2", "child-thread-2", "Babbage");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { firstSubAgent, secondSubAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    var listView = FindSingleControl<ListView>(form);
                    listView.Focus();
                    WaitForCondition(() => listView.Focused);
                    var firstItem = listView.Items[0];
                    var renamedFirstSubAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");
                    renamedFirstSubAgent.Title = "Updated title";

                    form.RefreshSubAgents(new List<ChatSession> { renamedFirstSubAgent, secondSubAgent });
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(listView.Items[0], Is.SameAs(firstItem));
                    Assert.That(listView.Items[0].SubItems[2].Text, Does.Contain("Updated title"));
                    Assert.That(listView.Focused, Is.True);
                }
            }
        }

        [Test]
        public void TabFromFocusedSubAgentListMovesToMessages()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var subAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { subAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    var listView = FindSingleControl<ListView>(form);
                    listView.Focus();
                    WaitForCondition(() => listView.Focused);

                    Assert.That(InvokeProcessCmdKey(form, Keys.Tab), Is.True);
                    WaitForCondition(() => FindSingleControl<ChatHistoryListBox>(form).Focused);
                }
            }
        }

        [Test]
        public void ShiftTabFromFocusedSubAgentListMovesToActivity()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var subAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { subAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    var listView = FindSingleControl<ListView>(form);
                    listView.Focus();
                    WaitForCondition(() => listView.Focused);

                    Assert.That(InvokeProcessCmdKey(form, Keys.Shift | Keys.Tab), Is.True);
                    WaitForCondition(() => FindSingleControl<ActivityVirtualBrowseControl>(form).Focused);
                }
            }
        }

        [Test]
        public void ShiftTabFromFocusedActivityMovesBackToWorkbenchControl()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var subAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { subAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    var activity = FindSingleControl<ActivityVirtualBrowseControl>(form);
                    activity.Focus();
                    WaitForCondition(() => activity.Focused);

                    Assert.That(InvokeProcessCmdKey(form, Keys.Shift | Keys.Tab), Is.True);
                    WaitForCondition(() => FindSingleControl<AttachmentComposerTextBox>(form).Focused);
                    Assert.That(FindSingleControl<ListView>(form).Focused, Is.False);
                }
            }
        }

        [Test]
        public void AltLFocusesActivityList()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                var subAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { subAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    FindSingleControl<AttachmentComposerTextBox>(form).Focus();
                    WaitForCondition(() => FindSingleControl<AttachmentComposerTextBox>(form).Focused);

                    Assert.That(InvokeProcessMnemonic(form, 'l'), Is.True);
                    WaitForCondition(() => FindSingleControl<ActivityVirtualBrowseControl>(form).Focused);
                    Assert.That(
                        GetLabels(form).Any(label => string.Equals(label.AccessibleName, "Activity", StringComparison.Ordinal) &&
                                                     label.Text.Contains("&L")),
                        Is.True);
                }
            }
        }

        [Test]
        public void SubAgentWorkbenchForwardsSpecificAttachmentCallbacksToChatWorkbench()
        {
            using (UseCulture("en-US"))
            {
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                operations.WorkbenchCallbacks = new ChatWorkbenchHostCallbacks
                {
                    SelectImageAttachmentsAsync = () => Task.FromResult((IReadOnlyList<PendingAttachmentItem>)new PendingAttachmentItem[0]),
                    CanPasteAttachments = () => true,
                    PasteAttachmentsAsync = () => Task.FromResult((IReadOnlyList<PendingAttachmentItem>)new[] { CreateFileAttachment() })
                };
                var subAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { subAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    var workbench = FindSingleControl<ChatWorkbenchControl>(form);

                    Assert.That(workbench.HasAttachmentActions, Is.True);
                    Assert.That(FindButton(form, "Insert").Enabled, Is.True);
                    Assert.That(workbench.PasteAttachmentsFromHostAsync().GetAwaiter().GetResult(), Is.EqualTo(1));
                    Assert.That(workbench.PendingAttachments, Has.Count.EqualTo(1));
                }
            }
        }

        [Test]
        public void SubAgentListEnterMarksKeyHandledAndMovesFocusBeforeAsyncActivationCompletes()
        {
            using (UseCulture("en-US"))
            {
                var pendingRead = new TaskCompletionSource<AppServerThreadDetails>();
                var operations = CreateOperations(
                    new List<string>(),
                    threadId => pendingRead.Task);
                var firstSubAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");
                var secondSubAgent = CreateSubAgent("child-2", "child-thread-2", "Babbage");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { firstSubAgent, secondSubAgent },
                    operations))
                {
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    Assert.That(form.ActiveSession, Is.Null);

                    var listView = FindSingleControl<ListView>(form);
                    Assert.That(listView.ContextMenu, Is.Not.Null);
                    Assert.That(listView.ContextMenu.MenuItems[0].Text, Is.EqualTo("Open(&O)"));
                    Assert.That(listView.ContextMenu.MenuItems[1].Text, Is.EqualTo("-"));
                    Assert.That(listView.ContextMenu.MenuItems[2].Text, Is.EqualTo("Delete(&D)"));
                    Assert.That(listView.ContextMenu.MenuItems[3].Text, Is.EqualTo("Delete All(&A)"));
                    listView.Items[1].Selected = true;
                    listView.Items[1].Focused = true;
                    System.Windows.Forms.Application.DoEvents();

                    var args = new KeyEventArgs(Keys.Enter);
                    InvokeSubAgentListKeyDown(form, listView, args);

                    Assert.That(args.SuppressKeyPress, Is.True);
                    Assert.That(args.Handled, Is.True);
                    WaitForCondition(() => FindSingleControl<ChatHistoryListBox>(form).Focused);

                    pendingRead.SetResult(new AppServerThreadDetails
                    {
                        ThreadId = "child-thread-2"
                    });
                    WaitForCondition(() => form.ActiveSession == secondSubAgent);
                }
            }
        }

        [Test]
        public void OpenContextMenuItemActivatesSelectedSubAgent()
        {
            using (UseCulture("en-US"))
            {
                var pendingRead = new TaskCompletionSource<AppServerThreadDetails>();
                var operations = CreateOperations(
                    new List<string>(),
                    threadId => pendingRead.Task);
                var firstSubAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");
                var secondSubAgent = CreateSubAgent("child-2", "child-thread-2", "Babbage");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { firstSubAgent, secondSubAgent },
                    operations))
                {
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();

                    var listView = FindSingleControl<ListView>(form);
                    listView.Items[1].Selected = true;
                    listView.Items[1].Focused = true;
                    System.Windows.Forms.Application.DoEvents();

                    listView.ContextMenu.MenuItems[0].PerformClick();
                    WaitForCondition(() => FindSingleControl<ChatHistoryListBox>(form).Focused);

                    pendingRead.SetResult(new AppServerThreadDetails
                    {
                        ThreadId = "child-thread-2"
                    });
                    WaitForCondition(() => form.ActiveSession == secondSubAgent);
                }
            }
        }

        [Test]
        public void DeleteKeyPermanentlyDeletesSelectedSubAgent()
        {
            using (UseCulture("en-US"))
            {
                var deletedSessions = new List<ChatSession>();
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                operations.ConfirmDelete = (sessions, isDeleteAll) => true;
                operations.DeleteAsync = sessions =>
                {
                    deletedSessions.AddRange(sessions);
                    return Task.CompletedTask;
                };
                var firstSubAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");
                var secondSubAgent = CreateSubAgent("child-2", "child-thread-2", "Babbage");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { firstSubAgent, secondSubAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    var listView = FindSingleControl<ListView>(form);
                    listView.Items[1].Selected = true;
                    listView.Items[1].Focused = true;
                    System.Windows.Forms.Application.DoEvents();

                    var args = new KeyEventArgs(Keys.Delete);
                    InvokeSubAgentListKeyDown(form, listView, args);
                    WaitForCondition(() => deletedSessions.Count == 1);

                    Assert.That(args.SuppressKeyPress, Is.True);
                    Assert.That(args.Handled, Is.True);
                    Assert.That(deletedSessions[0], Is.SameAs(secondSubAgent));
                    Assert.That(listView.Items, Has.Count.EqualTo(1));
                    Assert.That(listView.Items[0].Tag, Is.SameAs(firstSubAgent));
                }
            }
        }

        [Test]
        public void CtrlDeletePermanentlyDeletesAllSubAgents()
        {
            using (UseCulture("en-US"))
            {
                var deletedSessions = new List<ChatSession>();
                var readThreadIds = new List<string>();
                var operations = CreateOperations(readThreadIds);
                operations.ConfirmDelete = (sessions, isDeleteAll) => true;
                operations.DeleteAsync = sessions =>
                {
                    deletedSessions.AddRange(sessions);
                    return Task.CompletedTask;
                };
                var firstSubAgent = CreateSubAgent("child-1", "child-thread-1", "Ampere");
                var secondSubAgent = CreateSubAgent("child-2", "child-thread-2", "Babbage");

                using (var form = new SubAgentWorkbenchForm(
                    new ChatSession { Id = "main", ThreadId = "parent-thread", Title = "BoyRemote" },
                    new List<ChatSession> { firstSubAgent, secondSubAgent },
                    operations))
                {
                    ShowAndActivateSelectedSubAgent(form);

                    var listView = FindSingleControl<ListView>(form);
                    listView.Focus();
                    WaitForCondition(() => listView.Focused);

                    var args = new KeyEventArgs(Keys.Control | Keys.Delete);
                    InvokeSubAgentListKeyDown(form, listView, args);
                    WaitForCondition(() => deletedSessions.Count == 2);

                    Assert.That(args.SuppressKeyPress, Is.True);
                    Assert.That(args.Handled, Is.True);
                    Assert.That(deletedSessions, Is.EqualTo(new[] { firstSubAgent, secondSubAgent }));
                    Assert.That(listView.Items, Is.Empty);
                    Assert.That(form.ActiveSession, Is.Null);
                }
            }
        }

        private static async Task ShowAndActivateSelectedSubAgentAsync(SubAgentWorkbenchForm form)
        {
            form.Show();
            System.Windows.Forms.Application.DoEvents();
            await form.ActivateSelectedSubAgentAsync();
            WaitForCondition(() => form.ActiveSession != null);
        }

        private static void ShowAndActivateSelectedSubAgent(SubAgentWorkbenchForm form)
        {
            ShowAndActivateSelectedSubAgentAsync(form).GetAwaiter().GetResult();
        }

        private static SubAgentWorkbenchOperations CreateOperations(
            List<string> readThreadIds,
            Func<string, Task<AppServerThreadDetails>> readThreadAsync = null)
        {
            var displayBuilder = new SessionHistoryDisplayBuilder(
                "System",
                "Error",
                session => "model",
                CreateHistoryItem,
                ResolveCategory);
            return new SubAgentWorkbenchOperations
            {
                Workspace = new WorkspaceInfo { Id = "workspace", Name = "Workspace", RootPath = @"E:\Repo" },
                HistoryLoadWorkflow = new SessionHistoryLoadWorkflow(
                    new SessionHistoryStateStore(),
                    displayBuilder,
                    readThreadAsync ?? (threadId =>
                    {
                        readThreadIds.Add(threadId);
                        return Task.FromResult(new AppServerThreadDetails
                        {
                            ThreadId = threadId,
                            TurnCount = 1,
                            Messages =
                            {
                                new AppServerThreadMessage
                                {
                                    ThreadId = threadId,
                                    TurnId = "turn-1",
                                    Sender = "codex",
                                    Content = "history " + threadId
                                }
                            }
                        });
                    }),
                    _ => false,
                    _ => false),
                SendAsync = (session, request) => Task.FromResult(new ChatWorkbenchSendResult(true, true, "sent")),
                StopAsync = session => Task.CompletedTask,
                CanStop = session => false,
                PersistState = () => { }
            };
        }

        private static ChatSession CreateSubAgent(string id, string threadId, string nickname)
        {
            return new ChatSession
            {
                Id = id,
                ThreadId = threadId,
                Title = nickname + " session",
                IsSubAgent = true,
                ParentThreadId = "parent-thread",
                SubAgentNickname = nickname,
                SubAgentRole = "default"
            };
        }

        private static ChatHistoryItem CreateHistoryItem(string sender, string content, ChatHistoryCategory category)
        {
            return new ChatHistoryItem
            {
                Sender = sender,
                Category = category,
                DisplayText = sender + ": " + content,
                FullText = sender + ": " + content,
                MarkdownText = content
            };
        }

        private static PendingAttachmentItem CreateFileAttachment()
        {
            return new PendingAttachmentItem(
                @"E:\Repo\a.txt",
                "a.txt",
                false,
                AttachmentKind.File,
                "[File: a.txt]",
                null,
                null);
        }

        private static ChatHistoryCategory ResolveCategory(string sender)
        {
            return string.Equals(sender, "codex", StringComparison.OrdinalIgnoreCase)
                ? ChatHistoryCategory.Conversation
                : ChatHistoryCategory.System;
        }

        private static Button FindButton(Control root, string accessibleName)
        {
            return FindButtons(root)
                .Single(button => string.Equals(button.AccessibleName, accessibleName, StringComparison.Ordinal));
        }

        private static void InvokeSubAgentListKeyDown(SubAgentWorkbenchForm form, ListView listView, KeyEventArgs args)
        {
            typeof(SubAgentWorkbenchForm)
                .GetMethod("SubAgentListView_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { listView, args });
        }

        private static bool InvokeProcessCmdKey(SubAgentWorkbenchForm form, Keys keyData)
        {
            var message = new Message();
            var parameters = new object[] { message, keyData };
            return (bool)typeof(SubAgentWorkbenchForm)
                .GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, parameters);
        }

        private static bool InvokeProcessMnemonic(SubAgentWorkbenchForm form, char charCode)
        {
            return (bool)typeof(SubAgentWorkbenchForm)
                .GetMethod("ProcessMnemonic", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(form, new object[] { charCode });
        }

        private static IEnumerable<Button> FindButtons(Control root)
        {
            return root
                .Controls
                .Cast<Control>()
                .SelectMany(Flatten)
                .OfType<Button>();
        }

        private static IEnumerable<Label> GetLabels(Control root)
        {
            return root
                .Controls
                .Cast<Control>()
                .SelectMany(Flatten)
                .OfType<Label>();
        }

        private static T FindSingleControl<T>(Control root)
            where T : Control
        {
            return root
                .Controls
                .Cast<Control>()
                .SelectMany(Flatten)
                .OfType<T>()
                .Single();
        }

        private static IEnumerable<Control> Flatten(Control control)
        {
            yield return control;

            foreach (Control child in control.Controls)
            {
                foreach (var nested in Flatten(child))
                {
                    yield return nested;
                }
            }
        }

        private static void WaitForCondition(Func<bool> predicate)
        {
            for (var i = 0; i < 100; i++)
            {
                System.Windows.Forms.Application.DoEvents();
                if (predicate())
                {
                    return;
                }

                Thread.Sleep(10);
            }

            Assert.Fail("Condition was not met.");
        }

        private static IDisposable UseCulture(string cultureName)
        {
            return new CultureScope(cultureName);
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
