using System;
using System.Collections.Generic;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionHistoryOptimisticMerge
    {
        internal static ChatHistoryItem[] PreserveLocalConversationItems(
            IEnumerable<ChatHistoryItem> localItems,
            ChatHistoryItem[] remoteItems)
        {
            var result = new List<ChatHistoryItem>();
            if (remoteItems != null && remoteItems.Length > 0)
            {
                result.AddRange(remoteItems);
            }

            if (localItems == null)
            {
                return result.ToArray();
            }

            var localItemArray = ToArray(localItems);
            var optimisticStartIndex = FindOptimisticTailStartIndex(localItemArray, remoteItems);
            for (var index = optimisticStartIndex; index < localItemArray.Length; index++)
            {
                var item = localItemArray[index];
                if (!IsUserConversationItem(item))
                {
                    continue;
                }

                result.Add(item);
            }

            return result.ToArray();
        }

        private static ChatHistoryItem[] ToArray(IEnumerable<ChatHistoryItem> items)
        {
            if (items == null)
            {
                return Array.Empty<ChatHistoryItem>();
            }

            var result = new List<ChatHistoryItem>();
            foreach (var item in items)
            {
                result.Add(item);
            }

            return result.ToArray();
        }

        private static int FindOptimisticTailStartIndex(
            ChatHistoryItem[] localItems,
            IEnumerable<ChatHistoryItem> remoteItems)
        {
            if (localItems == null || localItems.Length == 0)
            {
                return 0;
            }

            var remoteItemCounts = BuildItemCounts(remoteItems);
            var lastConfirmedLocalIndex = -1;
            for (var index = 0; index < localItems.Length; index++)
            {
                var key = BuildSignature(localItems[index]);
                int count;
                if (string.IsNullOrWhiteSpace(key) ||
                    !remoteItemCounts.TryGetValue(key, out count) ||
                    count <= 0)
                {
                    continue;
                }

                lastConfirmedLocalIndex = index;
                if (count == 1)
                {
                    remoteItemCounts.Remove(key);
                }
                else
                {
                    remoteItemCounts[key] = count - 1;
                }
            }

            return lastConfirmedLocalIndex + 1;
        }

        private static Dictionary<string, int> BuildItemCounts(IEnumerable<ChatHistoryItem> items)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (items == null)
            {
                return counts;
            }

            foreach (var item in items)
            {
                var key = BuildSignature(item);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                int count;
                counts.TryGetValue(key, out count);
                counts[key] = count + 1;
            }

            return counts;
        }

        private static bool IsUserConversationItem(ChatHistoryItem item)
        {
            return item != null &&
                   item.Category == ChatHistoryCategory.Conversation &&
                   ChatHistoryItemFactory.IsUserSender(item.Sender);
        }

        private static string BuildSignature(ChatHistoryItem item)
        {
            return (item.FullText ?? item.DisplayText ?? item.MarkdownText ?? string.Empty).Trim();
        }
    }
}
