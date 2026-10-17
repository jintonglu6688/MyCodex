using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerThreadHistoryReaderTests
    {
        [Test]
        public async Task Pages_PreserveFullMessagesActivitiesEmptyTurnsAndOrder()
        {
            var first = Json("{\"id\":\"one\",\"status\":\"completed\",\"itemsView\":\"full\",\"items\":[{\"id\":\"user\",\"type\":\"userMessage\",\"content\":[{\"type\":\"text\",\"text\":\"Question\"}]},{\"id\":\"answer\",\"type\":\"agentMessage\",\"text\":\"Answer\"},{\"id\":\"agent\",\"type\":\"collabAgentToolCall\",\"tool\":\"spawnAgent\",\"receiverThreadIds\":[\"child\"]}]}");
            var empty = Json("{\"id\":\"two\",\"status\":\"completed\",\"items\":[]}");
            var last = Json("{\"id\":\"three\",\"status\":\"inProgress\",\"itemsView\":\"full\",\"items\":[{\"id\":\"last\",\"type\":\"agentMessage\",\"text\":\"Latest\"}]}");
            var metadata = Json("{\"id\":\"thread\",\"name\":\"Title\",\"cwd\":\"/repo\",\"createdAt\":1700000000,\"updatedAt\":1700000100,\"turns\":[]}");
            var calls = 0;
            using (var cts = new CancellationTokenSource())
            {
                var actual = await AppServerThreadHistoryReader.ReadAsync((method, args, token, timeout) =>
                {
                    Assert.That(token, Is.EqualTo(cts.Token));
                    var parameters = (IDictionary<string, object>)args;
                    Assert.That(parameters["threadId"], Is.EqualTo("thread"));
                    calls++;
                    if (method == "thread/read")
                    {
                        // An empty complete reply is confirmed by paging before it is trusted.
                        var complete = (bool)parameters["includeTurns"];
                        Assert.That(complete, Is.EqualTo(calls == 1));
                        Assert.That(timeout, Is.EqualTo(complete ? AppServerThreadHistoryReader.FullReadTimeoutMs : 60000));
                        return Result(new Dictionary<string, object> { { "thread", metadata } });
                    }
                    Assert.That(timeout, Is.EqualTo(60000));
                    Assert.That(method, Is.EqualTo("thread/turns/list"));
                    Assert.That(parameters["itemsView"], Is.EqualTo("full"));
                    Assert.That(parameters["sortDirection"], Is.EqualTo("asc"));
                    Assert.That(parameters["limit"], Is.EqualTo(AppServerThreadHistoryReader.PageSize));
                    if (calls == 3)
                    {
                        Assert.That(parameters.ContainsKey("cursor"), Is.False);
                        return Result(Page(new object[] { first, empty }, "next"));
                    }
                    Assert.That(parameters["cursor"], Is.EqualTo("next"));
                    return Result(Page(new object[] { empty, last }, null));
                }, "thread", true, cts.Token);

                var expected = new Dictionary<string, object>(metadata) { ["turns"] = new object[] { first, empty, last } };
                var mapped = AppServerThreadMapper.BuildThreadDetails(actual, path => @"E:\Repo");
                var legacy = AppServerThreadMapper.BuildThreadDetails(expected, path => @"E:\Repo");
                Assert.That(calls, Is.EqualTo(4));
                var serializer = new JavaScriptSerializer();
                Assert.That(serializer.Serialize(actual), Is.EqualTo(serializer.Serialize(expected)));
                Assert.That(serializer.Serialize(mapped.Messages), Is.EqualTo(serializer.Serialize(legacy.Messages)));
                Assert.That(mapped.TurnStatuses, Is.EquivalentTo(legacy.TurnStatuses));
                Assert.That(mapped.TurnCount, Is.EqualTo(3));
                Assert.That(mapped.Messages.Count, Is.EqualTo(3));
                Assert.That(mapped.Activities.Count, Is.EqualTo(1));
                Assert.That(mapped.WorkingDirectory, Is.EqualTo(@"E:\Repo"));
            }
        }

        [Test]
        public async Task LegacyRuntime_UsesExistingFullHistoryRequestOnly()
        {
            var calls = 0;
            var expected = Json("{\"id\":\"thread\",\"turns\":[]}");
            var actual = await AppServerThreadHistoryReader.ReadAsync((method, args, token, timeout) =>
            {
                calls++;
                Assert.That(method, Is.EqualTo("thread/read"));
                Assert.That(((IDictionary<string, object>)args)["includeTurns"], Is.True);
                return Result(new Dictionary<string, object> { { "thread", expected } });
            }, "thread", false, CancellationToken.None);
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public async Task EmptyHistory_IsACompleteEmptyResult()
        {
            var result = await AppServerThreadHistoryReader.ReadAsync((method, args, token, timeout) =>
                Result(method == "thread/read" ? Json("{\"thread\":{\"id\":\"thread\"}}") : Page(new object[0], null)),
                "thread", true, CancellationToken.None);
            Assert.That(AppServerJsonValueReader.GetArray(result, "turns"), Is.Empty);
        }

        [TestCase("cursor")]
        [TestCase("missing-data")]
        [TestCase("missing-cursor")]
        [TestCase("summary-only")]
        [TestCase("missing-items")]
        [TestCase("transport")]
        [TestCase("cancel")]
        public void FailedOrIncompletePage_DoesNotReturnPartialHistory(string failure)
        {
            using (var cts = new CancellationTokenSource())
            {
                var calls = 0;
                var completeTurn = Json("{\"id\":\"one\",\"status\":\"completed\",\"items\":[]}");
                Func<Task> read = async () => await AppServerThreadHistoryReader.ReadAsync((method, args, token, timeout) =>
                {
                    calls++;
                    if (method == "thread/read") return Result(Json("{\"thread\":{\"id\":\"thread\"}}"));
                    if (calls == 3)
                    {
                        if (failure == "cancel") cts.Cancel();
                        return Result(Page(new object[] { completeTurn }, "next"));
                    }
                    if (failure == "transport") throw new IOException("Disconnected");
                    if (failure == "missing-data") return Result(Json("{}"));
                    if (failure == "missing-cursor") return Result(Json("{\"data\":[]}"));
                    if (failure == "summary-only") return Result(Page(new object[] { Json("{\"id\":\"two\",\"itemsView\":\"summary\",\"items\":[]}") }, null));
                    if (failure == "missing-items") return Result(Page(new object[] { Json("{\"id\":\"two\"}") }, null));
                    return Result(Page(new object[] { completeTurn }, "next"));
                }, "thread", true, cts.Token);
                if (failure == "cancel") Assert.ThrowsAsync<OperationCanceledException>(async () => await read());
                else if (failure == "transport") Assert.ThrowsAsync<IOException>(async () => await read());
                else Assert.ThrowsAsync<InvalidDataException>(async () => await read());
                Assert.That(calls, Is.LessThanOrEqualTo(4));
            }
        }

        [Test]
        public async Task CompleteRead_LoadsWholeThreadInOneRequest()
        {
            var thread = Json("{\"id\":\"thread\",\"turns\":[{\"id\":\"one\",\"itemsView\":\"full\",\"items\":[{\"id\":\"a\",\"type\":\"agentMessage\",\"text\":\"Answer\"}]},{\"id\":\"two\",\"items\":[]}]}");
            var calls = new List<string>();
            var actual = await AppServerThreadHistoryReader.ReadAsync((method, args, token, timeout) =>
            {
                calls.Add(method);
                Assert.That(((IDictionary<string, object>)args)["includeTurns"], Is.True);
                Assert.That(timeout, Is.EqualTo(AppServerThreadHistoryReader.FullReadTimeoutMs));
                return Result(new Dictionary<string, object> { { "thread", thread } });
            }, "thread", true, CancellationToken.None);

            Assert.That(actual, Is.SameAs(thread));
            Assert.That(calls, Is.EqualTo(new[] { "thread/read" }));
        }

        [TestCase("error")]
        [TestCase("empty")]
        [TestCase("summary")]
        [TestCase("not-loaded")]
        [TestCase("missing-items")]
        [TestCase("wrong-thread")]
        public async Task CompleteRead_FallsBackToPagesWhenUnavailableOrIncomplete(string reply)
        {
            var turn = Json("{\"id\":\"one\",\"itemsView\":\"full\",\"items\":[{\"id\":\"a\",\"type\":\"agentMessage\",\"text\":\"Paged\"}]}");
            var calls = new List<string>();
            var actual = await AppServerThreadHistoryReader.ReadAsync((method, args, token, timeout) =>
            {
                var complete = method == "thread/read" && (bool)((IDictionary<string, object>)args)["includeTurns"];
                calls.Add(complete ? "complete" : method);
                if (!complete)
                {
                    return Result(method == "thread/read"
                        ? Json("{\"thread\":{\"id\":\"thread\"}}")
                        : Page(new object[] { turn }, null));
                }

                switch (reply)
                {
                    case "error": throw new InvalidOperationException("Full history is unavailable for paginated threads.");
                    case "empty": return Result(Json("{\"thread\":{\"id\":\"thread\",\"turns\":[]}}"));
                    case "summary": return Result(Json("{\"thread\":{\"id\":\"thread\",\"turns\":[{\"id\":\"one\",\"itemsView\":\"summary\",\"items\":[]}]}}"));
                    case "not-loaded": return Result(Json("{\"thread\":{\"id\":\"thread\",\"turns\":[{\"id\":\"one\",\"itemsView\":\"notLoaded\",\"items\":[]}]}}"));
                    case "missing-items": return Result(Json("{\"thread\":{\"id\":\"thread\",\"turns\":[{\"id\":\"one\"}]}}"));
                    default: return Result(Json("{\"thread\":{\"id\":\"other\",\"turns\":[{\"id\":\"one\",\"items\":[]}]}}"));
                }
            }, "thread", true, CancellationToken.None);

            Assert.That(calls, Is.EqualTo(new[] { "complete", "thread/read", "thread/turns/list" }));
            Assert.That(AppServerJsonValueReader.GetArray(actual, "turns"), Is.EqualTo(new object[] { turn }));
        }

        [TestCase("timeout")]
        [TestCase("cancel")]
        public void CompleteRead_TimeoutOrCancellationDoesNotStartPaging(string failure)
        {
            using (var cts = new CancellationTokenSource())
            {
                var calls = 0;
                Func<Task> read = async () => await AppServerThreadHistoryReader.ReadAsync((method, args, token, timeout) =>
                {
                    calls++;
                    if (failure == "cancel")
                    {
                        cts.Cancel();
                        throw new OperationCanceledException(cts.Token);
                    }

                    throw new TimeoutException("App Server request timed out: thread/read");
                }, "thread", true, cts.Token);

                if (failure == "cancel") Assert.ThrowsAsync<OperationCanceledException>(async () => await read());
                else Assert.ThrowsAsync<TimeoutException>(async () => await read());
                Assert.That(calls, Is.EqualTo(1), "Paging after a timeout would only take longer.");
            }
        }

        private static IDictionary<string, object> Page(object[] turns, string cursor)
        {
            return new Dictionary<string, object> { { "data", turns }, { "nextCursor", cursor } };
        }

        private static IDictionary<string, object> Json(string json)
        {
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
        }

        private static Task<IDictionary<string, object>> Result(IDictionary<string, object> value)
        {
            return Task.FromResult(value);
        }
    }
}
