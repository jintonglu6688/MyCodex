using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Processes;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal sealed class ComputerUseHostProcess : IDisposable
    {
        private readonly object _sync = new object();
        private readonly SemaphoreSlim _write = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, TaskCompletionSource<object>> _pending = new Dictionary<string, TaskCompletionSource<object>>();
        private readonly TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ComputerUseDesktopLease _lease;
        private readonly Process _process;
        private ComputerUseProcessJob _job;
        private readonly Action<string, string> _report;
        private readonly Func<string, string, CancellationToken, Task> _prepareExecution;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private int _sequence;
        private int _disposed;
        private int _cleanupConfirmed;

        public bool IsAlive { get { return Volatile.Read(ref _disposed) == 0 && !_process.HasExited; } }
        public bool IsCleanupConfirmed { get { return Volatile.Read(ref _cleanupConfirmed) != 0; } }

        public ComputerUseHostProcess(ComputerUseComponents components, string workingDirectory, Action<string, string> report,
            Func<string, string, CancellationToken, Task> prepareExecution = null)
        {
            _report = report;
            _prepareExecution = prepareExecution;
            var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "computer-use", "main.mjs");
            if (!File.Exists(script)) throw new InvalidOperationException("CUA_HOST_MISSING");
            var start = AppServerProcessLifecycle.BuildStartInfo(components.NodePath,
                WindowsCommandLineArgumentEscaper.Quote(script), workingDirectory,
                new Dictionary<string, string>
                {
                    { "MYCODEX_CUA_RUNTIME", components.RuntimeRoot },
                    { "CODEX_CLI_PATH", components.SelectedCodexPath }
                }, null);
            _process = new Process { StartInfo = start };
            _lease = new ComputerUseDesktopLease();
            try
            {
                _process.Start();
                _job = new ComputerUseProcessJob(_process);
                _process.StandardInput.AutoFlush = true;
                Task.Run(ReadAsync);
                // Component stderr can contain application data. Drain, never log it.
                Task.Run(async () => { try { while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) != null) { } } catch { } });
            }
            catch { Dispose(); throw; }
        }

        public async Task WaitReadyAsync(CancellationToken token)
        {
            if (await Task.WhenAny(_ready.Task, Task.Delay(10000, token)).ConfigureAwait(false) != _ready.Task)
            {
                TraceHostFailure("host_start", token.IsCancellationRequested ? "cancelled" : "timeout", null);
                Dispose();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("CUA_HOST_START_TIMEOUT");
            }
            await _ready.Task.ConfigureAwait(false);
        }

        public async Task<object> RequestAsync(string command, IDictionary<string, object> parameters = null, CancellationToken token = default(CancellationToken))
        {
            var id = Interlocked.Increment(ref _sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var message = parameters == null ? new Dictionary<string, object>() : new Dictionary<string, object>(parameters);
            message["id"] = id;
            message["command"] = command;
            var waiter = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                if (_disposed != 0) throw new InvalidOperationException("CUA_HOST_STOPPED");
                _pending.Add(id, waiter);
            }
            try
            {
                await _write.WaitAsync(token).ConfigureAwait(false);
                try { await _process.StandardInput.WriteLineAsync(new JavaScriptSerializer().Serialize(message)).ConfigureAwait(false); }
                finally { _write.Release(); }
                if (await Task.WhenAny(waiter.Task, Task.Delay(5000, token)).ConfigureAwait(false) != waiter.Task)
                {
                    // Control state is now unknown. Never retry an action or keep its lease alive.
                    TraceHostFailure("control_wait", token.IsCancellationRequested ? "cancelled" : "timeout", null);
                    Dispose();
                    token.ThrowIfCancellationRequested();
                    throw new InvalidOperationException("CUA_HOST_CONTROL_TIMEOUT");
                }
                return await waiter.Task.ConfigureAwait(false);
            }
            finally { lock (_sync) _pending.Remove(id); }
        }

        private async Task ReadAsync()
        {
            try
            {
                string line;
                while ((line = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (line.Length > 1024 * 1024) throw new InvalidOperationException("CUA_HOST_FRAME_LIMIT");
                    var value = new JavaScriptSerializer().DeserializeObject(line) as IDictionary<string, object>;
                    if (value == null) throw new InvalidOperationException("CUA_HOST_PROTOCOL");
                    if (value.ContainsKey("ready"))
                    {
                        if (!Equals(value["ready"], true) || !Equals(value["protocolVersion"], 2))
                            throw new InvalidOperationException("CUA_HOST_PROTOCOL");
                        _ready.TrySetResult(true);
                        continue;
                    }
                    var eventName = AppServerJsonValue.GetString(value, "event");
                    if (eventName == "fault" || eventName == "actionFailed" || eventName == "fatal")
                    {
                        // Do not serialize the event: it may also contain application or request data.
                        try { Trace.TraceWarning("[ComputerUse] {0}", FormatFailureDiagnostic(value)); }
                        catch { /* Diagnostics must not prevent status delivery or cleanup. */ }
                    }
                    if (eventName == "prepare")
                    {
                        HandlePrepareAsync(value).Forget("Computer Use execution preparation");
                        continue;
                    }
                    if (eventName == "available")
                    {
                        _report?.Invoke("Ready", AppServerJsonValue.GetString(value, "thread"));
                        continue;
                    }
                    if (eventName == "acquire")
                    {
                        HandleAcquireAsync(value).Forget("Computer Use lease");
                        continue;
                    }
                    if (eventName == "released")
                    {
                        _lease.Release(AppServerJsonValue.GetString(value, "leaseId"));
                        continue;
                    }
                    if (eventName == "fault")
                    {
                        var code = AppServerJsonValue.GetString(value, "code");
                        if (code != "CUA_PREPARATION_FAILED")
                            _report?.Invoke(code == "CUA_COMPONENT_CONTRACT" ? "ComponentContractUnsupported" : "ExecutionFailed",
                                AppServerJsonValue.GetString(value, "thread"));
                        continue;
                    }
                    if (eventName == "busy")
                    {
                        _report?.Invoke("DesktopBusy", AppServerJsonValue.GetString(value, "thread"));
                        continue;
                    }
                    if (eventName == "actionFailed")
                    {
                        var code = AppServerJsonValue.GetString(value, "code");
                        if (code == "CUA_GEOMETRY_UNAVAILABLE")
                            _report?.Invoke("GeometryUnavailable", AppServerJsonValue.GetString(value, "thread"));
                        else if (code == "CUA_WINDOW_OBSERVER_NOT_READY")
                            _report?.Invoke("WindowObserverNotReady", AppServerJsonValue.GetString(value, "thread"));
                        else if (code == "CUA_TARGET_OBSCURED")
                            _report?.Invoke("TargetObscured", AppServerJsonValue.GetString(value, "thread"));
                        else if (code == "CUA_OBSERVATION_STALE")
                            _report?.Invoke("ObservationStale", AppServerJsonValue.GetString(value, "thread"));
                        continue;
                    }
                    if (eventName == "fatal")
                    {
                        var code = AppServerJsonValue.GetString(value, "code");
                        if (code == "CUA_COMPONENT_CONTRACT")
                            _ready.TrySetException(new InvalidOperationException(code));
                        throw new InvalidOperationException("CUA_HOST_FAILED");
                    }
                    var id = AppServerJsonValue.GetString(value, "id");
                    TaskCompletionSource<object> waiter;
                    lock (_sync) { if (id == null || !_pending.TryGetValue(id, out waiter)) continue; }
                    if (value.ContainsKey("error"))
                    {
                        TraceHostFailure("control_response", "rejected", null);
                        waiter.TrySetException(new InvalidOperationException("CUA_HOST_CONTROL_FAILED"));
                    }
                    else { object result; value.TryGetValue("result", out result); waiter.TrySetResult(result); }
                }
                if (Volatile.Read(ref _disposed) == 0) TraceHostFailure("host_read", "eof", null);
            }
            catch (Exception exception)
            {
                if (Volatile.Read(ref _disposed) == 0) TraceHostFailure("host_read", "exception", exception);
            }
            finally { Dispose(); }
        }

        internal static string FormatFailureDiagnostic(IDictionary<string, object> message)
        {
            var diagnostic = AppServerJsonValue.GetDictionary(message, "diagnostic");
            Guid slot;
            var slotText = Guid.TryParse(AppServerJsonValue.GetString(message, "slot"), out slot)
                ? slot.ToString("D") : "unknown";
            var hresult = AppServerJsonValue.GetString(diagnostic, "hresult");
            return string.Format(CultureInfo.InvariantCulture,
                "Event={0}; Code={1}; Slot={2}; Generation={3}; Stage={4}; Method={5}; Approval={6}; " +
                "Category={7}; ErrorType={8}; ErrorCode={9}; HelperExitCode={10}; HResult={11}; ElapsedMs={12}",
                DiagnosticChoice(message, "event", "fault", "actionFailed", "fatal"),
                DiagnosticChoice(message, "code", "CUA_EXECUTION_FAILED", "CUA_COMPONENT_CONTRACT", "CUA_PREPARATION_FAILED",
                    "CUA_APPROVAL_TIMEOUT", "CUA_APPROVAL_CANCELLED", "CUA_CLIENT_DISCONNECTED", "CUA_TURN_ENDED",
                    "CUA_RESPONSE_TOO_LARGE", "CUA_CLOSE_TIMEOUT", "CUA_ACQUIRE_TIMEOUT", "CUA_PREPARATION_TIMEOUT",
                    "CUA_PIPE_FAILED", "CUA_INVALID_ENVIRONMENT", "CUA_INVALID_CONTROL", "CUA_CONTROL_TOO_LARGE",
                    "CUA_GEOMETRY_UNAVAILABLE", "CUA_WINDOW_OBSERVER_NOT_READY", "CUA_TARGET_OBSCURED", "CUA_OBSERVATION_STALE"),
                slotText, DiagnosticNumber(message, "generation", 0),
                DiagnosticChoice(diagnostic, "stage", "desktop_acquire", "transport_prepare", "transport_create",
                    "executor_request", "approval", "response_write"),
                DiagnosticChoice(diagnostic, "method", "list_apps", "list_windows", "get_window", "get_window_state",
                    "activate_window", "click", "click_element", "scroll", "drag", "press_key", "type_text", "launch_app",
                    "perform_secondary_action", "set_value", "end_turn"),
                DiagnosticChoice(diagnostic, "approval", "not_requested", "pending", "accept", "decline", "cancel", "invalid"),
                DiagnosticChoice(diagnostic, "category", "known_action_failure", "approval_declined", "request_timeout",
                    "helper_exit", "invalid_json", "system_error"),
                DiagnosticChoice(diagnostic, "errorType", "Error", "TypeError", "RangeError", "SyntaxError", "ReferenceError", "AbortError"),
                DiagnosticChoice(diagnostic, "errorCode", "ENOENT", "EACCES", "EPERM", "EPIPE", "ECONNRESET", "ECONNREFUSED",
                    "ETIMEDOUT", "ESRCH", "EBADF", "ERR_STREAM_DESTROYED", "ERR_STREAM_WRITE_AFTER_END"),
                DiagnosticNumber(diagnostic, "helperExitCode", int.MinValue, uint.MaxValue),
                hresult != null && Regex.IsMatch(hresult, @"\A0x[0-9a-fA-F]{8}\z") ? hresult : "unknown",
                DiagnosticNumber(diagnostic, "elapsedMs", 0));
        }

        private static string DiagnosticChoice(IDictionary<string, object> values, string key, params string[] allowed)
        {
            var value = AppServerJsonValue.GetString(values, key);
            return Array.IndexOf(allowed, value) >= 0 ? value : "unknown";
        }

        private static string DiagnosticNumber(IDictionary<string, object> values, string key, long min, long max = long.MaxValue)
        {
            object value;
            if (values == null || !values.TryGetValue(key, out value) || !(value is int || value is long)) return "unknown";
            var number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return number >= min && number <= max ? number.ToString(CultureInfo.InvariantCulture) : "unknown";
        }

        private static void TraceHostFailure(string stage, string reason, Exception exception)
        {
            try
            {
                var code = exception?.Message;
                if (code != "CUA_HOST_PROTOCOL" && code != "CUA_HOST_FRAME_LIMIT" && code != "CUA_HOST_FAILED") code = "unknown";
                Trace.TraceWarning("[ComputerUse] HostFailure; Stage={0}; Reason={1}; ExceptionType={2}; HResult={3}; Code={4}",
                    stage, reason, exception?.GetType().Name ?? "none",
                    exception == null ? "unknown" : "0x" + exception.HResult.ToString("x8", CultureInfo.InvariantCulture), code);
            }
            catch { /* Never let a diagnostic failure delay shutdown. */ }
        }

        private async Task HandleAcquireAsync(IDictionary<string, object> message)
        {
            var requestId = AppServerJsonValue.GetString(message, "id");
            if (string.IsNullOrWhiteSpace(requestId)) { Dispose(); return; }
            try
            {
                var granted = await _lease.TryAcquireAsync(requestId).ConfigureAwait(false);
                await RequestAsync("acquireResult", new Dictionary<string, object>
                {
                    { "requestId", requestId }, { "granted", granted }
                }).ConfigureAwait(false);
            }
            catch { Dispose(); }
        }

        private async Task HandlePrepareAsync(IDictionary<string, object> message)
        {
            var requestId = AppServerJsonValue.GetString(message, "id");
            if (string.IsNullOrWhiteSpace(requestId)) { Dispose(); return; }
            var prepared = false;
            try
            {
                if (_prepareExecution != null)
                {
                    await _prepareExecution(AppServerJsonValue.GetString(message, "thread"),
                        AppServerJsonValue.GetString(message, "turn"), _lifetime.Token).ConfigureAwait(false);
                    _lifetime.Token.ThrowIfCancellationRequested();
                    prepared = true;
                }
            }
            catch { /* The session manager reports a scoped failure; no desktop lease was acquired. */ }
            if (Volatile.Read(ref _disposed) != 0) return;
            try
            {
                await RequestAsync("prepareResult", new Dictionary<string, object>
                {
                    { "requestId", requestId }, { "prepared", prepared }
                }, _lifetime.Token).ConfigureAwait(false);
            }
            catch { Dispose(); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _lifetime?.Cancel();
            try
            {
                try { _process.StandardInput.Close(); } catch { }
                try { if (!_process.WaitForExit(2000)) ProcessTreeTerminator.TryKill(_process); } catch { }
                // Never release desktop ownership before terminating the owned executor tree.
                ProcessTreeTerminator.TryKill(_process);
            }
            finally
            {
                var stopped = _job == null || _job.TerminateAndWait();
                _job?.Dispose();
                if (stopped)
                {
                    _lease.Dispose();
                    Volatile.Write(ref _cleanupConfirmed, 1);
                }
                else _report?.Invoke("CleanupFailed", null); // Retain ownership until application exit.
                _ready.TrySetException(new InvalidOperationException("CUA_HOST_STOPPED"));
                lock (_sync)
                {
                    foreach (var waiter in _pending.Values) waiter.TrySetException(new InvalidOperationException("CUA_HOST_STOPPED"));
                    _pending.Clear();
                }
                _process.Dispose();
            }
        }
    }
}
