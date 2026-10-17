namespace MyCodex.Application.RemoteRelay
{
    public sealed class RelayPairingInvite
    {
        public string TenantId { get; set; }

        public string HostId { get; set; }

        public string InviteId { get; set; }

        public string Status { get; set; }

        public long CreatedAt { get; set; }

        public long ExpiresAt { get; set; }
    }
}

