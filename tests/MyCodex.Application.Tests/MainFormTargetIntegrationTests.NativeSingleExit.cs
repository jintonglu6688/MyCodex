using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Infrastructure.Diagnostics;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class MainFormTargetIntegrationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void StateSaveFailureIsWrittenToAppLogWithoutAnySessions(bool recoveryRequired)
        {
            using (var fixture = new TargetFormFixture())
            {
                Invoke(fixture.Form, "CompleteTargetStartup");
                Assert.That(fixture.State.Load().Sessions, Is.Empty);
                var writeError = new IOException("Write denied. password=test-password https://test:test-secret@example.invalid ghp_testtoken");
                fixture.State.SaveError = recoveryRequired
                    ? new PersistenceRecoveryRequiredException("state.json", "state.json.bak", writeError, null)
                    : new InvalidOperationException("Runtime state write failed.", writeError);
                var listeners = Trace.Listeners.Cast<TraceListener>().ToArray();
                var autoFlush = Trace.AutoFlush;
                Trace.Listeners.Clear();
                try
                {
                    var path = SessionTraceLog.Configure(Path.Combine(fixture.Root, "logs"), DateTimeOffset.Now, 1);
                    var preparation = (Task<bool>)Invoke(fixture.Form, "PrepareTargetExitAsync", "save-failure-test", false);
                    PumpUntil(() => preparation.IsCompleted);
                    Assert.That(preparation.GetAwaiter().GetResult(), Is.False);
                    Assert.That(fixture.Form.Enabled, Is.True);
                    Assert.That(Get<bool>(fixture.Form, "_isExitRequested"), Is.False);
                    Trace.Flush();
                    string log;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream)) log = reader.ReadToEnd();
                    Assert.That(log, Does.Contain("[ERROR]").And.Contain("[TargetState]"));
                    Assert.That(log, Does.Contain("Stage=save-state").And.Contain("ExitPreparing=True"));
                    Assert.That(log, Does.Contain("IOException: Write denied.").And.Contain("StateStore.Save"));
                    Assert.That(log, Does.Contain("password=***").And.Contain("https://***:***@example.invalid"));
                    Assert.That(log, Does.Not.Contain("test-password").And.Not.Contain("test-secret").And.Not.Contain("ghp_testtoken"));
                }
                finally
                {
                    foreach (TraceListener listener in Trace.Listeners) listener.Dispose();
                    Trace.Listeners.Clear();
                    Trace.Listeners.AddRange(listeners);
                    Trace.AutoFlush = autoFlush;
                }
            }
        }

        [TestCase("Window", true, false, false)]
        [TestCase("Menu", true, false, false)]
        [TestCase("Tray", true, false, false)]
        [TestCase("Window", false, false, false)]
        [TestCase("Window", true, true, false)]
        [TestCase("Window", false, false, true)]
        [TestCase("Menu", false, false, true)]
        [TestCase("Tray", false, false, true)]
        [TestCase("HiddenTray", true, false, false)]
        [TestCase("HiddenTray", false, false, true)]
        [TestCase("Window", true, false, false, false)]
        [TestCase("Menu", true, false, false, false)]
        [TestCase("Tray", true, false, false, false)]
        [TestCase("Window", false, false, true, false)]
        [TestCase("Window", true, true, false, false)]
        public void NativeSingleTargetExitRespectsConfirmation(string entry, bool accept, bool failSave, bool pressEscape, bool startupComplete = true)
        {
            using (var fixture = new TargetFormFixture())
            using (var responder = new Timer { Interval = 10 })
            {
                // Keep the real close path, but do not initialize connections or stop user services.
                fixture.Form.Shown -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), fixture.Form, "OnShown");
                fixture.Form.Activated -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), fixture.Form, "OnModelConfigurationActivated");
                fixture.Form.FormClosed -= (FormClosedEventHandler)Delegate.CreateDelegate(typeof(FormClosedEventHandler), fixture.Form, "OnFormClosed");
                fixture.Form.ShowInTaskbar = false;
                fixture.Form.StartPosition = FormStartPosition.Manual;
                fixture.Form.Location = new Point(-2000, -2000);
                fixture.Form.Show();
                if (entry == "HiddenTray") fixture.Form.Hide();
                if (startupComplete) Invoke(fixture.Form, "CompleteTargetStartup");
                fixture.State.FailSave = failSave;
                var wasVisible = fixture.Form.Visible;
                var ownerHandle = wasVisible ? fixture.Form.Handle : IntPtr.Zero;
                var sawConfirmation = false;
                var hasOnlyYesNo = false;
                var escapeTimedOut = false;
                var elapsed = Stopwatch.StartNew();
                responder.Tick += (sender, args) =>
                {
                    EnumerateExitThreadWindows(GetExitCurrentThreadId(), (window, state) =>
                    {
                        var name = new StringBuilder(64);
                        GetExitWindowClass(window, name, name.Capacity);
                        if (name.ToString() != "#32770" || (wasVisible && GetExitWindow(window, 4) != ownerHandle)) return true;
                        sawConfirmation = true;
                        hasOnlyYesNo = GetExitDialogItem(window, 6) != IntPtr.Zero &&
                            GetExitDialogItem(window, 7) != IntPtr.Zero && GetExitDialogItem(window, 2) == IntPtr.Zero;
                        if (pressEscape && elapsed.ElapsedMilliseconds < 1000)
                        {
                            PostExitWindowMessage(window, 0x0100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
                            PostExitWindowMessage(window, 0x0101, new IntPtr((int)Keys.Escape), IntPtr.Zero);
                        }
                        else
                        {
                            // Answer No on timeout so a broken Escape path cannot hang the test.
                            escapeTimedOut = pressEscape;
                            PostExitWindowMessage(window, 0x0111, new IntPtr(accept ? 6 : 7), IntPtr.Zero);
                        }
                        return true;
                    }, IntPtr.Zero);
                    if (elapsed.ElapsedMilliseconds > 3000) responder.Stop();
                };
                responder.Start();
                if (entry == "Window") fixture.Form.Close();
                else if (entry == "Menu") Invoke(fixture.Form, "OnExitMenuItemClick", null, EventArgs.Empty);
                else Invoke(fixture.Form, "ExitTrayMenuItem_Click", null, EventArgs.Empty);
                responder.Stop();
                System.Windows.Forms.Application.DoEvents();

                Assert.That(sawConfirmation, Is.True, "The test must answer the actual native exit confirmation.");
                Assert.That(hasOnlyYesNo, Is.True, "The exit confirmation must have only Yes and No buttons.");
                Assert.That(escapeTimedOut, Is.False, "Escape must dismiss the exit confirmation without a fallback click.");
                Assert.That(fixture.Form.IsDisposed, Is.EqualTo(accept && !failSave),
                    "Confirmed={0}; ExitRequested={1}; Preparing={2}; CleanupComplete={3}; Enabled={4}; Saves={5}; Status={6}",
                    accept, Get<bool>(fixture.Form, "_isExitRequested"),
                    typeof(MainForm).GetProperty("IsTargetExitPreparing", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture.Form),
                    Get<bool>(fixture.Form, "_targetExitCleanupComplete"), fixture.Form.Enabled,
                    fixture.State.SaveCalls, Get<ToolStripStatusLabel>(fixture.Form, "_statusLabel").Text);
                Assert.That(fixture.Client.ConnectCalls + fixture.Client.RestartCalls, Is.Zero);
                if (!accept || failSave) Assert.That(fixture.Form.Enabled, Is.True);
                if (pressEscape)
                {
                    Assert.That(fixture.Form.Visible, Is.EqualTo(wasVisible));
                    Assert.That(Get<bool>(fixture.Form, "_isExitRequested"), Is.False);
                    Assert.That(fixture.State.SaveCalls, Is.Zero);
                }
                if (failSave)
                {
                    Assert.That(Get<bool>(fixture.Form, "_isExitRequested"), Is.False);
                    Assert.That(Get<ToolStripStatusLabel>(fixture.Form, "_statusLabel").Text,
                        Is.EqualTo(UiLocalization.Text("MainForm.Exit.SaveFailed", "This target could not save its state. It remains running.")));
                }
            }
        }

        private delegate bool ExitWindowCallback(IntPtr window, IntPtr state);
        [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")] private static extern uint GetExitCurrentThreadId();
        [DllImport("user32.dll", EntryPoint = "EnumThreadWindows")] private static extern bool EnumerateExitThreadWindows(uint thread, ExitWindowCallback callback, IntPtr state);
        [DllImport("user32.dll", EntryPoint = "GetWindow")] private static extern IntPtr GetExitWindow(IntPtr window, uint command);
        [DllImport("user32.dll", EntryPoint = "GetDlgItem")] private static extern IntPtr GetExitDialogItem(IntPtr window, int id);
        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] private static extern int GetExitWindowClass(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostExitWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
