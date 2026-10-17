using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerSubAgentOperations
    {
        internal static string GetActiveTurnId(AppServerThreadDetails details)
        {
            var turns = details?.TurnStatuses.Where(pair => string.Equals(pair.Value, "inProgress", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (string.Equals(details?.ThreadStatus?.Type, "idle", StringComparison.Ordinal) && turns.Length == 0) return null;
            if (string.Equals(details?.ThreadStatus?.Type, "active", StringComparison.Ordinal) && turns.Length == 1) return turns[0].Key;
            throw new InvalidOperationException(MyCodex.Shared.Localization.SharedLocalization.Text("SubAgent.Error.NotLoaded", "The agent is not confirmed idle or running. Ask its parent to resume it, then refresh."));
        }

        internal static void ValidateIdentity(string threadId, AppServerThreadDetails details)
        {
            if (details == null || details.ParentThreadIdConflict || string.IsNullOrWhiteSpace(details.ParentThreadId) ||
                string.IsNullOrWhiteSpace(threadId) || !string.Equals(threadId, details.ThreadId, StringComparison.Ordinal))
                throw new InvalidOperationException(MyCodex.Shared.Localization.SharedLocalization.Text("SubAgent.Error.IdentityUnavailable", "The subagent identity could not be verified. Nothing was sent."));
        }

        internal static KeyValuePair<string, object> BuildInputRequest(AppServerThreadDetails details, object[] inputs)
        {
            ValidateIdentity(details?.ThreadId, details);
            var turnId = GetActiveTurnId(details);
            if (turnId == null && !string.Equals(details.ThreadStatus?.Type, "idle", StringComparison.Ordinal))
                throw new InvalidOperationException(MyCodex.Shared.Localization.SharedLocalization.Text("SubAgent.Error.NotLoaded", "The agent is not confirmed idle or running. Ask its parent to resume it, then refresh."));
            var parameters = new Dictionary<string, object> { { "threadId", details.ThreadId }, { "input", inputs } };
            if (turnId != null) parameters["expectedTurnId"] = turnId;
            return new KeyValuePair<string, object>(turnId == null ? "turn/start" : "turn/steer", parameters);
        }

        internal static async Task<List<AppServerThreadSummary>> ListAsync(
            string parentThreadId,
            bool supportsFilters,
            AppServerRequestInvoker send,
            Func<string, string> mapPath,
            CancellationToken cancellationToken,
            bool archived = false)
        {
            if (string.IsNullOrWhiteSpace(parentThreadId)) throw new ArgumentException(nameof(parentThreadId));
            var threads = new List<AppServerThreadSummary>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string cursor = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parameters = new Dictionary<string, object>
                {
                    { "limit", 100 },
                    { "sortKey", "updated_at" },
                    { "archived", archived },
                    { "sourceKinds", new[] { "subAgentThreadSpawn" } }
                };
                if (supportsFilters) parameters["ancestorThreadId"] = parentThreadId;
                if (cursor != null) parameters["cursor"] = cursor;
                var result = await send("thread/list", parameters, cancellationToken, 60000).ConfigureAwait(false);
                foreach (var item in AppServerJsonValueReader.GetArray(result, "data"))
                {
                    var thread = AppServerThreadMapper.BuildThreadSummary(item as IDictionary<string, object>, mapPath);
                    if (!string.IsNullOrWhiteSpace(thread.ThreadId) && ids.Add(thread.ThreadId)) threads.Add(thread);
                }
                cursor = AppServerJsonValueReader.GetString(result, "nextCursor");
                if (!string.IsNullOrEmpty(cursor) && !cursors.Add(cursor))
                {
                    throw new InvalidOperationException("Repeated thread list cursor.");
                }
            }
            while (!string.IsNullOrWhiteSpace(cursor));
            return threads;
        }
    }
}
