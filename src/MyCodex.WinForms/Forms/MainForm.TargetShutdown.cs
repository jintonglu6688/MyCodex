using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.CodingRemote;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private TargetExitPreparation _targetExitPreparation;
        private RemoteCommandDispatcher _remoteCommandDispatcher;
        private readonly Dictionary<Form, bool> _exitWindowEnabledStates = new Dictionary<Form, bool>();
        private bool _singleTargetExitInProgress;
        private bool _targetExitCleanupStarted;
        private bool _targetExitCleanupComplete;

        private bool IsTargetExitPreparing => _targetExitPreparation != null && _targetExitPreparation.IsPreparing;
        private bool IsTargetExitPrepared => _targetExitPreparation != null && _targetExitPreparation.IsReady;

        private async Task<bool> PrepareTargetExitAsync(string operationId, bool confirm = false)
        {
            if (_isExitRequested || _targetEditBusy ||
                _isRuntimeWindowTransitionInProgress) return false;
            if (confirm)
            {
                PauseTargetInitialization();
                try { if (!ConfirmApplicationExit()) return false; }
                finally { ResumeTargetInitialization(); }
            }
            if (_targetExitPreparation == null)
                _targetExitPreparation = new TargetExitPreparation(PrepareTargetWindowsAndStateAsync, FreezeTargetEntryPoints, CommitPreparedTarget);
            try { return await _targetExitPreparation.PrepareAsync(operationId); }
            catch (Exception ex)
            {
                TraceTargetStateError("prepare-exit", ex);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Exit.SaveFailed", "This target could not save its state. It remains running.") + " (" + ex.GetType().Name + ")");
                return false;
            }
        }

        private async Task<bool> PrepareTargetWindowsAndStateAsync(string operationId)
        {
            var forms = GetRuntimeWindowGroupForms();
            var modal = _targetWindowGroup?.FindModal();
            var knownExitDialog = modal is TargetExitAllForm || (modal != null && ReferenceEquals(modal, _targetManagerForm));
            if ((modal != null && !knownExitDialog) ||
                (_targetWindowGroup != null && _targetWindowGroup.HasNativeModal) ||
                forms.Any(form => !(form is AppServerFilePreviewForm) &&
                    form is IRuntimeTransitionWindow operation && operation.IsBusyForRuntimeTransition) ||
                _sessionWorkbenches.Values.Any(form => form != null && form.HasPendingTargetOperation) ||
                _subAgentWorkbenches.Values.Any(form => form != null && form.HasPendingTargetOperation))
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Exit.Busy", "Finish the active settings or file operation before exiting this target."));
                return false;
            }
            if ((_remoteCommandDispatcher != null && !_remoteCommandDispatcher.TryPauseForExit()) ||
                (_automationRuntimeHost != null && !_automationRuntimeHost.TryPauseForExit()))
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Exit.Busy", "Finish the active settings or file operation before exiting this target."));
                return false;
            }
            foreach (var preview in forms.OfType<AppServerFilePreviewForm>())
            {
                if (!await preview.PrepareForTargetExitAsync() || !_targetExitPreparation.IsCurrent(operationId))
                {
                    preview.CancelTargetExit();
                    return false;
                }
            }
            foreach (var workbench in _sessionWorkbenches.Values.ToArray())
                if (workbench != null && !workbench.IsDisposed && !workbench.Disposing) workbench.SaveStateForTargetExit();
            SaveCurrentComposeState();
            CaptureCurrentModeSelection();
            // Recovery disables writes to unread state, but must not trap the target in the application.
            // File previews and workbench save/cancel checks above still apply.
            if (!_isPersistenceRecoveryPending && !TryPersistState())
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Exit.SaveFailed", "This target could not save its state. It remains running."));
                return false;
            }
            return _targetExitPreparation.IsCurrent(operationId);
        }

        private void FreezeTargetEntryPoints(bool freeze)
        {
            if (freeze)
            {
                PauseTargetInitialization();
                _exitWindowEnabledStates.Clear();
                foreach (var form in GetRuntimeWindowGroupForms().Concat(new[] { this }).Distinct())
                {
                    if (form is TargetExitAllForm || ReferenceEquals(form, _targetManagerForm) || form is AppServerFilePreviewForm) continue;
                    _exitWindowEnabledStates[form] = form.Enabled;
                    form.Enabled = false;
                }
                SetStatus(UiLocalization.Text("MainForm.Exit.Preparing", "Preparing this target to exit..."));
            }
            else
            {
                ResumeTargetInitialization();
                _remoteCommandDispatcher?.ResumeAfterCancelledExit();
                _automationRuntimeHost?.ResumeAfterCancelledExit();
                foreach (var preview in GetRuntimeWindowGroupForms().OfType<AppServerFilePreviewForm>()) preview.CancelTargetExit();
                foreach (var pair in _exitWindowEnabledStates)
                    if (!pair.Key.IsDisposed && !pair.Key.Disposing) pair.Key.Enabled = pair.Value;
                _exitWindowEnabledStates.Clear();
                if (!IsDisposed && !_isExitRequested) RefreshInputState();
                SetStatus(UiLocalization.Text("MainForm.Exit.Cancelled", "Exit cancelled. This target remains running."));
            }
            UpdateAutomaticThreadSyncState();
            PublishTargetStatus(MyCodex.Infrastructure.Instances.TargetInstanceLifecycle.Running);
        }

        private void CancelTargetExitPreparation(string operationId)
        {
            _targetExitPreparation?.Cancel(operationId);
        }

        private bool CommitTargetExit(string operationId)
        {
            return _targetExitPreparation != null && _targetExitPreparation.Commit(operationId);
        }

        private bool CommitPreparedTarget()
        {
            var forms = GetRuntimeWindowGroupForms();
            if (forms.OfType<AppServerFilePreviewForm>().Any(form => !form.CanCommitTargetExit)) return false;
            foreach (var workbench in _sessionWorkbenches.Values)
                if (workbench != null && !workbench.IsDisposed) workbench.CommitTargetExit();
            foreach (var preview in forms.OfType<AppServerFilePreviewForm>()) preview.CommitTargetExit();
            _isExitRequested = true;
            StopTargetInitialization();
            MarkTargetClosing();
            return true;
        }

        private async void CloseCommittedTarget()
        {
            if (!_isExitRequested || _targetExitCleanupStarted || IsDisposed || Disposing) return;
            _targetExitCleanupStarted = true;
            try
            {
                await InterruptAllActiveRuntimeThreadsAsync();
                await TerminateAllActiveRuntimeCommandsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Committed target cleanup failed: {0}", ex.GetType().Name);
            }
            finally
            {
                _targetExitCleanupComplete = true;
                if (!IsDisposed && !Disposing) Close();
            }
        }

        private async Task RequestSingleTargetExitAsync(bool confirm)
        {
            if (_isExitRequested || _singleTargetExitInProgress || IsTargetExitPreparing) return;
            _singleTargetExitInProgress = true;
            var operationId = Guid.NewGuid().ToString("N");
            try
            {
                if (await PrepareTargetExitAsync(operationId, confirm) && CommitTargetExit(operationId)) CloseCommittedTarget();
            }
            finally { _singleTargetExitInProgress = false; }
        }

        private int CountUnsavedTargetWindows()
        {
            return GetRuntimeWindowGroupForms().OfType<AppServerFilePreviewForm>().Count(form => form.HasUnsavedTargetExitContent);
        }

        private int CountActiveTargetWork()
        {
            var count = _busyThreadIds.Count + _sessionCommandTerminals.Values.Count(host => host != null && host.HasActiveCommand);
            if (_isCommandRunning) count++;
            if (count == 0 && HasActiveRuntimeWorkForExit()) count = 1;
            return count;
        }
    }
}
