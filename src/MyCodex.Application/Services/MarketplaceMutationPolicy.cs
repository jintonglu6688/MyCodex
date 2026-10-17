using System;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public sealed class MarketplaceMutationAvailability
    {
        internal MarketplaceMutationAvailability(bool isAvailable, string reasonCode)
        {
            IsAvailable = isAvailable;
            ReasonCode = reasonCode;
        }

        public bool IsAvailable { get; private set; }

        public string ReasonCode { get; private set; }
    }

    public static class MarketplaceMutationPolicy
    {
        public static MarketplaceMutationAvailability EvaluateRegistration(
            AppServerProtocolCapabilities capabilities)
        {
            return EvaluateValidatedCapability(
                capabilities,
                value => value.SupportsMarketplaceRegistration);
        }

        public static MarketplaceMutationAvailability EvaluatePluginMutation(
            AppServerProtocolCapabilities capabilities)
        {
            return EvaluateValidatedCapability(
                capabilities,
                value => value.SupportsPluginMutation);
        }

        private static MarketplaceMutationAvailability EvaluateValidatedCapability(
            AppServerProtocolCapabilities capabilities,
            Func<AppServerProtocolCapabilities, bool> isSupported)
        {
            if (capabilities == null)
            {
                return Unavailable("plugin-capability-unavailable");
            }

            return isSupported(capabilities)
                ? Available()
                : Unavailable("plugin-capability-unavailable");
        }

        private static MarketplaceMutationAvailability Available()
        {
            return new MarketplaceMutationAvailability(true, null);
        }

        private static MarketplaceMutationAvailability Unavailable(string reasonCode)
        {
            return new MarketplaceMutationAvailability(false, reasonCode);
        }
    }
}
