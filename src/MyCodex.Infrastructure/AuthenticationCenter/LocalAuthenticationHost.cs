using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Processes;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal sealed class LocalAuthenticationHost
    {
        private readonly string _runtimeSource;
        private readonly string _explicitExecutable;
        private readonly string _explicitHome;
        private readonly string _explicitStore;
        private readonly bool _manageStartup;
        private bool _preflightNeedsRoute;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private bool _started;
        private volatile bool _stopping;
        private bool _stopped;
        private string _executable;
        private string _codexHome;
        private string _dataDirectory;
        private AuthenticationHostArtifact _artifact;
        private AuthenticationHostUsage _usage;
        private readonly HashSet<string> _ownedLoginIds = new HashSet<string>(StringComparer.Ordinal);
        private long _nextId;
        internal string CodexHome => _codexHome;

        internal LocalAuthenticationHost(string runtimeSource, string executable = null, string codexHome = null, string dataDirectory = null,
            bool manageStartup = false)
        {
            _runtimeSource = runtimeSource;
            _explicitExecutable = executable;
            _explicitHome = codexHome;
            _explicitStore = dataDirectory;
            _manageStartup = manageStartup;
        }

        internal async Task<IDictionary<string, object>> RequestAsync(string method, IDictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_stopping) throw new OperationCanceledException();
                if (!_started)
                {
                    await Task.Run(() => ResolvePaths(), cancellationToken).ConfigureAwait(false);
                    using (await _usage.EnterAsync(cancellationToken).ConfigureAwait(false))
                    {
                        await StartAsync(cancellationToken).ConfigureAwait(false);
                        var status = await SendAsync("status", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false);
                        _artifact.ValidateIdentity(status);
                        _usage.Register();
                        UpdateStartup(status);
                        _started = true;
                        if (method == "status") return status;
                    }
                }
                if (_manageStartup && RequiresStartupRegistration(method, parameters, _preflightNeedsRoute))
                    AuthenticationHostStartup.Configure(_executable, _codexHome, _dataDirectory, true);
                if (method == "gui/account/login/start" && parameters.TryGetValue("loginId", out var startedId) && startedId is string loginId)
                    _ownedLoginIds.Add(loginId);
                var response = await SendAsync(method, parameters, cancellationToken).ConfigureAwait(false);
                if (method == "gui/provider/preflight")
                    _preflightNeedsRoute = response.TryGetValue("routeRequired", out var required) && Equals(required, true);
                if (method == "status")
                {
                    _artifact.ValidateIdentity(response);
                    UpdateStartup(response);
                }
                return response;
            }
            catch (AuthenticationCenterException)
            {
                _started = false;
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                _started = false;
                // Process and JSON exceptions can contain arguments or payloads.
                throw new AuthenticationCenterException("backend_unavailable");
            }
            finally
            {
                _gate.Release();
            }
        }

        internal async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _stopping = true;
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Never resolve or start a backend just to stop it at application exit.
                if (_stopped || _artifact == null || _usage == null) return;
                using (await _usage.EnterAsync(cancellationToken).ConfigureAwait(false))
                {
                    var lastUser = false;
                    try
                    {
                        var status = await SendAsync("status", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false);
                        _artifact.ValidateIdentity(status);
                        await CancelOwnedLoginsAsync(SendAsync, _ownedLoginIds, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        lastUser = _usage.ReleaseAndIsLastUser();
                    }
                    _stopped = true;
                    if (!lastUser) return;
                    if (await StopBackendAsync(SendAsync, cancellationToken).ConfigureAwait(false) && _manageStartup)
                        AuthenticationHostStartup.Configure(_executable, _codexHome, _dataDirectory, false);
                }
            }
            catch (AuthenticationCenterException error) when (error.Code == "backend_unavailable")
            {
                _stopped = true;
            }
            finally
            {
                _started = false;
                _gate.Release();
            }
        }

        internal static async Task CancelOwnedLoginsAsync(
            Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> request,
            IEnumerable<string> ownedLoginIds, CancellationToken token)
        {
            foreach (var id in ownedLoginIds)
            {
                try
                {
                    await request("gui/account/login/cancel", new Dictionary<string, object> { { "loginId", id } }, token).ConfigureAwait(false);
                }
                catch (AuthenticationCenterException error) when (error.Code == "login_not_found") { }
            }
        }

        internal static async Task<bool> StopBackendAsync(
            Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> request, CancellationToken token)
        {
            while (true)
            {
                try
                {
                    // Keep the mode for the next application start. While the backend is down, Codex
                    // uses the provider in use directly; one that needs conversion keeps its route.
                    var result = await request("backend/shutdown",
                        new Dictionary<string, object> { { "forUpdate", true }, { "detach", true } }, token).ConfigureAwait(false);
                    if (!result.TryGetValue("status", out var value) || !Equals(value, "shutting_down"))
                        throw new AuthenticationCenterException("invalid_response");
                    return true;
                }
                catch (AuthenticationCenterException error) when (error.Code == "login_in_progress")
                {
                    // A login belonging to another client is not ours to cancel.
                    return false;
                }
                catch (AuthenticationCenterException error) when (error.Code == "route_requests_active") { }
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }

        private void UpdateStartup(IDictionary<string, object> status)
        {
            status["startupRecoveryReady"] = false;
            status["manualRestartRequired"] = !_manageStartup;
            if (_manageStartup)
            {
                try
                {
                    AuthenticationHostStartup.Configure(_executable, _codexHome, _dataDirectory,
                        AuthenticationHostStartup.RouteRequired(status));
                    status["startupRecoveryReady"] = true;
                    status["manualRestartRequired"] = false;
                }
                catch (AuthenticationCenterException)
                {
                    // Keep repair/switch-to-direct available when startup permissions change.
                    status["startupRecoveryReady"] = false;
                    status["manualRestartRequired"] = false;
                }
            }
        }

        internal static bool RequiresStartupRegistration(string method, IDictionary<string, object> parameters, bool routeRequired)
        {
            if (method == "gui/provider/apply") return routeRequired;
            return method == "gui/provider/save" && parameters.TryGetValue("kind", out var kind) &&
                (Equals(kind, "chat_completions") || Equals(kind, "anthropic"));
        }

        private async Task<IDictionary<string, object>> SendAsync(string method, IDictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576 };
            var id = Interlocked.Increment(ref _nextId);
            var input = serializer.Serialize(new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" }, { "id", id }, { "method", method }, { "params", parameters }
            });
            var result = await Task.Run(() => ProcessExecutionRunner.Run(new ProcessExecutionRequest
            {
                StartInfo = BuildStartInfo("rpc"), StandardInput = input,
                CancellationToken = cancellationToken,
                TimeoutMilliseconds = 90000,
                TimeoutMessage = "backend_unavailable"
            }), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                _started = false;
                throw new AuthenticationCenterException(ReadErrorCode(serializer, result.StandardError));
            }
            return ReadResponse(serializer, result.StandardOutput, id);
        }

        internal static IDictionary<string, object> ReadResponse(JavaScriptSerializer serializer, string output, long id)
        {
            var envelope = serializer.DeserializeObject(output) as IDictionary<string, object>;
            if (envelope == null || !envelope.TryGetValue("jsonrpc", out var version) || !Equals(version, "2.0") ||
                !envelope.TryGetValue("id", out var responseId) || Convert.ToInt64(responseId) != id)
            {
                throw new AuthenticationCenterException("invalid_response");
            }
            if (envelope.TryGetValue("error", out var error) && error is IDictionary<string, object> errorObject)
            {
                throw new AuthenticationCenterException(SafeCode(errorObject.TryGetValue("message", out var message) ? message as string : null));
            }
            if (!envelope.TryGetValue("result", out var value) || !(value is IDictionary<string, object> response))
            {
                throw new AuthenticationCenterException("invalid_response");
            }
            return response;
        }

        private void ResolvePaths()
        {
            if (string.Equals(_runtimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase) || CodexRuntimeSources.IsRemoteUnixSsh(_runtimeSource))
            {
                throw new AuthenticationCenterException("unsupported_platform");
            }
            _executable = _explicitExecutable ?? Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_PATH");
            if (string.IsNullOrEmpty(_executable))
            {
                _executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime",
                    "auth-core", "mycodex-auth-host.exe");
            }
            _artifact = AuthenticationHostArtifact.Load(_executable);
            _codexHome = _explicitHome ?? Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(_codexHome))
            {
                _codexHome = LocalSessionCatalog.GetDefaultLocalCodexHomePath();
            }
            if (!Path.IsPathRooted(_codexHome) || !Directory.Exists(_codexHome))
            {
                throw new AuthenticationCenterException("invalid_codex_home");
            }
            _codexHome = Path.GetFullPath(_codexHome);
            string targetHash;
            using (var sha = SHA256.Create())
            {
                targetHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(_codexHome.TrimEnd('\\').ToUpperInvariant()))).Replace("-", string.Empty);
            }
            _dataDirectory = _explicitStore ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyCodex", "auth-core", targetHash);
            if (!Path.IsPathRooted(_dataDirectory))
            {
                throw new AuthenticationCenterException("invalid_data_dir");
            }
            _dataDirectory = Path.GetFullPath(_dataDirectory);
            Directory.CreateDirectory(_dataDirectory);
            if (_usage == null) _usage = new AuthenticationHostUsage(_dataDirectory);
        }

        private ProcessStartInfo BuildStartInfo(string command)
        {
            return AppServerProcessLifecycle.BuildStartInfo(_executable,
                command + " --data-dir " + WindowsCommandLineArgumentEscaper.Quote(_dataDirectory) +
                " --codex-home " + WindowsCommandLineArgumentEscaper.Quote(_codexHome), null, null, null);
        }

        private async Task StartAsync(CancellationToken cancellationToken)
        {
            using (var process = new Process { StartInfo = BuildStartInfo("serve") })
            {
                process.Start();
                // The host may already have restored a route before readiness is
                // observed. UI cancellation must not terminate that independent service.
                {
                    process.StandardInput.Close();
                    var lineTask = process.StandardOutput.ReadLineAsync();
                    var timeout = Task.Delay(15000, cancellationToken);
                    if (await Task.WhenAny(lineTask, timeout).ConfigureAwait(false) != lineTask)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new AuthenticationCenterException("host_start_failed");
                    }
                    var line = await lineTask.ConfigureAwait(false);
                    var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576 };
                    if (line == null)
                    {
                        var error = ReadErrorCode(serializer, await process.StandardError.ReadToEndAsync().ConfigureAwait(false));
                        if (error == "backend_already_running")
                        {
                            return;
                        }
                        throw new AuthenticationCenterException(error);
                    }
                    var ready = serializer.DeserializeObject(line) as IDictionary<string, object>;
                    if (ready == null || !ready.TryGetValue("event", out var name) || !Equals(name, "ready") ||
                        !ready.TryGetValue("protocolVersion", out var protocol) || Convert.ToInt32(protocol) != 3)
                    {
                        throw new AuthenticationCenterException("protocol_mismatch");
                    }
                    _artifact.ValidateIdentity(ready);
                }
                // Dispose releases our handles, not the independently running host.
            }
        }

        internal static string ReadErrorCode(JavaScriptSerializer serializer, string text)
        {
            // The upstream-core entry point emits a single sanitized code.
            // SSH launch helpers may wrap their own sanitized errors in JSON.
            var plainCode = SafeCode(text?.Trim());
            if (plainCode != "backend_unavailable") return plainCode;
            try
            {
                var value = serializer.DeserializeObject(text) as IDictionary<string, object>;
                return SafeCode(value != null && value.TryGetValue("error", out var error) ? error as string : null);
            }
            catch
            {
                return "backend_unavailable";
            }
        }

        private static string SafeCode(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length > 64)
            {
                return "backend_unavailable";
            }
            foreach (var value in code)
            {
                if ((value < 'a' || value > 'z') && value != '_')
                {
                    return "backend_unavailable";
                }
            }
            return code;
        }
    }
}
