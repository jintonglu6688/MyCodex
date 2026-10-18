using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AppServer;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private SessionHistoryResumeRepairGate _sessionHistoryResumeRepairGate;

        private void InitializeSessionHistoryRepair()
        {
            _sessionHistoryResumeRepairGate = new SessionHistoryResumeRepairGate(
                CreateCurrentAuthenticationCenterClient, OnSessionHistoryRepairedBeforeResume);
            if (_appServerClient is AppServerClient client)
            {
                client.ThreadResumePreparation = _sessionHistoryResumeRepairGate.PrepareAsync;
            }
        }

        // The provider in use may have changed with the connection.
        private void ResetSessionHistoryResumeRepair()
        {
            _sessionHistoryResumeRepairGate?.Reset();
        }

        private IThreadHistoryRepairService CreateThreadHistoryRepairService(IThreadHistoryRepairService localRepair)
        {
            return new AuthenticationThreadHistoryRepairService(
                CreateCurrentAuthenticationCenterClient, localRepair, ConfirmCompactionReplacementAsync);
        }

        private void OnSessionHistoryRepairedBeforeResume(AuthenticationSessionRepairResult result)
        {
            this.BeginInvokeSafe(() => SetLowPriorityStatus(UiLocalization.Text(
                "SessionHistory.Status.RepairedBeforeResume",
                "Removed what other providers left in this session so the current provider can continue it. A backup was kept.")));
        }

        private Task<bool> ConfirmCompactionReplacementAsync(int compactions)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return Task.FromResult(false);
            }

            Func<bool> confirm = () => MessageBox.Show(
                this,
                string.Format(UiLocalization.Text(
                    "SessionHistory.Repair.CompactionConfirm",
                    "The current provider still refuses this session. Its earlier conversation is kept in {0} compacted summaries that another provider issued and the current provider cannot read.\r\n\r\nReplace them with a short note and send again? The model will no longer see that earlier conversation. A backup of the session is kept."),
                    compactions),
                UiLocalization.Text("SessionHistory.Repair.CompactionTitle", "Repair Session History"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            return Task.FromResult(InvokeRequired ? (bool)Invoke(confirm) : confirm());
        }
    }
}
