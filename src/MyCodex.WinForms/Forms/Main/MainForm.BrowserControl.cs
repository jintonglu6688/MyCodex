using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.BrowserUse;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private BrowserControlOptionsCoordinator _browserControlOptions;

        private async Task AddBrowserControlOptionsAsync(OptionsForm form)
        {
            form.AddBrowserControlPage(await GetBrowserControlOptions().CreatePageAsync(_appConfigSnapshot.BrowserControlEnabled));
        }

        private BrowserControlOptionsCoordinator GetBrowserControlOptions()
        {
            if (_browserControlOptions == null)
                _browserControlOptions = new BrowserControlOptionsCoordinator(this, _appServerClient,
                    new WindowsBrowserExtensionSetupService(_appServerClient), TryAcquireBrowserControlTransition,
                    ReconnectAfterAuthenticationAsync, SetHighPriorityStatus);
            return _browserControlOptions;
        }

        private Task RestoreBrowserControlAsync()
        {
            return _appConfigSnapshot.BrowserControlEnabled == true && _appServerClient.ProtocolCapabilities?.SupportsBrowserUseIntegration == true
                ? GetBrowserControlOptions().ApplyAsync(true, false) : Task.CompletedTask;
        }

        private IDisposable TryAcquireBrowserControlTransition()
        {
            if (IsTargetStopping || Form.ActiveForm?.Modal == true || _targetWindowGroup?.HasNativeModal == true) return null;
            try { return AcquireAuthenticationTransition(); }
            catch (AuthenticationCenterException) { return null; }
        }
    }
}
