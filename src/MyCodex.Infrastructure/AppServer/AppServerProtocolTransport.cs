using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerTransportWriteException : Exception
    {
        public AppServerTransportWriteException(string message, Exception innerException)
            : this(message, innerException, 0L)
        {
        }

        public AppServerTransportWriteException(string message, Exception innerException, long connectionEpoch)
            : base(message, innerException)
        {
            ConnectionEpoch = connectionEpoch;
        }

        public long ConnectionEpoch { get; }
    }

    internal sealed class AppServerProtocolTransport
    {
        // A short request timeout must not end a connection that is merely slow to accept a large message.
        private static readonly TimeSpan DefaultWriteStallTimeout = TimeSpan.FromSeconds(60);

        private readonly Func<TextWriter> _writerProvider;
        private readonly JavaScriptSerializer _serializer;
        private readonly SemaphoreSlim _writeLock;
        private readonly object _syncRoot;
        private readonly Func<string, int, TimeoutException> _timeoutFactory;
        private readonly Func<long> _connectionEpochProvider;
        private readonly Action<long, Exception> _writeStalled;
        private readonly TimeSpan _minimumWriteStallTimeout;
        private readonly Dictionary<string, TaskCompletionSource<IDictionary<string, object>>> _pendingRequests =
            new Dictionary<string, TaskCompletionSource<IDictionary<string, object>>>(StringComparer.Ordinal);
        private int _nextRequestId;
        private long? _stalledConnectionEpoch;

        public AppServerProtocolTransport(
            Func<TextWriter> writerProvider,
            JavaScriptSerializer serializer,
            SemaphoreSlim writeLock,
            object syncRoot,
            Func<string, int, TimeoutException> timeoutFactory,
            Func<long> connectionEpochProvider = null,
            Action<long, Exception> writeStalled = null,
            TimeSpan? minimumWriteStallTimeout = null)
        {
            _writeStalled = writeStalled;
            _minimumWriteStallTimeout = minimumWriteStallTimeout ?? DefaultWriteStallTimeout;
            _writerProvider = writerProvider ?? throw new ArgumentNullException(nameof(writerProvider));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _writeLock = writeLock ?? throw new ArgumentNullException(nameof(writeLock));
            _syncRoot = syncRoot ?? throw new ArgumentNullException(nameof(syncRoot));
            _timeoutFactory = timeoutFactory ?? throw new ArgumentNullException(nameof(timeoutFactory));
            _connectionEpochProvider = connectionEpochProvider ?? (() => 0L);
        }

        public IList<TaskCompletionSource<IDictionary<string, object>>> DrainPendingRequests()
        {
            lock (_syncRoot)
            {
                var pendingRequests = new List<TaskCompletionSource<IDictionary<string, object>>>(_pendingRequests.Values);
                _pendingRequests.Clear();
                return pendingRequests;
            }
        }

        public async Task<IDictionary<string, object>> SendRequestAsync(
            string method,
            IDictionary<string, object> parameters,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            long? expectedConnectionEpoch = null)
        {
            var requestId = Interlocked.Increment(ref _nextRequestId).ToString();
            var watch = Stopwatch.StartNew();
            var outcome = "ok";
            try
            {
                return await SendRequestCoreAsync(method, parameters, timeout, cancellationToken, expectedConnectionEpoch, requestId)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                outcome = "timeout";
                throw;
            }
            catch (OperationCanceledException)
            {
                outcome = "canceled";
                throw;
            }
            catch
            {
                outcome = "error";
                throw;
            }
            finally
            {
                if (watch.ElapsedMilliseconds >= AppServerHistoryLoadTrace.SlowRequestMilliseconds)
                {
                    Trace.TraceWarning(AppServerHistoryLoadTrace.BuildSlowRequestText(method, requestId, watch.ElapsedMilliseconds, outcome));
                }
            }
        }

        private async Task<IDictionary<string, object>> SendRequestCoreAsync(
            string method,
            IDictionary<string, object> parameters,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            long? expectedConnectionEpoch,
            string requestId)
        {
            var timeoutMs = GetTimeoutMilliseconds(timeout);
            var connectionEpoch = expectedConnectionEpoch ?? _connectionEpochProvider();
            var pendingRequest = new TaskCompletionSource<IDictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            // One deadline covers waiting for the write lock, writing, and the response.
            var elapsed = Stopwatch.StartNew();

            lock (_syncRoot)
            {
                _pendingRequests[requestId] = pendingRequest;
            }

            try
            {
                await WriteMessageAsync(
                    new Dictionary<string, object>
                    {
                        { "jsonrpc", "2.0" },
                        { "id", requestId },
                        { "method", method },
                        { "params", parameters ?? new Dictionary<string, object>() }
                    },
                    cancellationToken,
                    connectionEpoch,
                    method,
                    timeout).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                RemovePendingRequest(requestId, pendingRequest);
                pendingRequest.TrySetCanceled();
                throw;
            }
            catch (TimeoutException ex)
            {
                RemovePendingRequest(requestId, pendingRequest);
                pendingRequest.TrySetException(ex);
                throw;
            }
            catch (AppServerTransportWriteException ex)
            {
                RemovePendingRequest(requestId, pendingRequest);
                pendingRequest.TrySetException(ex);
                throw;
            }

            var delayTask = Task.Delay(GetRemainingTime(timeout, elapsed), cancellationToken);
            var completedTask = await Task.WhenAny(pendingRequest.Task, delayTask).ConfigureAwait(false);
            if (completedTask == pendingRequest.Task)
            {
                return await pendingRequest.Task.ConfigureAwait(false);
            }

            if (!RemovePendingRequest(requestId, pendingRequest))
            {
                return await pendingRequest.Task.ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                pendingRequest.TrySetCanceled();
                throw new OperationCanceledException(cancellationToken);
            }

            var timeoutException = _timeoutFactory(method, timeoutMs);
            pendingRequest.TrySetException(timeoutException);
            throw timeoutException;
        }

        public Task SendNotificationAsync(string method, IDictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            return WriteMessageAsync(
                new Dictionary<string, object>
                {
                    { "jsonrpc", "2.0" },
                    { "method", method },
                    { "params", parameters ?? new Dictionary<string, object>() }
                },
                cancellationToken);
        }

        public async Task SendRequestWithoutWaitingAsync(
            string method,
            IDictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            var connectionEpoch = _connectionEpochProvider();
            var requestId = Interlocked.Increment(ref _nextRequestId).ToString();
            await WriteMessageAsync(
                new Dictionary<string, object>
                {
                    { "jsonrpc", "2.0" },
                    { "id", requestId },
                    { "method", method },
                    { "params", parameters ?? new Dictionary<string, object>() }
                },
                cancellationToken,
                connectionEpoch).ConfigureAwait(false);
        }

        public Task SendServerRequestResponseAsync(object requestId, IDictionary<string, object> result, CancellationToken cancellationToken)
        {
            return SendServerRequestResponseAsync(requestId, result, _connectionEpochProvider(), cancellationToken);
        }

        public Task SendServerRequestResponseAsync(
            object requestId,
            IDictionary<string, object> result,
            long expectedConnectionEpoch,
            CancellationToken cancellationToken)
        {
            return WriteMessageAsync(
                new Dictionary<string, object>
                {
                    { "jsonrpc", "2.0" },
                    { "id", requestId },
                    { "result", result ?? new Dictionary<string, object>() }
                },
                cancellationToken,
                expectedConnectionEpoch);
        }

        public Task SendErrorResponseAsync(object requestId, int code, string message, CancellationToken cancellationToken)
        {
            return SendErrorResponseAsync(requestId, code, message, _connectionEpochProvider(), cancellationToken);
        }

        public Task SendErrorResponseAsync(
            object requestId,
            int code,
            string message,
            long expectedConnectionEpoch,
            CancellationToken cancellationToken)
        {
            return WriteMessageAsync(
                new Dictionary<string, object>
                {
                    { "jsonrpc", "2.0" },
                    { "id", requestId },
                    { "error", new Dictionary<string, object> { { "code", code }, { "message", message } } }
                },
                cancellationToken,
                expectedConnectionEpoch);
        }

        public bool TryHandleResponse(IDictionary<string, object> message, out string errorMessage)
        {
            errorMessage = null;
            var requestId = GetRequestId(message);
            TaskCompletionSource<IDictionary<string, object>> pendingRequest = null;

            lock (_syncRoot)
            {
                if (!string.IsNullOrEmpty(requestId) && _pendingRequests.TryGetValue(requestId, out pendingRequest))
                {
                    _pendingRequests.Remove(requestId);
                }
            }

            if (pendingRequest == null)
            {
                return true;
            }

            if (message != null && message.ContainsKey("error"))
            {
                var error = AppServerJsonValue.GetDictionary(message, "error");
                errorMessage = AppServerJsonValue.GetString(error, "message");
                if (string.IsNullOrWhiteSpace(errorMessage))
                {
                    errorMessage = "App Server request failed.";
                }

                pendingRequest.TrySetException(new InvalidOperationException(errorMessage));
                return true;
            }

            pendingRequest.TrySetResult(AppServerJsonValue.GetDictionary(message, "result"));
            return true;
        }

        private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
        {
            await WriteMessageAsync(message, cancellationToken, _connectionEpochProvider()).ConfigureAwait(false);
        }

        private Task WriteMessageAsync(object message, CancellationToken cancellationToken, long expectedConnectionEpoch)
        {
            return WriteMessageAsync(message, cancellationToken, expectedConnectionEpoch, "write", _minimumWriteStallTimeout);
        }

        private async Task WriteMessageAsync(
            object message,
            CancellationToken cancellationToken,
            long expectedConnectionEpoch,
            string operationName,
            TimeSpan timeout)
        {
            var elapsed = Stopwatch.StartNew();
            string json;
            try
            {
                json = _serializer.Serialize(message);
            }
            catch (Exception ex)
            {
                throw new AppServerTransportWriteException("Failed to write App Server message.", ex, expectedConnectionEpoch);
            }

            if (!await _writeLock.WaitAsync(timeout, cancellationToken).ConfigureAwait(false))
            {
                throw _timeoutFactory(operationName, GetTimeoutMilliseconds(timeout));
            }

            Task writeTask;
            var monitorOwnsWriteLock = false;
            try
            {
                if (_connectionEpochProvider() != expectedConnectionEpoch)
                {
                    throw new AppServerTransportWriteException("App Server connection changed before write.", null, expectedConnectionEpoch);
                }

                lock (_syncRoot)
                {
                    if (_stalledConnectionEpoch == expectedConnectionEpoch)
                    {
                        throw new AppServerTransportWriteException("An earlier App Server write did not finish.", null, expectedConnectionEpoch);
                    }
                }

                var writer = _writerProvider();
                if (writer == null)
                {
                    throw new AppServerTransportWriteException("App Server input writer is not available.", null, expectedConnectionEpoch);
                }

                cancellationToken.ThrowIfCancellationRequested();
                writeTask = WriteLineAndFlushAsync(writer, json, expectedConnectionEpoch);
                if (!writeTask.IsCompleted)
                {
                    // TextWriter has no cancellable write, so this caller may leave while the write is still
                    // running. The lock stays held until the write ends, which keeps messages from interleaving.
                    monitorOwnsWriteLock = true;
                    var stallTimeout = timeout > _minimumWriteStallTimeout ? timeout : _minimumWriteStallTimeout;
                    _ = MonitorWriteAsync(writeTask, stallTimeout, expectedConnectionEpoch);
                }
            }
            finally
            {
                if (!monitorOwnsWriteLock)
                {
                    _writeLock.Release();
                }
            }

            if (writeTask.IsCompleted)
            {
                await writeTask.ConfigureAwait(false);
                return;
            }

            using (var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var delayTask = Task.Delay(GetRemainingTime(timeout, elapsed), delayCancellation.Token);
                if (await Task.WhenAny(writeTask, delayTask).ConfigureAwait(false) == writeTask)
                {
                    delayCancellation.Cancel();
                    await writeTask.ConfigureAwait(false);
                    return;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw _timeoutFactory(operationName, GetTimeoutMilliseconds(timeout));
        }

        private static async Task WriteLineAndFlushAsync(TextWriter writer, string json, long expectedConnectionEpoch)
        {
            try
            {
                await writer.WriteLineAsync(json).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new AppServerTransportWriteException("Failed to write App Server message.", ex, expectedConnectionEpoch);
            }
        }

        // A write that never ends cannot be recovered: later writes fail for this connection, and the
        // lock is freed first because connection-lost handling takes the same lock to end the process.
        private async Task MonitorWriteAsync(Task writeTask, TimeSpan stallTimeout, long connectionEpoch)
        {
            Exception stall = null;
            try
            {
                using (var delayCancellation = new CancellationTokenSource())
                {
                    if (await Task.WhenAny(writeTask, Task.Delay(stallTimeout, delayCancellation.Token)).ConfigureAwait(false) == writeTask)
                    {
                        delayCancellation.Cancel();
                    }
                    else
                    {
                        lock (_syncRoot)
                        {
                            _stalledConnectionEpoch = connectionEpoch;
                        }

                        stall = new AppServerTransportWriteException("An App Server write did not finish in time.", null, connectionEpoch);
                    }
                }
            }
            finally
            {
                _writeLock.Release();
            }

            if (stall == null)
            {
                return;
            }

            try
            {
                _writeStalled?.Invoke(connectionEpoch, stall);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Handling a stalled App Server write failed: {0}", ex.Message);
            }
        }

        private static TimeSpan GetRemainingTime(TimeSpan timeout, Stopwatch elapsed)
        {
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                return timeout;
            }

            var remaining = timeout - elapsed.Elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        private bool RemovePendingRequest(
            string requestId,
            TaskCompletionSource<IDictionary<string, object>> pendingRequest)
        {
            lock (_syncRoot)
            {
                TaskCompletionSource<IDictionary<string, object>> existingRequest;
                if (_pendingRequests.TryGetValue(requestId, out existingRequest) &&
                    ReferenceEquals(existingRequest, pendingRequest))
                {
                    _pendingRequests.Remove(requestId);
                    return true;
                }
            }

            return false;
        }

        private static string GetRequestId(IDictionary<string, object> message)
        {
            object value;
            return message != null && message.TryGetValue("id", out value) && value != null
                ? Convert.ToString(value)
                : null;
        }

        private static int GetTimeoutMilliseconds(TimeSpan timeout)
        {
            if (timeout.TotalMilliseconds >= int.MaxValue)
            {
                return int.MaxValue;
            }

            if (timeout.TotalMilliseconds <= 0)
            {
                return 0;
            }

            return (int)timeout.TotalMilliseconds;
        }
    }
}
