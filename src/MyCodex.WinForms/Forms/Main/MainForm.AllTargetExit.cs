using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Infrastructure.Instances;
using MyCodex.WinForms.Localization;
using InstanceExitPreparation = MyCodex.Infrastructure.Instances.TargetExitPreparation;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private TargetExitAllForm _allTargetExitForm;
        private bool _allTargetExitInProgress;

        private async Task<bool> RunAllTargetExitAsync(Func<Task> afterOthersExited = null, Action exitCommitted = null)
        {
            if (_targetCatalog == null || _targetInstance == null || _allTargetExitInProgress || _isExitRequested) return false;
            var ownerEnabled = Enabled;
            using (var reservation = TargetInstanceCoordinator.TryReserveAllTargets(_targetChannel, _targetCatalog.GetSharedRoot()))
            {
                if (reservation == null)
                {
                    SetHighPriorityStatus(UiLocalization.Text("TargetExitAllForm.Status.AlreadyRunning", "Another window is already preparing all targets to exit."));
                    return false;
                }
                _allTargetExitInProgress = true;
                var coordinator = new TargetExitAllCoordinator(_targetInstance.InstanceRecord,
                    () => TargetInstanceCoordinator.Discover(_targetChannel, _targetCatalog.GetSharedRoot()), SendAllTargetExitRequestAsync);
                try
                {
                    using (var form = new TargetExitAllForm(coordinator, GetExitTargetName, _appDisplayName, afterOthersExited != null))
                    {
                        _allTargetExitForm = form;
                        if (form.ShowDialog(this) != DialogResult.OK)
                        {
                            await coordinator.AbortAsync();
                            ShowUnconfirmedExitCancellation(coordinator);
                            await ReturnToRemainingTargetsAsync(coordinator);
                            return false;
                        }
                    }
                    _allTargetExitForm = null;
                    // ShowDialog reenables its owner. Keep the prepared target frozen during helper handoff.
                    Enabled = false;
                    if (coordinator.GetRemainingOtherTargets().Count != 0)
                        throw new InvalidOperationException(UiLocalization.Text("TargetExitAllForm.Status.ScopeChanged", "The running targets changed. Cancel and retry."));
                    if (afterOthersExited != null) await afterOthersExited();
                    // The helper still requires this exact process to exit. Do fallible authorization I/O before commit.
                    exitCommitted?.Invoke();
                    if (!CommitTargetExit(coordinator.OperationId))
                    {
                        await coordinator.AbortAsync();
                        SetHighPriorityStatus(UiLocalization.Text("TargetExitAllForm.Status.CurrentNotReady", "This target could not confirm exit. Its window remains open."));
                        return false;
                    }
                    CloseCommittedTarget();
                    return true;
                }
                catch (Exception ex)
                {
                    await coordinator.AbortAsync();
                    SetHighPriorityStatus(UiLocalization.Text("TargetExitAllForm.Status.Failed", "Exit was not completed: ") + ex.Message);
                    ShowUnconfirmedExitCancellation(coordinator);
                    return false;
                }
                finally
                {
                    _allTargetExitForm = null;
                    _allTargetExitInProgress = false;
                    if (!_isExitRequested && !IsDisposed && !Disposing) Enabled = ownerEnabled;
                }
            }
        }

        private async Task<TargetInstanceReply> SendAllTargetExitRequestAsync(TargetInstanceRecord instance, TargetInstanceRequest request)
        {
            if (!TargetInstanceCoordinator.SameInstance(instance, _targetInstance.InstanceRecord))
                return await Task.Run(() => TargetInstanceCoordinator.SendToInstance(instance, _targetCatalog.GetSharedRoot(), request));
            if (request.Operation == TargetInstanceOperation.AbortExit)
            {
                CancelTargetExitPreparation(request.OperationId);
                return new TargetInstanceReply { Success = true, Instance = instance };
            }
            if (request.Operation != TargetInstanceOperation.PrepareExit)
                return TargetInstanceReply.Failure("The coordinating target commits its own exit after the other targets have closed.");
            var ready = await PrepareTargetExitAsync(request.OperationId, false);
            return new TargetInstanceReply
            {
                Success = ready, Instance = instance,
                ExitPreparation = ready ? InstanceExitPreparation.Ready : InstanceExitPreparation.Rejected,
                Error = ready ? null : UiLocalization.Text("TargetExitAllForm.Status.PrepareCancelled", "Exit preparation was cancelled or could not be saved.")
            };
        }

        private string GetExitTargetName(TargetInstanceRecord instance)
        {
            var target = _targetCatalog.Load().Targets.FirstOrDefault(item => item.Id == instance.TargetId);
            return target == null ? instance.TargetId : TargetManagerForm.DisplayName(target);
        }

        private async Task ReturnToRemainingTargetsAsync(TargetExitAllCoordinator coordinator)
        {
            if (_isExitRequested || IsDisposed) return;
            try
            {
                var remaining = await Task.Run(() => coordinator.GetRemainingOtherTargets());
                var target = remaining.FirstOrDefault(item => item.Instance != null && item.Success)?.Instance;
                if (target != null)
                    await Task.Run(() => TargetInstanceCoordinator.SendToInstance(target, _targetCatalog.GetSharedRoot(),
                        new TargetInstanceRequest { Operation = TargetInstanceOperation.Activate }));
            }
            catch { /* Actual failures remain visible in the coordinating window; never kill an owner. */ }
        }

        private void ShowUnconfirmedExitCancellation(TargetExitAllCoordinator coordinator)
        {
            if (coordinator.AbortFailures.Count == 0) return;
            SetHighPriorityStatus(UiLocalization.Text("TargetExitAllForm.Status.CancelUnconfirmed", "Some targets did not confirm cancellation. Inspect their windows; an abandoned preparation expires within 10 minutes: ") +
                string.Join(", ", coordinator.AbortFailures.Select(GetExitTargetName)));
        }
    }
}
