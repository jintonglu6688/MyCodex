using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.ComputerUse;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    public partial class AppServerClient
    {
        private ComputerUseSessionManager _computerUse;
        private readonly Dictionary<string, string> _computerUseStatuses = new Dictionary<string, string>(StringComparer.Ordinal);

        private void InitializeComputerUse(AppServerLaunchInfo launch)
        {
            if (launch.HostPlatform != AppServerHostPlatform.Windows) return;
            System.Diagnostics.Process process;
            long epoch;
            lock (_syncRoot)
            {
                process = _process;
                epoch = _connectionEpoch;
            }
            var executable = ComputerUseRuntimeExecutableResolver.Resolve(launch, process);
            string codexHome;
            if (!launch.EnvironmentVariables.TryGetValue("CODEX_HOME", out codexHome) || string.IsNullOrWhiteSpace(codexHome))
                codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(codexHome)) codexHome = LocalSessionCatalog.GetDefaultLocalCodexHomePath();
            ComputerUseSessionManager manager;
            CancellationToken lifetime;
            lock (_syncRoot)
            {
                if (_connectionEpoch != epoch || !ReferenceEquals(process, _process)) return;
                _computerUseStatuses.Clear();
                lifetime = _lifetimeCts?.Token ?? CancellationToken.None;
                if (_isConnected)
                    _computerUse = new ComputerUseSessionManager(executable, ProtocolCapabilities.RuntimeVersion, codexHome,
                        (method, parameters, token) => SendComputerUseScopedRequestAsync(method, parameters, token, lifetime, epoch),
                        (code, thread) => ReportComputerUseStatus(code, thread, epoch, lifetime));
                manager = _computerUse;
            }
            if (manager != null) _ = manager.WarmupAsync(lifetime);
        }

        private async Task<IDictionary<string, object>> SendComputerUseScopedRequestAsync(string method,
            IDictionary<string, object> parameters, CancellationToken token, CancellationToken lifetime, long epoch)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime))
            {
                EnsureComputerUseConnection(epoch, linked.Token);
                return await SendRequestDirectAsync(method, parameters, linked.Token, DefaultRequestTimeoutMs, epoch).ConfigureAwait(false);
            }
        }

        private void EnsureComputerUseConnection(long epoch, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (GetConnectionEpoch() != epoch) throw new OperationCanceledException("CUA_CONNECTION_CHANGED");
        }

        private async Task<IDictionary<string, object>> SendComputerUseRequestAsync(string method,
            IDictionary<string, object> parameters, CancellationToken token, int timeoutMs)
        {
            if (method != "thread/start" && method != "thread/resume" && method != "thread/fork" &&
                method != "turn/start" && method != "turn/interrupt")
                return await SendRequestDirectAsync(method, parameters, token, timeoutMs).ConfigureAwait(false);
            ComputerUseSessionManager manager;
            long epoch;
            CancellationToken lifetime;
            lock (_syncRoot)
            {
                manager = _computerUse;
                epoch = _connectionEpoch;
                lifetime = _lifetimeCts?.Token ?? CancellationToken.None;
            }
            if (manager == null) return await SendRequestDirectAsync(method, parameters, token, timeoutMs).ConfigureAwait(false);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime))
            {
                token = linked.Token;
                var isThread = method == "thread/start" || method == "thread/resume" || method == "thread/fork";
                ComputerUseSessionBinding binding = null;
                try
                {
                    var preparationWatch = System.Diagnostics.Stopwatch.StartNew();
                    if (isThread) binding = await manager.PrepareAsync(parameters, token, method == "thread/resume").ConfigureAwait(false);
                    else if (method == "turn/start")
                        await manager.BeforeTurnAsync(AppServerJsonValueReader.GetString(parameters, "threadId"), AppServerJsonValueReader.GetString(parameters, "cwd"), token).ConfigureAwait(false);
                    else if (method == "turn/interrupt")
                        manager.End(AppServerJsonValueReader.GetString(parameters, "threadId"), AppServerJsonValueReader.GetString(parameters, "turnId"));
                    System.Diagnostics.Trace.TraceInformation("[SendTiming] Operation={0}; Phase=computer-use; ElapsedMs={1}",
                        method, preparationWatch.ElapsedMilliseconds);
                    EnsureComputerUseConnection(epoch, token);
                    var result = await SendRequestDirectAsync(method, parameters, token, timeoutMs, epoch).ConfigureAwait(false);
                    EnsureComputerUseConnection(epoch, token);
                    if (binding != null)
                        await manager.BindAsync(binding, AppServerJsonValueReader.GetString(AppServerJsonValueReader.GetDictionary(result, "thread"), "id"), token).ConfigureAwait(false);
                    return result;
                }
                catch { manager.Abandon(binding); throw; }
            }
        }

        private void HandleComputerUseNotification(string method, IDictionary<string, object> parameters)
        {
            ComputerUseSessionManager manager;
            lock (_syncRoot) manager = _computerUse;
            if (manager == null) return;
            var thread = AppServerJsonValueReader.GetString(parameters, "threadId");
            var turn = AppServerJsonValueReader.GetString(AppServerJsonValueReader.GetDictionary(parameters, "turn"), "id") ?? AppServerJsonValueReader.GetString(parameters, "turnId");
            if (string.IsNullOrWhiteSpace(thread)) return;
            if (method == "turn/started" && !string.IsNullOrWhiteSpace(turn)) manager.Begin(thread, turn);
            else if (method == "turn/completed" && !string.IsNullOrWhiteSpace(turn)) manager.End(thread, turn);
            else if (method == "thread/closed" || method == "thread/deleted" || method == "thread/archived") manager.Remove(thread);
        }

        private void ReportComputerUseStatus(string code, string thread, long epoch, CancellationToken lifetime)
        {
            // A preflight without a thread is not the foreground session's availability.
            if (string.IsNullOrWhiteSpace(thread) && code != "CleanupFailed")
            {
                System.Diagnostics.Trace.TraceInformation("[ComputerUse] Preparation status. Code={0}", code);
                return;
            }
            var key = thread ?? string.Empty;
            lock (_syncRoot)
            {
                if (!_isConnected || _connectionEpoch != epoch || lifetime.IsCancellationRequested) return;
                string previous;
                if (_computerUseStatuses.TryGetValue(key, out previous) && previous == code) return;
                _computerUseStatuses[key] = code;
            }
            if (code == "Registered") return;
            // Status is not evidence that a thread is loaded. Also never call UI subscribers
            // while holding the connection-state lock.
            var args = new AppServerEventArgs(code == "Ready" || code == "PluginDisabled" ? "status" : "protocol_warning",
                ComputerUseStatusText.Get(code), threadId: thread, reason: "computer_use/" + code);
            AppServerEventDiagnostics.TraceMappedEvent(args);
            EventReceived?.Invoke(this, args);
        }
    }

    internal static class ComputerUseStatusText
    {
        public static string Get(string code)
        {
            switch (code)
            {
                case "Ready": return SharedLocalization.Text("ComputerUse.Status.Ready", "Computer Use is ready for this local Windows session.");
                case "BrowserDisabled": return SharedLocalization.Text("BrowserUse.Status.Disabled", "Browser automation is disabled by the current plugin or feature settings.");
                case "BrowserServerConflict": return SharedLocalization.Text("BrowserUse.Status.ConfigConflict", "An existing browser tool configuration conflicts with MyCodex integration. Your configuration has been preserved.");
                case "BrowserUnavailable": return SharedLocalization.Text("BrowserUse.Status.Unavailable", "Browser automation could not prepare its component package or authorization policy. Other desktop tools remain available.");
                case "BrowserReconnectRequired": return SharedLocalization.Text("BrowserUse.Status.ReconnectRequired", "The browser authorization host restarted. Reconnect the runtime and resume this session to establish a new browser binding.");
                case "PluginDisabled": return SharedLocalization.Text("ComputerUse.Status.PluginDisabled", "Computer Use is unavailable: its plugin is disabled or could not be enabled.");
                case "PolicyDenied": return SharedLocalization.Text("ComputerUse.Status.PolicyDenied", "Computer Use is disabled by the current policy.");
                case "PolicyUnsupported": return SharedLocalization.Text("ComputerUse.Status.PolicyUnsupported", "Computer Use is unavailable: the managed policy cannot be safely passed to the local executor.");
                case "NodeReplDisabled":
                case "NodeReplConflict": return SharedLocalization.Text("ComputerUse.Status.ConfigConflict", "Computer Use is unavailable: the existing node_repl configuration is disabled or conflicts with the verified components.");
                case "MissingComponents":
                case "InvalidComponents":
                case "ConflictingComponents": return SharedLocalization.Text("ComputerUse.Status.ComponentsMissing", "Computer Use is unavailable: MyCodex components are missing, damaged, or conflicting. Prepare or reinstall the component package.");
                case "UnsupportedRuntimeVersion":
                case "UnsupportedComponentVersion":
                case "ComponentContractUnsupported": return SharedLocalization.Text("ComputerUse.Status.Incompatible", "Computer Use is unavailable: the installed runtime or components do not support the required interfaces. Update MyCodex or select a compatible runtime; ordinary chat remains available.");
                case "CleanupFailed": return SharedLocalization.Text("ComputerUse.Status.CleanupFailed", "Computer Use stopped, but executor shutdown could not be confirmed. Desktop access remains blocked until MyCodex exits.");
                case "DesktopBusy": return SharedLocalization.Text("ComputerUse.Status.DesktopBusy", "Computer Use is busy in another session or MyCodex instance. This request was not queued; retry after that operation finishes.");
                case "ReconnectRequired": return SharedLocalization.Text("ComputerUse.Status.ReconnectRequired", "Computer Use is unavailable in this already loaded session. Reconnect the runtime and resume the session to apply its desktop configuration.");
                case "ExecutionFailed": return SharedLocalization.Text("ComputerUse.Status.ExecutionFailed", "Computer Use stopped after an executor failure. No input was replayed; start a new turn to retry.");
                case "GeometryUnavailable": return SharedLocalization.Text("ComputerUse.Status.GeometryUnavailable", "Computer Use could not click because screenshot geometry is unavailable. The session remains active; capture a fresh screenshot before choosing another action.");
                case "TargetObscured": return SharedLocalization.Text("ComputerUse.Status.TargetObscured", "Computer Use did not click because another window covers the target. The session remains active; activate the target and capture a fresh screenshot before choosing another action.");
                case "ObservationStale": return SharedLocalization.Text("ComputerUse.Status.ObservationStale", "Computer Use detected user input after the previous observation. The session remains active; read the window again before choosing another action.");
                case "WindowObserverNotReady": return SharedLocalization.Text("ComputerUse.Status.WindowObserverNotReady", "The official Computer Use executor could not initialize its window-opened observer. The session remains active; inspect the current windows before attempting another action.");
                default: return SharedLocalization.Text("ComputerUse.Status.PreparationFailed", "Computer Use is unavailable: component or policy preparation failed. Ordinary chat remains available.");
            }
        }
    }
}
