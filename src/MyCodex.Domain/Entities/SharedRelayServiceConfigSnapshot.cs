using System;
using System.Runtime.Serialization;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public sealed class SharedRelayServiceConfigSnapshot
    {
        [DataMember(Order = 1)]
        public string RelayHost { get; set; }

        [DataMember(Order = 2)]
        public int RelayPort { get; set; }

        [DataMember(Order = 3)]
        public bool UseTls { get; set; }

        [DataMember(Order = 4)]
        public string TenantId { get; set; }

        [DataMember(Order = 5)]
        public string EncryptedAccessSecret { get; set; }

        [DataMember(Order = 6)]
        public string AccessSecretSuffix { get; set; }

        [DataMember(Order = 7)]
        public string RemoteServiceMode { get; set; }

        [DataMember(Order = 8)]
        public bool EmbeddedRelayEnabled { get; set; }

        [DataMember(Order = 9)]
        public string EmbeddedRelayConfigPath { get; set; }

        [DataMember(Order = 10)]
        public string EmbeddedRelayStatePath { get; set; }

        [DataMember(Order = 11)]
        public int EmbeddedRelayProcessId { get; set; }

        [DataMember(Order = 12)]
        public DateTime? EmbeddedRelayLastStartedAt { get; set; }

        [DataMember(Order = 13)]
        public string EmbeddedRelayProcessExecutablePath { get; set; }

        [DataMember(Order = 14)]
        public long EmbeddedRelayProcessStartTimeUtcTicks { get; set; }

        [DataMember(Order = 15)]
        public string EmbeddedRelayCertificateSha256 { get; set; }

        [DataMember(Order = 16)]
        public string EmbeddedRelayInternalHost { get; set; }

        [DataMember(Order = 17)]
        public int EmbeddedRelayInternalPort { get; set; }

        [DataMember(Order = 18)]
        public bool EmbeddedRelayInternalUseTls { get; set; }

        [DataMember(Order = 19)]
        public string RelaySigningPublicKey { get; set; }

        [DataMember(Order = 20)]
        public string RelaySigningKeyFingerprint { get; set; }

        [DataMember(Order = 21)]
        public string EmbeddedRelayPublicHostOverride { get; set; }

        [DataMember(Order = 22)]
        public int EmbeddedRelayPublicPortOverride { get; set; }

        [DataMember(Order = 23)]
        public int SchemaVersion { get; set; }

        [DataMember(Order = 24)]
        public bool StartEmbeddedRelayWithApplication { get; set; }

        [DataMember(Order = 25)]
        public RemoteServiceProfileSnapshot EmbeddedProfile { get; set; }

        [DataMember(Order = 26)]
        public RemoteServiceProfileSnapshot ExternalProfile { get; set; }
    }

    [DataContract]
    public sealed class TargetRemoteIdentitySnapshot
    {
        [DataMember(Order = 1)]
        public string HostId { get; set; }

        [DataMember(Order = 2)]
        public string ThisPcName { get; set; }

        [DataMember(Order = 3)]
        public bool RemoteEnabled { get; set; }

        [DataMember(Order = 4)]
        public DateTime CreatedAt { get; set; }

        [DataMember(Order = 5)]
        public DateTime UpdatedAt { get; set; }

        [DataMember(Order = 6)]
        public DateTime? LastRegisteredAt { get; set; }
    }
}
