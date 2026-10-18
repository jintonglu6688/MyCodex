using System;
using System.Collections.Generic;
using System.Linq;

namespace MyCodex.WinForms.Forms
{
    public static class PluginMarketFilter
    {
        public static IEnumerable<PluginMarketListItem> Apply(
            IEnumerable<PluginMarketListItem> items,
            string marketName,
            string keyword,
            string category = null)
        {
            if (items == null)
            {
                return Array.Empty<PluginMarketListItem>();
            }

            IEnumerable<PluginMarketListItem> query = items.Where(item => item != null);

            if (!string.IsNullOrWhiteSpace(marketName))
            {
                query = query.Where(item => string.Equals(item.MarketName, marketName, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(item => string.Equals(GetCategory(item), category.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var normalizedKeyword = keyword.Trim();
                query = query.Where(item => MatchesKeyword(item, normalizedKeyword));
            }

            return query;
        }

        private static bool MatchesKeyword(PluginMarketListItem item, string keyword)
        {
            return Contains(item.DisplayName, keyword)
                || Contains(item.PluginId, keyword)
                || Contains(item.Summary, keyword);
        }

        private static string GetCategory(PluginMarketListItem item)
        {
            if (item == null)
            {
                return null;
            }

            if (item.RemoteEntry != null && !string.IsNullOrWhiteSpace(item.RemoteEntry.Category))
            {
                return item.RemoteEntry.Category;
            }

            return item.AppServerPlugin == null ? null : item.AppServerPlugin.Category;
        }

        private static bool Contains(string value, string keyword)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
