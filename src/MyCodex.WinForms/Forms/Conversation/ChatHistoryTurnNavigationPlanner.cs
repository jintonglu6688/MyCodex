using System;
using System.Collections.Generic;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal static class ChatHistoryTurnNavigationPlanner
    {
        internal static int ResolveNextUserMessageIndex(
            IReadOnlyList<ChatHistoryItem> items,
            int currentIndex,
            int direction,
            Func<string, bool> isUserSender)
        {
            if (items == null || items.Count == 0 || isUserSender == null || direction == 0)
            {
                return -1;
            }

            var step = direction < 0 ? -1 : 1;
            var index = currentIndex;
            if (index < 0 || index >= items.Count)
            {
                index = step < 0 ? items.Count : -1;
            }

            for (var candidate = index + step; candidate >= 0 && candidate < items.Count; candidate += step)
            {
                var item = items[candidate];
                if (item != null && isUserSender(item.Sender))
                {
                    return candidate;
                }
            }

            return -1;
        }
    }
}
