using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Infrastructure.AppServer
{
    // Builds the recent spoken and typed messages that a realtime v3 session starts with.
    internal static class AppServerVoiceContext
    {
        internal const int PageSize = 64;
        internal const int MaximumContextBytes = 16000;
        internal const int MaximumContextItems = 32;

        private const string ContinuationInstruction =
            "Continue the existing conversation below. These are recent messages from this same thread, including spoken messages. " +
            "The user is switching communication mode, not starting a new task. If older details are needed, ask the attached Codex agent rather than guessing.";

        // Context is a convenience: a failed or unexpected timeline read starts voice without it.
        internal static async Task<List<Dictionary<string, object>>> ReadInitialItemsAsync(
            AppServerRequestInvoker send,
            string threadId,
            CancellationToken cancellationToken)
        {
            IDictionary<string, object> page;
            try
            {
                // shortcut: reads only the first (most recent) page, like thread/turns/list's descending default; page further if voice needs older context.
                page = await send(
                    "thread/timeline/list",
                    new Dictionary<string, object> { { "threadId", threadId }, { "limit", PageSize } },
                    cancellationToken,
                    30000).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Voice conversation context was not read: {0}", ex.GetType().Name);
                return new List<Dictionary<string, object>>();
            }

            return BuildInitialItems(page);
        }

        internal static List<Dictionary<string, object>> BuildInitialItems(IDictionary<string, object> page)
        {
            var messages = AppServerJsonValueReader.GetArray(page, "data")
                .OfType<IDictionary<string, object>>()
                .OrderBy(entry => AppServerJsonValueReader.GetNullableLong(entry, "position") ?? 0)
                .Select(ReadMessage)
                .Where(message => message != null)
                .ToList();

            var result = new List<Dictionary<string, object>>();
            var remaining = MaximumContextBytes;
            for (var i = messages.Count - 1; i >= 0 && result.Count < MaximumContextItems && remaining > 0; i--)
            {
                var text = TakeTail((string)messages[i]["text"], remaining);
                if (text.Length == 0)
                {
                    break;
                }

                remaining -= Encoding.UTF8.GetByteCount(text);
                result.Add(new Dictionary<string, object> { { "role", messages[i]["role"] }, { "text", text } });
            }

            result.Reverse();
            if (result.Count > 0)
            {
                result.Insert(0, new Dictionary<string, object> { { "role", "developer" }, { "text", ContinuationInstruction } });
            }

            return result;
        }

        private static Dictionary<string, object> ReadMessage(IDictionary<string, object> entry)
        {
            var item = AppServerJsonValueReader.GetDictionary(entry, "item");
            var entryType = AppServerJsonValueReader.GetString(entry, "type");
            var itemType = AppServerJsonValueReader.GetString(item, "type");
            string role = null;
            string text = null;
            if (entryType == "realtime" && itemType == "transcriptSegment")
            {
                role = AppServerJsonValueReader.GetString(item, "role");
                text = AppServerJsonValueReader.GetString(item, "text");
            }
            else if (entryType == "item" && itemType == "userMessage")
            {
                role = "user";
                text = AppServerThreadMapper.ExtractUserMessageText(item);
            }
            else if (entryType == "item" && itemType == "agentMessage")
            {
                role = "assistant";
                text = AppServerJsonValueReader.GetString(item, "text");
            }

            if ((role != "user" && role != "assistant") || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return new Dictionary<string, object> { { "role", role }, { "text", text } };
        }

        internal static string TakeTail(string text, int maximumBytes)
        {
            text = text ?? string.Empty;
            var start = text.Length;
            var used = 0;
            while (start > 0)
            {
                var length = start >= 2 && char.IsLowSurrogate(text[start - 1]) && char.IsHighSurrogate(text[start - 2]) ? 2 : 1;
                var size = Encoding.UTF8.GetByteCount(text.Substring(start - length, length));
                if (used + size > maximumBytes)
                {
                    break;
                }

                used += size;
                start -= length;
            }

            return text.Substring(start);
        }
    }
}
