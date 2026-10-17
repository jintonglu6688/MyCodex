using System;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public static class RemoteServiceConfigurationComparer
    {
        public static bool ConnectionChanged(RemoteServiceConfigSnapshot previous, RemoteServiceConfigSnapshot current)
        {
            if (previous == null || current == null) return true;
            return (previous.RemoteEnabled != false) != (current.RemoteEnabled != false) ||
                !Equal(previous.RemoteServiceMode, current.RemoteServiceMode) ||
                !Equal(previous.HostId, current.HostId) || !Equal(previous.ThisPcName, current.ThisPcName) ||
                !Equal(previous.RelayHost, current.RelayHost) || previous.RelayPort != current.RelayPort ||
                previous.UseTls != current.UseTls || !Equal(previous.TenantId, current.TenantId) ||
                !Equal(previous.EncryptedAccessSecret, current.EncryptedAccessSecret) ||
                !Equal(previous.RelaySigningPublicKey, current.RelaySigningPublicKey) ||
                !Equal(previous.RelaySigningKeyFingerprint, current.RelaySigningKeyFingerprint) ||
                !Equal(previous.EmbeddedRelayInternalHost, current.EmbeddedRelayInternalHost) ||
                previous.EmbeddedRelayInternalPort != current.EmbeddedRelayInternalPort ||
                previous.EmbeddedRelayInternalUseTls != current.EmbeddedRelayInternalUseTls ||
                !Equal(previous.EmbeddedRelayCertificateSha256, current.EmbeddedRelayCertificateSha256) ||
                !Equal(previous.EmbeddedRelayPublicHostOverride, current.EmbeddedRelayPublicHostOverride) ||
                previous.EmbeddedRelayPublicPortOverride != current.EmbeddedRelayPublicPortOverride;
        }

        private static bool Equal(string left, string right)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }
}
