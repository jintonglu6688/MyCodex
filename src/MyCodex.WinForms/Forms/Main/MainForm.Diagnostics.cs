using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void TraceTargetStateError(string stage, Exception exception)
        {
            try
            {
                Trace.TraceError("[TargetState] Operation failed. Stage={0}; TargetId={1}; Runtime={2}; ExitPreparing={3}; Error={4}",
                    stage,
                    _fixedTarget?.Id ?? "(unconfigured)",
                    _appConfigSnapshot?.CodexRuntimeSource ?? "(unknown)",
                    IsTargetExitPreparing,
                    GitOutputSanitizer.RedactSensitiveText(exception.ToString()));
            }
            catch
            {
                // Diagnostic failures must not change the save/exit refusal.
            }
        }

        private string GetCurrentRuntimeProfileIdForDiagnostics()
        {
            return GetConfiguredRuntimeProfileId();
        }

        private void OnOpenLogDirectoryMenuItemClick(object sender, EventArgs e)
        {
            try
            {
                var directory = _appConfigService.GetLogDirectory();
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
                var status = UiLocalization.Text(
                    "MainForm.Status.LogDirectoryOpened",
                    "Log directory opened. Send the newest app log when reporting a problem.");
                SetLowPriorityStatus(status);
                _screenReaderService?.Speak(status, true, false, true);
                Trace.TraceInformation("[Diagnostics] Log directory opened by user.");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("[Diagnostics] Failed to open log directory. ErrorType={0}", ex.GetType().FullName);
                var message = UiLocalization.Text(
                    "MainForm.Error.OpenLogDirectoryFailed",
                    "The log directory could not be opened.");
                SetHighPriorityStatus(message);
                _screenReaderService?.Speak(message, true, false, true);
                MessageBox.Show(message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
