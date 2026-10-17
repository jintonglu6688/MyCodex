using System;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerProtocolCapabilityGuards
    {
        public static void EnsureSupported(
            AppServerProtocolCapabilities capabilities,
            bool isSupported,
            string featureName)
        {
            if (capabilities == null || isSupported)
            {
                return;
            }

            throw new InvalidOperationException(
                string.Format(
                    SharedLocalization.Text(
                        "AppServerProtocolCapabilities.Error.FeatureNotSupported.Template",
                        "{0} is not supported by the current Codex runtime. Switch to a standard or newer runtime to use this feature."),
                    string.IsNullOrWhiteSpace(featureName)
                        ? SharedLocalization.Text("AppServerProtocolCapabilities.Feature.ThisFeature", "This feature")
                        : featureName.Trim()));
        }
    }
}
