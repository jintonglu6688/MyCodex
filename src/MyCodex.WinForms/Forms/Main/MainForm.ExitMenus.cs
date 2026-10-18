using System;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void ConfigureAllTargetExitMenus()
        {
            var text = UiLocalization.Text("MainForm.ExitAll.Menu", "Exit all MyCodex targets(&Q)...");
            var fileExit = new MenuItem(text);
            var trayExit = new MenuItem(text);
            fileExit.Click += async (sender, args) => await RunAllTargetExitAsync();
            trayExit.Click += async (sender, args) => await RunAllTargetExitAsync();
            _fileMenuItem.MenuItems.Add(_exitMenuItem.Index, fileExit);
            _trayContextMenu.MenuItems.Add(trayExit);
        }
    }
}
