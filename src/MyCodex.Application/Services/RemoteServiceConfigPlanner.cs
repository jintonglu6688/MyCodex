using System;
using System.Collections.Generic;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Helpers;

namespace MyCodex.Application.Services
{
    public sealed class RemoteServiceValidationResult
    {
        public RemoteServiceValidationResult(bool isValid, IReadOnlyList<string> messages)
        {
            IsValid = isValid;
            Messages = messages ?? new List<string>();
        }

        public bool IsValid { get; private set; }

        public IReadOnlyList<string> Messages { get; private set; }
    }

    public sealed class RemoteServiceButtonState
    {
        public bool CanTestConnection { get; set; }

        public bool CanSave { get; set; }

        public bool CanRegister { get; set; }

        public bool CanCreateInvite { get; set; }

        public bool CanCopyPairingInfo { get; set; }

        public bool CanRefreshDevices { get; set; }

        public bool CanRevokeDevice { get; set; }
    }

    public static class RemoteServiceConfigPlanner
    {
        public const int CurrentSchemaVersion = 2;

        public static void EnsureDefaults(RemoteServiceConfigSnapshot snapshot, string machineName)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (string.IsNullOrWhiteSpace(snapshot.RemoteServiceMode))
            {
                snapshot.RemoteServiceMode = "Embedded";
            }

            NormalizeProfiles(snapshot);

            if (snapshot.RelayPort <= 0)
            {
                snapshot.RelayPort = 443;
            }

            if (snapshot.CreatedAt == default(DateTime))
            {
                snapshot.CreatedAt = DateTime.UtcNow;
                snapshot.UseTls = true;
            }

            Guid parsedHostId;
            if (string.IsNullOrWhiteSpace(snapshot.HostId) ||
                !Guid.TryParseExact(snapshot.HostId, "D", out parsedHostId) ||
                !string.Equals(
                    snapshot.HostId,
                    parsedHostId.ToString("D").ToLowerInvariant(),
                    StringComparison.Ordinal))
            {
                snapshot.HostId = Guid.NewGuid().ToString("D").ToLowerInvariant();
            }

            if (string.IsNullOrWhiteSpace(snapshot.ThisPcName))
            {
                snapshot.ThisPcName = string.IsNullOrWhiteSpace(machineName)
                    ? "MyCodex on Windows"
                    : machineName.Trim();
            }
        }

        public static void NormalizeProfiles(RemoteServiceConfigSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var legacyMode = NormalizeMode(snapshot.RemoteServiceMode);
            snapshot.RemoteServiceMode = legacyMode;
            snapshot.EmbeddedProfile = snapshot.EmbeddedProfile ?? new RemoteServiceProfileSnapshot();
            snapshot.ExternalProfile = snapshot.ExternalProfile ?? new RemoteServiceProfileSnapshot();

            if (snapshot.SchemaVersion < CurrentSchemaVersion)
            {
                if (HasLegacyProfileData(snapshot))
                {
                    CopyLegacyToProfile(snapshot, GetProfile(snapshot, legacyMode));
                }

                snapshot.SchemaVersion = CurrentSchemaVersion;
            }

            if (!HasProfileData(snapshot.ExternalProfile))
            {
                snapshot.ExternalProfile.RelayPort = 443;
                snapshot.ExternalProfile.UseTls = true;
            }
        }

        public static void SelectMode(RemoteServiceConfigSnapshot snapshot, string mode)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            NormalizeProfiles(snapshot);
            var selectedMode = NormalizeMode(mode);
            snapshot.RemoteServiceMode = selectedMode;
            CopyProfileToLegacy(GetProfile(snapshot, snapshot.RemoteServiceMode), snapshot);
        }

        public static void CaptureActiveProfile(RemoteServiceConfigSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            snapshot.EmbeddedProfile = snapshot.EmbeddedProfile ?? new RemoteServiceProfileSnapshot();
            snapshot.ExternalProfile = snapshot.ExternalProfile ?? new RemoteServiceProfileSnapshot();
            CopyLegacyToProfile(snapshot, GetProfile(snapshot, NormalizeMode(snapshot.RemoteServiceMode)));
            snapshot.SchemaVersion = CurrentSchemaVersion;
        }

        public static RemoteServiceProfileSnapshot GetProfile(RemoteServiceConfigSnapshot snapshot, string mode)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            snapshot.EmbeddedProfile = snapshot.EmbeddedProfile ?? new RemoteServiceProfileSnapshot();
            snapshot.ExternalProfile = snapshot.ExternalProfile ?? new RemoteServiceProfileSnapshot();
            return IsEmbeddedMode(mode) ? snapshot.EmbeddedProfile : snapshot.ExternalProfile;
        }

        public static bool IsEmbeddedMode(string mode)
        {
            return string.IsNullOrWhiteSpace(mode) ||
                string.Equals(mode.Trim(), "Embedded", StringComparison.OrdinalIgnoreCase);
        }

        public static RemoteServiceValidationResult Validate(RemoteServiceConfigSnapshot snapshot)
        {
            var messages = new List<string>();
            if (IsEmbeddedBeforeStart(snapshot))
            {
                return new RemoteServiceValidationResult(true, messages);
            }

            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.RelayHost))
            {
                messages.Add("Service host is required.");
            }

            if (snapshot == null || snapshot.RelayPort <= 0 || snapshot.RelayPort > 65535)
            {
                messages.Add("Port must be between 1 and 65535.");
            }

            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TenantId))
            {
                messages.Add("Tenant ID is required.");
            }

            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.EncryptedAccessSecret))
            {
                messages.Add("Access secret is required.");
            }

            return new RemoteServiceValidationResult(messages.Count == 0, messages);
        }

        public static RelayHostConfig BuildRelayHostConfig(RemoteServiceConfigSnapshot snapshot, string accessSecret)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var embedded = IsEmbeddedMode(snapshot.RemoteServiceMode);
            return new RelayHostConfig
            {
                HostDisplayName = TextValueHelper.TrimToNull(snapshot.ThisPcName),
                RelayHost = TextValueHelper.TrimToNull(snapshot.RelayHost),
                ConnectionHost = embedded ? TextValueHelper.TrimToNull(snapshot.EmbeddedRelayInternalHost) : null,
                ConnectionPort = embedded ? snapshot.EmbeddedRelayInternalPort : 0,
                ConnectionTlsRequired = embedded && snapshot.EmbeddedRelayInternalUseTls,
                RelayPort = snapshot.RelayPort,
                TlsRequired = snapshot.UseTls,
                CertificateSha256 = embedded ? TextValueHelper.TrimToNull(snapshot.EmbeddedRelayCertificateSha256) : null,
                TenantId = TextValueHelper.TrimToNull(snapshot.TenantId),
                TenantSecret = accessSecret,
                RelaySigningPublicKey = TextValueHelper.TrimToNull(snapshot.RelaySigningPublicKey),
                RelaySigningKeyFingerprint = TextValueHelper.TrimToNull(snapshot.RelaySigningKeyFingerprint)
            };
        }

        public static RemoteServiceButtonState BuildButtonState(
            bool configured,
            bool registered,
            bool inviteActive,
            bool deviceSelected,
            bool busy)
        {
            if (busy)
            {
                return new RemoteServiceButtonState();
            }

            return new RemoteServiceButtonState
            {
                CanTestConnection = configured,
                CanSave = configured,
                CanRegister = configured,
                CanCreateInvite = configured && registered,
                CanCopyPairingInfo = inviteActive,
                CanRefreshDevices = configured,
                CanRevokeDevice = deviceSelected
            };
        }

        private static string NormalizeMode(string mode)
        {
            return IsEmbeddedMode(mode) ? "Embedded" : "External";
        }

        private static bool HasLegacyProfileData(RemoteServiceConfigSnapshot snapshot)
        {
            return !string.IsNullOrWhiteSpace(snapshot.RelayHost) ||
                !string.IsNullOrWhiteSpace(snapshot.TenantId) ||
                !string.IsNullOrWhiteSpace(snapshot.EncryptedAccessSecret) ||
                !string.IsNullOrWhiteSpace(snapshot.RelaySigningPublicKey) ||
                !string.IsNullOrWhiteSpace(snapshot.RelaySigningKeyFingerprint);
        }

        private static bool HasProfileData(RemoteServiceProfileSnapshot profile)
        {
            return profile != null &&
                (!string.IsNullOrWhiteSpace(profile.RelayHost) ||
                 !string.IsNullOrWhiteSpace(profile.TenantId) ||
                 !string.IsNullOrWhiteSpace(profile.EncryptedAccessSecret) ||
                 !string.IsNullOrWhiteSpace(profile.RelaySigningPublicKey) ||
                 !string.IsNullOrWhiteSpace(profile.RelaySigningKeyFingerprint));
        }

        private static void CopyLegacyToProfile(
            RemoteServiceConfigSnapshot snapshot,
            RemoteServiceProfileSnapshot profile)
        {
            profile.RelayHost = snapshot.RelayHost;
            profile.RelayPort = snapshot.RelayPort;
            profile.UseTls = snapshot.UseTls;
            profile.TenantId = snapshot.TenantId;
            profile.EncryptedAccessSecret = snapshot.EncryptedAccessSecret;
            profile.AccessSecretSuffix = snapshot.AccessSecretSuffix;
            profile.RelaySigningPublicKey = snapshot.RelaySigningPublicKey;
            profile.RelaySigningKeyFingerprint = snapshot.RelaySigningKeyFingerprint;
        }

        private static void CopyProfileToLegacy(
            RemoteServiceProfileSnapshot profile,
            RemoteServiceConfigSnapshot snapshot)
        {
            snapshot.RelayHost = profile.RelayHost;
            snapshot.RelayPort = profile.RelayPort;
            snapshot.UseTls = profile.UseTls;
            snapshot.TenantId = profile.TenantId;
            snapshot.EncryptedAccessSecret = profile.EncryptedAccessSecret;
            snapshot.AccessSecretSuffix = profile.AccessSecretSuffix;
            snapshot.RelaySigningPublicKey = profile.RelaySigningPublicKey;
            snapshot.RelaySigningKeyFingerprint = profile.RelaySigningKeyFingerprint;
        }

        private static bool IsEmbeddedBeforeStart(RemoteServiceConfigSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            if (!IsEmbeddedMode(snapshot.RemoteServiceMode))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(snapshot.RelayHost)
                && string.IsNullOrWhiteSpace(snapshot.TenantId)
                && string.IsNullOrWhiteSpace(snapshot.EncryptedAccessSecret);
        }
    }
}
