using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    public static class MyCodexOfficialMarketplaceBootstrap
    {
        public const string MarketplaceId = "mycodex-official";
        public const string MarketplaceSource = "https://market.jtl.world/git/mycodex-official.git";

        public static async Task<AppServerMarketplaceRegistrationResult> EnsureRegisteredAsync(
            IAppServerPluginClient client,
            AppServerProtocolCapabilities capabilities,
            CancellationToken cancellationToken)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (!MarketplaceMutationPolicy.EvaluateRegistration(capabilities).IsAvailable)
            {
                return null;
            }

            var plugins = await client.ListPluginsAsync(cancellationToken).ConfigureAwait(false) ??
                          new List<AppServerPluginSummary>();
            if (plugins.Any(IsFromMyCodexOfficialMarketplace))
            {
                return null;
            }

            return await RegisterPinnedMarketplaceAsync(client, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<AppServerMarketplaceRegistrationResult> RegisterPinnedMarketplaceAsync(
            IAppServerPluginClient client,
            CancellationToken cancellationToken)
        {
            try
            {
                return await AppServerMarketplaceRegistrationService.RegisterMarketplaceAsync(
                        client,
                        MarketplaceSource,
                        MarketplaceId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (IsDifferentSourceConflict(ex.Message))
            {
                await client.RemoveMarketplaceAsync(MarketplaceId, cancellationToken).ConfigureAwait(false);
                return await AppServerMarketplaceRegistrationService.RegisterMarketplaceAsync(
                        client,
                        MarketplaceSource,
                        MarketplaceId,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static bool IsDifferentSourceConflict(string message)
        {
            return !string.IsNullOrWhiteSpace(message) &&
                   message.IndexOf(MarketplaceId, StringComparison.OrdinalIgnoreCase) >= 0 &&
                   message.IndexOf("already added from a different source", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   message.IndexOf("remove it before adding this source", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsFromMyCodexOfficialMarketplace(AppServerPluginSummary plugin)
        {
            var identity = AppServerPluginIdentity.FromSummary(plugin);
            return identity != null &&
                   string.Equals(identity.MarketplaceName, MarketplaceId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
