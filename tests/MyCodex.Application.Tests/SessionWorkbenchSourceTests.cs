using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionWorkbenchSourceTests
    {
        [Category(SourceGuardCategory.Architecture)]
        [Test]
        public void WorkbenchForm_HandlesOnlyItsOwnThreadEventsAndDoesNotAddScreenReaderSpeech()
        {
            var source = RepositorySource.ReadSourceFile("SessionWorkbenchForm.cs");

            Assert.That(source, Does.Contain("!string.Equals(Session.ThreadId, e.ThreadId, StringComparison.OrdinalIgnoreCase)"));
            Assert.That(source, Does.Contain("if (string.IsNullOrWhiteSpace(e.ThreadId))"));
            Assert.That(source, Does.Not.Contain("AnnounceStatus"));
            Assert.That(source, Does.Not.Contain(".Speak("));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void MainForm_ProvidesContextMenuAndCtrlEnterEntryPointForDetachedWindows()
        {
            var sessionSource = RepositorySource.ReadSourceFile("MainForm.SessionWorkbenches.cs");
            var keyboardSource = RepositorySource.ReadSourceFile("MainForm.ListKeyboard.cs");

            Assert.That(sessionSource, Does.Contain("MainForm.Session.OpenInWindow"));
            Assert.That(sessionSource, Does.Contain("form.Show();"));
            Assert.That(keyboardSource, Does.Contain("e.Control && e.KeyCode == Keys.Enter"));
            Assert.That(keyboardSource, Does.Contain("OpenSelectedSessionWorkbench();"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void DetachedTerminal_KeepsSessionBoundProcessAndUpdatesTheFormOnItsUiThread()
        {
            var source = RepositorySource.ReadSourceFile("SessionCommandTerminalHost.cs");

            Assert.That(source, Does.Contain("SessionId = _session.Id"));
            Assert.That(source, Does.Contain("WorkingDirectory = _workspace.RootPath"));
            Assert.That(source, Does.Contain("form.BeginInvoke"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void DetachedManualBrowse_KeepsOneDynamicContextPerSessionUntilTheSessionCloses()
        {
            var sessionSource = RepositorySource.ReadSourceFile("MainForm.SessionManualBrowse.cs");
            var terminalSource = RepositorySource.ReadSourceFile("SessionCommandTerminalHost.cs");

            Assert.That(sessionSource, Does.Contain("Dictionary<string, ManualBrowseContext> _sessionManualBrowseContexts"));
            Assert.That(sessionSource, Does.Contain("GetOrCreateSessionManualBrowseContext"));
            Assert.That(sessionSource, Does.Contain("HandleSessionTerminalScreenReaderHotkey"));
            Assert.That(sessionSource, Does.Not.Contain("ReleaseSessionManualBrowseContextIfUnused"));
            Assert.That(terminalSource, Does.Contain("ScreenReaderHotkeyPressed"));
            Assert.That(terminalSource, Does.Contain("GetReadableLines()"));
            Assert.That(terminalSource, Does.Contain("TerminalWindowClosed"));
        }
    }
}
