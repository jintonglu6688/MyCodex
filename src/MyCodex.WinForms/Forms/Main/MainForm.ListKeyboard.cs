using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async void WorkspaceList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Apps)
            {
                ShowPopupMenuForListViewSelection(_workspaceContextMenu, _workspaceList);
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                await DeleteSelectedWorkspaceAsync();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            await OpenSelectedWorkspaceFromListAsync();
            e.Handled = true;
        }

        private async Task DeleteSelectedWorkspaceAsync()
        {
            var selectedItem = GetSelectedWorkspaceListItem();
            if (selectedItem == null)
            {
                return;
            }

            await ExecuteWorkspaceDeletionAsync(selectedItem.Workspace, LocalSessionRemovalMode.LocalOnly);
        }

        private async void SessionList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Apps)
            {
                ShowPopupMenuForListViewSelection(_sessionContextMenu, _sessionList);
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                await DeleteSelectedSessionAsync();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.F2)
            {
                await RenameSelectedSessionAsync();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.Enter)
            {
                OpenSelectedSessionWorkbench();
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            await OpenSelectedSessionFromListAsync();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        private async Task DeleteSelectedSessionAsync()
        {
            var selectedItem = GetSelectedSessionListItem();
            if (selectedItem == null)
            {
                return;
            }

            await ExecuteSessionDeletionAsync(selectedItem.Session, LocalSessionRemovalMode.LocalOnly);
        }

        private async void OnSessionRenameMenuItemClick(object sender, EventArgs e)
        {
            await RenameSelectedSessionAsync();
        }

        private async Task RenameSelectedSessionAsync()
        {
            var selectedItem = GetSelectedSessionListItem();
            if (selectedItem == null)
            {
                return;
            }

            await ShowRenameSessionDialogAsync(selectedItem.Session);
        }
    }
}
