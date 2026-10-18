using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal static class PluginUninstallWorkflow
    {
        public static async Task ExecuteAsync(
            Func<Task> uninstallFromRuntimeAsync,
            Action removeTrackedPlugin)
        {
            if (uninstallFromRuntimeAsync == null)
            {
                throw new ArgumentNullException(nameof(uninstallFromRuntimeAsync));
            }

            await uninstallFromRuntimeAsync();
            removeTrackedPlugin?.Invoke();
        }

        public static bool IsStillInstalled(
            AppServerPluginSummary target,
            IEnumerable<AppServerPluginSummary> refreshedPlugins)
        {
            if (target == null || refreshedPlugins == null)
            {
                return false;
            }

            return refreshedPlugins.Any(candidate =>
                candidate != null &&
                candidate.Installed &&
                RefersToSamePlugin(target, candidate));
        }

        private static bool RefersToSamePlugin(
            AppServerPluginSummary target,
            AppServerPluginSummary candidate)
        {
            var targetRemoteId = Normalize(target.RemotePluginId);
            var candidateRemoteId = Normalize(candidate.RemotePluginId);
            if (!string.IsNullOrWhiteSpace(targetRemoteId) &&
                !string.IsNullOrWhiteSpace(candidateRemoteId))
            {
                return string.Equals(targetRemoteId, candidateRemoteId, StringComparison.OrdinalIgnoreCase);
            }

            var targetId = Normalize(target.PluginId);
            var candidateId = Normalize(candidate.PluginId);
            if (!string.IsNullOrWhiteSpace(targetId) &&
                string.Equals(targetId, candidateId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (HasMarketplaceQualifier(targetId) && HasMarketplaceQualifier(candidateId))
            {
                return false;
            }

            var targetMarketplace = PluginAppServerInstallSourceResolver.ResolveRemoteMarketplaceName(target);
            var candidateMarketplace = PluginAppServerInstallSourceResolver.ResolveRemoteMarketplaceName(candidate);
            var targetName = Normalize(target.Name);
            var candidateName = Normalize(candidate.Name);
            if (!string.IsNullOrWhiteSpace(targetMarketplace) &&
                !string.IsNullOrWhiteSpace(candidateMarketplace) &&
                !string.IsNullOrWhiteSpace(targetName) &&
                !string.IsNullOrWhiteSpace(candidateName))
            {
                return string.Equals(targetMarketplace, candidateMarketplace, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(targetName, candidateName, StringComparison.OrdinalIgnoreCase);
            }

            var targetKeys = new HashSet<string>(
                PluginAppServerInstallSourceResolver.BuildCacheKeys(target),
                StringComparer.OrdinalIgnoreCase);
            return PluginAppServerInstallSourceResolver.BuildCacheKeys(candidate)
                .Any(targetKeys.Contains);
        }

        private static bool HasMarketplaceQualifier(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                return false;
            }

            var separatorIndex = pluginId.IndexOf('@');
            return separatorIndex > 0 && separatorIndex < pluginId.Length - 1;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
