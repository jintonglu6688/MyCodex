using System;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class SubAgentDisplayPlanner
    {
        internal static string Abbreviate(string value)
        {
            var text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            return text.Length > 160 ? text.Substring(0, 157) + "..." : text;
        }

        internal static string BuildStatus(AppServerThreadStatus threadStatus, string agentStatus)
        {
            var status = threadStatus?.Type;
            if (threadStatus?.ActiveFlags != null && threadStatus.ActiveFlags.Contains("waitingOnApproval")) status = "waitingOnApproval";
            else if (threadStatus?.ActiveFlags != null && threadStatus.ActiveFlags.Contains("waitingOnUserInput")) status = "waitingOnUserInput";
            var text = Translate(status);
            return string.IsNullOrWhiteSpace(agentStatus) ? text : string.Format(
                UiLocalization.Text("SubAgentWorkbenchForm.State.Template", "Thread: {0}; agent reported: {1}"), text, Translate(agentStatus));
        }

        private static string Translate(string value)
        {
            switch (value)
            {
                case "active": case "running": return UiLocalization.Text("SubAgentWorkbenchForm.State.Running", "Running");
                case "idle": return UiLocalization.Text("SubAgentWorkbenchForm.State.Idle", "Idle");
                case "completed": return UiLocalization.Text("SubAgentWorkbenchForm.State.Completed", "Reported complete");
                case "waitingOnApproval": return UiLocalization.Text("SubAgentWorkbenchForm.State.Approval", "Waiting for approval");
                case "waitingOnUserInput": return UiLocalization.Text("SubAgentWorkbenchForm.State.Input", "Waiting for input");
                case "notLoaded": return UiLocalization.Text("SubAgentWorkbenchForm.State.Unloaded", "Not loaded");
                case "shutdown": return UiLocalization.Text("SubAgentWorkbenchForm.State.Closed", "Agent closed");
                case "interrupted": return UiLocalization.Text("SubAgentWorkbenchForm.State.Interrupted", "Interrupted");
                case "systemError": case "errored": return UiLocalization.Text("SubAgentWorkbenchForm.State.Error", "Error");
                case "pendingInit": return UiLocalization.Text("SubAgentWorkbenchForm.State.Starting", "Starting");
                default: return UiLocalization.Text("SubAgentWorkbenchForm.State.Unknown", "Unknown");
            }
        }
    }
}
