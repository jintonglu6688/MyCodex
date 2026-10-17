using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private bool _isPersistenceRecoveryPending;
        private bool _isPersistenceRecoveryDialogScheduled;
        private bool _isPersistenceRecoveryDialogShown;
        private string _persistenceRecoveryAffectedPath;
        private string _persistenceRecoveryDirectory;
        private RuntimeStateReadException _runtimeStateReadFailure;

        private void MarkPersistenceRecoveryPending(Exception exception)
        {
            _isPersistenceRecoveryPending = true;
            ResetDefaultTargetAfterPersistenceFailure();
            _runtimeStateReadFailure = exception as RuntimeStateReadException;
            var recoveryException = exception as PersistenceRecoveryRequiredException;
            var affectedPath = _runtimeStateReadFailure?.StatePath ?? recoveryException?.PrimaryPath;
            _persistenceRecoveryDirectory = _appConfigService.GetConfigDirectory();
            _persistenceRecoveryAffectedPath = string.IsNullOrWhiteSpace(affectedPath)
                ? _persistenceRecoveryDirectory
                : affectedPath;
            if (!string.IsNullOrWhiteSpace(affectedPath) && Path.IsPathRooted(affectedPath))
            {
                var affectedDirectory = Directory.Exists(affectedPath)
                    ? affectedPath
                    : Path.GetDirectoryName(affectedPath);
                if (!string.IsNullOrWhiteSpace(affectedDirectory))
                {
                    _persistenceRecoveryDirectory = affectedDirectory;
                }
            }

            if (_runtimeStateReadFailure != null)
            {
                Trace.TraceError("Runtime state read failed. Path={0}; Reason={1}",
                    _runtimeStateReadFailure.StatePath, _runtimeStateReadFailure.FailureReason);
            }

            SetHighPriorityStatus(_runtimeStateReadFailure != null
                ? UiLocalization.Text("MainForm.Status.RuntimeStateReadFailed",
                    "Cannot read execution target state. Automatic saves are disabled.")
                : UiLocalization.Text("MainForm.Status.PersistenceRecoveryRequired",
                    "Persisted data requires recovery. Automatic saves are disabled."));

            _automationRuntimeHost?.Stop();
            _automaticThreadSyncTimer?.Stop();
            _automaticThreadSyncFollowUpTimer?.Stop();
            _localThreadSessionWatchDebounceTimer?.Stop();
            _localSubAgentThreadSyncTimer?.Stop();
            _rateLimitAutoResetTimer?.Stop();

            if (Visible && IsHandleCreated &&
                !_isPersistenceRecoveryDialogScheduled &&
                !_isPersistenceRecoveryDialogShown)
            {
                _isPersistenceRecoveryDialogScheduled = true;
                this.BeginInvokeSafe(ShowPersistenceRecoveryRequiredAndExit);
            }
        }

        private void TryStartPersistenceDependentBackgroundServices()
        {
            if (_isPersistenceRecoveryPending)
            {
                return;
            }

            try
            {
                _automationRuntimeHost?.Start();
            }
            catch (Exception ex)
            {
                MarkPersistenceRecoveryPending(ex);
                return;
            }

            _localSubAgentThreadSyncTimer.Start();
            UpdateRateLimitAutoResetState();
        }

        private AppConfigSnapshot LoadAppConfigForAction()
        {
            if (_isPersistenceRecoveryPending)
            {
                return _appConfigSnapshot ?? new AppConfigSnapshot();
            }

            try
            {
                return _appConfigStore.Load() ?? _appConfigSnapshot ?? new AppConfigSnapshot();
            }
            catch (Exception ex)
            {
                MarkPersistenceRecoveryPending(ex);
                return _appConfigSnapshot ?? new AppConfigSnapshot();
            }
        }

        private bool TryLoadAutomationSnapshot(out AutomationStateSnapshot snapshot)
        {
            try
            {
                snapshot = _automationStore.Load();
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                snapshot = null;
                MarkPersistenceRecoveryPending(ex);
                return false;
            }
            catch
            {
                snapshot = null;
                return false;
            }
        }

        private bool TryHandleAutomationTurnTerminal(
            AppServerEventArgs e,
            AutomationRunCompletion completion,
            out bool automationUpdated)
        {
            try
            {
                automationUpdated = _automationRuntimeHost != null &&
                    _automationRuntimeHost.HandleTurnTerminal(e.ThreadId, e.TurnId, completion);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                automationUpdated = false;
                MarkPersistenceRecoveryPending(ex);
                return false;
            }
        }

        private void ShowPersistenceRecoveryRequiredAndExit()
        {
            _isPersistenceRecoveryDialogScheduled = false;
            if (_isPersistenceRecoveryDialogShown || IsDisposed)
            {
                return;
            }

            _isPersistenceRecoveryDialogShown = true;
            var directory = string.IsNullOrWhiteSpace(_persistenceRecoveryDirectory)
                ? _appConfigService.GetConfigDirectory()
                : _persistenceRecoveryDirectory;
            var message = _runtimeStateReadFailure != null
                ? string.Format(
                    UiLocalization.Text(
                        "MainForm.Message.RuntimeStateReadFailed.Template",
                        "MyCodex could not read saved state from the execution target. Automatic saves are disabled to protect existing data. The application will open the local configuration directory and exit. Check the connection and Shell setting, then restart MyCodex.\n\nTarget data: {0}\n\nReason: {1}\n\nLocal configuration directory: {2}"),
                    _persistenceRecoveryAffectedPath,
                    _runtimeStateReadFailure.FailureReason,
                    directory)
                : string.Format(
                    UiLocalization.Text(
                        "MainForm.Message.PersistenceRecoveryRequired.Template",
                        "MyCodex could not safely load persisted data. Automatic saves are disabled. The application will open the local configuration directory and exit. Repair or move the affected data, then restart MyCodex.\n\nAffected data: {0}\n\nLocal configuration directory: {1}"),
                    _persistenceRecoveryAffectedPath,
                    directory);
            MessageBox.Show(
                this,
                message,
                AppConstants.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
            }
            catch
            {
            }

            Close();
        }
    }
}
