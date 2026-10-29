using System;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.ComputerUse;

namespace MyCodex.Infrastructure.AppServer
{
    public partial class AppServerClient : IBrowserControlRuntime
    {
        public IDisposable BeginBrowserControlSetup(string marketplaceRoot)
        {
            return AppServerBrowserSetupScope.Enter(this, marketplaceRoot);
        }

        public Task RevokeBrowserControlAsync()
        {
            ComputerUseSessionManager manager;
            lock (_syncRoot) manager = _computerUse;
            return manager == null ? Task.CompletedTask : manager.RevokeBrowserAsync();
        }
    }
}
