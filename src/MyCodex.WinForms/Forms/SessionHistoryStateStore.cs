using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionHistoryStateStore
    {
        private readonly Dictionary<string, ChatHistoryItem[]> _cache;
        private readonly Dictionary<string, int> _generations;
        private readonly HashSet<string> _pendingReloadThreadIds;

        public SessionHistoryStateStore()
        {
            _cache = new Dictionary<string, ChatHistoryItem[]>(StringComparer.OrdinalIgnoreCase);
            _generations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _pendingReloadThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // Advances on every invalidation so a read that started earlier cannot cache outdated history.
        internal int GetGeneration(string threadId)
        {
            int generation;
            return !string.IsNullOrWhiteSpace(threadId) && _generations.TryGetValue(threadId, out generation) ? generation : 0;
        }

        internal bool TryStore(string threadId, int generation, ChatHistoryItem[] historyItems)
        {
            if (GetGeneration(threadId) != generation)
            {
                return false;
            }

            Store(threadId, historyItems);
            return !string.IsNullOrWhiteSpace(threadId);
        }

        internal bool TryGetCached(string threadId, out ChatHistoryItem[] historyItems)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                historyItems = null;
                return false;
            }

            return _cache.TryGetValue(threadId, out historyItems);
        }

        internal void Store(string threadId, ChatHistoryItem[] historyItems)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _cache[threadId] = historyItems ?? Array.Empty<ChatHistoryItem>();
        }

        internal void Invalidate(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _cache.Remove(threadId);
            _generations[threadId] = GetGeneration(threadId) + 1;
        }

        internal void MarkPendingReload(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _pendingReloadThreadIds.Add(threadId);
        }

        internal void ClearPendingReload(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _pendingReloadThreadIds.Remove(threadId);
        }

        internal bool HasPendingReload(string threadId)
        {
            return !string.IsNullOrWhiteSpace(threadId) &&
                   _pendingReloadThreadIds.Contains(threadId);
        }
    }

    internal static class SessionHistoryEventPolicy
    {
        internal static bool ShouldInvalidateForEvent(AppServerEventArgs e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.ThreadId))
            {
                return false;
            }

            switch ((e.EventType ?? string.Empty).Trim())
            {
                case "assistant":
                case "command_completed":
                case "error":
                case "file_change_completed":
                case "approval_requested":
                case "approval_resolved":
                case "tool_user_input_requested":
                case "tool_user_input_resolved":
                case "turn_started":
                case "turn_completed":
                case "turn_interrupted":
                case "turn_failed":
                case "thread_archived":
                case "thread_unarchived":
                case "context_compaction_completed":
                    return true;
                default:
                    return false;
            }
        }
    }

    internal static class SessionHistoryRealtimeRefreshPolicy
    {
        internal static bool ShouldReloadCurrentSessionForEvent(AppServerEventArgs e, string currentThreadId)
        {
            return ShouldReloadCurrentSessionForEvent(e, currentThreadId, false);
        }

        internal static bool ShouldReloadCurrentSessionForEvent(
            AppServerEventArgs e,
            string currentThreadId,
            bool isLocalDesktopRealtimeTurn)
        {
            if (e == null ||
                string.IsNullOrWhiteSpace(e.ThreadId) ||
                string.IsNullOrWhiteSpace(currentThreadId) ||
                !string.Equals(e.ThreadId, currentThreadId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (isLocalDesktopRealtimeTurn)
            {
                return false;
            }

            switch ((e.EventType ?? string.Empty).Trim())
            {
                case "turn_started":
                case "turn_completed":
                case "turn_interrupted":
                case "turn_failed":
                    return true;
                default:
                    return false;
            }
        }
    }

    internal static class SessionHistorySilentReloadSelectionPolicy
    {
        internal static bool ShouldPreserveSelection(bool chatHistoryHasFocus, int selectedIndex)
        {
            return chatHistoryHasFocus && selectedIndex >= 0;
        }
    }
}
