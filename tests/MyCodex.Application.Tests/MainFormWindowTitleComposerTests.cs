using System;
using System.IO;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormWindowTitleComposerTests
    {
        [Test]
        public void BuildWindowTitle_ReturnsAppNameWhenNoStatusOrRuntime()
        {
            Assert.That(
                MainFormWindowTitleComposer.BuildWindowTitle("MyCodex", null, null),
                Is.EqualTo("MyCodex"));
        }

        [Test]
        public void BuildWindowTitle_AppendsModeAfterAppName()
        {
            Assert.That(
                MainFormWindowTitleComposer.BuildWindowTitle("MyCodex", null, null, "Coding Mode"),
                Is.EqualTo("MyCodex - Coding Mode"));
        }

        [Test]
        public void BuildWindowTitle_IncludesStatusBeforeAppName()
        {
            Assert.That(
                MainFormWindowTitleComposer.BuildWindowTitle("MyCodex", "Working", null, "Coding Mode"),
                Is.EqualTo("Working - MyCodex - Coding Mode"));
        }

        [Test]
        public void BuildWindowTitle_IncludesRuntimeAfterStatus()
        {
            Assert.That(
                MainFormWindowTitleComposer.BuildWindowTitle("MyCodex", "Working", "remote-dev", "Office Mode"),
                Is.EqualTo("Working - remote-dev - MyCodex - Office Mode"));
        }

        [Test]
        public void BuildWindowTitle_UsesRuntimeWhenStatusIsEmpty()
        {
            Assert.That(
                MainFormWindowTitleComposer.BuildWindowTitle("MyCodex", "", "remote-dev"),
                Is.EqualTo("remote-dev - MyCodex"));
        }

        [TestCase("Ubuntu", "Coding Mode", "Ubuntu - MyCodex - Coding Mode")]
        [TestCase("Remote Linux", "Office Mode", "Remote Linux - MyCodex - Office Mode")]
        public void BuildWindowTitle_UsesActiveRuntimeNameAndSelectedMode(
            string runtimeName,
            string modeName,
            string expected)
        {
            Assert.That(
                MainFormWindowTitleComposer.BuildWindowTitle("MyCodex", null, runtimeName, modeName),
                Is.EqualTo(expected));
        }

        [Test]
        public void MainFormTitle_UsesNoRuntimeLabelForWindowsAndActiveTargetNameForWslOrSsh()
        {
            var source = ReadFormSource("MainForm.Status.cs");
            var compose = ExtractMethod(source, "private string ComposeWindowTitle()");
            var runtimeName = ExtractMethod(source, "private string GetCurrentWindowRuntimeDisplayName()");

            Assert.That(compose, Does.Contain("GetCurrentWindowRuntimeDisplayName()"));
            Assert.That(runtimeName, Does.Contain("if (!IsUnixRemoteRuntimeEnabled())"));
            Assert.That(runtimeName, Does.Contain("return null;"));
            Assert.That(runtimeName, Does.Contain("return GetCurrentRuntimeDisplayName();"));
        }

        [Test]
        public void MainFormWorkingTitle_DistinguishesCurrentSessionFromBackgroundWork()
        {
            var source = ReadFormSource("MainForm.Status.cs");
            var method = ExtractMethod(source, "private string GetWindowStatusText()");

            Assert.That(method, Does.Contain("IsThreadBusy(_currentSession.ThreadId)"));
            Assert.That(method, Does.Contain("SessionDisplayFormatter.BuildWorkingStatusText(_currentSession)"));
            Assert.That(method, Does.Contain("IsSessionWorking(_currentSession)"));
            Assert.That(method, Does.Contain("_busyThreadIds.Count > 0"));
            Assert.That(method, Does.Not.Contain("_currentInteractionMode"));
            Assert.That(method, Does.Contain("MainForm.WindowStatus.BackgroundWorking"));
        }

        [Test]
        public void MainFormTitle_ReportsLoginRequiredOnlyWhenAppServerRequiresOfficialLogin()
        {
            var source = ReadFormSource("MainForm.Status.cs");
            var method = ExtractMethod(source, "private string GetWindowStatusText()");

            Assert.That(
                method,
                Does.Contain("MainFormAuthenticationStatePlanner.RequiresOfficialLogin(_authenticationState)"));
            Assert.That(method, Does.Not.Contain("_authenticationState.IsAuthenticated"));
        }

        [Test]
        public void LiveWorkingIndicators_DoNotTreatInterruptCapabilityAsActiveWork()
        {
            var status = ExtractMethod(
                ReadFormSource("MainForm.Status.cs"),
                "private string GetWindowStatusText()");
            var sessions = ReadFormSource("MainForm.Sessions.cs");
            var liveStatus = ReadFormSource("MainForm.LiveWorkStatus.cs");
            var workbenches = ReadFormSource("MainForm.SessionWorkbenches.cs");

            Assert.That(status, Does.Contain("IsThreadBusy(_currentSession.ThreadId)"));
            Assert.That(status, Does.Not.Contain("IsThreadCurrentlyRunning(_currentSession.ThreadId)"));
            Assert.That(sessions, Does.Contain("SessionListProjection.IsWorking(entry, IsThreadBusy)"));
            Assert.That(liveStatus, Does.Not.Contain("IsThreadCurrentlyRunning"));
            Assert.That(liveStatus, Does.Contain("IsThreadBusy"));
            Assert.That(workbenches, Does.Contain("IsWorking = session => session != null && IsThreadBusy(session.ThreadId)"));
        }

        [Test]
        public void BuildRuntimeFileWindowTitle_UsesDefaultPurposeWhenPurposeMissing()
        {
            Assert.That(
                MainFormWindowTitleComposer.BuildRuntimeFileWindowTitle("WSL", " ", "File Browser"),
                Is.EqualTo("WSL - File Browser"));
        }

        [Test]
        public void BuildRuntimeFileWindowTitle_ReturnsPurposeWithoutRuntime()
        {
            Assert.That(
                MainFormWindowTitleComposer.BuildRuntimeFileWindowTitle(null, "Open File", "File Browser"),
                Is.EqualTo("Open File"));
        }

        private static string ReadFormSource(string fileName)
        {
            return File.ReadAllText(Path.Combine(
                GetRepositoryRoot(),
                "src",
                "MyCodex.WinForms",
                "Forms",
                fileName));
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
            var braceStart = source.IndexOf('{', start);
            Assert.That(braceStart, Is.GreaterThanOrEqualTo(0), signature);
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

            Assert.Fail("Method body not found: " + signature);
            return string.Empty;
        }

        private static string GetRepositoryRoot()
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "src")))
            {
                root = root.Parent;
            }

            Assert.That(root, Is.Not.Null);
            return root.FullName;
        }
    }
}
