using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Services;

namespace MyCodex.Infrastructure.Instances
{
    public sealed partial class TargetInstanceCoordinator : IDisposable
    {
        internal const int MaximumPayloadBytes = 65536;
        private readonly string _route;
        private readonly string _recordPath;
        private readonly TargetInstanceGate _gate;
        private readonly TargetInstanceRecord _record;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly object _sync = new object();
        private TargetInstanceStatus _status = new TargetInstanceStatus();
        private Func<TargetInstanceRequest, Task<TargetInstanceReply>> _handler;
        private Task _listener;
        private int _disposed;

        private TargetInstanceCoordinator(string route, string path, TargetInstanceGate gate, TargetInstanceRecord record)
        {
            _route = route;
            _recordPath = path;
            _gate = gate;
            _record = record;
        }

        public bool IsPrimaryInstance => _gate != null;
        public TargetInstanceRecord InstanceRecord => _record;

        public static TargetInstanceCoordinator Acquire(string channelId, string targetId, string sharedRoot)
        {
            using (var lifecycle = ReserveTargetStartup(channelId))
                return AcquireTarget(channelId, targetId, sharedRoot);
        }

        private static TargetInstanceCoordinator AcquireTarget(string channelId, string targetId, string sharedRoot)
        {
            var route = TargetInstanceSecurity.Route(channelId, targetId);
            var path = RecordPath(sharedRoot, route);
            var gate = TargetInstanceGate.TryAcquire(route);
            if (gate == null) return new TargetInstanceCoordinator(route, path, null, null);
            try
            {
                EnsurePreviousOwnerExited(path);
                var record = TargetInstanceSecurity.Capture(channelId, targetId);
                WriteRecord(path, record);
                var coordinator = new TargetInstanceCoordinator(route, path, gate, record);
                coordinator._listener = Task.Run(coordinator.ListenAsync);
                return coordinator;
            }
            catch { gate.Dispose(); throw; }
        }

        // The reservation must span the complete read-modify-save operation.
        public static IDisposable TryReserveOfflineEdit(string channelId, string targetId, string sharedRoot)
        {
            using (var lifecycle = ReserveTargetStartup(channelId))
                return ReserveOfflineTarget(channelId, targetId, sharedRoot);
        }

        private static IDisposable ReserveOfflineTarget(string channelId, string targetId, string sharedRoot)
        {
            var route = TargetInstanceSecurity.Route(channelId, targetId);
            var gate = TargetInstanceGate.TryAcquire(route);
            if (gate == null) return null;
            try
            {
                EnsurePreviousOwnerExited(RecordPath(sharedRoot, route));
                return gate;
            }
            catch { gate.Dispose(); throw; }
        }

        public void StartServer(Func<TargetInstanceRequest, Task<TargetInstanceReply>> handler)
        {
            if (!IsPrimaryInstance) throw new InvalidOperationException("Only the target owner can handle requests.");
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (_sync) _handler = handler;
        }

        public void UpdateStatus(TargetInstanceStatus status)
        {
            if (status == null) throw new ArgumentNullException(nameof(status));
            if (!Enum.IsDefined(typeof(TargetInstanceLifecycle), status.Lifecycle)) throw new ArgumentException("Invalid lifecycle.");
            if ((status.ConnectionDetail ?? string.Empty).Length > 4096) throw new ArgumentException("Status detail is too long.");
            if (status.ActiveWorkCount < 0 || status.UnsavedWindowCount < 0) throw new ArgumentException("Status counts cannot be negative.");
            lock (_sync)
            {
                if (_status.Lifecycle == TargetInstanceLifecycle.Closing && status.Lifecycle != TargetInstanceLifecycle.Closing)
                    throw new InvalidOperationException("A closing owner cannot reopen its target.");
                _status = CopyStatus(status);
            }
        }

        public static TargetInstanceReply Send(string channelId, string targetId, string sharedRoot,
            TargetInstanceRequest request, int timeoutMilliseconds = 3000)
        {
            return SendToOwner(channelId, targetId, sharedRoot, request, null, timeoutMilliseconds);
        }

        public static TargetInstanceReply SendToInstance(TargetInstanceRecord instance, string sharedRoot,
            TargetInstanceRequest request, int timeoutMilliseconds = 3000)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            return SendToOwner(instance.ChannelId, instance.TargetId, sharedRoot, request, instance, timeoutMilliseconds);
        }

        private static TargetInstanceReply SendToOwner(string channelId, string targetId, string sharedRoot,
            TargetInstanceRequest request, TargetInstanceRecord expected, int timeoutMilliseconds)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var maximumTimeout = string.Equals(targetId, "windows", StringComparison.Ordinal) &&
                request.Operation == TargetInstanceOperation.ApplyRuntimeSettings &&
                MyCodex.Shared.Constants.CodexRuntimeSources.IsBundledFamily(request.RuntimeSettings?.CodexRuntimeSource)
                ? 15 * 60 * 1000 : 30000;
            if (timeoutMilliseconds < 1 || timeoutMilliseconds > maximumTimeout) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            var route = TargetInstanceSecurity.Route(channelId, targetId);
            try
            {
                var record = ReadRecord(RecordPath(sharedRoot, route));
                if (!MatchesRoute(record, channelId, targetId) || TargetInstanceSecurity.IsAlive(record) != true)
                    return TargetInstanceReply.Failure("The target owner is absent or its identity cannot be verified.");
                if (expected != null && !SameInstance(record, expected))
                    return FailureWithInstance("The target owner changed. The new instance was not prepared and will not be closed.", record);
                if (!TargetInstanceSecurity.IsSameInstallation(record, IsInstallationExitRequest(request)))
                    return FailureWithInstance("The target is owned by another installation. Exit that instance normally first.", record);
                request.ChannelId = channelId;
                request.TargetId = targetId;
                request.InstanceId = record.InstanceId;
                request.Caller = TargetInstanceSecurity.Capture(channelId, targetId);
                ValidateRequest(request);
                return SendAsync(route, record, request, timeoutMilliseconds).GetAwaiter().GetResult();
            }
            catch (Exception exception) when (IsTransportFailure(exception))
            {
                return TargetInstanceReply.Failure("The target did not confirm the request. It may be starting, busy, or closing; an edit outcome is unknown.");
            }
        }

        public static IReadOnlyList<TargetInstanceReply> Discover(string channelId, string sharedRoot, int timeoutMilliseconds = 500)
        {
            var directory = RecordsDirectory(sharedRoot);
            if (!Directory.Exists(directory)) return new TargetInstanceReply[0];
            var results = new List<TargetInstanceReply>();
            foreach (var path in Directory.EnumerateFiles(directory, "MyCodex.Target.*.json"))
            {
                try
                {
                    var record = ReadRecord(path);
                    if (!string.Equals(record.ChannelId, channelId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!MatchesRoute(record, channelId, record.TargetId) ||
                        !string.Equals(Path.GetFileName(path), TargetInstanceSecurity.Route(channelId, record.TargetId) + ".json", StringComparison.Ordinal))
                    {
                        results.Add(TargetInstanceReply.Failure("An instance record could not be verified."));
                        continue;
                    }
                    if (TargetInstanceSecurity.IsAlive(record) == false) continue;
                    var reply = Send(channelId, record.TargetId, sharedRoot,
                        new TargetInstanceRequest { Operation = TargetInstanceOperation.Status }, timeoutMilliseconds);
                    if (reply.Instance == null) reply.Instance = record;
                    results.Add(reply);
                }
                catch (Exception exception) when (IsTransportFailure(exception))
                {
                    results.Add(TargetInstanceReply.Failure("An instance record could not be inspected."));
                }
            }
            return results;
        }

        private async Task ListenAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using (var pipe = TargetInstanceSecurity.CreatePipe(_route))
                    using (_stop.Token.Register(() => pipe.Dispose()))
                    {
                        await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                        using (var deadline = new CancellationTokenSource(5000))
                        using (deadline.Token.Register(() => pipe.Dispose()))
                        {
                            var request = await ReadAsync<TargetInstanceRequest>(pipe).ConfigureAwait(false);
                            ValidateRequest(request);
                            if (!MatchesRoute(request.Caller, _record.ChannelId, _record.TargetId) ||
                                !string.Equals(request.ChannelId, _record.ChannelId, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(request.TargetId, _record.TargetId, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(request.InstanceId, _record.InstanceId, StringComparison.Ordinal) ||
                                !TargetInstanceSecurity.VerifyPeer(pipe, request.Caller, true, IsInstallationExitRequest(request)))
                                continue;
                            // Bound wire I/O, not the owner's user confirmation dialog.
                            deadline.CancelAfter(Timeout.Infinite);
                            using (var dispatchStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                            {
                                var dispatch = DispatchAsync(request);
                                var stopped = Task.Delay(Timeout.Infinite, dispatchStop.Token);
                                if (await Task.WhenAny(dispatch, stopped).ConfigureAwait(false) != dispatch) return;
                                dispatchStop.Cancel();
                                var reply = await dispatch.ConfigureAwait(false);
                                reply.Instance = _record;
                                deadline.CancelAfter(5000);
                                try { await WriteAsync(pipe, reply).ConfigureAwait(false); }
                                finally { reply.AfterResponse?.Invoke(); }
                            }
                        }
                    }
                }
                catch (Exception exception) when (IsTransportFailure(exception))
                {
                    if (!_stop.IsCancellationRequested)
                    {
                        try { await Task.Delay(25, _stop.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                    }
                }
            }
        }

        private async Task<TargetInstanceReply> DispatchAsync(TargetInstanceRequest request)
        {
            Func<TargetInstanceRequest, Task<TargetInstanceReply>> handler;
            TargetInstanceStatus status;
            lock (_sync) { status = CopyStatus(_status); handler = _handler; }
            if (request.Operation == TargetInstanceOperation.Status)
            {
                lock (_sync) status.IsExitPrepared = status.IsExitPrepared || (_exitPreparation != null && !_exitPreparation.WasCancelled);
                return new TargetInstanceReply { Success = true, Status = status };
            }
            if (status.Lifecycle != TargetInstanceLifecycle.Running || handler == null)
                return new TargetInstanceReply { Error = "The target is starting or closing. Retry after its state changes.", Status = status };
            if (IsExitOperation(request.Operation)) return await DispatchExitAsync(request, handler).ConfigureAwait(false);
            lock (_sync)
            {
                if (_exitPreparation != null && (request.Operation == TargetInstanceOperation.ApplyRuntimeSettings || request.Operation == TargetInstanceOperation.RemoveTarget))
                    return TargetInstanceReply.Failure("This target is preparing to exit. Cancel that operation before changing settings.");
            }
            try { return await handler(request).ConfigureAwait(false) ?? TargetInstanceReply.Failure("The target did not confirm the request."); }
            catch { return TargetInstanceReply.Failure("The target could not complete the request."); }
        }

        private static async Task<TargetInstanceReply> SendAsync(string route, TargetInstanceRecord record,
            TargetInstanceRequest request, int timeoutMilliseconds)
        {
            using (var pipe = new NamedPipeClientStream(".", route, PipeDirection.InOut, PipeOptions.Asynchronous))
            using (var deadline = new CancellationTokenSource(timeoutMilliseconds))
            using (deadline.Token.Register(() => pipe.Dispose()))
            {
                await pipe.ConnectAsync(timeoutMilliseconds, deadline.Token).ConfigureAwait(false);
                if (!TargetInstanceSecurity.VerifyPeer(pipe, record, false, IsInstallationExitRequest(request)))
                    return FailureWithInstance("The target pipe owner could not be verified.", record);
                if (request.Operation == TargetInstanceOperation.Activate) TargetInstanceSecurity.AllowActivation(record.ProcessId);
                await WriteAsync(pipe, request).ConfigureAwait(false);
                var reply = await ReadAsync<TargetInstanceReply>(pipe).ConfigureAwait(false);
                if (reply == null || reply.Instance == null ||
                    !string.Equals(reply.Instance.InstanceId, record.InstanceId, StringComparison.Ordinal) ||
                    !MatchesRoute(reply.Instance, record.ChannelId, record.TargetId) ||
                    !TargetInstanceSecurity.VerifyPeer(pipe, reply.Instance, false, IsInstallationExitRequest(request)))
                    return FailureWithInstance("The target response identity could not be verified.", record);
                return reply;
            }
        }

        private static bool IsInstallationExitRequest(TargetInstanceRequest request)
        {
            // Other editions in this directory may inspect and coordinate exit only.
            return request.Operation == TargetInstanceOperation.Status || IsExitOperation(request.Operation);
        }

        internal static void ValidateRequest(TargetInstanceRequest request)
        {
            if (request == null || !Enum.IsDefined(typeof(TargetInstanceOperation), request.Operation))
                throw new InvalidDataException("Unsupported target operation.");
            if ((request.WorkspacePath ?? string.Empty).Length > 32767 || (request.WorkspacePath ?? string.Empty).IndexOf('\0') >= 0)
                throw new InvalidDataException("Invalid workspace path.");
            if (request.Operation != TargetInstanceOperation.Activate && !string.IsNullOrEmpty(request.WorkspacePath))
                throw new InvalidDataException("Only activation accepts a workspace.");
            if ((request.Operation == TargetInstanceOperation.ApplyRuntimeSettings) != (request.RuntimeSettings != null))
                throw new InvalidDataException("Runtime settings are required only for the settings operation.");
            Guid operationId;
            if (IsExitOperation(request.Operation)
                ? !Guid.TryParseExact(request.OperationId, "N", out operationId)
                : !string.IsNullOrEmpty(request.OperationId))
                throw new InvalidDataException("A bounded operation identity is required only for exit operations.");
            if (request.Operation == TargetInstanceOperation.RemoveTarget
                ? !TargetMigrationPlanner.IsSafeTargetId(request.RemovalTargetId) || string.Equals(request.RemovalTargetId, "windows", StringComparison.OrdinalIgnoreCase)
                : !string.IsNullOrEmpty(request.RemovalTargetId))
                throw new InvalidDataException("A safe non-Windows target is required only for removal.");
        }

        private static async Task WriteAsync<T>(Stream stream, T value)
        {
            var data = Encoding.UTF8.GetBytes(Serializer().Serialize(value));
            if (data.Length == 0 || data.Length > MaximumPayloadBytes) throw new InvalidDataException("Target message is too large.");
            var header = BitConverter.GetBytes(data.Length);
            await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        private static async Task<T> ReadAsync<T>(Stream stream)
        {
            var header = new byte[4];
            await ReadExactlyAsync(stream, header).ConfigureAwait(false);
            var size = BitConverter.ToInt32(header, 0);
            if (size < 1 || size > MaximumPayloadBytes) throw new InvalidDataException("Invalid target message length.");
            var data = new byte[size];
            await ReadExactlyAsync(stream, data).ConfigureAwait(false);
            return Serializer().Deserialize<T>(new UTF8Encoding(false, true).GetString(data));
        }

        private static async Task ReadExactlyAsync(Stream stream, byte[] data)
        {
            var offset = 0;
            while (offset < data.Length)
            {
                var count = await stream.ReadAsync(data, offset, data.Length - offset).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaximumPayloadBytes, RecursionLimit = 16 };
        }

        private static TargetInstanceRecord ReadRecord(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var length = stream.Length;
                if (length <= 0 || length > MaximumPayloadBytes) throw new InvalidDataException("Invalid instance record size.");
                var data = new byte[(int)length];
                var offset = 0;
                while (offset < data.Length)
                {
                    var count = stream.Read(data, offset, data.Length - offset);
                    if (count == 0) throw new EndOfStreamException();
                    offset += count;
                }
                if (stream.ReadByte() != -1) throw new InvalidDataException("Instance record changed while being read.");
                var record = Serializer().Deserialize<TargetInstanceRecord>(new UTF8Encoding(false, true).GetString(data));
                if (record == null) throw new InvalidDataException("Invalid instance record.");
                return record;
            }
        }

        private static void WriteRecord(string path, TargetInstanceRecord record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, Serializer().Serialize(record), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static void EnsurePreviousOwnerExited(string path)
        {
            if (!File.Exists(path)) return;
            if (TargetInstanceSecurity.IsAlive(ReadRecord(path)) != false)
                throw new InvalidOperationException("The previous target owner has not exited or cannot be verified. Retry after it exits normally.");
        }

        private static bool MatchesRoute(TargetInstanceRecord record, string channelId, string targetId)
        {
            return record != null && record.UserSid == TargetInstanceSecurity.CurrentSid &&
                !string.IsNullOrWhiteSpace(record.InstanceId) &&
                string.Equals(record.ChannelId, channelId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.TargetId, targetId, StringComparison.OrdinalIgnoreCase);
        }

        private static string RecordsDirectory(string sharedRoot) => Path.Combine(Path.GetFullPath(sharedRoot), "targets", ".instances");
        private static string RecordPath(string sharedRoot, string route) => Path.Combine(RecordsDirectory(sharedRoot), route + ".json");

        private static TargetInstanceReply FailureWithInstance(string error, TargetInstanceRecord record)
        {
            return new TargetInstanceReply { Error = error, Instance = record };
        }

        private static TargetInstanceStatus CopyStatus(TargetInstanceStatus status)
        {
            return new TargetInstanceStatus
            {
                Lifecycle = status.Lifecycle, IsWorking = status.IsWorking,
                HasPendingInteraction = status.HasPendingInteraction,
                IsConnected = status.IsConnected, ConnectionDetail = status.ConnectionDetail,
                IsPreparingBundledRuntime = status.IsPreparingBundledRuntime,
                IsExitPrepared = status.IsExitPrepared,
                ActiveWorkCount = status.ActiveWorkCount, UnsavedWindowCount = status.UnsavedWindowCount
            };
        }

        private static bool IsTransportFailure(Exception exception)
        {
            return exception is IOException || exception is InvalidDataException || exception is TimeoutException || exception is OperationCanceledException ||
                exception is ObjectDisposedException || exception is UnauthorizedAccessException || exception is ArgumentException ||
                exception is InvalidOperationException || exception is System.ComponentModel.Win32Exception;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (_sync) _status.Lifecycle = TargetInstanceLifecycle.Closing;
            _stop.Cancel();
            _exitPreparationTimer?.Dispose();
            try { _listener?.GetAwaiter().GetResult(); }
            finally
            {
                // Remove only our record while still holding the target gate.
                try
                {
                    if (_record != null && File.Exists(_recordPath) && ReadRecord(_recordPath).InstanceId == _record.InstanceId)
                        File.Delete(_recordPath);
                }
                finally { _gate?.Dispose(); _stop.Dispose(); }
            }
        }
    }
}
