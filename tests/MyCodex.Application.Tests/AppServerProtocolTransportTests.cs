using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerProtocolTransportTests
    {
        [Test]
        public async Task SendRequestAsync_WritesJsonRpcRequestAndCompletesFromMatchingResponse()
        {
            var writer = new StringWriter();
            var transport = CreateTransport(() => writer);
            var parameters = new Dictionary<string, object>
            {
                { "value", "input" }
            };

            var requestTask = transport.SendRequestAsync(
                "codex/test",
                parameters,
                TimeSpan.FromSeconds(5),
                CancellationToken.None);
            var message = ReadSingleWrittenMessage(writer);
            var requestId = Convert.ToString(message["id"]);

            Assert.That(message["jsonrpc"], Is.EqualTo("2.0"));
            Assert.That(message["method"], Is.EqualTo("codex/test"));
            Assert.That(requestId, Is.Not.Empty);
            Assert.That(AppServerJsonValue.GetDictionary(message, "params")["value"], Is.EqualTo("input"));

            string errorMessage;
            var handled = transport.TryHandleResponse(
                new Dictionary<string, object>
                {
                    { "id", requestId },
                    { "result", new Dictionary<string, object> { { "reply", "ok" } } }
                },
                out errorMessage);

            Assert.That(handled, Is.True);
            Assert.That(errorMessage, Is.Null);
            var result = await requestTask.ConfigureAwait(false);
            Assert.That(result["reply"], Is.EqualTo("ok"));
        }

        [Test]
        public void SendRequestAsync_ServerErrorCompletesWithInvalidOperationException()
        {
            var writer = new StringWriter();
            var transport = CreateTransport(() => writer);
            var requestTask = transport.SendRequestAsync(
                "codex/test",
                new Dictionary<string, object>(),
                TimeSpan.FromSeconds(5),
                CancellationToken.None);
            var requestId = Convert.ToString(ReadSingleWrittenMessage(writer)["id"]);

            string errorMessage;
            var handled = transport.TryHandleResponse(
                new Dictionary<string, object>
                {
                    { "id", requestId },
                    { "error", new Dictionary<string, object> { { "message", "Rejected by test" } } }
                },
                out errorMessage);

            Assert.That(handled, Is.True);
            Assert.That(errorMessage, Is.EqualTo("Rejected by test"));
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await requestTask.ConfigureAwait(false));
            Assert.That(exception.Message, Is.EqualTo("Rejected by test"));
        }

        [Test]
        public void SendRequestAsync_TimeoutRemovesPendingRequest()
        {
            var writer = new StringWriter();
            var transport = CreateTransport(() => writer);

            var exception = Assert.ThrowsAsync<TimeoutException>(async () =>
                await transport.SendRequestAsync(
                    "codex/test",
                    new Dictionary<string, object>(),
                    TimeSpan.FromMilliseconds(10),
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(exception.Message, Is.EqualTo("timeout: codex/test 10"));
            Assert.That(transport.DrainPendingRequests(), Has.Count.EqualTo(0));
        }

        [Test]
        public void SendRequestAsync_PreCanceledTokenRemovesPendingRequest()
        {
            var writer = new StringWriter();
            var transport = CreateTransport(() => writer);
            var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            var exception = Assert.CatchAsync<OperationCanceledException>(async () =>
                await transport.SendRequestAsync(
                    "codex/test",
                    new Dictionary<string, object>(),
                    TimeSpan.FromSeconds(5),
                    cancellationTokenSource.Token).ConfigureAwait(false));

            Assert.That(exception, Is.Not.Null);
            Assert.That(writer.ToString(), Is.Empty);
            Assert.That(transport.DrainPendingRequests(), Has.Count.EqualTo(0));
        }

        [Test]
        public void SendRequestAsync_CancellationWhilePendingRemovesPendingRequest()
        {
            var writer = new StringWriter();
            var transport = CreateTransport(() => writer);
            var cancellationTokenSource = new CancellationTokenSource();

            var requestTask = transport.SendRequestAsync(
                "codex/test",
                new Dictionary<string, object>(),
                TimeSpan.FromSeconds(30),
                cancellationTokenSource.Token);
            Assert.That(ReadSingleWrittenMessage(writer)["id"], Is.Not.Null);

            cancellationTokenSource.Cancel();

            var exception = Assert.CatchAsync<OperationCanceledException>(async () => await requestTask.ConfigureAwait(false));
            Assert.That(exception, Is.Not.Null);
            Assert.That(transport.DrainPendingRequests(), Has.Count.EqualTo(0));
        }

        [Test]
        public void SendRequestAsync_WriteFailureRemovesPendingAndThrowsTransportException()
        {
            var transport = CreateTransport(() => null);

            var exception = Assert.ThrowsAsync<AppServerTransportWriteException>(async () =>
                await transport.SendRequestAsync(
                    "codex/test",
                    new Dictionary<string, object>(),
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None).ConfigureAwait(false));

            Assert.That(exception.Message, Is.EqualTo("App Server input writer is not available."));
            Assert.That(transport.DrainPendingRequests(), Has.Count.EqualTo(0));
        }

        [Test]
        public void TryHandleResponse_UnknownRequestIdIsIgnoredWithoutErrorMessage()
        {
            var writer = new StringWriter();
            var transport = CreateTransport(() => writer);
            var requestTask = transport.SendRequestAsync(
                "codex/test",
                new Dictionary<string, object>(),
                TimeSpan.FromSeconds(30),
                CancellationToken.None);
            var requestId = Convert.ToString(ReadSingleWrittenMessage(writer)["id"]);

            string errorMessage;
            var handled = transport.TryHandleResponse(
                new Dictionary<string, object>
                {
                    { "id", requestId + "-unknown" },
                    { "error", new Dictionary<string, object> { { "message", "Unexpected error" } } }
                },
                out errorMessage);

            Assert.That(handled, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(requestTask.IsCompleted, Is.False);

            var pendingRequests = transport.DrainPendingRequests();
            Assert.That(pendingRequests, Has.Count.EqualTo(1));
            pendingRequests[0].TrySetException(new InvalidOperationException("drained"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await requestTask.ConfigureAwait(false));
        }

        [Test]
        public async Task SendNotificationAsync_WritesJsonRpcNotificationWithoutId()
        {
            var writer = new StringWriter();
            var transport = CreateTransport(() => writer);

            await transport.SendNotificationAsync(
                "codex/notice",
                new Dictionary<string, object> { { "value", "input" } },
                CancellationToken.None).ConfigureAwait(false);

            var message = ReadSingleWrittenMessage(writer);
            Assert.That(message["jsonrpc"], Is.EqualTo("2.0"));
            Assert.That(message["method"], Is.EqualTo("codex/notice"));
            Assert.That(AppServerJsonValue.GetDictionary(message, "params")["value"], Is.EqualTo("input"));
            Assert.That(message.ContainsKey("id"), Is.False);
        }

        [Test]
        public async Task SendServerRequestResponseAsync_WritesResultEnvelope()
        {
            var writer = new StringWriter();
            var transport = CreateTransport(() => writer);

            await transport.SendServerRequestResponseAsync(
                "server-request-1",
                new Dictionary<string, object> { { "allowed", true } },
                CancellationToken.None).ConfigureAwait(false);

            var message = ReadSingleWrittenMessage(writer);
            Assert.That(message["jsonrpc"], Is.EqualTo("2.0"));
            Assert.That(message["id"], Is.EqualTo("server-request-1"));
            Assert.That(AppServerJsonValue.GetDictionary(message, "result")["allowed"], Is.EqualTo(true));
            Assert.That(message.ContainsKey("method"), Is.False);
        }

        [Test]
        public async Task SendErrorResponseAsync_WritesErrorEnvelope()
        {
            var writer = new StringWriter();
            var transport = CreateTransport(() => writer);

            await transport.SendErrorResponseAsync(
                "server-request-1",
                -32601,
                "not implemented",
                CancellationToken.None).ConfigureAwait(false);

            var message = ReadSingleWrittenMessage(writer);
            var error = AppServerJsonValue.GetDictionary(message, "error");
            Assert.That(message["jsonrpc"], Is.EqualTo("2.0"));
            Assert.That(message["id"], Is.EqualTo("server-request-1"));
            Assert.That(error["code"], Is.EqualTo(-32601));
            Assert.That(error["message"], Is.EqualTo("not implemented"));
        }

        [Test]
        public void DrainPendingRequests_ReturnsAndClearsOutstandingRequests()
        {
            var writer = new StringWriter();
            var transport = CreateTransport(() => writer);
            var firstTask = transport.SendRequestAsync(
                "codex/first",
                new Dictionary<string, object>(),
                TimeSpan.FromSeconds(30),
                CancellationToken.None);
            var secondTask = transport.SendRequestAsync(
                "codex/second",
                new Dictionary<string, object>(),
                TimeSpan.FromSeconds(30),
                CancellationToken.None);

            var pendingRequests = transport.DrainPendingRequests();

            Assert.That(pendingRequests, Has.Count.EqualTo(2));
            Assert.That(transport.DrainPendingRequests(), Has.Count.EqualTo(0));

            foreach (var pendingRequest in pendingRequests)
            {
                pendingRequest.TrySetException(new InvalidOperationException("drained"));
            }

            Assert.ThrowsAsync<InvalidOperationException>(async () => await firstTask.ConfigureAwait(false));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await secondTask.ConfigureAwait(false));
        }

        [Test]
        public async Task SendRequestAsync_BlockedWriteHonorsTimeoutAndKeepsMessagesFromInterleaving()
        {
            var writer = new BlockedWriter();
            var transport = CreateTransport(() => writer);

            var request = transport.SendRequestAsync("codex/first", new Dictionary<string, object>(), TimeSpan.FromMilliseconds(50), CancellationToken.None);

            Assert.That(await Task.WhenAny(request, Task.Delay(10000)).ConfigureAwait(false), Is.SameAs(request));
            Assert.ThrowsAsync<TimeoutException>(async () => await request.ConfigureAwait(false));
            Assert.That(transport.DrainPendingRequests(), Is.Empty);

            // The first write still owns the writer, so the next message times out while queued.
            var queued = transport.SendRequestAsync("codex/second", new Dictionary<string, object>(), TimeSpan.FromMilliseconds(50), CancellationToken.None);
            Assert.That(await Task.WhenAny(queued, Task.Delay(10000)).ConfigureAwait(false), Is.SameAs(queued));
            Assert.ThrowsAsync<TimeoutException>(async () => await queued.ConfigureAwait(false));
            Assert.That(writer.WriteCount, Is.EqualTo(1));

            writer.Release();
            await transport.SendNotificationAsync("codex/next", new Dictionary<string, object>(), CancellationToken.None).ConfigureAwait(false);
            Assert.That(writer.WriteCount, Is.EqualTo(2));
        }

        [Test]
        public async Task SendRequestAsync_BlockedWriteHonorsCancellation()
        {
            var writer = new BlockedWriter();
            var transport = CreateTransport(() => writer);
            using (var cancellation = new CancellationTokenSource())
            {
                var request = transport.SendRequestAsync("codex/first", new Dictionary<string, object>(), TimeSpan.FromMinutes(5), cancellation.Token);
                cancellation.Cancel();

                Assert.That(await Task.WhenAny(request, Task.Delay(10000)).ConfigureAwait(false), Is.SameAs(request));
                Assert.That(request.IsCanceled, Is.True);
            }

            writer.Release();
        }

        [Test]
        public async Task StalledWrite_FreesLockBeforeReportingAndFailsLaterWritesForThatConnection()
        {
            var writer = new BlockedWriter();
            var writeLock = new SemaphoreSlim(1, 1);
            var reported = new TaskCompletionSource<long>();
            var lockFreeWhenReported = false;
            var transport = new AppServerProtocolTransport(
                () => writer,
                AppServerJsonValue.CreateSerializer(),
                writeLock,
                new object(),
                (method, timeoutMs) => new TimeoutException(method),
                () => 7L,
                (epoch, error) =>
                {
                    lockFreeWhenReported = writeLock.CurrentCount == 1;
                    reported.TrySetResult(epoch);
                },
                TimeSpan.FromMilliseconds(50));

            var notification = transport.SendNotificationAsync("codex/stuck", new Dictionary<string, object>(), CancellationToken.None);

            Assert.That(await Task.WhenAny(reported.Task, Task.Delay(10000)).ConfigureAwait(false), Is.SameAs(reported.Task));
            Assert.That(reported.Task.Result, Is.EqualTo(7L));
            Assert.That(lockFreeWhenReported, Is.True);

            var exception = Assert.ThrowsAsync<AppServerTransportWriteException>(async () =>
                await transport.SendNotificationAsync("codex/next", new Dictionary<string, object>(), CancellationToken.None).ConfigureAwait(false));
            Assert.That(exception.ConnectionEpoch, Is.EqualTo(7L));
            Assert.That(writer.WriteCount, Is.EqualTo(1));

            writer.Release();
            try
            {
                await notification.ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // The caller's own deadline ends at about the same moment as the stall.
            }
        }

        [Test]
        public void SendRequestAsync_BrokenPipeDuringSlowWriteIsReportedAsWriteFailure()
        {
            var writer = new BlockedWriter();
            var transport = CreateTransport(() => writer);
            var request = transport.SendRequestAsync("codex/first", new Dictionary<string, object>(), TimeSpan.FromMinutes(5), CancellationToken.None);

            writer.Fail(new IOException("broken pipe"));

            var exception = Assert.ThrowsAsync<AppServerTransportWriteException>(async () => await request.ConfigureAwait(false));
            Assert.That(exception.InnerException, Is.TypeOf<IOException>());
        }

        private sealed class BlockedWriter : TextWriter
        {
            private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();

            public int WriteCount { get; private set; }

            public override System.Text.Encoding Encoding
            {
                get { return System.Text.Encoding.UTF8; }
            }

            public override Task WriteLineAsync(string value)
            {
                WriteCount++;
                return _completion.Task;
            }

            public override Task FlushAsync()
            {
                return Task.CompletedTask;
            }

            public void Release()
            {
                _completion.TrySetResult(true);
            }

            public void Fail(Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        private static AppServerProtocolTransport CreateTransport(Func<TextWriter> writerProvider)
        {
            return new AppServerProtocolTransport(
                writerProvider,
                AppServerJsonValue.CreateSerializer(),
                new SemaphoreSlim(1, 1),
                new object(),
                (method, timeoutMs) => new TimeoutException("timeout: " + method + " " + timeoutMs.ToString()));
        }

        private static IDictionary<string, object> ReadSingleWrittenMessage(StringWriter writer)
        {
            var lines = writer.ToString()
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.That(lines, Has.Length.EqualTo(1));
            return AppServerJsonValue.DeserializeDictionary(lines.Single());
        }
    }
}
