using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Codex
{
    public sealed class CodexRuntimeManager
    {
        private readonly AppConfigService _appConfigService;

        public CodexRuntimeManager(AppConfigService appConfigService)
        {
            _appConfigService = appConfigService ?? throw new ArgumentNullException(nameof(appConfigService));
        }

        public CodexRuntimeStatus GetStatus(AppConfigSnapshot config)
        {
            AppServerLaunchInfo ignored;
            return ResolveStatus(config, out ignored, false);
        }

        internal AppServerLaunchInfo ResolveLaunchInfo(AppConfigSnapshot config)
        {
            return ResolveLaunchInfo(config, null);
        }

        internal AppServerLaunchInfo ResolveLaunchInfo(
            AppConfigSnapshot config,
            IDictionary<string, string> appServerEnvironmentVariables)
        {
            config = config ?? new AppConfigSnapshot();
            // The settings UI needs every candidate; connecting only needs the selected runtime.
            // Keep resolution fresh so changed paths/environment still invalidate the connection key.
            AppServerLaunchInfo launchInfo;
            switch (CodexRuntimeSources.CanonicalizeForExecution(config.CodexRuntimeSource))
            {
                case CodexRuntimeSources.Custom:
                    launchInfo = TryCreateCustomLaunchInfo(config.CodexCustomExecutablePath, appServerEnvironmentVariables);
                    break;
                case CodexRuntimeSources.System:
                    launchInfo = TryCreateSystemLaunchInfo(appServerEnvironmentVariables);
                    break;
                case CodexRuntimeSources.Wsl:
                    launchInfo = TryCreateWslLaunchInfo(config.WslAppServer, appServerEnvironmentVariables);
                    break;
                case CodexRuntimeSources.RemoteUnixSsh:
                case CodexRuntimeSources.RemoteMacSsh:
                    launchInfo = TryCreateRemoteMacSshLaunchInfo(config.GetSelectedRemoteUnixSshConfig(), true, appServerEnvironmentVariables);
                    break;
                default:
                    string version;
                    launchInfo = TryCreateBundledLaunchInfo(GetCurrentTargetTriple(), CodexRuntimeSources.Bundled, appServerEnvironmentVariables, out version);
                    break;
            }
            return launchInfo ?? throw new CodexRuntimeUnavailableException(GetStatus(config));
        }

        public async Task<string> VerifyExecutableAsync(string executablePath, string additionalPathDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("CodexRuntime.Error.ExecutablePathRequired", "Executable path cannot be empty."),
                    nameof(executablePath));
            }

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    SharedLocalization.Text("CodexRuntime.Error.ExecutableNotFound", "Codex executable was not found."),
                    executablePath);
            }

            var verificationProcess = BuildLaunchInfo(executablePath, "--version", additionalPathDirectory);
            var startInfo = new ProcessStartInfo
            {
                FileName = verificationProcess.FileName,
                Arguments = verificationProcess.Arguments,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            ApplyEnvironment(startInfo, verificationProcess.EnvironmentVariables);

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    try
                    {
                        if (!process.Start())
                        {
                            throw new InvalidOperationException(
                                SharedLocalization.Text("CodexRuntime.Error.UnableToStartExecutable", "Unable to start Codex executable."));
                        }
                    }
                    catch (Win32Exception ex)
                    {
                        throw new InvalidOperationException(
                            SharedLocalization.Text("CodexRuntime.Error.UnableToStartExecutable", "Unable to start Codex executable."),
                            ex);
                    }

                    var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                    var standardErrorTask = process.StandardError.ReadToEndAsync();

                    await WaitForExitAsync(process, cancellationToken).ConfigureAwait(false);
                    var standardOutput = await standardOutputTask.ConfigureAwait(false);
                    var standardError = await standardErrorTask.ConfigureAwait(false);

                    if (process.ExitCode != 0)
                    {
                        var errorMessage = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                        throw new InvalidOperationException(
                            string.Format(
                                SharedLocalization.Text("CodexRuntime.Error.VerificationFailed.Template", "Codex verification failed: {0}"),
                                string.IsNullOrWhiteSpace(errorMessage)
                                    ? SharedLocalization.Text("Common.Value.UnknownError", "Unknown error")
                                    : errorMessage.Trim()));
                    }

                    var version = (standardOutput ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(version))
                    {
                        version = (standardError ?? string.Empty).Trim();
                    }

                    return version;
                }
            }
            finally
            {
                DeleteSensitiveTempFiles(verificationProcess);
            }
        }

        private static void DeleteSensitiveTempFiles(AppServerLaunchInfo launchInfo)
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

        public Task<string> VerifyCustomExecutableAsync(string executablePath, CancellationToken cancellationToken)
        {
            return VerifyExecutableAsync(executablePath, InferAdditionalPathDirectory(executablePath), cancellationToken);
        }

        public Task<string> VerifyWslCodexAsync(WslAppServerConfigSnapshot wslConfig, CancellationToken cancellationToken)
        {
            wslConfig = wslConfig ?? new WslAppServerConfigSnapshot();

            var wslExecutablePath = FindWslExecutablePath();
            if (string.IsNullOrWhiteSpace(wslExecutablePath))
            {
                throw new FileNotFoundException(
                    SharedLocalization.Text("CodexRuntime.Error.WslExecutableNotFound", "wsl.exe was not found."),
                    "wsl.exe");
            }

            var verificationProcess = BuildWslLaunchInfo(
                wslExecutablePath,
                WslCommandLine.BuildHeadlessShellExecArguments(
                    wslConfig.DistributionName,
                    wslConfig.ShellCommand,
                    wslConfig.CodexCommand,
                    "--version"),
                CodexRuntimeSources.Wsl,
                wslConfig);

            return VerifyProcessAsync(
                verificationProcess,
                Environment.CurrentDirectory,
                cancellationToken);
        }

        public Task<string> VerifyRemoteMacCodexAsync(RemoteMacSshConfigSnapshot remoteConfig, CancellationToken cancellationToken)
        {
            return VerifyRemoteUnixCodexAsync(remoteConfig, cancellationToken);
        }

        public Task<string> VerifyRemoteUnixCodexAsync(RemoteMacSshConfigSnapshot remoteConfig, CancellationToken cancellationToken)
        {
            remoteConfig = remoteConfig ?? new RemoteMacSshConfigSnapshot();
            var verificationProcess = BuildRemoteMacSshLaunchInfo(
                remoteConfig,
                RemoteSshCommandLine.BuildShellCommand(
                    remoteConfig,
                    RemoteSshCommandLine.NormalizeCodexCommand(remoteConfig.CodexCommand),
                    "--version"),
                CodexRuntimeSources.RemoteUnixSsh);

            return VerifyProcessAsync(
                verificationProcess,
                Environment.CurrentDirectory,
                cancellationToken);
        }

        private CodexRuntimeStatus ResolveStatus(
            AppConfigSnapshot config,
            out AppServerLaunchInfo launchInfo,
            bool materializeSensitiveFiles,
            IDictionary<string, string> appServerEnvironmentVariables = null)
        {
            config = config ?? new AppConfigSnapshot();

            var targetTriple = GetCurrentTargetTriple();
            var normalizedSource = CodexRuntimeSources.CanonicalizeForExecution(config.CodexRuntimeSource);
            var customLaunchInfo = TryCreateCustomLaunchInfo(
                config.CodexCustomExecutablePath,
                appServerEnvironmentVariables);
            string bundledRuntimeVersion;
            var bundledLaunchInfo = TryCreateBundledLaunchInfo(
                targetTriple,
                CodexRuntimeSources.Bundled,
                appServerEnvironmentVariables,
                out bundledRuntimeVersion);
            var systemLaunchInfo = TryCreateSystemLaunchInfo(appServerEnvironmentVariables);
            var wslLaunchInfo = TryCreateWslLaunchInfo(
                config.WslAppServer,
                appServerEnvironmentVariables);
            var remoteConfig = config.GetSelectedRemoteUnixSshConfig();
            var remoteMacLaunchInfo = TryCreateRemoteMacSshLaunchInfo(
                remoteConfig,
                false,
                appServerEnvironmentVariables);

            launchInfo = SelectLaunchInfo(
                normalizedSource,
                customLaunchInfo,
                bundledLaunchInfo,
                systemLaunchInfo,
                wslLaunchInfo,
                remoteMacLaunchInfo);
            var effectiveSource = GetLaunchInfoSource(launchInfo);
            if (materializeSensitiveFiles &&
                string.Equals(effectiveSource, CodexRuntimeSources.RemoteUnixSsh, StringComparison.OrdinalIgnoreCase))
            {
                launchInfo = TryCreateRemoteMacSshLaunchInfo(
                    remoteConfig,
                    true,
                    appServerEnvironmentVariables);
                effectiveSource = GetLaunchInfoSource(launchInfo);
            }

            var status = new CodexRuntimeStatus
            {
                PreferredSource = normalizedSource,
                EffectiveSource = effectiveSource,
                CanLaunch = launchInfo != null,
                Message = BuildStatusMessage(normalizedSource, launchInfo, bundledRuntimeVersion),
                SystemExecutablePath = systemLaunchInfo != null ? ExtractExecutablePath(systemLaunchInfo) : null,
                BundledExecutablePath = bundledLaunchInfo != null ? ExtractExecutablePath(bundledLaunchInfo) : null,
                CustomExecutablePath = customLaunchInfo != null ? config.CodexCustomExecutablePath : null,
                WslExecutablePath = wslLaunchInfo != null ? ExtractExecutablePath(wslLaunchInfo) : null,
                WslDistributionName = config.WslAppServer.DistributionName,
                WslCodexCommand = config.WslAppServer.CodexCommand,
                RemoteMacHostName = remoteConfig.HostName,
                RemoteMacUserName = remoteConfig.UserName,
                RemoteMacCodexCommand = remoteConfig.CodexCommand,
                EffectiveExecutablePath = launchInfo != null ? ExtractExecutablePath(launchInfo) : null,
                EffectiveAdditionalPathDirectory = launchInfo != null ? ExtractAdditionalPathDirectory(launchInfo) : null,
                Version = string.Equals(effectiveSource, CodexRuntimeSources.Bundled, StringComparison.OrdinalIgnoreCase)
                        ? bundledRuntimeVersion
                        : null,
                EffectiveHostPlatform = launchInfo != null ? launchInfo.HostPlatform : AppServerHostPlatform.Windows
            };

            return status;
        }

        private AppServerLaunchInfo SelectLaunchInfo(
            string preferredSource,
            AppServerLaunchInfo customLaunchInfo,
            AppServerLaunchInfo bundledLaunchInfo,
            AppServerLaunchInfo systemLaunchInfo,
            AppServerLaunchInfo wslLaunchInfo,
            AppServerLaunchInfo remoteMacLaunchInfo)
        {
            switch (preferredSource)
            {
                case CodexRuntimeSources.Wsl:
                    return wslLaunchInfo;
                case CodexRuntimeSources.RemoteUnixSsh:
                case CodexRuntimeSources.RemoteMacSsh:
                    return remoteMacLaunchInfo;
                case CodexRuntimeSources.Custom:
                    return customLaunchInfo;
                case CodexRuntimeSources.Bundled:
                    return bundledLaunchInfo;
                case CodexRuntimeSources.System:
                    return systemLaunchInfo;
                default:
                    return bundledLaunchInfo;
            }
        }

        private string BuildStatusMessage(
            string preferredSource,
            AppServerLaunchInfo effectiveLaunchInfo,
            string bundledRuntimeVersion)
        {
            if (effectiveLaunchInfo == null)
            {
                return BuildUnavailableStatusMessage(preferredSource);
            }

            var effectiveSource = GetLaunchInfoSource(effectiveLaunchInfo);
            switch (effectiveSource)
            {
                case CodexRuntimeSources.Custom:
                    return SharedLocalization.Text("CodexRuntime.Status.UsingCustomExecutable", "Your selected Codex executable will be used.");
                case CodexRuntimeSources.Bundled:
                    return string.Format(
                        SharedLocalization.Text("CodexRuntime.Status.UsingBundledRuntime.Template", "Bundled Codex runtime ({0}) will be used."),
                        string.IsNullOrWhiteSpace(bundledRuntimeVersion) ? CodexRuntimeSources.BundledDefaultVersion : bundledRuntimeVersion);
                case CodexRuntimeSources.System:
                    return SharedLocalization.Text("CodexRuntime.Status.UsingSystemCodex", "System-installed Codex will be used.");
                case CodexRuntimeSources.Wsl:
                    return SharedLocalization.Text("CodexRuntime.Status.UsingWslCodex", "WSL Codex runtime will be used.");
                case CodexRuntimeSources.RemoteUnixSsh:
                case CodexRuntimeSources.RemoteMacSsh:
                    return SharedLocalization.Text("CodexRuntime.Status.UsingRemoteUnixSsh", "Remote Unix SSH Codex runtime will be used.");
                default:
                    return SharedLocalization.Text("CodexRuntime.Status.AvailableRuntimeDetected", "A usable Codex runtime has been detected.");
            }
        }

        private static string BuildUnavailableStatusMessage(string preferredSource)
        {
            switch (CodexRuntimeSources.CanonicalizeForExecution(preferredSource))
            {
                case CodexRuntimeSources.Bundled:
                    return SharedLocalization.Text(
                        "CodexRuntime.Status.BundledRuntimeMissing",
                        "Bundled Codex runtime is missing. Reinstall MyCodex, or choose System, Custom, or WSL in App Server Runtime settings.");
                case CodexRuntimeSources.System:
                    return SharedLocalization.Text(
                        "CodexRuntime.Status.SystemRuntimeMissing",
                        "System Codex was not found. Choose Bundled, Custom, or WSL in App Server Runtime settings.");
                case CodexRuntimeSources.Custom:
                    return SharedLocalization.Text(
                        "CodexRuntime.Status.CustomRuntimeMissing",
                        "Selected Codex executable is unavailable. Choose another executable, or switch runtime source in App Server Runtime settings.");
                case CodexRuntimeSources.Wsl:
                    return SharedLocalization.Text(
                        "CodexRuntime.Status.WslRuntimeMissing",
                        "WSL Codex runtime is unavailable. Check WSL settings, or switch runtime source in App Server Runtime settings.");
                case CodexRuntimeSources.RemoteUnixSsh:
                case CodexRuntimeSources.RemoteMacSsh:
                    return SharedLocalization.Text(
                        "CodexRuntime.Status.RemoteUnixSshRuntimeMissing",
                        "Remote Unix SSH runtime is unavailable. Check SSH settings, or switch runtime source in App Server Runtime settings.");
                default:
                    return SharedLocalization.Text(
                        "CodexRuntime.Status.NoAvailableRuntime",
                        "No available Codex runtime was detected. Choose a runtime source in App Server Runtime settings.");
            }
        }

        private AppServerLaunchInfo TryCreateCustomLaunchInfo(
            string customExecutablePath,
            IDictionary<string, string> appServerEnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(customExecutablePath))
            {
                return null;
            }

            var normalizedPath = customExecutablePath.Trim();
            if (!File.Exists(normalizedPath))
            {
                return null;
            }

            var additionalPathDirectory = InferAdditionalPathDirectory(normalizedPath);
            return BuildAppServerLaunchInfo(
                normalizedPath,
                additionalPathDirectory,
                CodexRuntimeSources.Custom,
                appServerEnvironmentVariables);
        }

        private AppServerLaunchInfo TryCreateBundledLaunchInfo(
            string targetTriple,
            string source,
            IDictionary<string, string> appServerEnvironmentVariables,
            out string version)
        {
            version = _appConfigService.GetLatestInstalledBundledCodexVersion(targetTriple);
            if (string.IsNullOrWhiteSpace(version))
            {
                version = CodexRuntimeSources.BundledDefaultVersion;
            }

            var executablePath = _appConfigService.GetBundledCodexExecutablePath(version, targetTriple);
            if (!File.Exists(executablePath))
            {
                version = null;
                return null;
            }

            var additionalPathDirectory = _appConfigService.GetBundledCodexPathDirectory(version, targetTriple);
            var launchInfo = BuildAppServerLaunchInfo(
                executablePath,
                additionalPathDirectory,
                CodexRuntimeSources.CanonicalizeForExecution(source),
                appServerEnvironmentVariables);
            launchInfo.EnvironmentVariables["MYCODEX_CODEX_VERSION"] = version ?? string.Empty;
            return launchInfo;
        }

        private AppServerLaunchInfo TryCreateSystemLaunchInfo(
            IDictionary<string, string> appServerEnvironmentVariables)
        {
            var cmdPath = FindExecutablePath("codex.cmd");
            if (!string.IsNullOrWhiteSpace(cmdPath))
            {
                return BuildAppServerLaunchInfo(
                    cmdPath,
                    null,
                    CodexRuntimeSources.System,
                    appServerEnvironmentVariables);
            }

            var exePath = FindExecutablePath("codex.exe");
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                return BuildAppServerLaunchInfo(
                    exePath,
                    null,
                    CodexRuntimeSources.System,
                    appServerEnvironmentVariables);
            }

            var barePath = FindExecutablePath("codex");
            if (!string.IsNullOrWhiteSpace(barePath))
            {
                return BuildAppServerLaunchInfo(
                    barePath,
                    null,
                    CodexRuntimeSources.System,
                    appServerEnvironmentVariables);
            }

            return null;
        }

        private AppServerLaunchInfo TryCreateWslLaunchInfo(
            WslAppServerConfigSnapshot wslConfig,
            IDictionary<string, string> appServerEnvironmentVariables)
        {
            var wslExecutablePath = FindWslExecutablePath();
            if (string.IsNullOrWhiteSpace(wslExecutablePath))
            {
                return null;
            }

            wslConfig = wslConfig ?? new WslAppServerConfigSnapshot();
            try
            {
                return BuildWslLaunchInfo(
                    wslExecutablePath,
                    WslCommandLine.BuildHeadlessShellExecArguments(
                        wslConfig.DistributionName,
                        wslConfig.ShellCommand,
                        wslConfig.CodexCommand,
                        BuildAppServerCommandParts()),
                    CodexRuntimeSources.Wsl,
                    wslConfig,
                    appServerEnvironmentVariables);
            }
            catch (InvalidOperationException ex)
            {
                Trace.TraceWarning("WSL Codex runtime configuration is unavailable: {0}", ex.Message);
                return null;
            }
        }

        private AppServerLaunchInfo TryCreateRemoteMacSshLaunchInfo(
            RemoteMacSshConfigSnapshot remoteConfig,
            bool materializeSensitiveFiles,
            IDictionary<string, string> appServerEnvironmentVariables)
        {
            remoteConfig = remoteConfig ?? new RemoteMacSshConfigSnapshot();
            if (string.IsNullOrWhiteSpace(remoteConfig.HostName))
            {
                return null;
            }

            if (remoteConfig.AuthenticationMode == RemoteSshAuthenticationMode.PrivateKey &&
                !string.IsNullOrWhiteSpace(remoteConfig.PrivateKeyPath) &&
                !File.Exists(remoteConfig.PrivateKeyPath.Trim()))
            {
                return null;
            }

            try
            {
                return BuildRemoteMacSshLaunchInfo(
                    remoteConfig,
                    RemoteSshCommandLine.BuildShellCommand(
                        remoteConfig,
                        appServerEnvironmentVariables,
                        BuildRemoteAppServerCommandParts(remoteConfig)),
                    CodexRuntimeSources.RemoteUnixSsh,
                    materializeSensitiveFiles,
                    appServerEnvironmentVariables);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Remote Unix SSH Codex runtime configuration is unavailable: {0}", ex.Message);
                return null;
            }
        }

        private AppServerLaunchInfo BuildAppServerLaunchInfo(
            string executablePath,
            string additionalPathDirectory,
            string source,
            IDictionary<string, string> appServerEnvironmentVariables = null)
        {
            var launchInfo = BuildLaunchInfo(
                executablePath,
                BuildAppServerArgumentsForSource(source),
                additionalPathDirectory);
            ApplyUtf8Environment(launchInfo.EnvironmentVariables);
            launchInfo.EnvironmentVariables["MYCODEX_CODEX_SOURCE"] = source;
            launchInfo.EnvironmentVariables["MYCODEX_CODEX_EXECUTABLE"] = executablePath;
            if (!string.IsNullOrWhiteSpace(additionalPathDirectory))
            {
                launchInfo.EnvironmentVariables["MYCODEX_CODEX_EXTRA_PATH"] = additionalPathDirectory;
            }

            ApplyAdditionalEnvironmentVariables(launchInfo.EnvironmentVariables, appServerEnvironmentVariables);
            ApplyOfficeCliEnvironmentIfInstalled(
                launchInfo.EnvironmentVariables,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            return launchInfo;
        }

        internal static bool ApplyOfficeCliEnvironmentIfInstalled(
            IDictionary<string, string> environmentVariables,
            string localApplicationDataPath)
        {
            if (environmentVariables == null || string.IsNullOrWhiteSpace(localApplicationDataPath))
            {
                return false;
            }

            var officeCliExecutablePath = Path.Combine(
                Path.GetFullPath(localApplicationDataPath.Trim()),
                OfficeCliIntegrationPlanner.DirectoryName,
                OfficeCliIntegrationPlanner.ExecutableFileName);
            return OfficeCliIntegrationPlanner.TryApplyRuntimeEnvironment(
                environmentVariables,
                officeCliExecutablePath,
                File.Exists);
        }

        private static string BuildAppServerArgumentsForSource(string source)
        {
            var parts = BuildAppServerCommandParts();
            var quotedParts = new string[parts.Length];
            for (var index = 0; index < parts.Length; index++)
            {
                quotedParts[index] = WindowsCommandLineArgumentEscaper.Quote(parts[index]);
            }

            return string.Join(" ", quotedParts);
        }

        private static string[] BuildAppServerCommandParts()
        {
            var parts = new List<string>
            {
                "app-server",
                "--enable",
                "goals",
                "--listen",
                "stdio://"
            };
            return parts.ToArray();
        }

        private static string[] BuildRemoteAppServerCommandParts(
            RemoteMacSshConfigSnapshot remoteConfig)
        {
            var appServerParts = BuildAppServerCommandParts();
            var commandParts = new string[appServerParts.Length + 1];
            commandParts[0] = RemoteSshCommandLine.NormalizeCodexCommand(
                remoteConfig != null ? remoteConfig.CodexCommand : null);
            Array.Copy(appServerParts, 0, commandParts, 1, appServerParts.Length);
            return commandParts;
        }

        private AppServerLaunchInfo BuildWslLaunchInfo(
            string wslExecutablePath,
            string arguments,
            string source,
            WslAppServerConfigSnapshot wslConfig,
            IDictionary<string, string> appServerEnvironmentVariables = null)
        {
            var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "MYCODEX_CODEX_SOURCE", source },
                { "MYCODEX_CODEX_EXECUTABLE", wslExecutablePath },
                { "MYCODEX_WSL_DISTRIBUTION", WslCommandLine.NormalizeDistributionName(wslConfig != null ? wslConfig.DistributionName : null) },
                { "MYCODEX_WSL_CODEX_COMMAND", WslCommandLine.NormalizeExecutable(wslConfig != null ? wslConfig.CodexCommand : null) }
            };
            ApplyUtf8Environment(environmentVariables);
            ApplyAdditionalEnvironmentVariables(environmentVariables, appServerEnvironmentVariables);

            return new AppServerLaunchInfo(
                wslExecutablePath,
                arguments,
                environmentVariables,
                AppServerHostPlatform.Wsl);
        }

        private AppServerLaunchInfo BuildRemoteMacSshLaunchInfo(
            RemoteMacSshConfigSnapshot remoteConfig,
            string remoteCommand,
            string source,
            bool materializeSensitiveFiles = true,
            IDictionary<string, string> appServerEnvironmentVariables = null)
        {
            remoteConfig = remoteConfig ?? new RemoteMacSshConfigSnapshot();
            var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "MYCODEX_CODEX_SOURCE", source },
                { "MYCODEX_REMOTE_MAC_HOST", remoteConfig.HostName ?? string.Empty },
                { "MYCODEX_REMOTE_MAC_USER", remoteConfig.UserName ?? string.Empty },
                { "MYCODEX_REMOTE_MAC_CODEX_COMMAND", RemoteSshCommandLine.NormalizeCodexCommand(remoteConfig.CodexCommand) }
            };
            ApplyUtf8Environment(environmentVariables);
            ApplyAdditionalEnvironmentVariables(environmentVariables, appServerEnvironmentVariables);

            var sensitiveTempFilePaths = new List<string>();
            try
            {
                if (remoteConfig.AuthenticationMode == RemoteSshAuthenticationMode.Password)
                {
                    var commandFilePath = materializeSensitiveFiles
                        ? RemoteSshSensitiveCommandFile.CreateCommandFile(
                            _appConfigService.GetTempDirectory(),
                            remoteCommand)
                        : null;
                    if (!string.IsNullOrWhiteSpace(commandFilePath))
                    {
                        sensitiveTempFilePaths.Add(commandFilePath);
                    }

                    var plinkPath = PlinkExecutableResolver.Resolve(remoteConfig.PlinkExecutablePath);
                    var passwordFilePath = materializeSensitiveFiles
                        ? RemoteSshPasswordFile.Create(
                            _appConfigService.GetTempDirectory(),
                            RemoteSshPasswordProtector.Unprotect(remoteConfig.ProtectedPassword))
                        : null;
                    if (!string.IsNullOrWhiteSpace(passwordFilePath))
                    {
                        sensitiveTempFilePaths.Add(passwordFilePath);
                    }

                    return new AppServerLaunchInfo(
                        plinkPath,
                        !string.IsNullOrWhiteSpace(commandFilePath)
                            ? RemoteSshCommandLine.BuildPlinkArgumentsWithCommandFile(remoteConfig, commandFilePath, passwordFilePath)
                            : RemoteSshCommandLine.BuildPlinkArguments(remoteConfig, remoteCommand, passwordFilePath),
                        environmentVariables,
                        AppServerHostPlatform.RemoteMac,
                        sensitiveTempFilePaths);
                }

                var openSshConfigFilePath = materializeSensitiveFiles
                    ? RemoteSshSensitiveCommandFile.CreateOpenSshConfigFile(
                        _appConfigService.GetTempDirectory(),
                        remoteConfig,
                        remoteCommand)
                    : null;
                if (!string.IsNullOrWhiteSpace(openSshConfigFilePath))
                {
                    sensitiveTempFilePaths.Add(openSshConfigFilePath);
                }

                return new AppServerLaunchInfo(
                    "ssh",
                    !string.IsNullOrWhiteSpace(openSshConfigFilePath)
                        ? RemoteSshCommandLine.BuildSshArgumentsWithConfigFile(openSshConfigFilePath, "mycodex-remote-appserver")
                        : RemoteSshCommandLine.BuildSshArguments(remoteConfig, remoteCommand),
                    environmentVariables,
                    AppServerHostPlatform.RemoteMac,
                    sensitiveTempFilePaths);
            }
            catch
            {
                foreach (var path in sensitiveTempFilePaths)
                {
                    RemoteSshPasswordFile.TryDelete(path);
                }

                throw;
            }
        }

        private static void ApplyUtf8Environment(IDictionary<string, string> environmentVariables)
        {
            if (environmentVariables == null)
            {
                return;
            }

            environmentVariables["MYCODEX_TEXT_ENCODING"] = "utf-8";
            environmentVariables["PYTHONUTF8"] = "1";
            environmentVariables["PYTHONIOENCODING"] = "utf-8";
            environmentVariables["LANG"] = "C.UTF-8";
            environmentVariables["LC_ALL"] = "C.UTF-8";
            environmentVariables["LESSCHARSET"] = "utf-8";
        }

        private static void ApplyAdditionalEnvironmentVariables(
            IDictionary<string, string> environmentVariables,
            IDictionary<string, string> additionalEnvironmentVariables)
        {
            if (environmentVariables == null || additionalEnvironmentVariables == null)
            {
                return;
            }

            foreach (var pair in additionalEnvironmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    environmentVariables[pair.Key.Trim()] = pair.Value ?? string.Empty;
                }
            }
        }

        private AppServerLaunchInfo BuildLaunchInfo(string executablePath, string arguments, string additionalPathDirectory)
        {
            var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(additionalPathDirectory) && Directory.Exists(additionalPathDirectory))
            {
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                environmentVariables["PATH"] = string.IsNullOrWhiteSpace(currentPath)
                    ? additionalPathDirectory
                    : additionalPathDirectory + Path.PathSeparator + currentPath;
            }

            if (IsCommandScript(executablePath))
            {
                return new AppServerLaunchInfo(
                    "cmd.exe",
                    string.Format("/d /c \"\"{0}\" {1}\"", executablePath, arguments),
                    environmentVariables);
            }

            return new AppServerLaunchInfo(executablePath, arguments, environmentVariables);
        }

        private static void ApplyEnvironment(
            ProcessStartInfo startInfo,
            IDictionary<string, string> environmentVariables,
            ICollection<string> environmentVariableNames = null)
        {
            if (startInfo == null || environmentVariables == null)
            {
                return;
            }

            foreach (var pair in environmentVariables)
            {
                if (pair.Key == null)
                {
                    continue;
                }

                startInfo.EnvironmentVariables[pair.Key] = pair.Value ?? string.Empty;
                if (environmentVariableNames != null)
                {
                    environmentVariableNames.Add(pair.Key);
                }
            }
        }

        private static async Task<string> VerifyProcessAsync(AppServerLaunchInfo processLaunchInfo, string workingDirectory, CancellationToken cancellationToken)
        {
            if (processLaunchInfo == null)
            {
                throw new ArgumentNullException(nameof(processLaunchInfo));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = processLaunchInfo.FileName,
                Arguments = processLaunchInfo.Arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            var environmentVariableNames = new List<string>();
            ApplyEnvironment(startInfo, processLaunchInfo.EnvironmentVariables, environmentVariableNames);
            if (processLaunchInfo.HostPlatform == AppServerHostPlatform.Wsl)
            {
                var existingWslEnv = startInfo.EnvironmentVariables["WSLENV"];
                var updatedWslEnv = WslEnvironmentBridge.BuildWslEnvValue(existingWslEnv, environmentVariableNames);
                if (!string.IsNullOrWhiteSpace(updatedWslEnv))
                {
                    startInfo.EnvironmentVariables["WSLENV"] = updatedWslEnv;
                }
            }

            try
            {
                using (var process = new Process { StartInfo = startInfo })
                {
                    try
                    {
                        if (!process.Start())
                        {
                            throw new InvalidOperationException(
                                SharedLocalization.Text("CodexRuntime.Error.UnableToStartExecutable", "Unable to start Codex executable."));
                        }
                    }
                    catch (Win32Exception ex)
                    {
                        throw new InvalidOperationException(
                            SharedLocalization.Text("CodexRuntime.Error.UnableToStartExecutable", "Unable to start Codex executable."),
                            ex);
                    }

                    var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                    var standardErrorTask = process.StandardError.ReadToEndAsync();

                    await WaitForExitAsync(process, cancellationToken).ConfigureAwait(false);
                    var standardOutput = await standardOutputTask.ConfigureAwait(false);
                    var standardError = await standardErrorTask.ConfigureAwait(false);

                    if (process.ExitCode != 0)
                    {
                        var errorMessage = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
                        throw new InvalidOperationException(
                            string.Format(
                                SharedLocalization.Text("CodexRuntime.Error.VerificationFailed.Template", "Codex verification failed: {0}"),
                                string.IsNullOrWhiteSpace(errorMessage)
                                    ? SharedLocalization.Text("Common.Value.UnknownError", "Unknown error")
                                    : errorMessage.Trim()));
                    }

                    var version = (standardOutput ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(version))
                    {
                        version = (standardError ?? string.Empty).Trim();
                    }

                    return version;
                }
            }
            finally
            {
                DeleteSensitiveTempFiles(processLaunchInfo);
            }
        }

        private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
        {
            while (!process.WaitForExit(100))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        private static string GetCurrentTargetTriple()
        {
            var primaryArchitecture = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty).Trim();
            var redirectedArchitecture = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? string.Empty).Trim();
            if (string.Equals(primaryArchitecture, "ARM64", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(redirectedArchitecture, "ARM64", StringComparison.OrdinalIgnoreCase))
            {
                return "aarch64-pc-windows-msvc";
            }

            return "x86_64-pc-windows-msvc";
        }

        private static string InferAdditionalPathDirectory(string executablePath)
        {
            try
            {
                var executableDirectory = Path.GetDirectoryName(executablePath);
                if (string.IsNullOrWhiteSpace(executableDirectory))
                {
                    return null;
                }

                var directoryName = Path.GetFileName(executableDirectory);
                var targetDirectory = Directory.GetParent(executableDirectory);
                if (targetDirectory == null)
                {
                    return null;
                }

                if (string.Equals(directoryName, "bin", StringComparison.OrdinalIgnoreCase))
                {
                    var currentPathDirectory = Path.Combine(targetDirectory.FullName, "codex-path");
                    return Directory.Exists(currentPathDirectory) ? currentPathDirectory : null;
                }

                if (string.Equals(directoryName, "codex", StringComparison.OrdinalIgnoreCase))
                {
                    var legacyPathDirectory = Path.Combine(targetDirectory.FullName, "path");
                    return Directory.Exists(legacyPathDirectory) ? legacyPathDirectory : null;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsCommandScript(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            var extension = Path.GetExtension(executablePath);
            return string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindExecutablePath(string commandName)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = commandName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return null;
                    }

                    var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                    var standardErrorTask = process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    Task.WaitAll(standardOutputTask, standardErrorTask);
                    var standardOutput = standardOutputTask.Result;
                    if (process.ExitCode != 0)
                    {
                        return null;
                    }

                    using (var reader = new StringReader(standardOutput))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
                            {
                                return line.Trim();
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private static string FindWslExecutablePath()
        {
            var path = FindExecutablePath("wsl.exe");
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windowsDirectory))
            {
                var system32Path = Path.Combine(windowsDirectory, "System32", "wsl.exe");
                if (File.Exists(system32Path))
                {
                    return system32Path;
                }
            }

            return null;
        }

        private static string GetLaunchInfoSource(AppServerLaunchInfo launchInfo)
        {
            if (launchInfo == null)
            {
                return null;
            }

            string source;
            if (launchInfo.EnvironmentVariables != null &&
                launchInfo.EnvironmentVariables.TryGetValue("MYCODEX_CODEX_SOURCE", out source) &&
                !string.IsNullOrWhiteSpace(source))
            {
                return source;
            }

            return null;
        }

        private static string ExtractExecutablePath(AppServerLaunchInfo launchInfo)
        {
            if (launchInfo == null)
            {
                return null;
            }

            string executablePath;
            if (launchInfo.EnvironmentVariables != null &&
                launchInfo.EnvironmentVariables.TryGetValue("MYCODEX_CODEX_EXECUTABLE", out executablePath))
            {
                return executablePath;
            }

            return launchInfo.FileName;
        }

        private static string ExtractAdditionalPathDirectory(AppServerLaunchInfo launchInfo)
        {
            if (launchInfo == null || launchInfo.EnvironmentVariables == null)
            {
                return null;
            }

            string additionalPath;
            if (launchInfo.EnvironmentVariables.TryGetValue("MYCODEX_CODEX_EXTRA_PATH", out additionalPath))
            {
                return additionalPath;
            }

            return null;
        }

    }
}
