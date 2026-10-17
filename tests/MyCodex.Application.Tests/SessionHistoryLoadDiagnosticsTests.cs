using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class SessionHistoryLoadDiagnosticsTests
    {
        private const string PrivateText = "private-message-text-do-not-log";

        [Test]
        public async Task PaginatedRead_LogsEachPageWithoutMessageContent()
        {
            var threadId = "thread-" + Guid.NewGuid().ToString("N");
            var turn = Turn("one", PrivateText);
            var log = await CaptureAsync(threadId, () => AppServerThreadHistoryReader.ReadAsync((method, args, token, timeout) =>
            {
                if (method == "thread/read")
                {
                    return Result(new Dictionary<string, object> { { "thread", new Dictionary<string, object> { { "id", threadId } } } });
                }

                var cursor = ((IDictionary<string, object>)args).ContainsKey("cursor");
                return Result(Page(cursor ? new object[] { Turn("two", PrivateText) } : new object[] { turn }, cursor ? null : "next"));
            }, threadId, true, CancellationToken.None));

            Assert.That(Phases(log), Is.EqualTo(new[] { "complete-read-fallback", "read-start", "thread-read", "turns-page", "turns-page", "read-complete" }));
            Assert.That(log, Has.Some.Contains("Page=1; PageTurns=1; PageItems=2; TotalTurns=1"));
            Assert.That(log, Has.Some.Contains("Page=2; PageTurns=1; PageItems=2; TotalTurns=2"));
            Assert.That(log, Has.Some.Contains("Pages=2; Turns=2"));
            Assert.That(string.Join("\n", log), Does.Not.Contain(PrivateText));
        }

        [Test]
        public async Task FailedPage_LogsCompletedPagesAndErrorType()
        {
            var threadId = "thread-" + Guid.NewGuid().ToString("N");
            var log = await CaptureAsync(threadId, async () =>
            {
                try
                {
                    await AppServerThreadHistoryReader.ReadAsync((method, args, token, timeout) =>
                    {
                        if (method == "thread/read")
                        {
                            return Result(new Dictionary<string, object> { { "thread", new Dictionary<string, object> { { "id", threadId } } } });
                        }

                        if (((IDictionary<string, object>)args).ContainsKey("cursor"))
                        {
                            throw new TimeoutException("App Server request timed out: thread/turns/list");
                        }

                        return Result(Page(new object[] { Turn("one", PrivateText) }, "next"));
                    }, threadId, true, CancellationToken.None);
                    Assert.Fail("The failed page must not return partial history.");
                }
                catch (TimeoutException)
                {
                }

                return (IDictionary<string, object>)null;
            });

            var failure = log.Single(line => line.Contains("Phase=read-failed"));
            Assert.That(failure, Does.Contain("CompletedPages=1; TotalTurns=1"));
            Assert.That(failure, Does.Contain("ErrorType=System.TimeoutException"));
            Assert.That(string.Join("\n", log), Does.Not.Contain(PrivateText));
        }

        [Test]
        public async Task RemoteLoad_LogsWorkflowPhasesAndCacheHit()
        {
            var threadId = "thread-" + Guid.NewGuid().ToString("N");
            var session = new ChatSession { Id = "session", ThreadId = threadId, Title = "Title" };
            var details = new AppServerThreadDetails { ThreadId = threadId, TurnCount = 1 };
            details.Messages.Add(new AppServerThreadMessage { ThreadId = threadId, TurnId = "one", TurnIndex = 1, Sender = "User", Content = PrivateText });
            var workflow = CreateWorkflow(id => Task.FromResult(details));

            var log = await CaptureAsync(threadId, async () =>
            {
                var request = workflow.BuildRequest(session, false);
                var result = await workflow.CompleteRemoteLoadAsync(request, (s, id) => true, s => false, () => Task.CompletedTask);
                Assert.That(result.Kind, Is.EqualTo(SessionHistoryLoadResultKind.LoadedRemoteHistory));
                workflow.BuildRequest(session, false);
                return (IDictionary<string, object>)null;
            });

            Assert.That(Phases(log), Is.EqualTo(new[] { "load-start", "read-cached", "remote-read", "wait-before-build", "build-items", "cache-hit" }));
            Assert.That(log, Has.Some.Contains("HistoryItems=1; DisplayItems=1"));
            Assert.That(log, Has.Some.Contains("Phase=cache-hit; ThreadId=" + threadId + "; HistoryItems=1"));
            Assert.That(string.Join("\n", log), Does.Not.Contain(PrivateText));
        }

        [Test]
        public async Task RemoteFailure_LogsErrorTypeAndElapsedTime()
        {
            var threadId = "thread-" + Guid.NewGuid().ToString("N");
            var session = new ChatSession { Id = "session", ThreadId = threadId, Title = "Title" };
            var workflow = CreateWorkflow(id =>
            {
                var failed = new TaskCompletionSource<AppServerThreadDetails>();
                failed.SetException(new TimeoutException("App Server request timed out: thread/turns/list"));
                return failed.Task;
            });

            var log = await CaptureAsync(threadId, async () =>
            {
                var result = await workflow.CompleteRemoteLoadAsync(workflow.BuildRequest(session, false), (s, id) => true, s => false);
                Assert.That(result.Kind, Is.EqualTo(SessionHistoryLoadResultKind.Error));
                return (IDictionary<string, object>)null;
            });

            var failure = log.Single(line => line.Contains("Phase=remote-failed"));
            Assert.That(failure, Does.Contain("ErrorType=System.TimeoutException"));
            Assert.That(failure, Does.Contain("ElapsedMs="));
        }

        private static SessionHistoryLoadWorkflow CreateWorkflow(Func<string, Task<AppServerThreadDetails>> read)
        {
            var builder = new SessionHistoryDisplayBuilder(
                "System",
                "Error",
                _ => "model",
                (sender, message, category) => new ChatHistoryItem { Sender = sender, FullText = message, DisplayText = message, Category = category },
                sender => ChatHistoryCategory.Conversation);
            return new SessionHistoryLoadWorkflow(new SessionHistoryStateStore(), builder, read, _ => false, _ => false);
        }

        private static async Task<string[]> CaptureAsync(string threadId, Func<Task<IDictionary<string, object>>> action)
        {
            var output = new StringWriter();
            using (var listener = new TextWriterTraceListener(output))
            {
                Trace.Listeners.Add(listener);
                try
                {
                    await action();
                    Trace.Flush();
                }
                finally
                {
                    Trace.Listeners.Remove(listener);
                }
            }

            return output.ToString()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("[HistoryLoadTiming]") && line.Contains("ThreadId=" + threadId))
                .ToArray();
        }

        private static string[] Phases(IEnumerable<string> log)
        {
            return log.Select(line =>
            {
                var start = line.IndexOf("Phase=", StringComparison.Ordinal) + "Phase=".Length;
                return line.Substring(start, line.IndexOf(';', start) - start);
            }).ToArray();
        }

        private static IDictionary<string, object> Turn(string id, string text)
        {
            return new Dictionary<string, object>
            {
                { "id", id },
                { "status", "completed" },
                { "itemsView", "full" },
                {
                    "items", new object[]
                    {
                        new Dictionary<string, object> { { "id", id + "-user" }, { "type", "userMessage" }, { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } } },
                        new Dictionary<string, object> { { "id", id + "-agent" }, { "type", "agentMessage" }, { "text", text } }
                    }
                }
            };
        }

        private static IDictionary<string, object> Page(object[] turns, string cursor)
        {
            return new Dictionary<string, object> { { "data", turns }, { "nextCursor", cursor } };
        }

        private static Task<IDictionary<string, object>> Result(IDictionary<string, object> value)
        {
            return Task.FromResult(value);
        }
    }
}
