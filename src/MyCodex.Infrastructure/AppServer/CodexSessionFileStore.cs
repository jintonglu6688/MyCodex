using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class CodexSessionFileStore
    {
        private readonly Func<AppServerHostPlatform> _getHostPlatform;
        private readonly Func<AppConfigSnapshot> _loadConfig;
        private readonly IWslFileSystemBridge _wslFileSystemBridge;
        private readonly IRemoteMacFileSystemBridge _remoteMacFileSystemBridge;
        private readonly Action _beforeLocalDeleteValidation;

        internal CodexSessionFileStore(Func<AppServerHostPlatform> getHostPlatform, Func<AppConfigSnapshot> loadConfig)
            : this(getHostPlatform, loadConfig, new WslFileSystemBridge(), new RemoteMacFileSystemBridge())
        {
        }

        internal CodexSessionFileStore(
            Func<AppServerHostPlatform> getHostPlatform,
            Func<AppConfigSnapshot> loadConfig,
            IWslFileSystemBridge wslFileSystemBridge)
            : this(getHostPlatform, loadConfig, wslFileSystemBridge, new RemoteMacFileSystemBridge())
        {
        }

        internal CodexSessionFileStore(
            Func<AppServerHostPlatform> getHostPlatform,
            Func<AppConfigSnapshot> loadConfig,
            IWslFileSystemBridge wslFileSystemBridge,
            IRemoteMacFileSystemBridge remoteMacFileSystemBridge)
            : this(getHostPlatform, loadConfig, wslFileSystemBridge, remoteMacFileSystemBridge, null)
        {
        }

        internal CodexSessionFileStore(
            Func<AppServerHostPlatform> getHostPlatform,
            Func<AppConfigSnapshot> loadConfig,
            IWslFileSystemBridge wslFileSystemBridge,
            IRemoteMacFileSystemBridge remoteMacFileSystemBridge,
            Action beforeLocalDeleteValidation)
        {
            _getHostPlatform = getHostPlatform ?? throw new ArgumentNullException(nameof(getHostPlatform));
            _loadConfig = loadConfig ?? throw new ArgumentNullException(nameof(loadConfig));
            _wslFileSystemBridge = wslFileSystemBridge ?? throw new ArgumentNullException(nameof(wslFileSystemBridge));
            _remoteMacFileSystemBridge = remoteMacFileSystemBridge ?? throw new ArgumentNullException(nameof(remoteMacFileSystemBridge));
            _beforeLocalDeleteValidation = beforeLocalDeleteValidation;
        }

        internal Task DeleteThreadFileAsync(string sessionPath)
        {
            ValidateSessionPathRequired(sessionPath);
            var appConfig = _loadConfig();
            var codexHomePaths = LocalSessionCatalog.ResolveLocalCodexHomePathsForThreadScan(
                _getHostPlatform(),
                appConfig);
            return DeleteThreadFileAsync(sessionPath, codexHomePaths, appConfig);
        }

        internal Task DeleteThreadFileAsync(
            string sessionPath,
            IEnumerable<string> allowedCodexHomePaths)
        {
            ValidateSessionPathRequired(sessionPath);
            return DeleteThreadFileAsync(sessionPath, allowedCodexHomePaths, _loadConfig());
        }

        private Task DeleteThreadFileAsync(
            string sessionPath,
            IEnumerable<string> allowedCodexHomePaths,
            AppConfigSnapshot appConfig)
        {
            if (AppServerPathUtilities.LooksLikeUnixAbsolutePath(sessionPath))
            {
                if (_getHostPlatform() == AppServerHostPlatform.RemoteMac)
                {
                    return _remoteMacFileSystemBridge.DeleteCodexSessionPathAsync(
                        appConfig != null ? appConfig.GetSelectedRemoteUnixSshConfig() : null,
                        sessionPath);
                }

                if (_getHostPlatform() == AppServerHostPlatform.Wsl)
                {
                    return _wslFileSystemBridge.DeleteCodexSessionPathAsync(
                        appConfig != null ? appConfig.WslAppServer : null,
                        sessionPath);
                }

                throw new InvalidOperationException(L(
                    "AppServerClient.Error.WslThreadFilePathUnsupported",
                    "WSL session files cannot be deleted through Windows file access."));
            }

            return DeleteLocalThreadPathAsync(sessionPath, allowedCodexHomePaths);
        }

        private static void ValidateSessionPathRequired(string sessionPath)
        {
            if (string.IsNullOrWhiteSpace(sessionPath))
            {
                throw new ArgumentException(
                    L("AppServerClient.Error.ThreadFilePathRequired", "Thread file path cannot be empty."),
                    nameof(sessionPath));
            }
        }

        private Task DeleteLocalThreadPathAsync(
            string sessionPath,
            IEnumerable<string> allowedCodexHomePaths)
        {
            var allowedSessionRoots = BuildAllowedSessionRoots(allowedCodexHomePaths);
            var normalizedPath = CodexSessionPathPolicy.ValidateDeletePath(sessionPath, allowedSessionRoots);
            var fileExists = File.Exists(normalizedPath);
            var directoryExists = Directory.Exists(normalizedPath);

            if (!fileExists && !directoryExists)
            {
                return Task.CompletedTask;
            }

            if (fileExists)
            {
                _beforeLocalDeleteValidation?.Invoke();
                CodexSessionPathPolicy.ValidateDeletePath(normalizedPath, allowedSessionRoots);
                File.Delete(normalizedPath);
                return Task.CompletedTask;
            }

            _beforeLocalDeleteValidation?.Invoke();
            CodexSessionPathPolicy.ValidateDeletePath(normalizedPath, allowedSessionRoots);
            Directory.Delete(normalizedPath, true);
            return Task.CompletedTask;
        }

        private static IReadOnlyList<string> BuildAllowedSessionRoots(
            IEnumerable<string> allowedCodexHomePaths)
        {
            var roots = new List<string>();
            if (allowedCodexHomePaths == null)
            {
                return roots;
            }

            foreach (var codexHomePath in allowedCodexHomePaths)
            {
                if (string.IsNullOrWhiteSpace(codexHomePath))
                {
                    continue;
                }

                roots.Add(Path.Combine(codexHomePath, "sessions"));
                roots.Add(Path.Combine(codexHomePath, "archived_sessions"));
            }

            return roots;
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }
    }
}
