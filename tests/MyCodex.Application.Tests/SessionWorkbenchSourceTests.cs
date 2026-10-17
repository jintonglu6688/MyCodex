using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionWorkbenchSourceTests
    {
        [Test]
        public void WorkbenchForm_HandlesOnlyItsOwnThreadEventsAndDoesNotAddScreenReaderSpeech()
        {
            var source = File.ReadAllText(GetFormSourcePath("SessionWorkbenchForm.cs"));

            Assert.That(source, Does.Contain("!string.Equals(Session.ThreadId, e.ThreadId, StringComparison.OrdinalIgnoreCase)"));
            Assert.That(source, Does.Contain("if (string.IsNullOrWhiteSpace(e.ThreadId))"));
            Assert.That(source, Does.Not.Contain("AnnounceStatus"));
            Assert.That(source, Does.Not.Contain(".Speak("));
        }

        [Test]
        public void MainForm_ProvidesContextMenuAndCtrlEnterEntryPointForDetachedWindows()
        {
            var sessionSource = File.ReadAllText(GetFormSourcePath("MainForm.SessionWorkbenches.cs"));
            var keyboardSource = File.ReadAllText(GetFormSourcePath("MainForm.ListKeyboard.cs"));

            Assert.That(sessionSource, Does.Contain("MainForm.Session.OpenInWindow"));
            Assert.That(sessionSource, Does.Contain("form.Show();"));
            Assert.That(keyboardSource, Does.Contain("e.Control && e.KeyCode == Keys.Enter"));
            Assert.That(keyboardSource, Does.Contain("OpenSelectedSessionWorkbench();"));
        }

        [Test]
        public void DetachedTerminal_KeepsSessionBoundProcessAndUpdatesTheFormOnItsUiThread()
        {
            var source = File.ReadAllText(GetFormSourcePath("SessionCommandTerminalHost.cs"));

            Assert.That(source, Does.Contain("SessionId = _session.Id"));
            Assert.That(source, Does.Contain("WorkingDirectory = _workspace.RootPath"));
            Assert.That(source, Does.Contain("form.BeginInvoke"));
        }

        [Test]
        public void DetachedManualBrowse_KeepsOneDynamicContextPerSessionUntilTheSessionCloses()
        {
            var sessionSource = File.ReadAllText(GetFormSourcePath("MainForm.SessionManualBrowse.cs"));
            var terminalSource = File.ReadAllText(GetFormSourcePath("SessionCommandTerminalHost.cs"));

            Assert.That(sessionSource, Does.Contain("Dictionary<string, ManualBrowseContext> _sessionManualBrowseContexts"));
            Assert.That(sessionSource, Does.Contain("GetOrCreateSessionManualBrowseContext"));
            Assert.That(sessionSource, Does.Contain("HandleSessionTerminalScreenReaderHotkey"));
            Assert.That(sessionSource, Does.Not.Contain("ReleaseSessionManualBrowseContextIfUnused"));
            Assert.That(terminalSource, Does.Contain("ScreenReaderHotkeyPressed"));
            Assert.That(terminalSource, Does.Contain("GetReadableLines()"));
            Assert.That(terminalSource, Does.Contain("TerminalWindowClosed"));
        }

        private static string GetFormSourcePath(string fileName)
        {
            return Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.WinForms",
                "Forms",
                fileName));
        }
    }
}
