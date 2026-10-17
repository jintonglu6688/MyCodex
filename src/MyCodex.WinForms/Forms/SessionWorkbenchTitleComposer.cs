using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionWorkbenchTitleComposer
    {
        internal static string Build(WorkspaceInfo workspace, ChatSession session)
        {
            var workspaceName = workspace == null || string.IsNullOrWhiteSpace(workspace.Name)
                ? UiLocalization.Text("MainForm.Value.UnknownWorkspace", "Workspace")
                : workspace.Name.Trim();
            var sessionTitle = session == null
                ? UiLocalization.Text("MainForm.Value.UnnamedSession", "Unnamed session")
                : SessionDisplayFormatter.BuildDisplayTitle(session);
            return workspaceName + " - " + sessionTitle;
        }

        internal static string Build(WorkspaceInfo workspace, ChatSession session, bool isWorking)
        {
            var title = Build(workspace, session);
            return !isWorking
                ? title
                : SessionDisplayFormatter.BuildWorkingStatusText(session) + " - " + title;
        }
    }
}
