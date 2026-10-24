using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    // thread/read has no spoken turns; this puts the realtime transcript back between the typed messages.
    internal static class AppServerVoiceHistory
    {
        internal const int MaximumPages = 50;
        private const int PageTimeoutMs = 10000;

        // Voice history is a convenience: a failed timeline read leaves the ordinary history as it was.
        internal static async Task MergeAsync(
            AppServerRequestInvoker send,
            string threadId,
            AppServerThreadDetails details,
            CancellationToken cancellationToken)
        {
            var entries = new List<IDictionary<string, object>>();
            try
            {
                string cursor = null;
                var cursors = new HashSet<string>(StringComparer.Ordinal);
                for (var page = 0; page < MaximumPages; page++)
                {
                    var parameters = new Dictionary<string, object> { { "threadId", threadId }, { "limit", AppServerVoiceContext.PageSize } };
                    if (cursor != null)
                    {
                        parameters["cursor"] = cursor;
                    }

                    var result = await send("thread/timeline/list", parameters, cancellationToken, PageTimeoutMs).ConfigureAwait(false);
                    entries.AddRange(AppServerJsonValueReader.GetArray(result, "data").OfType<IDictionary<string, object>>());
                    cursor = AppServerJsonValueReader.GetString(result, "nextCursor");
                    if (string.IsNullOrEmpty(cursor) || !cursors.Add(cursor))
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Voice history was not read: {0}", ex.GetType().Name);
                return;
            }

            Merge(threadId, details, entries);
        }

        internal static void Merge(string threadId, AppServerThreadDetails details, IEnumerable<IDictionary<string, object>> entries)
        {
            var itemPositions = new Dictionary<string, long>(StringComparer.Ordinal);
            var voices = new List<KeyValuePair<long, AppServerThreadMessage>>();
            foreach (var entry in entries)
            {
                var position = AppServerJsonValueReader.GetNullableLong(entry, "position") ?? long.MaxValue;
                var item = AppServerJsonValueReader.GetDictionary(entry, "item");
                var id = AppServerJsonValueReader.GetString(item, "id");
                var entryType = AppServerJsonValueReader.GetString(entry, "type");
                if (entryType == "item" && !string.IsNullOrEmpty(id))
                {
                    itemPositions[id] = position;
                }
                else if (entryType == "realtime" && AppServerJsonValueReader.GetString(item, "type") == "transcriptSegment")
                {
                    var role = AppServerJsonValueReader.GetString(item, "role");
                    var text = AppServerJsonValueReader.GetString(item, "text");
                    if ((role == "user" || role == "assistant") && !string.IsNullOrWhiteSpace(text))
                    {
                        voices.Add(new KeyValuePair<long, AppServerThreadMessage>(position, new AppServerThreadMessage
                        {
                            ThreadId = threadId,
                            SourceItemId = id,
                            Sender = role == "user" ? "User" : "Codex",
                            Content = text.Trim()
                        }));
                    }
                }
            }

            if (voices.Count == 0)
            {
                return;
            }

            // Spoken turns are not rewind anchors, so they keep TurnId null and TurnIndex 0.
            var pending = new Queue<KeyValuePair<long, AppServerThreadMessage>>(voices.OrderBy(voice => voice.Key));
            var merged = new List<AppServerThreadMessage>(details.Messages.Count + voices.Count);
            AppServerThreadMessage lastVoiceRequest = null;
            foreach (var message in details.Messages)
            {
                long position;
                if (message.SourceItemId != null && itemPositions.TryGetValue(message.SourceItemId, out position))
                {
                    while (pending.Count > 0 && pending.Peek().Key < position)
                    {
                        var voice = pending.Dequeue().Value;
                        merged.Add(voice);
                        lastVoiceRequest = voice.Sender == "User" ? voice : null;
                    }
                }

                // The task voice hands to Codex often repeats what the user just said.
                if (message.IsVoiceDelegation && lastVoiceRequest != null &&
                    merged[merged.Count - 1] == lastVoiceRequest &&
                    string.Equals(NormalizeSpace(message.Content), NormalizeSpace(lastVoiceRequest.Content), StringComparison.Ordinal))
                {
                    continue;
                }

                merged.Add(message);
            }

            merged.AddRange(pending.Select(voice => voice.Value));
            details.Messages.Clear();
            details.Messages.AddRange(merged);
        }

        private static string NormalizeSpace(string text)
        {
            return string.Join(" ", (text ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
