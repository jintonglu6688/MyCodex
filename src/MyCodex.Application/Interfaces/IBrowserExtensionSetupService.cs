using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IBrowserExtensionSetupService
    {
        Task<BrowserExtensionSetupReport> CheckAsync(BrowserExtensionBrowser browser, CancellationToken cancellationToken);
        Task<bool> PrepareAsync(CancellationToken cancellationToken);
        void OpenPage(BrowserExtensionBrowser browser, BrowserExtensionSetupPage page);
    }
}
