using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerThreadHistoryReader
    {
        // The runtime pays a large fixed cost per request for long threads (about 16 seconds was
        // observed for a 338-turn thread) and currently caps a page at 100 turns, so request as
        // many turns per page as it allows.
        internal const int PageSize = 1000;
        // A complete read returns the whole thread in one reply, as before pagination.
        internal const int FullReadTimeoutMs = 180000;

        internal static async Task<IDictionary<string, object>> ReadAsync(
            AppServerRequestInvoker send, string threadId, bool paginate, CancellationToken cancellationToken)
        {
            if (paginate)
            {
                // One complete read costs one request instead of one per page. Pagination stays as
                // the fallback because complete reads are deprecated for paginated threads.
                var complete = await TryReadCompleteAsync(send, threadId, cancellationToken).ConfigureAwait(false);
                if (complete != null)
                {
                    return complete;
                }
            }

            var total = Stopwatch.StartNew();
            var step = Stopwatch.StartNew();
            var turns = new List<object>();
            var pages = 0;
            AppServerHistoryLoadTrace.Information("read-start", threadId, "Paginated={0}", paginate);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await send("thread/read", new Dictionary<string, object>
                {
                    { "threadId", threadId },
                    { "includeTurns", !paginate }
                }, cancellationToken, 60000).ConfigureAwait(false);
                var thread = AppServerJsonValueReader.GetDictionary(result, "thread");
                AppServerHistoryLoadTrace.Information("thread-read", threadId, "Turns={0}; ElapsedMs={1}",
                    paginate ? 0 : CountItems(AppServerJsonValueReader.GetValue(thread, "turns")), step.ElapsedMilliseconds);
                if (!paginate) return thread;
                if (!string.Equals(AppServerJsonValueReader.GetString(thread, "id"), threadId, StringComparison.Ordinal))
                    throw InvalidHistoryResponse();

                var positions = new Dictionary<string, int>(StringComparer.Ordinal);
                var cursors = new HashSet<string>(StringComparer.Ordinal);
                string cursor = null;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var parameters = new Dictionary<string, object>
                    {
                        { "threadId", threadId },
                        { "limit", PageSize },
                        { "sortDirection", "asc" },
                        { "itemsView", "full" }
                    };
                    if (cursor != null) parameters["cursor"] = cursor;
                    step.Restart();
                    var page = await send("thread/turns/list", parameters, cancellationToken, 60000).ConfigureAwait(false);
                    pages++;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!(AppServerJsonValueReader.GetValue(page, "data") is IList) || !page.ContainsKey("nextCursor"))
                        throw InvalidHistoryResponse();
                    var pageTurns = 0;
                    var pageItems = 0;
                    foreach (var entry in AppServerJsonValueReader.GetArray(page, "data"))
                    {
                        var turn = entry as IDictionary<string, object>;
                        var id = AppServerJsonValueReader.GetString(turn, "id");
                        var view = AppServerJsonValueReader.GetString(turn, "itemsView");
                        var items = AppServerJsonValueReader.GetValue(turn, "items") as IList;
                        if (string.IsNullOrWhiteSpace(id) ||
                            items == null ||
                            (view != null && !string.Equals(view, "full", StringComparison.Ordinal)))
                            throw InvalidHistoryResponse();

                        pageTurns++;
                        pageItems += items.Count;
                        // An overlapping page may contain an updated turn; keep its original position.
                        int position;
                        if (positions.TryGetValue(id, out position)) turns[position] = turn;
                        else
                        {
                            positions.Add(id, turns.Count);
                            turns.Add(turn);
                        }
                    }
                    AppServerHistoryLoadTrace.Information("turns-page", threadId,
                        "Page={0}; PageTurns={1}; PageItems={2}; TotalTurns={3}; ElapsedMs={4}; TotalElapsedMs={5}",
                        pages, pageTurns, pageItems, turns.Count, step.ElapsedMilliseconds, total.ElapsedMilliseconds);
                    cursor = AppServerJsonValueReader.GetString(page, "nextCursor");
                    if (!string.IsNullOrWhiteSpace(cursor) && !cursors.Add(cursor))
                        throw InvalidHistoryResponse();
                }
                while (!string.IsNullOrWhiteSpace(cursor));

                AppServerHistoryLoadTrace.Information("read-complete", threadId, "Pages={0}; Turns={1}; ElapsedMs={2}",
                    pages, turns.Count, total.ElapsedMilliseconds);
                var complete = new Dictionary<string, object>(thread);
                complete["turns"] = turns.ToArray();
                return complete;
            }
            catch (Exception ex)
            {
                // Record the page that stalled so a timeout can be told apart from a slow but progressing load.
                AppServerHistoryLoadTrace.Warning("read-failed", threadId,
                    "CompletedPages={0}; TotalTurns={1}; StepElapsedMs={2}; ElapsedMs={3}; {4}",
                    pages, turns.Count, step.ElapsedMilliseconds, total.ElapsedMilliseconds, AppServerHistoryLoadTrace.DescribeError(ex));
                throw;
            }
        }

        private static async Task<IDictionary<string, object>> TryReadCompleteAsync(
            AppServerRequestInvoker send, string threadId, CancellationToken cancellationToken)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await send("thread/read", new Dictionary<string, object>
                {
                    { "threadId", threadId },
                    { "includeTurns", true }
                }, cancellationToken, FullReadTimeoutMs).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var thread = AppServerJsonValueReader.GetDictionary(result, "thread");
                var turns = AppServerJsonValueReader.GetValue(thread, "turns") as IList;
                if (IsCompleteHistory(thread, turns, threadId))
                {
                    AppServerHistoryLoadTrace.Information("complete-read", threadId, "Turns={0}; ElapsedMs={1}",
                        turns.Count, watch.ElapsedMilliseconds);
                    return thread;
                }

                AppServerHistoryLoadTrace.Warning("complete-read-fallback", threadId, "Reason=incomplete; ElapsedMs={0}",
                    watch.ElapsedMilliseconds);
                return null;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && !(ex is TimeoutException) &&
                                       !cancellationToken.IsCancellationRequested)
            {
                // Paging after a timeout would only take longer; other failures may mean the runtime
                // no longer serves complete reads.
                AppServerHistoryLoadTrace.Warning("complete-read-fallback", threadId, "ElapsedMs={0}; {1}",
                    watch.ElapsedMilliseconds, AppServerHistoryLoadTrace.DescribeError(ex));
                return null;
            }
        }

        private static bool IsCompleteHistory(IDictionary<string, object> thread, IList turns, string threadId)
        {
            // An empty list may be a deprecated reply that omits paginated history; paging confirms it cheaply.
            if (turns == null || turns.Count == 0 ||
                !string.Equals(AppServerJsonValueReader.GetString(thread, "id"), threadId, StringComparison.Ordinal))
            {
                return false;
            }

            foreach (var entry in turns)
            {
                var turn = entry as IDictionary<string, object>;
                var view = AppServerJsonValueReader.GetString(turn, "itemsView");
                if (string.IsNullOrWhiteSpace(AppServerJsonValueReader.GetString(turn, "id")) ||
                    !(AppServerJsonValueReader.GetValue(turn, "items") is IList) ||
                    (view != null && !string.Equals(view, "full", StringComparison.Ordinal)))
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountItems(object value)
        {
            var list = value as IList;
            return list != null ? list.Count : 0;
        }

        private static InvalidDataException InvalidHistoryResponse()
        {
            return new InvalidDataException(SharedLocalization.Text(
                "AppServerClient.Error.InvalidHistoryPage",
                "The runtime returned incomplete history or a repeated page cursor. Please retry loading the conversation."));
        }
    }
}
