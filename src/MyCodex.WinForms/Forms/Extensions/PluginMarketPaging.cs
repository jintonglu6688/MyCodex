using System;
using System.Collections.Generic;

namespace MyCodex.WinForms.Forms
{
    public sealed class PluginMarketNavigationItem
    {
        public PluginMarketNavigationItem(int targetPage, string label)
        {
            TargetPage = targetPage;
            Label = label ?? string.Empty;
        }

        public int TargetPage { get; private set; }

        public string Label { get; private set; }
    }

    public static class PluginMarketPaging
    {
        public const int PageSize = 100;

        public static int GetTotalPages(int totalCount)
        {
            return totalCount <= 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)PageSize);
        }

        public static PluginMarketNavigationItem[] GetNavigationItems(
            int page,
            int totalCount,
            string previousLabel,
            string nextLabel)
        {
            var totalPages = GetTotalPages(totalCount);
            if (totalPages <= 1)
            {
                return new PluginMarketNavigationItem[0];
            }

            var normalizedPage = Math.Max(1, Math.Min(page, totalPages));
            var items = new List<PluginMarketNavigationItem>();
            if (normalizedPage > 1)
            {
                items.Add(new PluginMarketNavigationItem(normalizedPage - 1, previousLabel));
            }

            if (normalizedPage < totalPages)
            {
                items.Add(new PluginMarketNavigationItem(normalizedPage + 1, nextLabel));
            }

            return items.ToArray();
        }
    }
}
