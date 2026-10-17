using System;
using System.IO;
using System.Text;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Persistence
{
    public class AppConfigService
    {
        private const string LegacyVendorDirectoryName = "BoyPcReader";
        private const string AppDirectoryName = "MyCodex";
        private const string DevAppDirectoryName = "MyCodex-Dev";
        private readonly string _appDataDirectoryOverride;
        private readonly string _installationDirectoryOverride;
        private readonly string _sharedDataDirectoryOverride;
        private readonly string _appDirectoryName;
        private readonly bool _hasSeparateDataRoot;

        public bool HasSeparateDataRoot
        {
            get { return _hasSeparateDataRoot; }
        }

        internal bool HasAppDataDirectoryOverride
        {
            get { return _appDataDirectoryOverride != null; }
        }

        public AppConfigService(string appDataDirectoryOverride = null, string appDirectoryName = null)
        {
            _appDataDirectoryOverride = string.IsNullOrWhiteSpace(appDataDirectoryOverride)
                ? null
                : Path.GetFullPath(appDataDirectoryOverride.Trim());
            _appDirectoryName = string.IsNullOrWhiteSpace(appDirectoryName)
                ? AppDirectoryName
                : appDirectoryName.Trim();
            _installationDirectoryOverride = _appDataDirectoryOverride;
        }

        public AppConfigService(
            string targetDataDirectory,
            string sharedInstallationDirectory,
            string appDirectoryName,
            string sharedDataDirectory = null)
            : this(RequireDirectory(targetDataDirectory, nameof(targetDataDirectory)), appDirectoryName)
        {
            _installationDirectoryOverride = RequireDirectory(
                sharedInstallationDirectory, nameof(sharedInstallationDirectory));
            _sharedDataDirectoryOverride = string.IsNullOrWhiteSpace(sharedDataDirectory)
                ? _appDataDirectoryOverride
                : Path.GetFullPath(sharedDataDirectory.Trim());
            _hasSeparateDataRoot = true;
        }

        public string GetAppDataDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_appDataDirectoryOverride))
            {
                return _appDataDirectoryOverride;
            }

            var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppDataPath, _appDirectoryName);
        }

        public string GetConfigDirectory()
        {
            return Path.Combine(GetAppDataDirectory(), "config");
        }

        public string GetInstallationDirectory()
        {
            return _installationDirectoryOverride ?? AppDomain.CurrentDomain.BaseDirectory;
        }

        public string GetSharedDataDirectory()
        {
            return _sharedDataDirectoryOverride ?? GetAppDataDirectory();
        }

        public string GetConfigFilePath()
        {
            return Path.Combine(GetConfigDirectory(), "config.json");
        }

        public string GetModeSettingsFilePath()
        {
            return Path.Combine(GetConfigDirectory(), "mode-settings.json");
        }

        internal string GetRuntimeAppStateFilePath()
        {
            return string.Equals(_appDirectoryName, DevAppDirectoryName, StringComparison.OrdinalIgnoreCase)
                ? "~/.codex/mycodex-dev/app-state.json"
                : "~/.codex/mycodex/app-state.json";
        }

        public string GetRuntimeDirectory()
        {
            return Path.Combine(GetInstallationDirectory(), "runtime");
        }

        public string GetBundledCodexDirectory()
        {
            return Path.Combine(GetRuntimeDirectory(), "codex");
        }

        public string GetBundledCodexVersionDirectory(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("AppConfigService.Error.VersionRequired", "Version cannot be empty."),
                    nameof(version));
            }

            return Path.Combine(GetBundledCodexDirectory(), ValidateRuntimePathSegment(version, nameof(version)));
        }

        public string GetLatestInstalledBundledCodexVersion(string targetTriple)
        {
            if (string.IsNullOrWhiteSpace(targetTriple))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("AppConfigService.Error.TargetPlatformRequired", "Target platform cannot be empty."),
                    nameof(targetTriple));
            }

            var bundledCodexDirectory = GetBundledCodexDirectory();
            if (!Directory.Exists(bundledCodexDirectory))
            {
                return null;
            }

            string bestVersionName = null;
            Version bestVersion = null;
            foreach (var versionDirectory in Directory.GetDirectories(bundledCodexDirectory))
            {
                var versionName = Path.GetFileName(versionDirectory);
                if (!TryValidateRuntimePathSegment(versionName, out versionName))
                {
                    continue;
                }

                if (!File.Exists(GetBundledCodexExecutablePath(versionName, targetTriple)))
                {
                    continue;
                }

                var parsedVersion = ParseCodexRuntimeVersion(versionName);
                if (bestVersionName == null ||
                    CompareCodexRuntimeVersions(versionName, parsedVersion, bestVersionName, bestVersion) > 0)
                {
                    bestVersionName = versionName;
                    bestVersion = parsedVersion;
                }
            }

            return bestVersionName;
        }

        public string GetBundledCodexInstallDirectory(string version, string targetTriple)
        {
            if (string.IsNullOrWhiteSpace(targetTriple))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("AppConfigService.Error.TargetPlatformRequired", "Target platform cannot be empty."),
                    nameof(targetTriple));
            }

            return Path.Combine(
                GetBundledCodexVersionDirectory(version),
                ValidateRuntimePathSegment(targetTriple, nameof(targetTriple)));
        }

        public string GetBundledCodexExecutablePath(string version, string targetTriple)
        {
            var newPath = GetBundledCodexExecutablePath(version, targetTriple, true);
            var legacyPath = GetBundledCodexExecutablePath(version, targetTriple, false);
            if (File.Exists(newPath) || !File.Exists(legacyPath))
            {
                return newPath;
            }

            return legacyPath;
        }

        public string GetBundledCodexPathDirectory(string version, string targetTriple)
        {
            var newPath = GetBundledCodexPathDirectory(version, targetTriple, true);
            var legacyPath = GetBundledCodexPathDirectory(version, targetTriple, false);
            if (Directory.Exists(newPath) || !Directory.Exists(legacyPath))
            {
                return newPath;
            }

            return legacyPath;
        }

        private string GetBundledCodexExecutablePath(string version, string targetTriple, bool useCurrentLayout)
        {
            return Path.Combine(
                GetBundledCodexInstallDirectory(version, targetTriple),
                "vendor",
                targetTriple,
                useCurrentLayout ? "bin" : "codex",
                "codex.exe");
        }

        private string GetBundledCodexPathDirectory(string version, string targetTriple, bool useCurrentLayout)
        {
            return Path.Combine(
                GetBundledCodexInstallDirectory(version, targetTriple),
                "vendor",
                targetTriple,
                useCurrentLayout ? "codex-path" : "path");
        }

        public string GetAppStateFilePath()
        {
            return Path.Combine(GetConfigDirectory(), "app-state.json");
        }

        public string GetAutomationsFilePath()
        {
            return Path.Combine(GetConfigDirectory(), "automations.json");
        }

        public string GetAutomationWorkspaceDirectory()
        {
            return Path.Combine(GetAppDataDirectory(), "automations", "workspace");
        }

        public string GetRemoteServiceConfigFilePath()
        {
            return Path.Combine(GetConfigDirectory(), "remote-service.json");
        }

        public string GetRemoteBindingsFilePath()
        {
            return Path.Combine(GetConfigDirectory(), "remote-bindings.json");
        }

        public string GetLogDirectory()
        {
            return Path.Combine(GetAppDataDirectory(), "logs");
        }

        internal static string NormalizePathSegment(string value)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
            var builder = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                builder.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            }

            return builder.Length == 0 ? "default" : builder.ToString();
        }

        internal static string ValidateRuntimePathSegment(string value, string parameterName)
        {
            var text = value == null ? null : value.Trim();
            if (string.IsNullOrWhiteSpace(text) ||
                string.Equals(text, ".", StringComparison.Ordinal) ||
                string.Equals(text, "..", StringComparison.Ordinal) ||
                Path.IsPathRooted(text) ||
                text.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                text.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
                text.IndexOf('/') >= 0 ||
                text.IndexOf('\\') >= 0 ||
                !string.Equals(Path.GetFileName(text), text, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("AppConfigService.Error.RuntimePathSegmentInvalid", "Runtime path segment is invalid."),
                    parameterName);
            }

            for (var index = 0; index < text.Length; index++)
            {
                var ch = text[index];
                if (!((ch >= 'a' && ch <= 'z') ||
                      (ch >= 'A' && ch <= 'Z') ||
                      (ch >= '0' && ch <= '9') ||
                      ch == '.' ||
                      ch == '-' ||
                      ch == '_'))
                {
                    throw new ArgumentException(
                        SharedLocalization.Text("AppConfigService.Error.RuntimePathSegmentInvalid", "Runtime path segment is invalid."),
                        parameterName);
                }
            }

            return text;
        }

        internal static bool TryValidateRuntimePathSegment(string value, out string normalizedValue)
        {
            normalizedValue = null;
            try
            {
                normalizedValue = ValidateRuntimePathSegment(value, nameof(value));
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public string GetTempDirectory()
        {
            return Path.Combine(GetAppDataDirectory(), "temp");
        }

        public string GetAttachmentTempDirectory()
        {
            return Path.Combine(GetTempDirectory(), "attachments");
        }

        public string GetUpdateTempDirectory()
        {
            return Path.Combine(GetTempDirectory(), "updates");
        }

        public void EnsureConfigDirectory()
        {
            Directory.CreateDirectory(GetAppDataDirectory());
            Directory.CreateDirectory(GetConfigDirectory());
            Directory.CreateDirectory(GetLogDirectory());
            Directory.CreateDirectory(GetAttachmentTempDirectory());
            Directory.CreateDirectory(GetUpdateTempDirectory());
            Directory.CreateDirectory(GetAutomationWorkspaceDirectory());
            MigrateLegacyConfigFiles(GetLegacyConfigDirectory());
        }

        internal void MigrateLegacyConfigFiles(string legacyConfigDirectory)
        {
            if (_hasSeparateDataRoot || !Directory.Exists(legacyConfigDirectory))
            {
                return;
            }

            TryMigrateLegacyFile(
                Path.Combine(legacyConfigDirectory, "config.json"),
                GetConfigFilePath());
            TryMigrateLegacyFile(
                Path.Combine(legacyConfigDirectory, "app-state.json"),
                GetAppStateFilePath());
        }

        private void TryMigrateLegacyFile(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath) || File.Exists(destinationPath))
            {
                return;
            }

            try
            {
                var destinationDirectory = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(sourcePath, destinationPath, false);
            }
            catch
            {
                // Keep startup resilient. If legacy migration fails, continue with defaults at the new location.
            }
        }

        internal string GetLegacyConfigDirectory()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(documentsPath, LegacyVendorDirectoryName, "apps", AppDirectoryName);
        }

        private static string RequireDirectory(string directory, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Directory cannot be empty.", parameterName);
            }

            return Path.GetFullPath(directory.Trim());
        }

        private static Version ParseCodexRuntimeVersion(string versionName)
        {
            if (string.IsNullOrWhiteSpace(versionName))
            {
                return null;
            }

            var normalized = versionName.Trim();
            var suffixIndex = normalized.IndexOf('-');
            if (suffixIndex > 0)
            {
                normalized = normalized.Substring(0, suffixIndex);
            }

            Version version;
            return Version.TryParse(normalized, out version) ? version : null;
        }

        private static int CompareCodexRuntimeVersions(
            string leftName,
            Version leftVersion,
            string rightName,
            Version rightVersion)
        {
            if (leftVersion != null && rightVersion != null)
            {
                var parsedComparison = leftVersion.CompareTo(rightVersion);
                if (parsedComparison != 0)
                {
                    return parsedComparison;
                }
            }
            else if (leftVersion != null)
            {
                return 1;
            }
            else if (rightVersion != null)
            {
                return -1;
            }

            return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
