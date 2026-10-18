using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void RefreshLiveWorkStatus()
        {
            // Visible navigation lists are small; an in-place scan keeps focus stable without a status index.
            foreach (System.Windows.Forms.ListViewItem listViewItem in _sessionList.Items)
            {
                var item = listViewItem.Tag as SessionListItem;
                if (item != null)
                {
                    item.UpdateWorkingState(
                        listViewItem,
                        SessionListProjection.IsWorking(
                            new SessionListEntry(item.Session, item.SubAgents),
                            IsThreadBusy));
                }
            }

            if (_currentInteractionMode == AppInteractionMode.Coding)
            {
                foreach (System.Windows.Forms.ListViewItem listViewItem in _workspaceList.Items)
                {
                    var item = listViewItem.Tag as WorkspaceListItem;
                    if (item != null && item.Workspace != null)
                    {
                        item.UpdateWorkingSessionCount(
                            listViewItem,
                            GetWorkingSessionCount(item.Workspace.Id));
                    }
                }
            }

            RefreshWindowTitle();
        }

        private int GetWorkingSessionCount(string workspaceId)
        {
            return SessionListProjection.CountWorkingEntries(
                SessionListProjection.BuildWorkspaceEntries(_sessions, workspaceId, _currentInteractionMode),
                IsThreadBusy);
        }

        private bool IsSessionWorking(ChatSession session)
        {
            if (session == null)
            {
                return false;
            }

            return SessionListProjection.IsWorking(
                new SessionListEntry(session, GetSubAgentsForParent(session)),
                IsThreadBusy);
        }
    }
}
