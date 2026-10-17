using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class MainFormSubAgentBusyStateTests
    {
        [TestCase("idle")]
        [TestCase("notLoaded")]
        [TestCase("systemError")]
        [TestCase(null)]
        [TestCase("futureStatus")]
        public void MessageToCompletedAgentDoesNotLeaveParentWorking(string observedStatus)
        {
            WithForm((form, client) =>
            {
                client.Status = "active";
                Activity(form, client, "started");
                Assert.That(Invoke(form, "IsSessionWorking", Field(form, "_currentSession")), Is.True);

                client.Status = "idle";
                Activity(form, client, "completed");
                Assert.That(Invoke(form, "IsSessionWorking", Field(form, "_currentSession")), Is.False);

                // Recorded sequence: completed, then send_message (no new turn), then parent finishes.
                client.Status = observedStatus;
                Activity(form, client, "interacted");
                Event(form, client, "turn_completed", "parent");
                Assert.That(Invoke(form, "IsSessionWorking", Field(form, "_currentSession")), Is.False,
                    "Sending a message to a completed agent must not leave its parent working.");
                Assert.That(((Dictionary<string, string>)Field(form, "_subAgentStatuses"))["child"], Is.EqualTo("completed"));
                Assert.That(form.Text, Does.Not.Contain(UiLocalization.Text("MainForm.WindowStatus.Working", "Working")));
            });
        }

        [Test]
        public void RealFollowupKeepsParentWorkingUntilChildCompletes()
        {
            WithForm((form, client) =>
            {
                client.Status = "idle";
                Activity(form, client, "completed");
                client.Status = "active";
                Activity(form, client, "interacted");
                Event(form, client, "turn_completed", "parent");
                Assert.That(Invoke(form, "IsSessionWorking", Field(form, "_currentSession")), Is.True);
                Assert.That(form.Text, Does.Contain(UiLocalization.Text("MainForm.WindowStatus.Working", "Working")));
                client.Status = "idle";
                Activity(form, client, "completed");
                Assert.That(Invoke(form, "IsSessionWorking", Field(form, "_currentSession")), Is.False);
            });
        }

        [TestCase("turn_started", "child", "idle", true)]
        [TestCase("turn_completed", "child", "active", false)]
        [TestCase("turn_completed", "parent", "active", true)]
        [TestCase("thread_status_changed", "child", "idle", true)]
        [TestCase("thread_status_changed", "child", "active", false)]
        public void PendingReadRespectsNewerEventsForTheSameThread(string eventType, string eventThread, string snapshotStatus, bool working)
        {
            WithForm((form, client) =>
            {
                client.Status = working ? "idle" : "active";
                Activity(form, client, working ? "completed" : "started");
                client.PendingRead = new TaskCompletionSource<AppServerThreadDetails>();
                var read = (Task<AppServerThreadDetails>)Invoke(form, "ReadSubAgentThreadAsync", "child");
                if (eventType == "thread_status_changed")
                    Invoke(form, "AppServerClientOnEventReceived", client,
                        new AppServerEventArgs(eventType, eventType, threadId: eventThread,
                            threadStatus: new AppServerThreadStatus { Type = working ? "active" : "idle" }));
                else Event(form, client, eventType, eventThread);
                client.PendingRead.SetResult(new AppServerThreadDetails
                {
                    ThreadId = "child", ThreadStatus = new AppServerThreadStatus { Type = snapshotStatus }
                });
                var timer = Stopwatch.StartNew();
                while (!read.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(5))
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(1);
                }
                Assert.That(read.IsCompleted, Is.True);
                read.GetAwaiter().GetResult();
                Assert.That(Invoke(form, "IsSessionWorking", Field(form, "_currentSession")), Is.EqualTo(working));
            });
        }

        private static void Event(MainForm form, Client client, string eventType, string threadId)
        {
            Invoke(form, "AppServerClientOnEventReceived", client,
                new AppServerEventArgs(eventType, eventType, threadId: threadId, turnId: "turn"));
        }

        private static void Activity(MainForm form, Client client, string kind)
        {
            Invoke(form, "AppServerClientOnEventReceived", client,
                new AppServerEventArgs("subagent_activity", kind, threadId: "parent",
                    subAgentActivity: new AppServerSubAgentActivity { AgentThreadId = "child", Kind = kind }));
        }

        private static void WithForm(Action<MainForm, Client> check)
        {
            var paths = new AppConfigService(Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "subagent-busy-" + Guid.NewGuid().ToString("N")));
            var config = new JsonAppConfigStore(paths);
            var runtime = new CodexRuntimeManager(paths);
            using (var client = new Client(runtime, config))
            using (var form = new MainForm(client, new JsonAppStateStore(paths), config, paths, new Reader(), runtime))
            {
                try
                {
                    var handle = form.Handle;
                    var parent = new ChatSession { Id = "parent", ThreadId = "parent" };
                    var sessions = (List<ChatSession>)Field(form, "_sessions");
                    sessions.Add(parent);
                    sessions.Add(new ChatSession { Id = "child", ThreadId = "child", ParentThreadId = "parent", IsSubAgent = true });
                    typeof(MainForm).GetField("_currentSession", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, parent);
                    check(form, client);
                }
                finally
                {
                    // Do not run FormClosed: it also stops the user's authentication backend.
                    Microsoft.Win32.SystemEvents.UserPreferenceChanged -= (Microsoft.Win32.UserPreferenceChangedEventHandler)
                        Delegate.CreateDelegate(typeof(Microsoft.Win32.UserPreferenceChangedEventHandler), form, "OnSystemUserPreferenceChanged");
                    Invoke(form, "DisposeThreadSyncTimersOnExit");
                    Invoke(form, "DisposeLocalSyncTimersOnExit");
                    Invoke(form, "DisposeRateLimitAutoResetTimerOnExit");
                    Invoke(form, "DisposeRemoteHostService");
                    Invoke(form, "DisposeTrayIconOnExit");
                }
            }
        }

        private static object Field(object target, string name)
        {
            return typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            return typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        }

        private sealed class Client : AppServerClient, IAppServerClient
        {
            public string Status { get; set; }
            public TaskCompletionSource<AppServerThreadDetails> PendingRead { get; set; }
            public Client(CodexRuntimeManager runtime, IAppConfigStore config) : base(runtime, config) { }
            public new Task<AppServerThreadDetails> ReadThreadAsync(string threadId)
            {
                if (PendingRead != null) return PendingRead.Task;
                return Task.FromResult(new AppServerThreadDetails
                {
                    ThreadId = threadId,
                    ThreadStatus = new AppServerThreadStatus { Type = Status }
                });
            }
        }

        private sealed class Reader : IScreenReaderService
        {
            public bool IsAvailable => false;
            public bool IsInitialized => false;
            public string LastError => null;
            public bool EnsureInitialized() => false;
            public bool IsReaderRunning() => false;
            public int GetReaderState() => 0;
            public bool Speak(string text, bool useSlave, bool append, bool allowBreak) => false;
            public bool Stop(bool useSlave) => false;
            public void Dispose() { }
        }
    }
}
