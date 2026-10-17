using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Processes;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal sealed class WslAuthenticationHost
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
            "uname -m\n" +
            "sha256sum \"$host\" | cut -c1-64\n" +
            "cat \"$HOME/.local/share/MyCodex/auth-core/bin/mycodex-auth-host.manifest.json\"";

        private readonly WslAppServerConfigSnapshot _config;
        private readonly IWslCommandExecutor _executor;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private bool _started;
        private string _executable;
        private string _codexHome;
        private string _dataDirectory;
        private AuthenticationHostArtifact _artifact;
        private long _nextId;

        internal WslAuthenticationHost(WslAppServerConfigSnapshot config, IWslCommandExecutor executor = null)
        {
            _config = config ?? new WslAppServerConfigSnapshot();
            _executor = executor ?? new WslCommandExecutor();
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
                var release = await AuthenticationHostDeployment.ProbeWslAsync(_config, _executor, cancellationToken).ConfigureAwait(false);
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
                    await AuthenticationHostDeployment.InstallWslAsync(_config, release, true, maintenanceToken, verifiedBase64).ConfigureAwait(false);
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
                        await AuthenticationHostDeployment.FinalizeWslAsync(_config, _executor, release, true, maintenanceToken).ConfigureAwait(false);
                    }
                    _artifact = oldArtifact;
                    try { await StartAsync(maintenanceToken).ConfigureAwait(false); } catch { }
                    throw;
                }
                _started = true;
                await AuthenticationHostDeployment.FinalizeWslAsync(_config, _executor, release, false, maintenanceToken).ConfigureAwait(false);
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
            var paths = await _executor.ExecuteAsync(_config,
                ResolveScript, "mycodex-auth-paths", 20000,
                cancellationToken).ConfigureAwait(false);
            if (paths.ExitCode == 11)
            {
                var release = await AuthenticationHostDeployment.ProbeWslAsync(_config, _executor, cancellationToken).ConfigureAwait(false);
                await AuthenticationHostDeployment.InstallWslAsync(_config, release, false, cancellationToken).ConfigureAwait(false);
                paths = await _executor.ExecuteAsync(_config, ResolveScript, "mycodex-auth-paths", 20000,
                    cancellationToken).ConfigureAwait(false);
            }
            var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576 };
            if (paths.ExitCode != 0)
            {
                throw new AuthenticationCenterException(paths.ExitCode == 11 ? "wsl_backend_missing" :
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
                ManifestScript, "mycodex-auth-package", 20000,
                cancellationToken).ConfigureAwait(false);
            if (package.ExitCode != 0)
            {
                throw new AuthenticationCenterException("backend_manifest_missing");
            }
            var lines = (package.StandardOutput ?? string.Empty).Split(new[] { '\n' }, 3);
            if (lines.Length != 3) throw new AuthenticationCenterException("backend_manifest_invalid");
            var releaseArchive = AuthenticationHostRelease.ForTarget("Linux", lines[0].Trim());
            try { _artifact = AuthenticationHostArtifact.LoadTarget(lines[2], lines[1].Trim(), releaseArchive.Target); }
            catch (AuthenticationCenterException error) when (error.Code == "backend_incompatible")
            {
                _artifact = AuthenticationHostArtifact.LoadTargetForUpdate(lines[2], lines[1].Trim(), releaseArchive.Target);
                throw new AuthenticationCenterException("backend_update_available");
            }
            if (!AuthenticationHostRelease.IsInstalledRelease(lines[2], lines[1].Trim(), releaseArchive))
                throw new AuthenticationCenterException("backend_update_available");
            await PrepareDataDirectoryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task PrepareDataDirectoryAsync(CancellationToken cancellationToken)
        {
            var prepared = await _executor.ExecuteAsync(_config, "set -eu\numask 077\nmkdir -p -- \"$1\"",
                "mycodex-auth-prepare", 20000, cancellationToken, _dataDirectory).ConfigureAwait(false);
            if (prepared.ExitCode != 0) throw new AuthenticationCenterException("invalid_data_dir");
        }

        private static bool UnixPath(IDictionary<string, object> values, string name, out string path)
        {
            path = values.TryGetValue(name, out var value) ? value as string : null;
            return !string.IsNullOrEmpty(path) && path[0] == '/' &&
                path.IndexOfAny(new[] { '\0', '\r', '\n' }) < 0;
        }

        private ProcessStartInfo BuildStartInfo(string command)
        {
            return new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = WslCommandLine.BuildHeadlessExecArguments(_config.DistributionName, _executable,
                    command, "--data-dir", _dataDirectory, "--codex-home", _codexHome),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
        }

        private async Task StartAsync(CancellationToken cancellationToken)
        {
            using (var process = new Process { StartInfo = BuildStartInfo("serve") })
            {
                process.Start();
                process.StandardInput.Close();
                var lineTask = process.StandardOutput.ReadLineAsync();
                var timeout = Task.Delay(30000, cancellationToken);
                if (await Task.WhenAny(lineTask, timeout).ConfigureAwait(false) != lineTask)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new AuthenticationCenterException("host_start_failed");
                }
                var line = await lineTask.ConfigureAwait(false);
                var serializer = new JavaScriptSerializer { MaxJsonLength = 1048576 };
                if (line == null)
                {
                    var code = LocalAuthenticationHost.ReadErrorCode(serializer,
                        await process.StandardError.ReadToEndAsync().ConfigureAwait(false));
                    if (code == "backend_already_running") return;
                    throw new AuthenticationCenterException(code);
                }
                var ready = serializer.DeserializeObject(line) as IDictionary<string, object>;
                if (ready == null || !ready.TryGetValue("event", out var name) || !Equals(name, "ready"))
                {
                    throw new AuthenticationCenterException("protocol_mismatch");
                }
                _artifact.ValidateIdentity(ready);
            }
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
            var result = await Task.Run(() => ProcessExecutionRunner.Run(new ProcessExecutionRequest
            {
                StartInfo = BuildStartInfo("rpc"),
                StandardInput = input,
                CancellationToken = cancellationToken,
                TimeoutMilliseconds = 100000,
                TimeoutMessage = "backend_unavailable"
            }), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw new AuthenticationCenterException(LocalAuthenticationHost.ReadErrorCode(serializer, result.StandardError));
            }
            return LocalAuthenticationHost.ReadResponse(serializer, result.StandardOutput, id);
        }
    }
}
