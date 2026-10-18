using System;

namespace MyCodex.WinForms.Forms
{
    internal static class PluginMarketStatusResolver
    {
        public static PluginMarketAvailabilityStatus ResolveRemoteStatus(
            bool isInstalled,
            string installedVersion,
            string availableVersion)
        {
            if (!isInstalled)
            {
                return PluginMarketAvailabilityStatus.InstallAvailable;
            }

            if (IsRemoteVersionNewer(installedVersion, availableVersion))
            {
                return PluginMarketAvailabilityStatus.UpdateAvailable;
            }

            return PluginMarketAvailabilityStatus.None;
        }

        public static bool IsRemoteVersionNewer(string installedVersion, string availableVersion)
        {
            if (string.IsNullOrWhiteSpace(installedVersion) || string.IsNullOrWhiteSpace(availableVersion))
            {
                return false;
            }

            Version installed;
            Version available;
            if (!Version.TryParse(installedVersion.Trim(), out installed) ||
                !Version.TryParse(availableVersion.Trim(), out available))
            {
                return false;
            }

            return available.CompareTo(installed) > 0;
        }
    }
}
