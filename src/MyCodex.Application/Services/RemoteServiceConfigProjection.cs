using System;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public static class RemoteServiceConfigProjection
    {
        public static SharedRelayServiceConfigSnapshot SplitShared(RemoteServiceConfigSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new SharedRelayServiceConfigSnapshot
            {
                RelayHost = snapshot.RelayHost,
                RelayPort = snapshot.RelayPort,
                UseTls = snapshot.UseTls,
                TenantId = snapshot.TenantId,
                EncryptedAccessSecret = snapshot.EncryptedAccessSecret,
                AccessSecretSuffix = snapshot.AccessSecretSuffix,
                RemoteServiceMode = snapshot.RemoteServiceMode,
                EmbeddedRelayEnabled = snapshot.EmbeddedRelayEnabled,
                EmbeddedRelayConfigPath = snapshot.EmbeddedRelayConfigPath,
                EmbeddedRelayStatePath = snapshot.EmbeddedRelayStatePath,
                EmbeddedRelayProcessId = snapshot.EmbeddedRelayProcessId,
                EmbeddedRelayLastStartedAt = snapshot.EmbeddedRelayLastStartedAt,
                EmbeddedRelayProcessExecutablePath = snapshot.EmbeddedRelayProcessExecutablePath,
                EmbeddedRelayProcessStartTimeUtcTicks = snapshot.EmbeddedRelayProcessStartTimeUtcTicks,
                EmbeddedRelayCertificateSha256 = snapshot.EmbeddedRelayCertificateSha256,
                EmbeddedRelayInternalHost = snapshot.EmbeddedRelayInternalHost,
                EmbeddedRelayInternalPort = snapshot.EmbeddedRelayInternalPort,
                EmbeddedRelayInternalUseTls = snapshot.EmbeddedRelayInternalUseTls,
                RelaySigningPublicKey = snapshot.RelaySigningPublicKey,
                RelaySigningKeyFingerprint = snapshot.RelaySigningKeyFingerprint,
                EmbeddedRelayPublicHostOverride = snapshot.EmbeddedRelayPublicHostOverride,
                EmbeddedRelayPublicPortOverride = snapshot.EmbeddedRelayPublicPortOverride,
                SchemaVersion = snapshot.SchemaVersion,
                StartEmbeddedRelayWithApplication = snapshot.StartEmbeddedRelayWithApplication,
                EmbeddedProfile = CloneProfile(snapshot.EmbeddedProfile),
                ExternalProfile = CloneProfile(snapshot.ExternalProfile)
            };
        }

        public static TargetRemoteIdentitySnapshot SplitTargetIdentity(RemoteServiceConfigSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new TargetRemoteIdentitySnapshot
            {
                HostId = snapshot.HostId,
                ThisPcName = snapshot.ThisPcName,
                RemoteEnabled = snapshot.RemoteEnabled ?? true,
                CreatedAt = snapshot.CreatedAt,
                UpdatedAt = snapshot.UpdatedAt,
                LastRegisteredAt = snapshot.LastRegisteredAt
            };
        }

        public static RemoteServiceConfigSnapshot Compose(
            SharedRelayServiceConfigSnapshot shared,
            TargetRemoteIdentitySnapshot identity)
        {
            var snapshot = new RemoteServiceConfigSnapshot();
            Apply(shared, identity, snapshot);
            return snapshot;
        }

        public static void Apply(
            SharedRelayServiceConfigSnapshot shared,
            TargetRemoteIdentitySnapshot identity,
            RemoteServiceConfigSnapshot snapshot)
        {
            if (shared == null)
            {
                throw new ArgumentNullException(nameof(shared));
            }

            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            snapshot.RelayHost = shared.RelayHost;
            snapshot.RelayPort = shared.RelayPort;
            snapshot.UseTls = shared.UseTls;
            snapshot.TenantId = shared.TenantId;
            snapshot.EncryptedAccessSecret = shared.EncryptedAccessSecret;
            snapshot.AccessSecretSuffix = shared.AccessSecretSuffix;
            snapshot.RemoteServiceMode = shared.RemoteServiceMode;
            snapshot.EmbeddedRelayEnabled = shared.EmbeddedRelayEnabled;
            snapshot.EmbeddedRelayConfigPath = shared.EmbeddedRelayConfigPath;
            snapshot.EmbeddedRelayStatePath = shared.EmbeddedRelayStatePath;
            snapshot.EmbeddedRelayProcessId = shared.EmbeddedRelayProcessId;
            snapshot.EmbeddedRelayLastStartedAt = shared.EmbeddedRelayLastStartedAt;
            snapshot.EmbeddedRelayProcessExecutablePath = shared.EmbeddedRelayProcessExecutablePath;
            snapshot.EmbeddedRelayProcessStartTimeUtcTicks = shared.EmbeddedRelayProcessStartTimeUtcTicks;
            snapshot.EmbeddedRelayCertificateSha256 = shared.EmbeddedRelayCertificateSha256;
            snapshot.EmbeddedRelayInternalHost = shared.EmbeddedRelayInternalHost;
            snapshot.EmbeddedRelayInternalPort = shared.EmbeddedRelayInternalPort;
            snapshot.EmbeddedRelayInternalUseTls = shared.EmbeddedRelayInternalUseTls;
            snapshot.RelaySigningPublicKey = shared.RelaySigningPublicKey;
            snapshot.RelaySigningKeyFingerprint = shared.RelaySigningKeyFingerprint;
            snapshot.EmbeddedRelayPublicHostOverride = shared.EmbeddedRelayPublicHostOverride;
            snapshot.EmbeddedRelayPublicPortOverride = shared.EmbeddedRelayPublicPortOverride;
            snapshot.SchemaVersion = shared.SchemaVersion;
            snapshot.StartEmbeddedRelayWithApplication = shared.StartEmbeddedRelayWithApplication;
            snapshot.EmbeddedProfile = CloneProfile(shared.EmbeddedProfile);
            snapshot.ExternalProfile = CloneProfile(shared.ExternalProfile);
            snapshot.HostId = identity.HostId;
            snapshot.ThisPcName = identity.ThisPcName;
            snapshot.RemoteEnabled = identity.RemoteEnabled;
            snapshot.CreatedAt = identity.CreatedAt;
            snapshot.UpdatedAt = identity.UpdatedAt;
            snapshot.LastRegisteredAt = identity.LastRegisteredAt;
        }

        private static RemoteServiceProfileSnapshot CloneProfile(RemoteServiceProfileSnapshot profile)
        {
            return profile == null ? null : new RemoteServiceProfileSnapshot
            {
                RelayHost = profile.RelayHost,
                RelayPort = profile.RelayPort,
                UseTls = profile.UseTls,
                TenantId = profile.TenantId,
                EncryptedAccessSecret = profile.EncryptedAccessSecret,
                AccessSecretSuffix = profile.AccessSecretSuffix,
                RelaySigningPublicKey = profile.RelaySigningPublicKey,
                RelaySigningKeyFingerprint = profile.RelaySigningKeyFingerprint
            };
        }
    }
}
