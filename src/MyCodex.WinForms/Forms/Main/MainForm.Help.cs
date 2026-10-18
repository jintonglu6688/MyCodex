using System;
using System.Windows.Forms;
using MyCodex.Shared.Constants;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void OnOfficialWebsiteMenuItemClick(object sender, EventArgs e)
        {
            try
            {
                ExternalUrlLauncher.Open("https://mycodex.jtl.world/");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
