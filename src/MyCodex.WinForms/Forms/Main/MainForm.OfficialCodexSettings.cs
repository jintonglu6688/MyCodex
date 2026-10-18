using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private bool ConfirmOfficialOptionsUpdate(IWin32Window owner = null)
        {
            return MessageBox.Show(
                owner ?? this,
                UiLocalization.Text(
                    "MainForm.SessionSettings.Confirm.OfficialOptions.Body",
                    "Official Codex settings affect Codex runtime behavior. Save these changes?"),
                UiLocalization.Text(
                    "MainForm.SessionSettings.Confirm.OfficialOptions.Title",
                    "Confirm Official Settings"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private static OfficialCodexSettingsSelection CreateOfficialCodexSettingsSelection(
            OfficialCodexSettingsForm form)
        {
            return new OfficialCodexSettingsSelection
            {
                ModelReasoningSummary = form.ModelReasoningSummary,
                ModelVerbosity = form.ModelVerbosity,
                ServiceTier = form.ServiceTier,
                WebSearchMode = form.WebSearchMode,
                WebSearchContextSize = form.WebSearchContextSize,
                ViewImageToolEnabled = form.ViewImageToolEnabled,
                ToolOutputTokenLimit = form.ToolOutputTokenLimit,
                ProjectDocMaxBytes = form.ProjectDocMaxBytes
            };
        }

        private async Task<bool> TryWriteOfficialCodexSettingsAsync(
            OfficialCodexSettingsSelection requested)
        {
            return await TryWriteOfficialCodexSettingsAsync(
                snapshot => OfficialCodexSettingsEditPlanner.BuildEdits(snapshot, requested));
        }

        private async Task<bool> TryWriteOfficialCodexSettingsAsync(
            Func<AppServerConfigSnapshot, IReadOnlyCollection<AppServerConfigWriteEdit>> buildEdits)
        {
            if (!await EnsureAppServerAvailableForActionAsync(
                UiLocalization.Text(
                    "MainForm.SessionSettings.Action.WriteOfficialConfig",
                    "Write official config")))
            {
                return false;
            }

            try
            {
                var snapshot = await _appServerClient.ReadConfigAsync(null, true);
                var edits = buildEdits(snapshot);
                if (edits.Count == 0)
                {
                    _globalServerConfigSnapshot = snapshot;
                    return false;
                }

                var guard = AppServerConfigWriteGuard.EvaluateUserConfig(snapshot, edits);
                if (!guard.CanWrite)
                {
                    ShowOfficialConfigWriteWarning(UiLocalization.Text(
                        "MainForm.OfficialSettings.ConfigWriteRequiresReload",
                        "Official Codex settings changed or could not be verified. Reload the target, review the settings, and try again."));
                    return false;
                }

                await _appServerClient.WriteConfigBatchAsync(
                    edits,
                    expectedVersion: guard.ExpectedVersion,
                    reloadUserConfig: true);
                _globalServerConfigSnapshot = null;
                _globalConfigRequirementsSnapshot = null;
                await RefreshGlobalServerConfigAsync(false);
                AddChatHistoryItem(
                    GetStatusSenderText(),
                    UiLocalization.Text(
                        "MainForm.SessionSettings.Chat.OfficialCodexSettingsUpdated",
                        "Codex official settings updated."),
                    ChatHistoryCategory.System);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "Failed to update official Codex settings: {0}",
                    RelaySecretRedactor.Redact(ex.Message));
                ShowOfficialConfigWriteWarning(UiLocalization.Text(
                    "MainForm.OfficialSettings.ConfigWriteConflict",
                    "Official Codex settings were not changed. Another client may have changed them. Reload the target, review the settings, and try again."));
                return false;
            }
        }

        private void ShowOfficialConfigWriteWarning(string message)
        {
            SetHighPriorityStatus(message);
            MessageBox.Show(
                this,
                message,
                AppConstants.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
