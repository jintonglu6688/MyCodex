using System;
using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RuntimeWindowGroupTests
    {
        [Test]
        public void RuntimeWindowGroup_PreflightBlocksUnsafeWindowsButNotActiveTurnsOrCommands()
        {
            var source = ReadFormSource("MainForm.RuntimeWindows.cs");
            var preflight = ExtractMethod(source, "private bool PreflightRuntimeWindowsForTransition");

            Assert.That(preflight, Does.Contain("TryGetUnsafeRuntimeTransitionBlocker(out blockingForm)"));
            Assert.That(preflight, Does.Not.Contain("_busyThreadIds"));
            Assert.That(preflight, Does.Not.Contain("_isCommandRunning"));
            Assert.That(preflight, Does.Not.Contain("host.HasActiveCommand"));
            Assert.That(source, Does.Contain("form.IsBusy"));
            Assert.That(source, Does.Contain("form.Modal || !form.Enabled"));
            Assert.That(preflight, Does.Not.Contain("TryCloseRuntimeWindowGroup"));
        }

        [Test]
        public void RuntimeWindowGroup_ContainsEveryRuntimeRelatedWindowRegistry()
        {
            var source = ReadFormSource("MainForm.RuntimeWindows.cs");

            Assert.That(source, Does.Contain("_sessionCommandTerminals.Values"));
            Assert.That(source, Does.Contain("_commandTerminalForm"));
            Assert.That(source, Does.Contain("_subAgentWorkbenches.Values"));
            Assert.That(source, Does.Contain("_sessionWorkbenches.Values"));
            Assert.That(source, Does.Contain("_appServerFileWindowManager.GetOpenFormsSnapshot()"));
            Assert.That(source, Does.Contain("_runtimeFileBrowserForms"));
            Assert.That(source, Does.Contain("_gitWorkbenchForms.Values"));
            Assert.That(source, Does.Contain("_targetWindowGroup.GetForms()"));
            Assert.That(source, Does.Contain("IRuntimeTransitionWindow"));
        }

        [Test]
        public void GitSettingsEntrypointsUseModalWhileOtherEntrypointsRemainModeless()
        {
            var source = ReadFormSource("MainForm.GitHosting.cs");
            foreach (var name in new[] { "ShowGitEnvironmentForm", "ShowGitRemoteCredentialsForm" })
            {
                var method = ExtractMethod(source, "private void " + name);
                Assert.That(method, Does.Contain("form.ShowDialog(this)"));
                Assert.That(method, Does.Not.Contain(".Show()"));
            }
            foreach (var name in new[] { "ShowGitCloneForm", "ShowGitRemoteRepositoriesForm", "ShowGitHubStoreForm" })
            {
                var method = ExtractMethod(source, "private void " + name);
                Assert.That(method, Does.Contain(".Show()"));
                Assert.That(method, Does.Not.Contain("ShowDialog"));
            }
        }

        [Test]
        public void RuntimeWindowGroup_StopCoordinatorTerminatesActivityThenClosesAndFinalizes()
        {
            var source = ReadFormSource("MainForm.RuntimeWindows.cs");
            var stop = ExtractMethod(source, "private async Task StopCurrentRuntimeActivityAsync");
            var automationIndex = stop.IndexOf("_automationRuntimeHost?.StopForRuntimeTransition(reason);", StringComparison.Ordinal);
            var saveIndex = stop.IndexOf("SaveDetachedSessionWorkbenchDrafts();", StringComparison.Ordinal);
            var interruptIndex = stop.IndexOf("await InterruptAllActiveRuntimeThreadsAsync();", StringComparison.Ordinal);
            var commandIndex = stop.IndexOf("await TerminateAllActiveRuntimeCommandsAsync();", StringComparison.Ordinal);
            var closeIndex = stop.IndexOf("TryCloseRuntimeWindowGroup()", StringComparison.Ordinal);
            var finalizeIndex = stop.IndexOf("FinalizeRuntimeTransitionState(reason);", StringComparison.Ordinal);

            Assert.That(automationIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(saveIndex, Is.GreaterThan(automationIndex));
            Assert.That(interruptIndex, Is.GreaterThan(saveIndex));
            Assert.That(commandIndex, Is.GreaterThan(interruptIndex));
            Assert.That(closeIndex, Is.GreaterThan(commandIndex));
            Assert.That(finalizeIndex, Is.GreaterThan(closeIndex));
            Assert.That(stop, Does.Not.Contain("_sessions.Clear"));
            Assert.That(stop, Does.Not.Contain("_recentWorkspaces.Clear"));
            Assert.That(stop, Does.Not.Contain("_composeDraftStore.Clear"));

            var terminalIndex = source.IndexOf("TryCloseSessionCommandTerminals()", closeIndex, StringComparison.Ordinal);
            var subAgentIndex = source.IndexOf("TryCloseForms(_subAgentWorkbenches.Values)", terminalIndex, StringComparison.Ordinal);
            var sessionIndex = source.IndexOf("TryCloseForms(_sessionWorkbenches.Values)", subAgentIndex, StringComparison.Ordinal);
            var fileIndex = source.IndexOf("_appServerFileWindowManager.TryCloseAllForRuntimeTransition()", sessionIndex, StringComparison.Ordinal);
            var gitIndex = source.IndexOf("TryCloseForms(_gitWorkbenchForms.Values)", fileIndex, StringComparison.Ordinal);

            Assert.That(terminalIndex, Is.GreaterThan(closeIndex));
            Assert.That(subAgentIndex, Is.GreaterThan(terminalIndex));
            Assert.That(sessionIndex, Is.GreaterThan(subAgentIndex));
            Assert.That(fileIndex, Is.GreaterThan(sessionIndex));
            Assert.That(gitIndex, Is.GreaterThan(fileIndex));
            Assert.That(source, Does.Contain("if (GetRuntimeWindowGroupForms().Count != 0)"));
            Assert.That(source, Does.Contain("_sessionManualBrowseContexts.Clear();"));
        }

        [Test]
        public void RuntimeSettings_PreflightsTargetChangeBeforeAnyStateOrConfigMutation()
        {
            var source = ReadFormSource("MainForm.RuntimeSettings.cs");
            var targetChangeIndex = source.IndexOf("var runtimeTargetChanged =", StringComparison.Ordinal);
            var gateIndex = source.IndexOf("PreflightRuntimeWindowsForTransition()", targetChangeIndex, StringComparison.Ordinal);
            var confirmationIndex = source.IndexOf("ConfirmRuntimeTargetChange()", gateIndex, StringComparison.Ordinal);
            var stateSaveIndex = source.IndexOf("SaveCurrentComposeState();", confirmationIndex, StringComparison.Ordinal);
            var mutationIndex = source.IndexOf("_appConfigSnapshot.CodexRuntimeSource = form.SelectedRuntimeSource;", StringComparison.Ordinal);
            var configSaveIndex = source.IndexOf("SaveAppConfig()", mutationIndex, StringComparison.Ordinal);
            var switchIndex = source.IndexOf("SwitchRuntimeTargetAfterSaveAsync(", configSaveIndex, StringComparison.Ordinal);

            Assert.That(targetChangeIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(gateIndex, Is.GreaterThan(targetChangeIndex));
            Assert.That(confirmationIndex, Is.GreaterThan(gateIndex));
            Assert.That(stateSaveIndex, Is.GreaterThan(confirmationIndex));
            Assert.That(mutationIndex, Is.GreaterThan(stateSaveIndex));
            Assert.That(configSaveIndex, Is.GreaterThan(mutationIndex));
            Assert.That(switchIndex, Is.GreaterThan(configSaveIndex));
        }

        [Test]
        public void ModeSwitchAndAutomation_KeepRuntimeWindowsAlive()
        {
            var modesSource = ReadFormSource("MainForm.Modes.cs");
            var mainFormSource = ReadFormSource("MainForm.Automation.cs");

            Assert.That(modesSource, Does.Contain("private bool SelectInteractionModeTab(AppInteractionMode nextMode)"));
            Assert.That(modesSource, Does.Not.Contain("PrepareRuntimeWindowsForTransition"));
            Assert.That(mainFormSource, Does.Contain("if (!SelectInteractionModeTab(interactionMode))"));
        }

        [Test]
        public void FileWindowManager_ProvidesReadOnlySnapshotAndVerifiedClose()
        {
            var source = ReadFormSource("AppServerFileWindowManager.cs");

            Assert.That(source, Does.Contain("internal IReadOnlyList<Form> GetOpenFormsSnapshot()"));
            Assert.That(source, Does.Contain("internal Form GetBusyFormForRuntimeTransition()"));
            Assert.That(source, Does.Contain("internal bool TryCloseAllForRuntimeTransition()"));
            Assert.That(source, Does.Contain("return GetOpenFormsSnapshot().Count == 0;"));
        }

        [Test]
        public void RuntimeWindowEntrypoints_RefuseToOpenWhileTransitionIsInProgress()
        {
            Assert.That(ReadFormSource("MainForm.CommandTerminal.cs"), Does.Contain("_isRuntimeWindowTransitionInProgress"));
            Assert.That(ReadFormSource("MainForm.DiffWorkbench.cs"), Does.Contain("_isRuntimeWindowTransitionInProgress"));
            Assert.That(ReadFormSource("MainForm.SessionSettings.cs"), Does.Contain("_isRuntimeWindowTransitionInProgress"));
            Assert.That(ReadFormSource("MainForm.SessionWorkbenches.cs"), Does.Contain("_isRuntimeWindowTransitionInProgress"));
            Assert.That(ReadFormSource("MainForm.Workspaces.cs"), Does.Contain("_isRuntimeWindowTransitionInProgress"));
            Assert.That(ReadFormSource("MainForm.GitHosting.cs"), Does.Contain("_isRuntimeWindowTransitionInProgress"));
        }

        private static string ReadFormSource(string fileName)
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "src")))
            {
                root = root.Parent;
            }

            Assert.That(root, Is.Not.Null);
            return File.ReadAllText(Path.Combine(root.FullName, "src", "MyCodex.WinForms", "Forms", fileName));
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "Method signature not found: " + signature);
            var brace = source.IndexOf('{', start);
            Assert.That(brace, Is.GreaterThan(start));
            var depth = 0;
            for (var index = brace; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(start, index - start + 1);
                    }
                }
            }

            Assert.Fail("Method body is incomplete: " + signature);
            return string.Empty;
        }
    }
}
