using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;

namespace MyCodex.Infrastructure.Persistence
{
    public class JsonAppConfigStore : IAppConfigStore
    {
        private readonly AppConfigService _appConfigService;
        private readonly DataContractJsonSerializer _serializer;
        private volatile bool _saveBlocked;

        public JsonAppConfigStore(AppConfigService appConfigService)
        {
            _appConfigService = appConfigService ?? throw new ArgumentNullException(nameof(appConfigService));
            _serializer = new DataContractJsonSerializer(typeof(AppConfigSnapshot));
        }

        public AppConfigSnapshot Load()
        {
            ThrowIfSaveBlocked();
            var filePath = _appConfigService.GetConfigFilePath();
            return PersistenceFileRecovery.Load(
                filePath,
                filePath + ".bak",
                LoadFile,
                () => new AppConfigSnapshot(),
                () => _saveBlocked = true);
        }

        public void Save(AppConfigSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            ThrowIfSaveBlocked();

            snapshot = NormalizeSnapshot(snapshot);
            _appConfigService.EnsureConfigDirectory();

            var filePath = _appConfigService.GetConfigFilePath();
            AtomicFileWriter.Write(filePath, stream => _serializer.WriteObject(stream, snapshot), filePath + ".bak");
        }

        private AppConfigSnapshot LoadFile(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            {
                return NormalizeSnapshot(_serializer.ReadObject(stream) as AppConfigSnapshot);
            }
        }

        private void ThrowIfSaveBlocked()
        {
            if (_saveBlocked)
            {
                var primaryPath = _appConfigService.GetConfigFilePath();
                throw new PersistenceRecoveryRequiredException(
                    primaryPath,
                    primaryPath + ".bak",
                    null,
                    null);
            }
        }

        internal static AppConfigSnapshot NormalizeSnapshot(AppConfigSnapshot snapshot)
        {
            snapshot = snapshot ?? new AppConfigSnapshot();
            snapshot.WslAppServer = snapshot.WslAppServer ?? new WslAppServerConfigSnapshot();
            snapshot.RemoteMacSsh = snapshot.RemoteMacSsh ?? new RemoteMacSshConfigSnapshot();
            snapshot.UpdateSettings = snapshot.UpdateSettings ?? new UpdateSettingsSnapshot();
            if (string.IsNullOrWhiteSpace(snapshot.GitHostingProviderId))
            {
                snapshot.GitHostingProviderId = AppConfigSnapshot.DefaultGitHostingProviderId;
            }

            snapshot.CodexRuntimeSource = CodexRuntimeSources.CanonicalizeForPersistence(snapshot.CodexRuntimeSource);
            NormalizeCustomApprovalSettings(snapshot);
            snapshot.WindowsShellPreference = WindowsShellPreferences.Normalize(snapshot.WindowsShellPreference);
            snapshot.UiThemePreference = UiThemePreference.Normalize(snapshot.UiThemePreference);
            snapshot.MessageDetailPreviewMode = MessageDetailPreviewMode.Normalize(snapshot.MessageDetailPreviewMode);
            snapshot.SessionListDisplayOrder = SessionListDisplayOrder.Normalize(snapshot.SessionListDisplayOrder);
            snapshot.SendMessageShortcutMode = SendMessageShortcutMode.Normalize(snapshot.SendMessageShortcutMode);
            snapshot.ActiveTurnFollowUpMode = ActiveTurnFollowUpMode.Normalize(snapshot.ActiveTurnFollowUpMode);
            snapshot.MainWindowHotKeyModifiers = MainWindowHotKeyConfig.NormalizeModifiers(snapshot.MainWindowHotKeyModifiers);
            snapshot.MainWindowHotKeyVirtualKey = MainWindowHotKeyConfig.NormalizeVirtualKey(snapshot.MainWindowHotKeyVirtualKey);
            snapshot.ScreenReaderProvider = NormalizeScreenReaderProvider(snapshot.ScreenReaderProvider);
            NormalizeRemoteUnixProfiles(snapshot);
            if (snapshot.RuntimeProfiles == null)
            {
                snapshot.RuntimeProfiles = new List<RuntimeProfileConfigSnapshot>();
            }

            return snapshot;
        }

        private static void NormalizeCustomApprovalSettings(AppConfigSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.CustomApprovalSettings = snapshot.CustomApprovalSettings ?? new CustomApprovalSettingsSnapshot();
            var normalized = new List<CustomApprovalPermissionKind>();
            var seen = new HashSet<CustomApprovalPermissionKind>();
            foreach (var permission in snapshot.CustomApprovalSettings.EnabledPermissions)
            {
                if (!Enum.IsDefined(typeof(CustomApprovalPermissionKind), permission) ||
                    permission == CustomApprovalPermissionKind.Unknown ||
                    seen.Contains(permission))
                {
                    continue;
                }

                seen.Add(permission);
                normalized.Add(permission);
            }

            snapshot.CustomApprovalSettings.EnabledPermissions = normalized;
            snapshot.CustomApprovalSettings.CommandPrefixes = NormalizeCustomApprovalCommandPrefixes(
                snapshot.CustomApprovalSettings.CommandPrefixes);
        }

        private static List<string> NormalizeCustomApprovalCommandPrefixes(IEnumerable<string> prefixes)
        {
            var normalized = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (prefixes == null)
            {
                return normalized;
            }

            foreach (var prefix in prefixes)
            {
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    continue;
                }

                var trimmed = prefix.Trim();
                if (seen.Add(trimmed))
                {
                    normalized.Add(trimmed);
                }
            }

            return normalized;
        }

        private static ScreenReaderProviderKind NormalizeScreenReaderProvider(ScreenReaderProviderKind provider)
        {
            if ((int)provider == 1)
            {
                return ScreenReaderProviderKind.ByCtrl;
            }

            switch (provider)
            {
                case ScreenReaderProviderKind.Auto:
                case ScreenReaderProviderKind.ByCtrl:
                case ScreenReaderProviderKind.BaoYiWebApi:
                case ScreenReaderProviderKind.Zdsr:
                case ScreenReaderProviderKind.Nvda:
                    return provider;
                default:
                    return ScreenReaderProviderKind.Auto;
            }
        }

        private static void NormalizeRemoteUnixProfiles(AppConfigSnapshot snapshot)
        {
            if (snapshot.RemoteUnixSshProfiles == null)
            {
                snapshot.RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>();
            }

            if (snapshot.RemoteUnixSshProfiles.Count == 0 && HasRemoteSshConfig(snapshot.RemoteMacSsh))
            {
                snapshot.RemoteUnixSshProfiles.Add(new RemoteUnixSshProfileConfigSnapshot
                {
                    Id = BuildRemoteUnixProfileId(snapshot.RemoteMacSsh),
                    Name = BuildRemoteUnixProfileName(snapshot.RemoteMacSsh),
                    Ssh = CloneRemoteConfig(snapshot.RemoteMacSsh)
                });
            }

            for (var i = 0; i < snapshot.RemoteUnixSshProfiles.Count; i++)
            {
                var profile = snapshot.RemoteUnixSshProfiles[i] ?? new RemoteUnixSshProfileConfigSnapshot();
                profile.Ssh = profile.Ssh ?? new RemoteMacSshConfigSnapshot();
                if (string.IsNullOrWhiteSpace(profile.Id))
                {
                    profile.Id = BuildRemoteUnixProfileId(profile.Ssh);
                }

                if (string.IsNullOrWhiteSpace(profile.Name))
                {
                    profile.Name = BuildRemoteUnixProfileName(profile.Ssh);
                }

                snapshot.RemoteUnixSshProfiles[i] = profile;
            }

            if (snapshot.RemoteUnixSshProfiles.Count > 0 &&
                !ContainsRemoteProfileId(snapshot.RemoteUnixSshProfiles, snapshot.SelectedRemoteUnixSshProfileId))
            {
                snapshot.SelectedRemoteUnixSshProfileId = snapshot.RemoteUnixSshProfiles[0].Id;
            }

            var selectedProfile = snapshot.GetSelectedRemoteUnixSshProfile();
            if (selectedProfile != null)
            {
                snapshot.RemoteMacSsh = CloneRemoteConfig(selectedProfile.Ssh);
            }
        }

        private static bool HasRemoteSshConfig(RemoteMacSshConfigSnapshot config)
        {
            return config != null &&
                   (!string.IsNullOrWhiteSpace(config.HostName) ||
                    !string.IsNullOrWhiteSpace(config.UserName) ||
                    !string.IsNullOrWhiteSpace(config.PrivateKeyPath));
        }

        private static bool ContainsRemoteProfileId(IEnumerable<RemoteUnixSshProfileConfigSnapshot> profiles, string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId) || profiles == null)
            {
                return false;
            }

            foreach (var profile in profiles)
            {
                if (profile != null && string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildRemoteUnixProfileId(RemoteMacSshConfigSnapshot config)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var seed = string.Join(
                "-",
                new[]
                {
                    "remote-unix",
                    SanitizeProfileIdPart(config.UserName),
                    SanitizeProfileIdPart(config.HostName),
                    RemoteSshPortText(config)
                });
            return string.Equals(seed, "remote-unix-----22", StringComparison.OrdinalIgnoreCase)
                ? "remote-unix-" + Guid.NewGuid().ToString("N")
                : seed;
        }

        private static string BuildRemoteUnixProfileName(RemoteMacSshConfigSnapshot config)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            if (string.IsNullOrWhiteSpace(config.HostName))
            {
                return "Remote Unix";
            }

            return (string.IsNullOrWhiteSpace(config.UserName) ? string.Empty : config.UserName.Trim() + "@") +
                   config.HostName.Trim() +
                   ":" +
                   RemoteSshPortText(config);
        }

        private static string RemoteSshPortText(RemoteMacSshConfigSnapshot config)
        {
            var port = config != null && config.Port > 0 && config.Port <= 65535
                ? config.Port
                : RemoteMacSshConfigSnapshot.DefaultPort;
            return port.ToString();
        }

        private static string SanitizeProfileIdPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "-";
            }

            var chars = value.Trim().ToLowerInvariant().ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                {
                    chars[i] = '-';
                }
            }

            var sanitized = new string(chars).Trim('-');
            return string.IsNullOrWhiteSpace(sanitized) ? "-" : sanitized;
        }

        private static RemoteMacSshConfigSnapshot CloneRemoteConfig(RemoteMacSshConfigSnapshot config)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            return new RemoteMacSshConfigSnapshot
            {
                HostName = config.HostName,
                Port = config.Port,
                UserName = config.UserName,
                AuthenticationMode = config.AuthenticationMode,
                PrivateKeyPath = config.PrivateKeyPath,
                ProtectedPassword = config.ProtectedPassword,
                HostKeyFingerprint = config.HostKeyFingerprint,
                PlinkExecutablePath = config.PlinkExecutablePath,
                ShellCommand = config.ShellCommand,
                CodexCommand = config.CodexCommand
            };
        }
    }
}
