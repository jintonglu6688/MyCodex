using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal static class PluginAppServerInstallSourceResolver
    {
        public static bool CanInstall(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(plugin.MarketplacePath) &&
                !string.IsNullOrWhiteSpace(plugin.Name))
            {
                return true;
            }

            return HasRemoteMarketplaceInstallSource(plugin);
        }

        public static bool HasRemoteMarketplaceInstallSource(AppServerPluginSummary plugin)
        {
            return plugin != null &&
                string.IsNullOrWhiteSpace(plugin.MarketplacePath) &&
                IsRemoteMarketplacePlugin(plugin) &&
                !string.IsNullOrWhiteSpace(ResolveRemoteMarketplaceName(plugin)) &&
                !string.IsNullOrWhiteSpace(ResolveRemotePluginId(plugin));
        }

        public static bool IsRemoteMarketplacePlugin(AppServerPluginSummary plugin)
        {
            if (plugin == null || IsLocalSourceType(plugin.SourceType))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(plugin.MarketplacePath) &&
                (string.Equals(plugin.SourceType, "remote", StringComparison.OrdinalIgnoreCase) ||
                 !string.IsNullOrWhiteSpace(plugin.RemotePluginId) ||
                 !string.IsNullOrWhiteSpace(GetPluginMarketplaceSuffix(plugin.PluginId)));
        }

        public static string ResolveRemoteMarketplaceName(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return null;
            }

            var marketplaceId = Normalize(plugin.MarketplaceId);
            if (!string.IsNullOrWhiteSpace(marketplaceId))
            {
                return marketplaceId;
            }

            var suffix = GetPluginMarketplaceSuffix(plugin.PluginId);
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                return suffix;
            }

            return Normalize(plugin.MarketplaceName);
        }

        public static string ResolveRemotePluginId(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return null;
            }

            var remotePluginId = Normalize(plugin.RemotePluginId);
            if (!string.IsNullOrWhiteSpace(remotePluginId))
            {
                return remotePluginId;
            }

            var pluginName = Normalize(plugin.Name);
            if (!string.IsNullOrWhiteSpace(pluginName))
            {
                return pluginName;
            }

            return Normalize(GetPluginBaseKey(plugin.PluginId));
        }

        public static string ResolveUninstallPluginId(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return null;
            }

            if (IsRemoteMarketplacePlugin(plugin))
            {
                return ResolveRemotePluginId(plugin) ?? Normalize(plugin.PluginId);
            }

            return Normalize(plugin.PluginId);
        }

        public static List<string> BuildCacheKeys(AppServerPluginSummary plugin)
        {
            var keys = new List<string>();
            if (plugin == null)
            {
                return keys;
            }

            AddKey(keys, plugin.PluginId);
            if (!string.IsNullOrWhiteSpace(plugin.MarketplacePath) &&
                !string.IsNullOrWhiteSpace(plugin.Name))
            {
                AddKey(keys, plugin.MarketplacePath.Trim() + "|" + plugin.Name.Trim());
            }

            AddKey(keys, plugin.Name);
            AddKey(keys, plugin.RemotePluginId);
            AddKey(keys, GetPluginBaseKey(plugin.PluginId));

            var remoteMarketplaceName = ResolveRemoteMarketplaceName(plugin);
            var remotePluginId = ResolveRemotePluginId(plugin);
            if (!string.IsNullOrWhiteSpace(remoteMarketplaceName) &&
                !string.IsNullOrWhiteSpace(remotePluginId))
            {
                AddKey(keys, remotePluginId + "@" + remoteMarketplaceName);
            }

            return keys;
        }

        private static void AddKey(List<string> keys, string value)
        {
            var key = Normalize(value);
            if (string.IsNullOrWhiteSpace(key) ||
                keys.Any(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            keys.Add(key);
        }

        private static bool IsLocalSourceType(string sourceType)
        {
            return string.Equals(sourceType, "local", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceType, "market", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sourceType, "runtime", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPluginMarketplaceSuffix(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                return null;
            }

            var text = pluginId.Trim();
            var separatorIndex = text.LastIndexOf('@');
            return separatorIndex > 0 && separatorIndex < text.Length - 1
                ? text.Substring(separatorIndex + 1)
                : null;
        }

        private static string GetPluginBaseKey(string pluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
            {
                return null;
            }

            var text = pluginId.Trim();
            var separatorIndex = text.LastIndexOf('@');
            return separatorIndex > 0 ? text.Substring(0, separatorIndex) : text;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
