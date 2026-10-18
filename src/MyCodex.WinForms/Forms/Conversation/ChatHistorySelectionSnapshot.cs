using System;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ChatHistorySelectionSnapshot
    {
        private ChatHistorySelectionSnapshot(
            string threadId,
            string turnId,
            int turnIndex,
            string sender,
            ChatHistoryCategory category,
            string markdownText,
            string fullText,
            int selectedIndex)
        {
            ThreadId = threadId ?? string.Empty;
            TurnId = turnId ?? string.Empty;
            TurnIndex = turnIndex;
            Sender = sender ?? string.Empty;
            Category = category;
            MarkdownText = markdownText ?? string.Empty;
            FullText = fullText ?? string.Empty;
            SelectedIndex = selectedIndex;
        }

        private string ThreadId { get; }

        private string TurnId { get; }

        private int TurnIndex { get; }

        private string Sender { get; }

        private ChatHistoryCategory Category { get; }

        private string MarkdownText { get; }

        private string FullText { get; }

        private int SelectedIndex { get; }

        internal static ChatHistorySelectionSnapshot Capture(ChatHistoryItem selectedItem, int selectedIndex)
        {
            return Capture(selectedItem, selectedIndex, true);
        }

        internal static ChatHistorySelectionSnapshot Capture(ChatHistoryItem selectedItem, int selectedIndex, bool preserveSelection)
        {
            if (selectedItem == null && selectedIndex < 0)
            {
                return null;
            }

            if (!preserveSelection)
            {
                return null;
            }

            return new ChatHistorySelectionSnapshot(
                selectedItem != null ? selectedItem.ThreadId : null,
                selectedItem != null ? selectedItem.TurnId : null,
                selectedItem != null ? selectedItem.TurnIndex : 0,
                selectedItem != null ? selectedItem.Sender : null,
                selectedItem != null ? selectedItem.Category : ChatHistoryCategory.System,
                selectedItem != null ? selectedItem.MarkdownText : null,
                selectedItem != null ? selectedItem.FullText : null,
                selectedIndex);
        }

        internal int ResolveSelectedIndex(ChatHistoryItem[] items)
        {
            if (items == null || items.Length == 0)
            {
                return -1;
            }

            var resolvedIndex = ResolveMatchingIndex(items);
            if (resolvedIndex >= 0)
            {
                return resolvedIndex;
            }

            if (SelectedIndex < 0)
            {
                return -1;
            }

            return Math.Min(SelectedIndex, items.Length - 1);
        }

        internal bool Matches(ChatHistoryItem item)
        {
            return item != null && ResolveMatchingIndex(new[] { item }) >= 0;
        }

        internal int ResolveMatchingIndex(ChatHistoryItem[] items)
        {
            if (items == null || items.Length == 0)
            {
                return -1;
            }

            var resolvedIndex = FindByFullText(items);
            if (resolvedIndex >= 0)
            {
                return resolvedIndex;
            }

            resolvedIndex = FindByMarkdownText(items);
            if (resolvedIndex >= 0)
            {
                return resolvedIndex;
            }

            resolvedIndex = FindByTurnId(items);
            if (resolvedIndex >= 0)
            {
                return resolvedIndex;
            }

            return FindByTurnIndex(items);
        }

        private int FindByTurnId(ChatHistoryItem[] items)
        {
            if (string.IsNullOrWhiteSpace(TurnId))
            {
                return -1;
            }

            return FindUniqueMatch(
                items,
                item => string.Equals(item.TurnId, TurnId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(item.ThreadId ?? string.Empty, ThreadId, StringComparison.OrdinalIgnoreCase) &&
                        item.Category == Category &&
                        IsEquivalentSender(item.Sender));
        }

        private int FindByTurnIndex(ChatHistoryItem[] items)
        {
            if (TurnIndex <= 0)
            {
                return -1;
            }

            return FindUniqueMatch(
                items,
                item => item.TurnIndex == TurnIndex &&
                        string.Equals(item.ThreadId ?? string.Empty, ThreadId, StringComparison.OrdinalIgnoreCase) &&
                        item.Category == Category &&
                        IsEquivalentSender(item.Sender));
        }

        private int FindByFullText(ChatHistoryItem[] items)
        {
            if (string.IsNullOrWhiteSpace(FullText))
            {
                return -1;
            }

            return FindUniqueMatch(
                items,
                item => item.Category == Category &&
                        IsEquivalentSender(item.Sender) &&
                        string.Equals(item.FullText ?? string.Empty, FullText, StringComparison.Ordinal));
        }

        private int FindByMarkdownText(ChatHistoryItem[] items)
        {
            if (string.IsNullOrWhiteSpace(MarkdownText))
            {
                return -1;
            }

            return FindUniqueMatch(
                items,
                item => item.Category == Category &&
                        IsEquivalentSender(item.Sender) &&
                        string.Equals(item.MarkdownText ?? string.Empty, MarkdownText, StringComparison.Ordinal));
        }

        private static int FindUniqueMatch(ChatHistoryItem[] items, Predicate<ChatHistoryItem> isMatch)
        {
            var matchingIndex = -1;
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item == null || !isMatch(item))
                {
                    continue;
                }

                if (matchingIndex >= 0)
                {
                    return -1;
                }

                matchingIndex = i;
            }

            return matchingIndex;
        }

        private bool IsEquivalentSender(string sender)
        {
            if (string.Equals(sender ?? string.Empty, Sender, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (ChatHistoryItemFactory.IsUserSender(sender) &&
                ChatHistoryItemFactory.IsUserSender(Sender))
            {
                return true;
            }

            if (IsCodexSender(sender) && IsCodexSender(Sender))
            {
                return true;
            }

            if (IsReviewSender(sender) && IsReviewSender(Sender))
            {
                return true;
            }

            if (IsSystemSender(sender) && IsSystemSender(Sender))
            {
                return true;
            }

            if (IsStatusSender(sender) && IsStatusSender(Sender))
            {
                return true;
            }

            return IsErrorSender(sender) && IsErrorSender(Sender);
        }

        private static bool IsCodexSender(string sender)
        {
            return string.Equals(sender, "codex", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sender, "Codex", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsReviewSender(string sender)
        {
            return string.Equals(sender, "Review", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sender, "\u5ba1\u6838", StringComparison.Ordinal);
        }

        private static bool IsSystemSender(string sender)
        {
            return string.Equals(sender, "System", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sender, "\u7cfb\u7edf", StringComparison.Ordinal);
        }

        private static bool IsStatusSender(string sender)
        {
            return string.Equals(sender, "Status", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sender, "\u72b6\u6001", StringComparison.Ordinal);
        }

        private static bool IsErrorSender(string sender)
        {
            return string.Equals(sender, "Error", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sender, "\u9519\u8BEF", StringComparison.Ordinal);
        }
    }
}
