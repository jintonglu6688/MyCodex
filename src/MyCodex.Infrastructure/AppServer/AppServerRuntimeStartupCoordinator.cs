using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Markets;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.Processes;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerRuntimeStartupCoordinator : IDisposable
    {
        private const int WslNetworkReadinessCacheMilliseconds = 15000;

        private readonly SemaphoreSlim _wslNetworkReadinessLock = new SemaphoreSlim(1, 1);
        private readonly IWslNetworkReadinessProbe _wslNetworkReadinessProbe;
        private readonly Func<AppServerHostPlatform> _getCurrentHostPlatform;

        private DateTime _lastWslNetworkReadyAtUtc = DateTime.MinValue;

        public AppServerRuntimeStartupCoordinator(
            IWslNetworkReadinessProbe wslNetworkReadinessProbe,
            Func<AppServerHostPlatform> getCurrentHostPlatform)
        {
            _wslNetworkReadinessProbe = wslNetworkReadinessProbe ?? throw new ArgumentNullException(nameof(wslNetworkReadinessProbe));
            _getCurrentHostPlatform = getCurrentHostPlatform ?? (() => AppServerHostPlatform.Windows);
        }

        public async Task EnsureWslNetworkReadyAsync(
            AppConfigSnapshot appConfig,
            AppServerLaunchInfo launchInfo,
            int timeoutMilliseconds,
            bool force,
            string operationName)
        {
            if (launchInfo != null && launchInfo.HostPlatform != AppServerHostPlatform.Wsl)
            {
                return;
            }

            if (launchInfo == null && GetCurrentHostPlatform() != AppServerHostPlatform.Wsl)
            {
                return;
            }

            await _wslNetworkReadinessLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!force && IsWslNetworkReadinessCacheValid())
                {
                    return;
                }

                var result = await _wslNetworkReadinessProbe.WaitForReadyAsync(
                    appConfig != null ? appConfig.WslAppServer : null,
                    timeoutMilliseconds).ConfigureAwait(false);

                if (result != null && result.IsReady)
                {
                    _lastWslNetworkReadyAtUtc = DateTime.UtcNow;
                    Trace.TraceInformation(
                        "WSL network readiness probe succeeded. Operation={0}; Host={1}; Attempts={2}; ElapsedMs={3}",
                        operationName,
                        result.Host,
                        result.Attempts,
                        (int)result.Elapsed.TotalMilliseconds);
                    return;
                }

                Trace.TraceWarning(
                    "WSL network readiness probe did not confirm DNS readiness; continuing. Operation={0}; Host={1}; Attempts={2}; ElapsedMs={3}; LastError={4}",
                    operationName,
                    result != null ? result.Host : string.Empty,
                    result != null ? result.Attempts : 0,
                    result != null ? (int)result.Elapsed.TotalMilliseconds : 0,
                    result != null ? AbbreviateForLog(result.LastError, 1000) : string.Empty);
            }
            finally
            {
                _wslNetworkReadinessLock.Release();
            }
        }

        public void InvalidateWslNetworkReadiness()
        {
            _lastWslNetworkReadyAtUtc = DateTime.MinValue;
        }

        public AppServerRuntimeProcessStartResult StartProcess(
            AppConfigSnapshot appConfig,
            AppServerLaunchInfo launchInfo,
            EventHandler exitedHandler)
        {
            return StartProcess(
                appConfig,
                launchInfo,
                exitedHandler,
                (info, environmentVariableNames) => AppServerProcessLifecycle.BuildStartInfo(
                    info.FileName,
                    info.Arguments,
                    Environment.CurrentDirectory,
                    info.EnvironmentVariables,
                    environmentVariableNames));
        }

        internal AppServerRuntimeProcessStartResult StartProcess(
            AppConfigSnapshot appConfig,
            AppServerLaunchInfo launchInfo,
            EventHandler exitedHandler,
            Func<AppServerLaunchInfo, ICollection<string>, ProcessStartInfo> buildStartInfo)
        {
            if (launchInfo == null)
            {
                throw new ArgumentNullException(nameof(launchInfo));
            }

            if (buildStartInfo == null)
            {
                throw new ArgumentNullException(nameof(buildStartInfo));
            }

            var resources = new AppServerProcessResources();
            try
            {
                var lifetimeCts = new CancellationTokenSource();
                resources.LifetimeCts = lifetimeCts;
                var pathMappingPolicy = AppServerPathMappingPolicy.ForHostPlatform(launchInfo.HostPlatform);
                var commandLinePolicy = AppServerCommandLinePolicy.ForHostPlatform(
                    launchInfo.HostPlatform,
                    appConfig == null ? null : appConfig.WindowsShellPreference,
                    GetUnixShellCommand(appConfig, launchInfo.HostPlatform));

                var environmentVariableNames = new List<string>();
                var startInfo = buildStartInfo(launchInfo, environmentVariableNames);
                if (launchInfo.HostPlatform == AppServerHostPlatform.Wsl)
                {
                    var existingWslEnv = startInfo.EnvironmentVariables["WSLENV"];
                    var updatedWslEnv = WslEnvironmentBridge.BuildWslEnvValue(existingWslEnv, environmentVariableNames);
                    if (!string.IsNullOrWhiteSpace(updatedWslEnv))
                    {
                        startInfo.EnvironmentVariables["WSLENV"] = updatedWslEnv;
                    }
                }

                var process = new Process
                {
                    StartInfo = startInfo,
                    EnableRaisingEvents = true
                };
                resources.Process = process;
                if (exitedHandler != null)
                {
                    process.Exited += exitedHandler;
                }

                if (!process.Start())
                {
                    throw new InvalidOperationException(L("AppServerClient.Error.UnableToStartAppServer", "Unable to start codex app-server."));
                }

                Trace.TraceInformation(
                    "Started Codex App Server process. HostPlatform={0}; FileName={1}; Arguments={2}; Environment={3}",
                    launchInfo.HostPlatform,
                    launchInfo.FileName,
                    BuildConnectionKeyArguments(launchInfo),
                    BuildSafeEnvironmentNameSummary(environmentVariableNames));

                var inputWriter = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };
                resources.InputWriter = inputWriter;

                return new AppServerRuntimeProcessStartResult
                {
                    Process = process,
                    InputWriter = inputWriter,
                    LifetimeCts = lifetimeCts,
                    PathMappingPolicy = pathMappingPolicy,
                    CommandLinePolicy = commandLinePolicy
                };
            }
            catch
            {
                DeleteSensitiveTempFiles(launchInfo);
                AppServerProcessLifecycle.CleanupResources(
                    resources,
                    process =>
                    {
                        if (exitedHandler != null)
                        {
                            process.Exited -= exitedHandler;
                        }
                    });
                throw;
            }
        }


        public static string BuildRuntimeConnectionKey(
            AppConfigSnapshot appConfig,
            AppServerLaunchInfo launchInfo)
        {
            if (launchInfo == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            builder.Append(launchInfo.HostPlatform);
            builder.Append('|');
            builder.Append(launchInfo.FileName ?? string.Empty);
            builder.Append('|');
            builder.Append(BuildConnectionKeyArguments(launchInfo));

            if (launchInfo.EnvironmentVariables != null)
            {
                foreach (var pair in launchInfo.EnvironmentVariables.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append('|');
                    builder.Append(pair.Key ?? string.Empty);
                    builder.Append('=');
                    builder.Append(BuildConnectionKeyEnvironmentValue(pair.Key, pair.Value));
                }
            }

            if (appConfig != null)
            {
                builder.Append("|runtime=");
                builder.Append(appConfig.CodexRuntimeSource ?? string.Empty);
                builder.Append("|custom=");
                builder.Append(appConfig.CodexCustomExecutablePath ?? string.Empty);
                builder.Append("|remote=");
                builder.Append(appConfig.SelectedRemoteUnixSshProfileId ?? string.Empty);
            }

            return builder.ToString();
        }

        public void Dispose()
        {
            _wslNetworkReadinessLock.Dispose();
        }

        public static void DeleteSensitiveTempFiles(AppServerLaunchInfo launchInfo)
        {
            if (launchInfo == null || launchInfo.SensitiveTempFilePaths == null)
            {
                return;
            }

            for (var i = 0; i < launchInfo.SensitiveTempFilePaths.Count; i++)
            {
                RemoteSshPasswordFile.TryDelete(launchInfo.SensitiveTempFilePaths[i]);
            }
        }

        private bool IsWslNetworkReadinessCacheValid()
        {
            return _lastWslNetworkReadyAtUtc != DateTime.MinValue &&
                (DateTime.UtcNow - _lastWslNetworkReadyAtUtc).TotalMilliseconds < WslNetworkReadinessCacheMilliseconds;
        }

        private AppServerHostPlatform GetCurrentHostPlatform()
        {
            return _getCurrentHostPlatform();
        }

        private static string BuildConnectionKeyEnvironmentValue(string key, string value)
        {
            if (IsSensitiveEnvironmentVariableName(key))
            {
                return "<sensitive-env-sha256:" + ComputeSha256Hex(value ?? string.Empty) + ">";
            }

            return value ?? string.Empty;
        }

        private static string ComputeSha256Hex(string value)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
                var hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        internal static string BuildConnectionKeyArguments(AppServerLaunchInfo launchInfo)
        {
            var arguments = launchInfo != null ? launchInfo.Arguments ?? string.Empty : string.Empty;
            if (launchInfo == null)
            {
                return arguments;
            }

            if (launchInfo.SensitiveTempFilePaths != null)
            {
                for (var i = 0; i < launchInfo.SensitiveTempFilePaths.Count; i++)
                {
                    var path = launchInfo.SensitiveTempFilePaths[i];
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        arguments = arguments.Replace(path, "<sensitive-temp-file>");
                    }
                }
            }

            return RedactSensitiveEnvironmentValues(arguments, launchInfo.EnvironmentVariables);
        }

        private static string RedactSensitiveEnvironmentValues(string text, IDictionary<string, string> environmentVariables)
        {
            var redacted = text ?? string.Empty;
            if (environmentVariables == null || redacted.Length == 0)
            {
                return redacted;
            }

            foreach (var pair in environmentVariables)
            {
                if (IsSensitiveEnvironmentVariableName(pair.Key) && !string.IsNullOrEmpty(pair.Value))
                {
                    redacted = redacted.Replace(pair.Value, "<sensitive-env-value>");
                    redacted = RedactSensitiveEnvironmentAssignment(redacted, pair.Key);
                }
            }

            return redacted;
        }

        private static string RedactSensitiveEnvironmentAssignment(string text, string variableName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(variableName))
            {
                return text ?? string.Empty;
            }

            var marker = variableName.Trim() + "=";
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            while (index >= 0)
            {
                var valueStart = index + marker.Length;
                var valueEnd = valueStart;
                while (valueEnd < text.Length && !char.IsWhiteSpace(text[valueEnd]))
                {
                    valueEnd++;
                }

                text = text.Substring(0, valueStart) +
                    "<sensitive-env-value>" +
                    text.Substring(valueEnd);
                index = text.IndexOf(marker, valueStart + "<sensitive-env-value>".Length, StringComparison.Ordinal);
            }

            return text;
        }

        private static bool IsSensitiveEnvironmentVariableName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) &&
                (name.IndexOf("KEY", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("PASS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("TOKEN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("SECRET", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string GetUnixShellCommand(AppConfigSnapshot appConfig, AppServerHostPlatform hostPlatform)
        {
            if (hostPlatform == AppServerHostPlatform.Wsl)
            {
                return appConfig != null && appConfig.WslAppServer != null
                    ? appConfig.WslAppServer.ShellCommand
                    : null;
            }

            if (hostPlatform == AppServerHostPlatform.RemoteMac)
            {
                var remoteConfig = appConfig != null ? appConfig.GetSelectedRemoteUnixSshConfig() : null;
                return remoteConfig != null
                    ? remoteConfig.ShellCommand
                    : null;
            }

            return null;
        }

        private static string BuildSafeEnvironmentNameSummary(IEnumerable<string> environmentVariableNames)
        {
            if (environmentVariableNames == null)
            {
                return string.Empty;
            }

            var names = environmentVariableNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Where(name => !IsSensitiveEnvironmentVariableName(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return names.Length == 0 ? "(none)" : string.Join(",", names);
        }

        private static string AbbreviateForLog(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0 || text.Length <= maxLength)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, maxLength) + "...";
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }
    }

    internal sealed class AppServerRuntimeProcessStartResult
    {
        public Process Process { get; set; }

        public StreamWriter InputWriter { get; set; }

        public CancellationTokenSource LifetimeCts { get; set; }

        public AppServerPathMappingPolicy PathMappingPolicy { get; set; }

        public AppServerCommandLinePolicy CommandLinePolicy { get; set; }
    }
}
