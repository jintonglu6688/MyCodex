using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Helpers;

namespace MyCodex.Application.CodingRemote
{
    /// <summary>
    /// Async questions come from an assistant message rather than an App Server request, so the
    /// phone sees them as tool input requests whose IDs carry a prefix that routes answers back.
    /// </summary>
    public static class RemoteAsyncInputRequests
    {
        public const string QueuedEventType = "async_input_queued";
        public const string ResolvedEventType = "async_input_resolved";
        public const string Kind = "async";

        private const string RequestIdPrefix = "async:";

        public static string CreateRequestId(AppServerEventArgs source)
        {
            if (source == null)
            {
                return null;
            }

            var itemId = TextValueHelper.TrimToNull(source.ItemId);
            if (itemId != null)
            {
                return RequestIdPrefix + (source.ThreadId ?? string.Empty) + ":" + itemId;
            }

            using (var algorithm = SHA256.Create())
            {
                var key = (source.ThreadId ?? string.Empty) + "\n" + (source.TurnId ?? string.Empty) + "\n" + (source.Content ?? string.Empty);
                var bytes = algorithm.ComputeHash(new UTF8Encoding(false).GetBytes(key));
                return RequestIdPrefix + (source.ThreadId ?? string.Empty) + ":h" +
                    string.Concat(bytes.Take(16).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        public static bool IsAsyncRequestId(string requestId)
        {
            return requestId != null && requestId.StartsWith(RequestIdPrefix, StringComparison.Ordinal);
        }

        public static IDictionary<string, string> FlattenAnswers(IDictionary<string, string[]> answers)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in answers ?? new Dictionary<string, string[]>())
            {
                var values = (pair.Value ?? new string[0])
                    .Select(TextValueHelper.TrimToNull)
                    .Where(value => value != null)
                    .ToArray();
                if (!string.IsNullOrWhiteSpace(pair.Key) && values.Length > 0)
                {
                    result[pair.Key] = string.Join(", ", values);
                }
            }

            return result;
        }
    }
}
