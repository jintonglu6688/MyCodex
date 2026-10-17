using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;

namespace MyCodex.WinForms.Forms
{
    internal static class PluginMarketListComposer
    {
        public static List<PluginMarketListItem> ComposeAvailableItems(
            IEnumerable<AppServerPluginSummary> plugins,
            IEnumerable<PluginMarketListItem> remoteItems,
            string runtimeMarketName)
        {
            var pluginList = plugins == null
                ? new List<AppServerPluginSummary>()
                : plugins.Where(item => item != null).ToList();
            var availableEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var registeredMarketplaces = new HashSet<string>(
                pluginList
                    .Select(AppServerPluginIdentity.FromSummary)
                    .Where(identity => identity != null)
                    .Select(identity => identity.MarketplaceName),
                StringComparer.OrdinalIgnoreCase);
            var items = new List<PluginMarketListItem>();

            foreach (var plugin in pluginList.Where(item => !item.Installed))
            {
                var identity = AppServerPluginIdentity.FromSummary(plugin);
                var pluginId = identity == null ? GetPluginIdentity(plugin) : identity.PluginName;
                var marketName = string.IsNullOrWhiteSpace(plugin.MarketplaceName)
                    ? runtimeMarketName
                    : plugin.MarketplaceName.Trim();
                var entryKey = identity == null ? BuildEntryKey(marketName, pluginId) : identity.Key;
                if (!string.IsNullOrWhiteSpace(entryKey) && !availableEntries.Add(entryKey))
                {
                    continue;
                }

                items.Add(new PluginMarketListItem
                {
                    MarketName = marketName,
                    PluginId = pluginId,
                    DisplayName = GetPluginDisplayName(plugin),
                    Summary = GetPluginSummary(plugin),
                    IsRemoteCatalog = false,
                    AppServerPlugin = plugin
                });
            }

            foreach (var item in (remoteItems ?? Enumerable.Empty<PluginMarketListItem>()).Where(item => item != null))
            {
                var pluginId = string.IsNullOrWhiteSpace(item.PluginId) ? null : item.PluginId.Trim();
                var marketId = item.RemoteEntry == null ? null : item.RemoteEntry.MarketId;
                var identity = AppServerPluginIdentity.Create(pluginId, marketId);
                if (identity != null && registeredMarketplaces.Contains(identity.MarketplaceName))
                {
                    continue;
                }

                var entryKey = identity == null ? BuildEntryKey(item.MarketName, pluginId) : identity.Key;
                if (!string.IsNullOrWhiteSpace(entryKey) && !availableEntries.Add(entryKey))
                {
                    continue;
                }

                items.Add(item);
            }

            return items;
        }

        private static string BuildEntryKey(string marketName, string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                return null;
            }

            var normalizedMarketName = string.IsNullOrWhiteSpace(marketName) ? string.Empty : marketName.Trim();
            return normalizedMarketName + "|" + pluginId.Trim();
        }

        private static string GetPluginIdentity(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return null;
            }

            var pluginId = !string.IsNullOrWhiteSpace(plugin.PluginId) ? plugin.PluginId : plugin.Name;
            return string.IsNullOrWhiteSpace(pluginId) ? null : pluginId.Trim();
        }

        private static string GetPluginDisplayName(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(plugin.DisplayName))
            {
                return plugin.DisplayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(plugin.Name))
            {
                return plugin.Name.Trim();
            }

            return string.Empty;
        }

        private static string GetPluginSummary(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return string.Empty;
            }

            var summary = !string.IsNullOrWhiteSpace(plugin.ShortDescription)
                ? plugin.ShortDescription
                : plugin.LongDescription;
            var prefixParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(plugin.MarketplaceName))
            {
                prefixParts.Add(plugin.MarketplaceName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(plugin.Category))
            {
                prefixParts.Add(plugin.Category.Trim());
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                return prefixParts.Count == 0 ? string.Empty : string.Join(" / ", prefixParts);
            }

            summary = summary.Replace("\r", " ").Replace("\n", " ").Trim();
            summary = summary.Length <= 88 ? summary : summary.Substring(0, 88) + "...";
            if (prefixParts.Count == 0)
            {
                return summary;
            }

            return string.Join(" / ", prefixParts) + "; " + summary;
        }
    }
}
