using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class MainFormArchiveNotificationTests
    {
        [Test]
        public void ArchiveNotificationRemovesOnlyConfirmedThreadAndPersistsRemainingSessions()
        {
            var paths = new AppConfigService(Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "archive-notification-" + Guid.NewGuid().ToString("N")));
            var config = new JsonAppConfigStore(paths);
            var store = new JsonAppStateStore(paths);
            var runtime = new CodexRuntimeManager(paths);
            using (var client = new AppServerClient(runtime, config))
            using (var form = new MainForm(client, store, config, paths, new Reader(), runtime))
            {
                try
                {
                    var handle = form.Handle;
                    var sessions = (List<ChatSession>)typeof(MainForm).GetField("_sessions",
                        BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    sessions.Add(new ChatSession { Id = "root", ThreadId = "root" });
                    sessions.Add(new ChatSession { Id = "child", ThreadId = "child", ParentThreadId = "root", IsSubAgent = true });
                    sessions.Add(new ChatSession { Id = "fork", ThreadId = "fork", ForkSourceThreadId = "root", IsFork = true });

                    FormTestDriver.Invoke(form, "AppServerClientOnEventReceived", client, new AppServerEventArgs("thread_archived", "archived", threadId: "root"));
                    Assert.That(sessions.Select(session => session.ThreadId), Is.EquivalentTo(new[] { "child", "fork" }));
                    Assert.That(store.Load().Sessions.Select(session => session.ThreadId), Is.EquivalentTo(new[] { "child", "fork" }));

                    FormTestDriver.Invoke(form, "AppServerClientOnEventReceived", client, new AppServerEventArgs("thread_archived", "archived", threadId: "child"));
                    Assert.That(sessions.Select(session => session.ThreadId), Is.EqualTo(new[] { "fork" }));
                }
                finally
                {
                    // Do not run FormClosed: it also stops the user's authentication backend.
                    Microsoft.Win32.SystemEvents.UserPreferenceChanged -= (Microsoft.Win32.UserPreferenceChangedEventHandler)
                        Delegate.CreateDelegate(typeof(Microsoft.Win32.UserPreferenceChangedEventHandler), form, "OnSystemUserPreferenceChanged");
                    FormTestDriver.Invoke(form, "DisposeThreadSyncTimersOnExit");
                    FormTestDriver.Invoke(form, "DisposeLocalSyncTimersOnExit");
                    FormTestDriver.Invoke(form, "DisposeRateLimitAutoResetTimerOnExit");
                    FormTestDriver.Invoke(form, "DisposeRemoteHostService");
                    FormTestDriver.Invoke(form, "DisposeTrayIconOnExit");
                }
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
