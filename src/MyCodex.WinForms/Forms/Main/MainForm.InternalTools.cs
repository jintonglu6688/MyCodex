using MyCodex.Application.DTOs;
using MyCodex.Application.InternalTools;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void HandleInternalToolStatusEvent(AppServerEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (e.GuardianApprovalReview != null)
            {
                HandleGuardianApprovalReview(e);
                return;
            }

            var toolName = string.IsNullOrWhiteSpace(e.McpToolName) ? e.Content : e.McpToolName;
            if (string.Equals(e.EventType, "internal_tool_running", System.StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(string.Format(
                    UiLocalization.Text("MainForm.Status.InternalToolRunning", "Running internal tool: {0}"),
                    toolName));
                AppendLog(string.Format(
                    UiLocalization.Text("MainForm.Log.InternalToolRunning", "Internal tool running: {0}"),
                    toolName));
                return;
            }

            if (string.Equals(e.EventType, "internal_tool_failed", System.StringComparison.OrdinalIgnoreCase))
            {
                SetHighPriorityStatus(string.Format(
                    UiLocalization.Text("MainForm.Status.InternalToolFailed", "Internal tool failed: {0}"),
                    toolName));
                AppendLog(string.Format(
                    UiLocalization.Text("MainForm.Log.InternalToolCompleted", "Internal tool finished: {0}"),
                    e.Content));
                return;
            }

            SetStatus(string.Format(
                UiLocalization.Text("MainForm.Status.InternalToolCompleted", "Internal tool completed: {0}"),
                toolName));
            AppendLog(string.Format(
                UiLocalization.Text("MainForm.Log.InternalToolCompleted", "Internal tool finished: {0}"),
                e.Content));
        }

        private AppServerThreadToolContext BuildThreadToolContext(ChatSession session, WorkspaceInfo workspace)
        {
            if (session != null && session.Mode == AppInteractionMode.Office)
            {
                return null;
            }

            return InternalToolThreadContextBuilder.BuildForSession(_internalToolRegistry, session, workspace);
        }
    }
}
