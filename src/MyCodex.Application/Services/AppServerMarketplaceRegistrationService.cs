using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    public static class AppServerMarketplaceRegistrationService
    {
        public static async Task<AppServerMarketplaceRegistrationResult> RegisterMarketplaceAsync(
            IAppServerPluginClient client,
            string source,
            string marketId,
            CancellationToken cancellationToken)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (!PluginMarketAppServerSourcePolicy.IsAllowed(source))
            {
                throw new InvalidOperationException("The marketplace source is unavailable.");
            }

            var before = await client.GetInstalledPluginsAsync(cancellationToken).ConfigureAwait(false) ??
                         new AppServerPluginInstalledSnapshot();
            var registration = await client.AddMarketplaceAsync(source.Trim(), cancellationToken).ConfigureAwait(false);
            var marketplaceName = registration == null || string.IsNullOrWhiteSpace(registration.MarketplaceName)
                ? Normalize(marketId)
                : registration.MarketplaceName.Trim();
            if (string.IsNullOrWhiteSpace(marketplaceName))
            {
                throw new InvalidOperationException("The registered marketplace identity is unavailable.");
            }

            var catalog = await client.ListPluginsAsync(cancellationToken).ConfigureAwait(false) ??
                          new List<AppServerPluginSummary>();
            var after = await client.GetInstalledPluginsAsync(cancellationToken).ConfigureAwait(false) ??
                        new AppServerPluginInstalledSnapshot();
            var beforeKeys = new HashSet<string>(
                (before.Plugins ?? new List<AppServerPluginSummary>())
                    .Select(AppServerPluginIdentity.FromSummary)
                    .Where(identity => identity != null)
                    .Select(identity => identity.Key),
                StringComparer.OrdinalIgnoreCase);
            var rediscoveredCount = (after.Plugins ?? new List<AppServerPluginSummary>())
                .Select(AppServerPluginIdentity.FromSummary)
                .Where(identity => identity != null &&
                                   string.Equals(identity.MarketplaceName, marketplaceName, StringComparison.OrdinalIgnoreCase) &&
                                   !beforeKeys.Contains(identity.Key))
                .Select(identity => identity.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            return new AppServerMarketplaceRegistrationResult
            {
                MarketplaceName = marketplaceName,
                RediscoveredInstalledCount = rediscoveredCount,
                CatalogPlugins = catalog.Where(plugin => plugin != null).ToList(),
                InstalledPlugins = (after.Plugins ?? new List<AppServerPluginSummary>())
                    .Where(plugin => plugin != null)
                    .ToList()
            };
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
