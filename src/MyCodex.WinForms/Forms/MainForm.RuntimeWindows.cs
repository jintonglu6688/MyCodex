using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private bool _isRuntimeWindowTransitionInProgress;
        private TargetWindowGroup _targetWindowGroup;

        private void InitializeTargetWindowGroup()
        {
            if (_targetWindowGroup != null) return;
            _targetWindowGroup = new TargetWindowGroup(this, GetCurrentWindowRuntimeDisplayName);
            _targetWindowGroup.TrackNativeDialogs();
        }

        private bool TryActivateTargetModalWindow()
        {
            return _targetWindowGroup != null && _targetWindowGroup.TryActivateModal();
        }

        private bool PreflightRuntimeWindowsForTransition()
        {
            if (_isRuntimeWindowTransitionInProgress)
            {
                SetStatus(UiLocalization.Text(
                    "MainForm.RuntimeWindows.Status.TransitionInProgress",
                    "A runtime switch is already in progress."));
                return false;
            }

            Form blockingForm;
            if (TryGetUnsafeRuntimeTransitionBlocker(out blockingForm) ||
                (_targetWindowGroup != null && _targetWindowGroup.HasNativeModal))
            {
                SetHighPriorityStatus(UiLocalization.Text(
                    "MainForm.RuntimeWindows.Status.Busy",
                    "Finish or close the active Git or modal operation before switching."));
                ActivateRuntimeWindow(blockingForm);
                return false;
            }

            return true;
        }

        private bool TryGetUnsafeRuntimeTransitionBlocker(out Form blockingForm)
        {
            var gitWorkbench = _gitWorkbenchForms.Values.FirstOrDefault(form => IsRuntimeWindowBusy(form, form != null && form.IsBusy));
            if (gitWorkbench != null)
            {
                blockingForm = gitWorkbench;
                return true;
            }

            var runtimeFileBrowser = _runtimeFileBrowserForms.FirstOrDefault(form => IsRuntimeWindowBusy(form, form != null && form.IsBusyForRuntimeTransition));
            if (runtimeFileBrowser != null)
            {
                blockingForm = runtimeFileBrowser;
                return true;
            }

            var appServerFileBusyForm = _appServerFileWindowManager.GetBusyFormForRuntimeTransition();
            if (appServerFileBusyForm != null)
            {
                blockingForm = appServerFileBusyForm;
                return true;
            }

            blockingForm = GetRuntimeWindowGroupForms().FirstOrDefault(form => IsRuntimeWindowBusy(form, false));
            return blockingForm != null;
        }

        private static bool IsRuntimeWindowBusy(Form form, bool isBusy)
        {
            return form != null &&
                   !form.IsDisposed &&
                   !form.Disposing &&
                   (isBusy || (form is IRuntimeTransitionWindow operation && operation.IsBusyForRuntimeTransition) ||
                    form.Modal || !form.Enabled);
        }

        private async Task StopCurrentRuntimeActivityAsync(string reason)
        {
            try
            {
                _automationRuntimeHost?.StopForRuntimeTransition(reason);
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message);
            }

            try
            {
                SaveDetachedSessionWorkbenchDrafts();
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message);
            }

            await InterruptAllActiveRuntimeThreadsAsync();
            await TerminateAllActiveRuntimeCommandsAsync();
            try
            {
                if (!TryCloseRuntimeWindowGroup())
                {
                    AppendLog(UiLocalization.Text(
                        "MainForm.RuntimeWindows.Status.CloseFailed",
                        "One or more old-runtime windows could not be closed during the runtime switch."));
                }
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message);
            }

            FinalizeRuntimeTransitionState(reason);
        }

        private async Task InterruptAllActiveRuntimeThreadsAsync()
        {
            var threadIds = new HashSet<string>(_busyThreadIds, StringComparer.OrdinalIgnoreCase);
            AddSessionThreadIds(threadIds, _sessions);
            AddSessionThreadIds(threadIds, _inactiveRuntimeSessions);
            foreach (var workbench in _subAgentWorkbenches.Values)
            {
                if (workbench != null &&
                    workbench.ActiveSession != null &&
                    !string.IsNullOrWhiteSpace(workbench.ActiveSession.ThreadId))
                {
                    threadIds.Add(workbench.ActiveSession.ThreadId);
                }
            }

            AddRunningAutomationThreadIds(threadIds);
            foreach (var pair in _threadTurnSummaryStates)
            {
                if (pair.Value != null && !pair.Value.IsFinalized)
                {
                    threadIds.Add(pair.Key);
                }
            }

            foreach (var threadId in threadIds)
            {
                try
                {
                    if (!CanInterruptThread(threadId))
                    {
                        continue;
                    }

                    await _appServerClient.InterruptAsync(threadId, GetInterruptibleTurnId(threadId));
                }
                catch (Exception ex)
                {
                    AppendLog(ex.Message);
                }
            }
        }

        private void AddRunningAutomationThreadIds(ISet<string> threadIds)
        {
            if (threadIds == null || _automationStore == null)
            {
                return;
            }

            try
            {
                var snapshot = _automationStore.Load();
                foreach (var run in snapshot == null || snapshot.Runs == null
                    ? Enumerable.Empty<MyCodex.Domain.Entities.AutomationRunSnapshot>()
                    : snapshot.Runs)
                {
                    if (run != null && !string.IsNullOrWhiteSpace(run.ThreadId))
                    {
                        threadIds.Add(run.ThreadId);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message);
            }
        }

        private static void AddSessionThreadIds(ISet<string> threadIds, IEnumerable<MyCodex.Domain.Entities.ChatSession> sessions)
        {
            if (threadIds == null || sessions == null)
            {
                return;
            }

            foreach (var session in sessions)
            {
                if (session != null && !string.IsNullOrWhiteSpace(session.ThreadId))
                {
                    threadIds.Add(session.ThreadId);
                }
            }
        }

        private void FinalizeRuntimeTransitionState(string reason)
        {
            _pendingRemoteInteractions.Clear();
            _subAgentRuntimeVersion++;
            _subAgentEventVersions.Clear();
            ClearGuardianApprovals();
            _subAgentSummaries.Clear();
            _subAgentRecentWork.Clear();
            _subAgentStatuses.Clear();
            _subAgentPrimedParents.Clear();
            ClearAppServerFileWindowWatches();
            ClearAllThreadBusy();
            ClearAllInFlightThreadStates();
            FinalizeAllActiveTurnSummaries(reason);
            _pendingExecutablePlans.Clear();
            _pendingRemotePlanExecutionPrompts.Clear();
            _pendingInvalidEncryptedContentRetries.Clear();
            _sessionsPendingDefaultCollaborationMode.Clear();
            _bufferedCommandOutputState.Reset();
            _bufferedTurnSummaryEventState.Reset();
            ClearModeHistoryViewStates();
            _approvalRequestDialogQueue.Clear();
            _remoteInteractionDialogs.CloseAll();
            _isLocalSubAgentThreadSyncRunning = false;
            RefreshHeader();
            RefreshInputState();
        }

        private List<Form> GetRuntimeWindowGroupForms()
        {
            var forms = new List<Form>();
            forms.AddRange(_sessionCommandTerminals.Values.Select(host => host != null ? host.Window : null));
            forms.Add(_commandTerminalForm);
            forms.AddRange(_subAgentWorkbenches.Values);
            forms.AddRange(_sessionWorkbenches.Values);
            forms.AddRange(_appServerFileWindowManager.GetOpenFormsSnapshot());
            forms.AddRange(_runtimeFileBrowserForms);
            forms.AddRange(_gitWorkbenchForms.Values);
            if (_targetWindowGroup != null) forms.AddRange(_targetWindowGroup.GetForms());
            return forms
                .Where(form => form != null && !form.IsDisposed && !form.Disposing)
                .Distinct()
                .ToList();
        }

        private void SaveDetachedSessionWorkbenchDrafts()
        {
            foreach (var form in _sessionWorkbenches.Values.ToArray())
            {
                if (form != null && !form.IsDisposed && !form.Disposing)
                {
                    form.SaveDraftForRuntimeTransition();
                }
            }
        }

        private bool TryCloseRuntimeWindowGroup()
        {
            if (!TryCloseSessionCommandTerminals() ||
                !TryCloseMainCommandTerminal() ||
                !TryCloseForms(_subAgentWorkbenches.Values) ||
                !TryCloseForms(_sessionWorkbenches.Values) ||
                !_appServerFileWindowManager.TryCloseAllForRuntimeTransition() ||
                !TryCloseForms(_runtimeFileBrowserForms) ||
                !TryCloseForms(_gitWorkbenchForms.Values) ||
                !TryCloseForms(GetRuntimeWindowGroupForms()))
            {
                return false;
            }

            if (GetRuntimeWindowGroupForms().Count != 0)
            {
                return false;
            }

            _sessionManualBrowseContexts.Clear();
            _sessionWorkbenchMessageSelections.Clear();
            return true;
        }

        private void CloseRuntimeWindowGroupOnExit()
        {
            if (!IsTargetExitPrepared) SaveDetachedSessionWorkbenchDrafts();
            TryCloseRuntimeWindowGroup();
            if (_targetWindowGroup != null) TryCloseForms(_targetWindowGroup.GetForms(true));
        }

        private bool TryCloseSessionCommandTerminals()
        {
            foreach (var host in _sessionCommandTerminals.Values.ToArray())
            {
                if (host != null && !host.TryCloseWindow())
                {
                    return false;
                }
            }

            _sessionCommandTerminals.Clear();
            return true;
        }

        private bool TryCloseMainCommandTerminal()
        {
            if (_commandTerminalForm == null || _commandTerminalForm.IsDisposed || _commandTerminalForm.Disposing)
            {
                return true;
            }

            _commandTerminalForm.Close();
            return _commandTerminalForm == null ||
                   _commandTerminalForm.IsDisposed ||
                   _commandTerminalForm.Disposing;
        }

        private static bool TryCloseForms<TForm>(IEnumerable<TForm> source) where TForm : Form
        {
            var forms = (source ?? Enumerable.Empty<TForm>())
                .Where(form => form != null && !form.IsDisposed && !form.Disposing)
                .Distinct()
                .ToArray();
            foreach (var form in forms)
            {
                form.Close();
            }

            return forms.All(form => form.IsDisposed || form.Disposing);
        }

        private void ActivateRuntimeWindow(Form form)
        {
            if (TryActivateTargetModalWindow()) return;
            var modal = FindVisibleModalDialog(form);
            if (modal != null)
            {
                modal.Activate();
                modal.BringToFront();
                return;
            }

            if (form == null || ReferenceEquals(form, this))
            {
                ShowMainWindowFromTray();
                return;
            }

            if (form.IsDisposed || form.Disposing || !form.Enabled)
            {
                return;
            }

            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }

            form.Show();
            form.Activate();
            form.BringToFront();
        }

        private static Form FindVisibleModalDialog(Form owner)
        {
            if (owner == null)
            {
                return null;
            }

            var groupedModal = TargetWindowGroup.Find(owner)?.FindModal();
            if (groupedModal != null) return groupedModal;

            Form selected = null;
            var selectedDepth = -1;
            foreach (Form form in System.Windows.Forms.Application.OpenForms)
            {
                if (form == null || form.IsDisposed || form.Disposing || !form.Visible || !form.Modal || !form.Enabled)
                {
                    continue;
                }

                var depth = 0;
                for (var currentOwner = form.Owner; currentOwner != null; currentOwner = currentOwner.Owner)
                {
                    depth++;
                    if (ReferenceEquals(currentOwner, owner))
                    {
                        if (depth > selectedDepth) { selected = form; selectedDepth = depth; }
                        break;
                    }
                }
            }

            return selected;
        }
    }
}
