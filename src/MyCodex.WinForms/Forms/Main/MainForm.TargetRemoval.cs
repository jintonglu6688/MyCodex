using System;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async Task<TargetInstanceReply> RemoveTargetFromManagerAsync(string id)
        {
            if (id != _fixedTarget.Id) return await RemoveTargetFromOwnerAsync(id);
            // A target cannot keep cleaning its data after its own UI thread exits. Hand off to Windows.
            var opened = await _targetLauncher.OpenAsync("windows");
            if (!opened.Success) return opened;
            SetStatus(UiLocalization.Text("MainForm.Removal.Handoff", "The local target will finish removing this target after it exits."));
            return await Task.Run(() => TargetInstanceCoordinator.Send(_targetChannel, "windows", _targetCatalog.GetSharedRoot(),
                new TargetInstanceRequest { Operation = TargetInstanceOperation.RemoveTarget, RemovalTargetId = id }, 30000));
        }

        private async Task<TargetInstanceReply> RemoveTargetFromOwnerAsync(string id)
        {
            if (_isExitRequested || IsTargetExitPreparing || _targetEditBusy || _allTargetExitInProgress) return TargetInstanceReply.Failure(
                UiLocalization.Text("MainForm.Exit.Busy", "Finish the active settings or file operation before exiting this target."));
            _targetEditBusy = true;
            try
            {
                var result = await new TargetRemovalService(_targetCatalog, _targetChannel).RemoveAsync(id);
                var message = result.Removed
                    ? UiLocalization.Text("MainForm.Removal.Complete", "Target removed. Remove its old device entry manually on the phone.")
                    : UiLocalization.Text("MainForm.Removal.Incomplete", "Target removal was not completed. The target entry remains available.");
                if (result.RelayCleanupPending)
                    message += " " + UiLocalization.Text("MainForm.Removal.RelayPending", "Local device access was revoked; relay cleanup is pending. Offline phones have not confirmed removal.");
                if (result.RetainedArchivePath != null)
                    message += " " + string.Format(UiLocalization.Text("MainForm.Removal.Archive", "Local cleanup is incomplete. Remaining files: {0}"), result.RetainedArchivePath);
                SetHighPriorityStatus(message);
                RefreshTargetsMenu();
                return new TargetInstanceReply { Success = result.Removed,
                    Error = !result.Removed || result.RelayCleanupPending || result.RetainedArchivePath != null ? message : null };
            }
            catch (Exception ex)
            {
                return TargetInstanceReply.Failure(UiLocalization.Text("MainForm.Removal.Inspect", "Removal was not completed. Check the actual target state and local removal archive before retrying.") + " (" + ex.GetType().Name + ")");
            }
            finally { _targetEditBusy = false; }
        }
    }
}
