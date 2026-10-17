using System;

namespace MyCodex.Application.RemoteRelay
{
    public sealed class RelayDeviceSummary
    {
        public string TenantId { get; set; }

        public string HostId { get; set; }

        public string DeviceId { get; set; }

        public string DeviceSigningPublicKey { get; set; }

        public string DeviceAgreementPublicKey { get; set; }

        public int DeviceKeyVersion { get; set; }

        public int BindingVersion { get; set; }

        public bool Revoked { get; set; }

        public string DisplayName { get; set; }

        public string Platform { get; set; }

        public DateTime BoundAtUtc { get; set; }

        public long ApprovedAt { get; set; }

        public long? LastSeenAt { get; set; }

        public bool Online { get; set; }
    }
}
