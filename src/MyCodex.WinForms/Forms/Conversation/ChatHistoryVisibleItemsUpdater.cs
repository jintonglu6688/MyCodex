using System;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal static class ChatHistoryVisibleItemsUpdater
    {
        internal static bool HasChanges(ListBox.ObjectCollection items, ChatHistoryItem[] visibleItems)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            visibleItems = visibleItems ?? Array.Empty<ChatHistoryItem>();
            if (items.Count != visibleItems.Length) return true;
            for (var index = 0; index < visibleItems.Length; index++)
                if (!HasSameState(items[index] as ChatHistoryItem, visibleItems[index])) return true;
            return false;
        }

        internal static bool Apply(ListBox.ObjectCollection items, ChatHistoryItem[] visibleItems)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            visibleItems = visibleItems ?? Array.Empty<ChatHistoryItem>();
            var changed = false;
            var commonCount = Math.Min(items.Count, visibleItems.Length);

            for (var i = 0; i < commonCount; i++)
            {
                var currentItem = items[i] as ChatHistoryItem;
                var nextItem = visibleItems[i];
                if (HasSameState(currentItem, nextItem))
                {
                    continue;
                }

                changed = true;
                if (currentItem != null && HasSamePresentation(currentItem, nextItem))
                {
                    CopyState(currentItem, nextItem);
                    continue;
                }

                items[i] = nextItem;
            }

            for (var i = items.Count - 1; i >= visibleItems.Length; i--)
            {
                changed = true;
                items.RemoveAt(i);
            }

            for (var i = items.Count; i < visibleItems.Length; i++)
            {
                changed = true;
                items.Add(visibleItems[i]);
            }

            return changed;
        }

        private static bool HasSameState(ChatHistoryItem left, ChatHistoryItem right)
        {
            return HasSamePresentation(left, right) &&
                   string.Equals(left != null ? left.ThreadId : null, right != null ? right.ThreadId : null, StringComparison.Ordinal) &&
                   string.Equals(left != null ? left.TurnId : null, right != null ? right.TurnId : null, StringComparison.Ordinal) &&
                   (left != null ? left.TurnIndex : 0) == (right != null ? right.TurnIndex : 0) &&
                   (left != null ? left.TurnCount : 0) == (right != null ? right.TurnCount : 0);
        }

        private static bool HasSamePresentation(ChatHistoryItem left, ChatHistoryItem right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return string.Equals(left.Sender, right.Sender, StringComparison.Ordinal) &&
                   left.Category == right.Category &&
                   string.Equals(left.DisplayText, right.DisplayText, StringComparison.Ordinal) &&
                   string.Equals(left.FullText, right.FullText, StringComparison.Ordinal) &&
                   string.Equals(left.AccessibleText, right.AccessibleText, StringComparison.Ordinal) &&
                   string.Equals(left.MarkdownText, right.MarkdownText, StringComparison.Ordinal);
        }

        private static void CopyState(ChatHistoryItem target, ChatHistoryItem source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.ThreadId = source.ThreadId;
            target.TurnId = source.TurnId;
            target.TurnIndex = source.TurnIndex;
            target.TurnCount = source.TurnCount;
            target.Sender = source.Sender;
            target.Category = source.Category;
            target.DisplayText = source.DisplayText;
            target.FullText = source.FullText;
            target.AccessibleText = source.AccessibleText;
            target.MarkdownText = source.MarkdownText;
        }
    }
}
