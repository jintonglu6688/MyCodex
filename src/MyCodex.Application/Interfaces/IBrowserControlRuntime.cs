using System;
using System.Threading.Tasks;

namespace MyCodex.Application.Interfaces
{
    public interface IBrowserControlRuntime
    {
        IDisposable BeginBrowserControlSetup(string marketplaceRoot);
        Task RevokeBrowserControlAsync();
    }
}
