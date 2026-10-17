using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    public static class AppServerInstalledPluginObservationService
    {
        public static async Task<AppServerPluginInstalledSnapshot> LoadAsync(
            IAppServerPluginClient client,
            AppServerProtocolCapabilities capabilities,
            CancellationToken cancellationToken)
        {
            return await LoadAsync(client, capabilities, null, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<AppServerPluginInstalledSnapshot> LoadAsync(
            IAppServerPluginClient client,
            AppServerProtocolCapabilities capabilities,
            IEnumerable<AppServerPluginSummary> catalog,
            CancellationToken cancellationToken)
        {
            if (client == null)
            {
                return new AppServerPluginInstalledSnapshot();
            }

            if (capabilities != null && capabilities.SupportsInstalledPluginObservation)
            {
                return await client.GetInstalledPluginsAsync(cancellationToken).ConfigureAwait(false) ??
                    new AppServerPluginInstalledSnapshot();
            }

            var catalogPlugins = catalog == null
                ? await client.ListPluginsAsync(cancellationToken).ConfigureAwait(false)
                : catalog.ToList();
            return new AppServerPluginInstalledSnapshot
            {
                Plugins = (catalogPlugins ?? new List<AppServerPluginSummary>())
                    .Where(plugin => plugin != null && plugin.Installed)
                    .ToList()
            };
        }
    }
}
