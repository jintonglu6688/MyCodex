using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly GuardianDeniedActionApprovalState _guardianApprovalState = new GuardianDeniedActionApprovalState();
        private string _activeGuardianApprovalRequestId;

        private void HandleGuardianApprovalReview(AppServerEventArgs notification)
        {
            SetStatus(notification.Content);
            AppendLog(notification.Content);
            if (_guardianApprovalState.TryRegister(notification))
            {
                QueueApprovalRequestDialog(notification);
            }
        }

        private async Task ShowGuardianDeniedActionApprovalAsync(AppServerEventArgs notification)
        {
            if (!_guardianApprovalState.TryTake(notification))
            {
                return;
            }

            var runtimeVersion = _subAgentRuntimeVersion;
            using (var form = new GuardianDeniedActionApprovalForm(notification.GuardianApprovalReview.ApprovalRequest))
            {
                form.Text += " - " + BuildNotificationTargetText(notification.ThreadId);
                _activeGuardianApprovalRequestId = notification.RequestId;
                using (_remoteInteractionDialogs.Register(notification.RequestId, form.CloseAsExternallyResolved))
                {
                    try
                    {
                        if (form.ShowDialog(this) != DialogResult.OK || form.WasExternallyResolved ||
                            runtimeVersion != _subAgentRuntimeVersion)
                        {
                            return;
                        }

                        await _appServerClient.ApproveGuardianDeniedActionAsync(notification.GuardianApprovalReview.ApprovalRequest);
                        if (runtimeVersion == _subAgentRuntimeVersion)
                        {
                            var message = UiLocalization.Text("MainForm.Status.GuardianActionApproved",
                                "Authorization submitted. If the turn has ended, send Continue in the original session.");
                            SetHighPriorityStatus(message);
                            AppendLog(message);
                        }
                    }
                    finally
                    {
                        _activeGuardianApprovalRequestId = null;
                    }
                }
            }
        }

        private void ClearGuardianApprovals()
        {
            _guardianApprovalState.Clear();
            _remoteInteractionDialogs.Complete(_activeGuardianApprovalRequestId);
        }
    }
}
