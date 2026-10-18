using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class MainFormAutomaticReadingTests
    {
        [Test]
        public void OptionsSave_WritesCompletePreferencesOnceAndKeepsDraftAfterFailure()
        {
            var paths = new AppConfigService(Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "options-save-" + Guid.NewGuid().ToString("N")));
            var store = new PreferenceStore
            {
                Snapshot = new AppConfigSnapshot { MainWindowHotKeyEnabled = false },
                FailSave = true
            };
            var runtime = new CodexRuntimeManager(paths);
            using (var client = new AppServerClient(runtime, store))
            using (var main = new MainForm(client, new JsonAppStateStore(paths), store, paths, new Reader(), runtime))
            using (var options = new OptionsForm(store.Snapshot, null))
            {
                try
                {
                    var handle = main.Handle;
                    var original = store.Snapshot;
                    var theme = (ComboBox)FormTestDriver.Field<object>(options, "_themeComboBox");
                    theme.SelectedIndex = 2;
                    ((CheckBox)FormTestDriver.Field<object>(options, "_automaticChatReadingCheckBox")).Checked = true;
                    options.ValidateSelection = () =>
                    {
                        typeof(OptionsForm).GetProperty("MainWindowHotKeyVirtualKey").SetValue(options, (int)Keys.F8);
                        var args = new object[] { options, false, null };
                        return (bool)typeof(MainForm).GetMethod("TrySaveOptionsLocalSettings",
                            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(main, args);
                    };
                    FormTestDriver.Invoke(options, "OkButton_Click", options, EventArgs.Empty);
                    Assert.That(store.SaveAttempts, Is.EqualTo(1));
                    Assert.That(options.DialogResult, Is.EqualTo(DialogResult.None));
                    Assert.That(FormTestDriver.Field<object>(main, "_appConfigSnapshot"), Is.SameAs(original));
                    Assert.That(original.UiThemePreference, Is.EqualTo("auto"));
                    Assert.That(original.EnableAutomaticChatReading, Is.False);
                    Assert.That(original.MainWindowHotKeyVirtualKey, Is.EqualTo((int)Keys.M));
                    Assert.That(((CheckBox)FormTestDriver.Field<object>(options, "_automaticChatReadingCheckBox")).Checked, Is.True);
                    store.FailSave = false;
                    FormTestDriver.Invoke(options, "OkButton_Click", options, EventArgs.Empty);
                    Assert.That(options.DialogResult, Is.EqualTo(DialogResult.OK));
                    Assert.That(store.SaveAttempts, Is.EqualTo(2), "The retry saves one complete snapshot, with no second write after closure.");
                    Assert.That(store.Snapshot.UiThemePreference, Is.EqualTo("dark"));
                    Assert.That(store.Snapshot.EnableAutomaticChatReading, Is.True);
                    Assert.That(store.Snapshot.MainWindowHotKeyEnabled, Is.False);
                    Assert.That(store.Snapshot.MainWindowHotKeyVirtualKey, Is.EqualTo((int)Keys.F8));
                }
                finally
                {
                    DisposeTestMainForm(main);
                }
            }
        }

        [Test]
        public void HiddenTargetReadsOnlySelectedIncomingThreadAndDisablingKeepsManualReading()
        {
            var paths = new AppConfigService(Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "automatic-reading-" + Guid.NewGuid().ToString("N")));
            var config = new JsonAppConfigStore(paths);
            config.Save(new AppConfigSnapshot
            {
                EnableAutomaticChatReading = true,
                ApprovalRequestNotificationMode = NotificationPromptMode.Speech
            });
            var runtime = new CodexRuntimeManager(paths);
            var reader = new Reader();
            using (var client = new AppServerClient(runtime, config))
            using (var form = new MainForm(client, new JsonAppStateStore(paths), config, paths, reader, runtime))
            {
                try
                {
                    FormTestDriver.SetField(form, "_currentSession", new ChatSession { Id = "selected", ThreadId = "selected" });
                    var item = ChatHistoryItemFactory.Create("Codex", "new reply");
                    Assert.That(form.Visible, Is.False);
                    FormTestDriver.Invoke(form, "TryAutoReadIncomingMessage", item, "other-thread");
                    Assert.That(reader.Texts, Is.Empty);
                    FormTestDriver.Invoke(form, "TryAutoReadIncomingMessage", item, "selected");
                    Assert.That(reader.Texts, Has.Count.EqualTo(1));
                    FormTestDriver.Invoke(form, "HandleApprovalRequestedNotification", new AppServerEventArgs(
                        "approval_requested", "Approval needed", threadId: "selected", requestId: "test-approval"));
                    Assert.That(reader.Texts, Has.Count.EqualTo(1), "Notifications must not add speech to reply reading.");
                    var stopCount = reader.StopCount;
                    FormTestDriver.Invoke(form, "ToggleAutomaticChatReading");
                    Assert.That(config.Load().EnableAutomaticChatReading, Is.False);
                    Assert.That(reader.StopCount, Is.EqualTo(stopCount));
                    FormTestDriver.Invoke(form, "TryAutoReadIncomingMessage", item, "selected");
                    Assert.That(reader.Texts, Has.Count.EqualTo(1));
                    Assert.That(FormTestDriver.Invoke(form, "SpeakManualTextWithScreenReader", "manual navigation"), Is.True);
                    Assert.That(reader.Texts, Has.Count.EqualTo(2));
                }
                finally
                {
                    DisposeTestMainForm(form);
                }
            }
        }

        [Test]
        public void TwoHiddenTargetsReadTheirSelectedRepliesIndependently()
        {
            var forms = new List<MainForm>();
            var clients = new List<AppServerClient>();
            var readers = new List<Reader>();
            try
            {
                for (var index = 0; index < 2; index++)
                {
                    var paths = new AppConfigService(Path.Combine(TestContext.CurrentContext.WorkDirectory,
                        "target-reading-" + Guid.NewGuid().ToString("N")));
                    var config = new JsonAppConfigStore(paths);
                    config.Save(new AppConfigSnapshot { EnableAutomaticChatReading = true });
                    var runtime = new CodexRuntimeManager(paths);
                    var client = new AppServerClient(runtime, config);
                    var reader = new Reader();
                    var form = new MainForm(client, new JsonAppStateStore(paths), config, paths, reader, runtime);
                    clients.Add(client);
                    readers.Add(reader);
                    forms.Add(form);
                    FormTestDriver.SetField(form, "_currentSession", new ChatSession { Id = "selected", ThreadId = "selected" });
                }

                for (var index = 0; index < forms.Count; index++)
                {
                    Assert.That(forms[index].Visible, Is.False);
                    FormTestDriver.Invoke(forms[index], "TryAutoReadIncomingMessage", ChatHistoryItemFactory.Create("Codex", "reply " + index), "selected");
                    Assert.That(readers[index].Texts, Has.Count.EqualTo(1));
                    Assert.That(readers[index].Texts[0], Does.Contain("reply " + index));
                }
            }
            finally
            {
                foreach (var form in forms)
                {
                    DisposeTestMainForm(form);
                    form.Dispose();
                }
                foreach (var client in clients) client.Dispose();
            }
        }

        private static void DisposeTestMainForm(MainForm form)
        {
            // FormClosed would also affect the real authentication backend; tests never start a runtime.
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= (Microsoft.Win32.UserPreferenceChangedEventHandler)
                Delegate.CreateDelegate(typeof(Microsoft.Win32.UserPreferenceChangedEventHandler), form, "OnSystemUserPreferenceChanged");
            FormTestDriver.Invoke(form, "DisposeThreadSyncTimersOnExit");
            FormTestDriver.Invoke(form, "DisposeLocalSyncTimersOnExit");
            FormTestDriver.Invoke(form, "DisposeRateLimitAutoResetTimerOnExit");
            FormTestDriver.Invoke(form, "DisposeRemoteHostService");
            FormTestDriver.Invoke(form, "DisposeTrayIconOnExit");
        }

        private sealed class PreferenceStore : IAppConfigStore
        {
            public AppConfigSnapshot Snapshot { get; set; }
            public bool FailSave { get; set; }
            public int SaveAttempts { get; private set; }
            public AppConfigSnapshot Load() => Snapshot;
            public void Save(AppConfigSnapshot snapshot)
            {
                SaveAttempts++;
                if (FailSave)
                {
                    throw new IOException("Preference write failed.");
                }

                Snapshot = snapshot;
            }
        }

        private sealed class Reader : IScreenReaderService
        {
            public List<string> Texts { get; } = new List<string>();
            public int StopCount { get; private set; }
            public bool IsAvailable => true;
            public bool IsInitialized => true;
            public string LastError => null;
            public bool EnsureInitialized() => true;
            public bool IsReaderRunning() => true;
            public int GetReaderState() => 0;
            public bool Speak(string text, bool useSlave, bool append, bool allowBreak) { Texts.Add(text); return true; }
            public bool Stop(bool useSlave) { StopCount++; return true; }
            public void Dispose() { }
        }
    }
}
