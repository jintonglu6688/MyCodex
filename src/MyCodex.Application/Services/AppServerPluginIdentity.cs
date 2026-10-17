using System;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public sealed class AppServerPluginIdentity
    {
        private AppServerPluginIdentity(string pluginName, string marketplaceName)
        {
            PluginName = pluginName;
            MarketplaceName = marketplaceName;
        }

        public string PluginName { get; private set; }

        public string MarketplaceName { get; private set; }

        public string Key
        {
            get { return PluginName + "@" + MarketplaceName; }
        }

        public static AppServerPluginIdentity FromSummary(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return null;
            }

            var pluginName = Normalize(plugin.RemotePluginId) ?? Normalize(plugin.Name);
            var marketplaceName = Normalize(plugin.MarketplaceId);
            ParseQualifiedKey(plugin.PluginId, ref pluginName, ref marketplaceName);
            return Create(pluginName, marketplaceName);
        }

        public static AppServerPluginIdentity Create(string pluginName, string marketplaceName)
        {
            pluginName = Normalize(pluginName);
            marketplaceName = Normalize(marketplaceName);
            ParseQualifiedKey(pluginName, ref pluginName, ref marketplaceName);
            return string.IsNullOrWhiteSpace(pluginName) || string.IsNullOrWhiteSpace(marketplaceName)
                ? null
                : new AppServerPluginIdentity(pluginName, marketplaceName);
        }

        private static void ParseQualifiedKey(string key, ref string pluginName, ref string marketplaceName)
        {
            key = Normalize(key);
            var separatorIndex = string.IsNullOrWhiteSpace(key) ? -1 : key.LastIndexOf('@');
            if (separatorIndex <= 0 || separatorIndex >= key.Length - 1)
            {
                return;
            }

            pluginName = key.Substring(0, separatorIndex);
            marketplaceName = key.Substring(separatorIndex + 1);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
