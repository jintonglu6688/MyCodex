using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Infrastructure.Updates;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async Task<bool> InstallDownloadedUpdateAsync(string installerPath)
        {
            if (_targetCatalog == null || _targetInstance == null || IsTargetExitPreparing || _isExitRequested) return false;
            using (var current = Process.GetCurrentProcess())
                if (!string.Equals(Path.GetFileName(current.MainModule.FileName), "MyCodex.exe", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, UiLocalization.Text("MainForm.Update.DevInstaller", "This installer updates the release application. Build and replace the development version to update this development window."),
                        _appDisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
            // Use the same standalone entry as a manual installation. The installer
            // coordinates save/exit and can recover from an old client's broken exit UI.
            return await Task.Run(() =>
            {
                using (var installer = Process.Start(AppUpdateService.CreateInstallerStartInfo(
                    installerPath, AppDomain.CurrentDomain.BaseDirectory)))
                    return installer != null;
            });
        }
    }
}
