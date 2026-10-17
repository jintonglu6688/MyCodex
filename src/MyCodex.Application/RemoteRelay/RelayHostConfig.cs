namespace MyCodex.Application.RemoteRelay
{
    public sealed class RelayHostConfig
    {
        public string HostDisplayName { get; set; }

        public string RelayHost { get; set; }

        public string ConnectionHost { get; set; }

        public int ConnectionPort { get; set; }

        public bool ConnectionTlsRequired { get; set; }

        public int RelayPort { get; set; }

        public bool TlsRequired { get; set; }

        public string CertificateSha256 { get; set; }

        public string RelaySigningPublicKey { get; set; }

        public string RelaySigningKeyFingerprint { get; set; }

        public string TenantId { get; set; }

        public string TenantSecret { get; set; }
    }
}

