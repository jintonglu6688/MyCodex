using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormShutdownDiagnosticsTests
    {
        [Test]
        public void RunStep_LogsSlowShutdownStep()
        {
            var elapsedValues = new Queue<TimeSpan>(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1500)
            });
            var messages = new List<string>();

            MainFormShutdownDiagnostics.RunStep(
                "Stop gateway",
                () => { },
                () => elapsedValues.Dequeue(),
                messages.Add);

            Assert.That(messages, Has.Count.EqualTo(1));
            Assert.That(messages[0], Does.Contain("Stop gateway"));
            Assert.That(messages[0], Does.Contain("1500"));
        }

        [Test]
        public void RunStep_DoesNotLogFastShutdownStep()
        {
            var elapsedValues = new Queue<TimeSpan>(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(250)
            });
            var messages = new List<string>();

            MainFormShutdownDiagnostics.RunStep(
                "Fast step",
                () => { },
                () => elapsedValues.Dequeue(),
                messages.Add);

            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void MainFormShutdown_UsesBudgetedScreenReaderCleanupInsteadOfBlockingUiThread()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.cs");
            var method = RepositorySource.ExtractMethod(source, "private void OnFormClosed(object sender, FormClosedEventArgs e)");

            Assert.That(source, Does.Contain("StopScreenReaderServiceWithinExitBudget"));
            Assert.That(source, Does.Contain("ExitScreenReaderStopBudget"));
            Assert.That(method, Does.Not.Contain("MainFormShutdownDiagnostics.RunStep(\"stop-screen-reader-output\", StopAllScreenReaderOutput)"));
            Assert.That(method, Does.Not.Contain("MainFormShutdownDiagnostics.RunStep(\"dispose-screen-reader-service\", () => _screenReaderService.Dispose())"));
        }

        [Test]
        public void MainFormShutdown_DoesNotDisposeScreenReaderServiceDuringExitCleanup()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.cs");
            var method = RepositorySource.ExtractMethod(source, "private void StopScreenReaderServiceWithinExitBudget()");

            Assert.That(method, Does.Not.Contain("_screenReaderService.Dispose()"));
        }

        [Test]
        public void ApplicationExit_WindowCloseFileMenuAndTrayUseOneConfirmationImplementation()
        {
            var mainSource = RepositorySource.ReadSourceFile("MainForm.cs");
            var traySource = RepositorySource.ReadSourceFile("MainForm.UpdatesTray.cs");
            var closing = RepositorySource.ExtractMethod(mainSource, "private async void OnFormClosing(object sender, FormClosingEventArgs e)");
            var fileExit = RepositorySource.ExtractMethod(mainSource, "private void OnExitMenuItemClick(object sender, EventArgs e)");
            var trayExit = RepositorySource.ExtractMethod(traySource, "private void ExitTrayMenuItem_Click(object sender, EventArgs e)");
            var request = RepositorySource.ExtractMethod(traySource, "private async void RequestApplicationExit()");
            var confirm = RepositorySource.ExtractMethod(traySource, "private bool ConfirmApplicationExit()");

            Assert.That(RepositorySource.CountOccurrences(mainSource + traySource, "private bool ConfirmApplicationExit()"), Is.EqualTo(1));
            Assert.That(closing, Does.Contain("RequestSingleTargetExitAsync"));
            Assert.That(fileExit, Does.Contain("RequestApplicationExit();"));
            Assert.That(trayExit, Does.Contain("RequestApplicationExit();"));
            Assert.That(request, Does.Contain("RequestSingleTargetExitAsync(true)"));
            Assert.That(closing, Does.Not.Contain("MessageBox.Show"));
            Assert.That(request, Does.Not.Contain("MessageBox.Show"));
            Assert.That(confirm, Does.Contain("ExitConfirmationMessageBox.Show"));
        }

        [Test]
        public void ApplicationExitStopsLocalAuthenticationAfterAppServerAndRestoresItBeforeConnect()
        {
            var main = RepositorySource.ReadSourceFile("MainForm.cs");
            var closing = RepositorySource.ExtractMethod(main, "private void OnFormClosed(object sender, FormClosedEventArgs e)");
            Assert.That(closing.IndexOf("\"stop-local-authentication-backend\"", StringComparison.Ordinal),
                Is.GreaterThan(closing.IndexOf("\"dispose-app-server-client\"", StringComparison.Ordinal)));
            var auth = RepositorySource.ReadSourceFile("MainForm.AuthenticationCenter.cs");
            Assert.That(auth, Does.Contain("!IsCurrentExecutionTargetWindows()"));
            Assert.That(auth, Does.Contain("ShutdownLocalBackendAsync"));
            var stop = RepositorySource.ExtractMethod(auth, "private void StopLocalAuthenticationBackendOnExit()");
            Assert.That(stop.IndexOf("IsLocalBackendCreated", StringComparison.Ordinal),
                Is.LessThan(stop.IndexOf("ShutdownLocalBackendAsync", StringComparison.Ordinal)));
            Assert.That(stop, Does.Not.Contain("IsCurrentExecutionTargetWindows()"), "A Windows lease must be released after switching to WSL/SSH.");
            var connection = RepositorySource.ReadSourceFile("MainForm.RuntimeConnection.cs");
            var connect = RepositorySource.ExtractMethod(connection, "private async Task<bool> EnsureAppServerConnectedAsync()");
            Assert.That(connect.IndexOf("EnsureLocalAuthenticationBackendBeforeConnectAsync()", StringComparison.Ordinal),
                Is.LessThan(connect.IndexOf("_appServerClient.ConnectAsync()", StringComparison.Ordinal)));
        }

        [Test]
        public void ApplicationExit_UsesConcisePromptWhenIdleAndDualModeWarningForAnyActiveRuntimeWork()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.UpdatesTray.cs");
            var active = RepositorySource.ExtractMethod(source, "private bool HasActiveRuntimeWorkForExit()");
            var prompt = RepositorySource.ExtractMethod(source, "private string BuildApplicationExitPrompt()");

            Assert.That(prompt, Does.Contain("HasActiveRuntimeWorkForExit()"));
            Assert.That(prompt, Does.Contain("MainForm.Message.ConfirmExit.Active"));
            Assert.That(prompt, Does.Contain("MainForm.Message.ConfirmExit"));
            Assert.That(prompt, Does.Not.Contain("_currentInteractionMode"));
            Assert.That(active, Does.Contain("_busyThreadIds.Count > 0"));
            Assert.That(active, Does.Contain("_isCommandRunning"));
            Assert.That(active, Does.Contain("HasActiveCommand"));
            Assert.That(active, Does.Contain("AutomationRunStatus.Running"));
            Assert.That(active, Does.Not.Contain("_currentInteractionMode"));
        }

        [Test]
        public void ApplicationExit_ConfirmsThenSavesMergedDualModeStateBeforeCleanup()
        {
            var mainSource = RepositorySource.ReadSourceFile("MainForm.cs");
            var traySource = RepositorySource.ReadSourceFile("MainForm.UpdatesTray.cs");
            var request = RepositorySource.ExtractMethod(traySource, "private async void RequestApplicationExit()");
            var closing = RepositorySource.ExtractMethod(mainSource, "private async void OnFormClosing(object sender, FormClosingEventArgs e)");
            var shutdown = RepositorySource.ReadSourceFile("MainForm.TargetShutdown.cs");
            var prepare = RepositorySource.ExtractMethod(shutdown, "private async Task<bool> PrepareTargetExitAsync(string operationId, bool confirm = false)");
            var save = RepositorySource.ExtractMethod(shutdown, "private async Task<bool> PrepareTargetWindowsAndStateAsync(string operationId)");
            var closed = RepositorySource.ExtractMethod(mainSource, "private void OnFormClosed(object sender, FormClosedEventArgs e)");

            Assert.That(request, Does.Contain("RequestSingleTargetExitAsync(true)"));
            Assert.That(closing, Does.Contain("e.Cancel = true"));
            Assert.That(closing, Does.Contain("RequestSingleTargetExitAsync"));
            Assert.That(prepare.IndexOf("ConfirmApplicationExit()", StringComparison.Ordinal),
                Is.LessThan(prepare.IndexOf("PrepareAsync(operationId)", StringComparison.Ordinal)));
            Assert.That(save, Does.Not.Contain("_isExitRequested = true"));
            Assert.That(save, Does.Not.Contain("InterruptAllActiveRuntimeThreadsAsync"));
            var cleanup = RepositorySource.ExtractMethod(shutdown, "private async void CloseCommittedTarget()");
            Assert.That(cleanup, Does.Contain("await InterruptAllActiveRuntimeThreadsAsync()"));
            Assert.That(cleanup, Does.Contain("await TerminateAllActiveRuntimeCommandsAsync()"));
            Assert.That(cleanup.IndexOf("await TerminateAllActiveRuntimeCommandsAsync()", StringComparison.Ordinal),
                Is.LessThan(cleanup.IndexOf("Close();", StringComparison.Ordinal)));
            Assert.That(save.IndexOf("SaveCurrentComposeState();", StringComparison.Ordinal),
                Is.LessThan(save.IndexOf("CaptureCurrentModeSelection();", StringComparison.Ordinal)));
            Assert.That(save.IndexOf("CaptureCurrentModeSelection();", StringComparison.Ordinal),
                Is.LessThan(save.IndexOf("TryPersistState()", StringComparison.Ordinal)));
            Assert.That(save, Does.Contain("if (!_isPersistenceRecoveryPending && !TryPersistState())"));
            Assert.That(closed, Does.Contain("clear-compose-state"));
            Assert.That(closed, Does.Not.Contain("SaveCurrentComposeState"));
        }
    }
}
