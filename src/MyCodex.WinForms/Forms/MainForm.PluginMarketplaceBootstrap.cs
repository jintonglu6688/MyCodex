using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Services;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async Task EnsureMyCodexOfficialMarketplaceRegisteredAsync()
        {
            try
            {
                using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_targetShutdownCancellation.Token))
                {
                    cancellation.CancelAfter(TimeSpan.FromSeconds(30));
                    await MyCodexOfficialMarketplaceBootstrap.EnsureRegisteredAsync(
                        _appServerClient,
                        _appServerClient.ProtocolCapabilities,
                        cancellation.Token);
                }
            }
            catch (Exception)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                AppendLog(UiLocalization.Text(
                    "MainForm.Log.MyCodexOfficialMarketplaceBootstrapFailed",
                    "MyCodex Official marketplace registration was skipped because the target App Server could not complete it."));
            }
        }
    }
}
