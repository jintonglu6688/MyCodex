using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal sealed class RemoteAuthenticationHost
    {
        private const string ResolveScript =
            "set -eu\n" +
            "[ -n \"${HOME:-}\" ] || exit 12\n" +
            "host=\"$HOME/.local/share/MyCodex/auth-core/bin/mycodex-auth-host\"\n" +
            "[ -x \"$host\" ] || exit 11\n" +
            "exec \"$host\" --paths-json";
        private const string ManifestScript =
            "set -eu\n" +
            "host=\"$HOME/.local/share/MyCodex/auth-core/bin/mycodex-auth-host\"\n" +
            "uname -s\n" +
            "uname -m\n" +
            "if command -v sha256sum >/dev/null 2>&1; then sha256sum \"$host\" | cut -c1-64; " +
            "else shasum -a 256 \"$host\" | cut -c1-64; fi\n" +
            "cat \"$HOME/.local/share/MyCodex/auth-core/bin/mycodex-auth-host.manifest.json\"";
        internal const string StartScript =
            "set -eu\n" +
            "ready_file=$(mktemp)\nerror_file=$(mktemp)\n" +
            "trap 'rm -f \"$ready_file\" \"$error_file\"' EXIT\n" +
            "nohup \"$1\" serve --data-dir \"$2\" --codex-home \"$3\" " +
            "</dev/null >\"$ready_file\" 2>\"$error_file\" &\n" +
            "pid=$!\ni=0\n" +
            "while [ \"$i\" -lt 120 ]; do\n" +
            "  if IFS= read -r ready < \"$ready_file\"; then printf '%s\\n' \"$ready\"; exit 0; fi\n" +
            "  if ! kill -0 \"$pid\" 2>/dev/null; then\n" +
            "    if grep -q 'backend_already_running' \"$error_file\"; then " +
            "printf '{\"event\":\"already_running\"}\\n'; exit 0; fi\n" +
            "    cat \"$error_file\" >&2; exit 1\n" +
            "  fi\n" +
            "  i=$((i+1)); sleep 0.25\n" +
            "done\necho '{\"error\":\"host_start_failed\"}' >&2\nexit 1";
        private const string RpcScript =
            "set -eu\nexec \"$1\" rpc --data-dir \"$2\" --codex-home \"$3\"";

        private readonly RemoteMacSshConfigSnapshot _config;
        private readonly RemoteSshCommandExecutor _executor;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private bool _started;
        private string _executable;
        private string _codexHome;
        private string _dataDirectory;
        private AuthenticationHostArtifact _artifact;
        private long _nextId;

        internal RemoteAuthenticationHost(RemoteMacSshConfigSnapshot config, RemoteSshCommandExecutor executor = null)
        {
            _config = config ?? new RemoteMacSshConfigSnapshot();
            _executor = executor ?? new RemoteSshCommandExecutor();
        }

        internal async Task<IDictionary<string, object>> RequestAsync(string method,
            IDictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_started)
                {
                    await ResolveAsync(cancellationToken).ConfigureAwait(false);
                    await StartAsync(cancellationToken).ConfigureAwait(false);
                    var status = await SendAsync("status", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false);
                    _artifact.ValidateIdentity(status);
                    await AuthenticationHostRelease.CleanupCacheAsync((string)status["target"]).ConfigureAwait(false);
                    status["startupRecoveryReady"] = false;
                    status["manualRestartRequired"] = true;
                    _started = true;
                    if (method == "status") return status;
                }
                var response = await SendAsync(method, parameters, cancellationToken).ConfigureAwait(false);
                if (method == "status")
                {
                    _artifact.ValidateIdentity(response);
                    response["startupRecoveryReady"] = false;
                    response["manualRestartRequired"] = true;
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
                throw new AuthenticationCenterException("backend_unavailable");
            }
            finally
            {
                _gate.Release();
            }
        }

        internal async Task UpdateAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                try
                {
                    await ResolveAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (AuthenticationCenterException error) when (error.Code == "backend_update_available") { }
                await PrepareDataDirectoryAsync(cancellationToken).ConfigureAwait(false);
                var release = await AuthenticationHostDeployment.ProbeRemoteAsync(_config, _executor, cancellationToken).ConfigureAwait(false);
                var verifiedBase64 = await AuthenticationHostRelease.ReadVerifiedBase64Async(release, cancellationToken).ConfigureAwait(false);
                try
                {
                    _artifact.ValidateIdentity(await SendAsync("status", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false));
                }
                catch (AuthenticationCenterException error) when (error.Code == "backend_unavailable")
                {
                    await StartAsync(cancellationToken).ConfigureAwait(false);
                    _artifact.ValidateIdentity(await SendAsync("status", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false));
                }
                var result = await SendAsync("backend/shutdown", new Dictionary<string, object> { { "forUpdate", true } },
                    cancellationToken).ConfigureAwait(false);
                if (!result.TryGetValue("status", out var state) || !Equals(state, "shutting_down"))
                    throw new AuthenticationCenterException("backend_update_unsafe");
                // Once shutdown succeeds, finish or roll back even if the dialog closes.
                var maintenanceToken = CancellationToken.None;
                var oldArtifact = _artifact;
                var installed = false;
                try
                {
                    await WaitForExitAsync(maintenanceToken).ConfigureAwait(false);
                    await AuthenticationHostDeployment.InstallRemoteAsync(_config, _executor, release, true, maintenanceToken, verifiedBase64).ConfigureAwait(false);
                    installed = true;
                    _started = false;
                    await ResolveAsync(maintenanceToken).ConfigureAwait(false);
                    await StartAsync(maintenanceToken).ConfigureAwait(false);
                    _artifact.ValidateIdentity(await SendAsync("status", new Dictionary<string, object>(), maintenanceToken).ConfigureAwait(false));
                }
                catch
                {
                    _started = false;
                    if (installed)
                    {
                        await StopReplacementAsync(maintenanceToken).ConfigureAwait(false);
                        await AuthenticationHostDeployment.FinalizeRemoteAsync(_config, _executor, release, true, maintenanceToken).ConfigureAwait(false);
                    }
                    _artifact = oldArtifact;
                    try { await StartAsync(maintenanceToken).ConfigureAwait(false); } catch { }
                    throw;
                }
                _started = true;
                // The new backend already runs; the next install removes a rollback copy a failed commit leaves.
                try { await AuthenticationHostDeployment.FinalizeRemoteAsync(_config, _executor, release, false, maintenanceToken).ConfigureAwait(false); }
                catch { }
                await AuthenticationHostRelease.CleanupCacheAsync(release.Target).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
        }

        private async Task StopReplacementAsync(CancellationToken cancellationToken)
        {
            try
            {
                await SendAsync("status", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false);
                await SendAsync("backend/shutdown", new Dictionary<string, object> { { "forUpdate", true } },
                    cancellationToken).ConfigureAwait(false);
                await WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (AuthenticationCenterException error) when (error.Code == "backend_unavailable") { }
        }

        private async Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                try { await SendAsync("status", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false); }
                catch (AuthenticationCenterException error) when (error.Code == "backend_unavailable") { return; }
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            throw new AuthenticationCenterException("backend_update_unsafe");
        }

        private async Task ResolveAsync(CancellationToken cancellationToken)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576 };
            var paths = await _executor.ExecuteAsync(_config,
                ResolveScript,
                "mycodex-auth-paths", 30000, cancellationToken).ConfigureAwait(false);
            if (paths.ExitCode == 11)
            {
                var release = await AuthenticationHostDeployment.ProbeRemoteAsync(_config, _executor, cancellationToken).ConfigureAwait(false);
                await AuthenticationHostDeployment.InstallRemoteAsync(_config, _executor, release, false, cancellationToken).ConfigureAwait(false);
                paths = await _executor.ExecuteAsync(_config, ResolveScript,
                    "mycodex-auth-paths", 30000, cancellationToken).ConfigureAwait(false);
            }
            if (paths.ExitCode != 0)
            {
                throw new AuthenticationCenterException(paths.ExitCode == 11 ? "remote_backend_missing" :
                    LocalAuthenticationHost.ReadErrorCode(serializer, paths.StandardError));
            }
            var values = serializer.DeserializeObject(paths.StandardOutput) as IDictionary<string, object>;
            if (values == null || !UnixPath(values, "codexHome", out _codexHome) ||
                !UnixPath(values, "dataDir", out _dataDirectory) ||
                !UnixPath(values, "executablePath", out _executable))
            {
                throw new AuthenticationCenterException("invalid_response");
            }
            var package = await _executor.ExecuteAsync(_config,
                ManifestScript,
                "mycodex-auth-package", 30000, cancellationToken).ConfigureAwait(false);
            if (package.ExitCode != 0)
            {
                throw new AuthenticationCenterException("backend_manifest_missing");
            }
            var lines = (package.StandardOutput ?? string.Empty).Split(new[] { '\n' }, 4);
            if (lines.Length != 4) throw new AuthenticationCenterException("backend_manifest_invalid");
            var expectedTarget = TargetTriple(lines[0].Trim(), lines[1].Trim());
            try { _artifact = AuthenticationHostArtifact.LoadTarget(lines[3], lines[2].Trim(), expectedTarget); }
            catch (AuthenticationCenterException error) when (error.Code == "backend_incompatible")
            {
                _artifact = AuthenticationHostArtifact.LoadTargetForUpdate(lines[3], lines[2].Trim(), expectedTarget);
                throw new AuthenticationCenterException("backend_update_available");
            }
            var releaseArchive = AuthenticationHostRelease.ForTarget(lines[0].Trim(), lines[1].Trim());
            if (!AuthenticationHostRelease.IsInstalledRelease(lines[3], lines[2].Trim(), releaseArchive))
                throw new AuthenticationCenterException("backend_update_available");
            await PrepareDataDirectoryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task PrepareDataDirectoryAsync(CancellationToken cancellationToken)
        {
            var prepared = await _executor.ExecuteAsync(_config, "set -eu\numask 077\nmkdir -p -- \"$1\"",
                "mycodex-auth-prepare", 30000, cancellationToken, _dataDirectory).ConfigureAwait(false);
            if (prepared.ExitCode != 0) throw new AuthenticationCenterException("invalid_data_dir");
        }

        internal static string TargetTriple(string system, string architecture)
        {
            if (system == "Linux" && architecture == "x86_64") return "x86_64-unknown-linux-gnu";
            if (system == "Linux" && (architecture == "aarch64" || architecture == "arm64")) return "aarch64-unknown-linux-gnu";
            if (system == "Darwin" && architecture == "x86_64") return "x86_64-apple-darwin";
            if (system == "Darwin" && (architecture == "aarch64" || architecture == "arm64")) return "aarch64-apple-darwin";
            throw new AuthenticationCenterException("unsupported_platform");
        }

        private static bool UnixPath(IDictionary<string, object> values, string name, out string path)
        {
            path = values.TryGetValue(name, out var value) ? value as string : null;
            return !string.IsNullOrEmpty(path) && path[0] == '/' &&
                path.IndexOfAny(new[] { '\0', '\r', '\n' }) < 0;
        }

        private async Task StartAsync(CancellationToken cancellationToken)
        {
            var result = await _executor.ExecuteAsync(_config, StartScript,
                "mycodex-auth-start", 45000, cancellationToken,
                _executable, _dataDirectory, _codexHome).ConfigureAwait(false);
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576 };
            if (result.ExitCode != 0)
            {
                throw new AuthenticationCenterException(LocalAuthenticationHost.ReadErrorCode(serializer, result.StandardError));
            }
            var ready = serializer.DeserializeObject(result.StandardOutput) as IDictionary<string, object>;
            if (ready == null || !ready.TryGetValue("event", out var name))
            {
                throw new AuthenticationCenterException("protocol_mismatch");
            }
            if (Equals(name, "already_running")) return;
            if (!Equals(name, "ready")) throw new AuthenticationCenterException("protocol_mismatch");
            _artifact.ValidateIdentity(ready);
        }

        private async Task<IDictionary<string, object>> SendAsync(string method,
            IDictionary<string, object> parameters, CancellationToken cancellationToken)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576 };
            var id = Interlocked.Increment(ref _nextId);
            var input = serializer.Serialize(new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" }, { "id", id }, { "method", method }, { "params", parameters }
            });
            var timeout = 110000;
            var result = await _executor.ExecuteWithInputAsync(_config, RpcScript,
                "mycodex-auth-rpc", timeout, cancellationToken, input,
                _executable, _dataDirectory, _codexHome).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new AuthenticationCenterException(LocalAuthenticationHost.ReadErrorCode(serializer, result.StandardError));
            }
            return LocalAuthenticationHost.ReadResponse(serializer, result.StandardOutput, id);
        }
    }
}
