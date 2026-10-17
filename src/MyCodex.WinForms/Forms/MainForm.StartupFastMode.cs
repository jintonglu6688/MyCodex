using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private bool _hasHandledStartupFastModePrompt;

        private async Task PromptForStartupFastModeIfNeededAsync()
        {
            if (_hasHandledStartupFastModePrompt)
            {
                return;
            }

            if (!StartupFastModePromptPlanner.ShouldPrompt(_globalServerConfigSnapshot))
            {
                if (_globalServerConfigSnapshot != null)
                {
                    _hasHandledStartupFastModePrompt = true;
                }

                return;
            }

            _hasHandledStartupFastModePrompt = true;
            var result = MessageBox.Show(
                this,
                UiLocalization.Text(
                    "MainForm.StartupFastModePrompt.Message",
                    "Fast mode is currently enabled. This mode consumes double quota. Continue using fast mode? Choose No to turn it off automatically."),
                UiLocalization.Text("MainForm.StartupFastModePrompt.Title", "Fast Mode Enabled"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result == DialogResult.Yes)
            {
                return;
            }

            if (await TryWriteOfficialCodexSettingsAsync(
                StartupFastModePromptPlanner.BuildDisableFastModeEdits))
            {
                SetStatus(UiLocalization.Text(
                    "MainForm.StartupFastModePrompt.Status.Disabled",
                    "Fast mode has been turned off"));
            }
        }
    }
}
