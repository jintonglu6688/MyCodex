using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerVoiceContext
    {
        // A recent page and a compact speech context; never walk the whole conversation on voice start.
        internal const int PageSize = 64;
        internal const int MaximumContextBytes = 16000;
        internal const int MaximumContextItems = 32;

        internal static string BuildTypedInput(IEnumerable<AppServerUserInputItem> items)
        {
            var text = string.Empty;
            if (items == null) return text;
            foreach (var item in items)
            {
                if (item?.Type != "text" || string.IsNullOrWhiteSpace(item.Text)) continue;
                text = TakeTail(text + "\n" + TakeTail(item.Text, MaximumContextBytes), MaximumContextBytes);
            }
            return text.Trim();
        }

        public static async Task<RealtimeVoiceContextPage> ReadAsync(AppServerRequestInvoker send, string threadId,
            string cursor, CancellationToken token)
        {
            var parameters = new Dictionary<string, object> { { "threadId", threadId }, { "limit", PageSize } };
            if (!string.IsNullOrEmpty(cursor)) parameters["cursor"] = cursor;
            var page = await send("thread/timeline/list", parameters, token, 30000).ConfigureAwait(false);
            if (!(AppServerJsonValueReader.GetValue(page, "data") is IList) || !page.ContainsKey("nextCursor"))
                throw new InvalidDataException(SharedLocalization.Text("VoiceChat.Error.IncompleteTimeline", "The runtime returned an incomplete conversation timeline."));
            var items = new List<RealtimeVoiceContextItem>();
            var activeVoice = AppServerJsonValueReader.GetString(page, "activeRealtimeSessionAtPageStart");
            foreach (var entryObject in AppServerJsonValueReader.GetArray(page, "data"))
            {
                var entry = entryObject as IDictionary<string, object>;
                var item = AppServerJsonValueReader.GetDictionary(entry, "item");
                var type = AppServerJsonValueReader.GetString(item, "type");
                string role = null;
                string text = null;
                var voice = AppServerJsonValueReader.GetString(entry, "type") == "realtime";
                if (voice && type == "realtimeSessionStarted") activeVoice = AppServerJsonValueReader.GetString(item, "realtimeSessionId");
                if (voice && type == "realtimeSessionClosed") activeVoice = null;
                if (voice && type == "transcriptSegment")
                {
                    role = AppServerJsonValueReader.GetString(item, "role");
                    text = AppServerJsonValueReader.GetString(item, "text");
                }
                else if (!voice && type == "userMessage")
                {
                    role = "user";
                    text = AppServerThreadMapper.ExtractUserMessageText(item);
                }
                else if (!voice && type == "agentMessage")
                {
                    role = "assistant";
                    text = AppServerJsonValueReader.GetString(item, "text");
                }
                if ((role == "user" || role == "assistant") && !string.IsNullOrWhiteSpace(text))
                    items.Add(new RealtimeVoiceContextItem
                    {
                        ItemId = AppServerJsonValueReader.GetString(item, "id"), Role = role, Text = text, IsVoice = voice,
                        IsVoiceBackend = !string.IsNullOrEmpty(activeVoice) && !voice && role == "assistant",
                        MessagePhase = AppServerJsonValueReader.GetString(item, "phase"),
                        RealtimeSessionId = voice ? AppServerJsonValueReader.GetString(item, "realtimeSessionId") : activeVoice
                    });
            }
            return new RealtimeVoiceContextPage { ThreadId = threadId, Items = items, NextCursor = AppServerJsonValueReader.GetString(page, "nextCursor") };
        }

        public static List<Dictionary<string, object>> BuildInitialItems(RealtimeVoiceContextPage page)
        {
            var result = new List<Dictionary<string, object>>();
            var remaining = MaximumContextBytes;
            var items = page?.Items;
            for (var i = (items?.Count ?? 0) - 1; i >= 0 && result.Count < MaximumContextItems && remaining > 0; i--)
            {
                var item = items[i];
                var text = TakeTail(item.Text, remaining);
                if (text.Length == 0) break;
                remaining -= Encoding.UTF8.GetByteCount(text);
                result.Add(new Dictionary<string, object> { { "role", item.Role }, { "text", text } });
            }
            result.Reverse();
            if (result.Count > 0)
                result.Insert(0, new Dictionary<string, object>
                {
                    { "role", "developer" },
                    { "text", "Continue the existing conversation below. These are recent messages from this same thread, including spoken messages. The user is switching communication mode, not starting a new task. If older details are needed, ask the attached Codex agent rather than guessing." }
                });
            return result;
        }

        internal static string TakeTail(string text, int bytes)
        {
            text = text ?? string.Empty;
            var start = text.Length;
            var used = 0;
            while (start > 0)
            {
                var length = start >= 2 && char.IsLowSurrogate(text[start - 1]) && char.IsHighSurrogate(text[start - 2]) ? 2 : 1;
                var size = Encoding.UTF8.GetByteCount(text.Substring(start - length, length));
                if (used + size > bytes) break;
                used += size;
                start -= length;
            }
            return text.Substring(start);
        }
    }
}
