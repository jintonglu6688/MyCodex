using System.Collections.Generic;

namespace MyCodex.Application.RemoteRelay
{
    public enum EmbeddedRelayRunState
    {
        NotConfigured,
        Stopped,
        Starting,
        Running,
        Failed
    }

    public sealed class EmbeddedRelayStatus
    {
        public EmbeddedRelayRunState State { get; set; }

        public string Message { get; set; }

        public string RelayUrl { get; set; }

        public string HealthUrl { get; set; }

        public string RelayHost { get; set; }

        public int RelayPort { get; set; }

        public bool TlsRequired { get; set; }

        public string CertificateSha256 { get; set; }

        public string InternalHost { get; set; }

        public int InternalPort { get; set; }

        public bool InternalTlsRequired { get; set; }

        public string TenantId { get; set; }

        public int ProcessId { get; set; }

        public string ConfigPath { get; set; }

        public string StatePath { get; set; }

        public string StdoutLogPath { get; set; }

        public string StderrLogPath { get; set; }
    }

    public sealed class EmbeddedRelayStartResult
    {
        public bool Success { get; set; }

        public EmbeddedRelayStatus Status { get; set; }

        public string TenantSecret { get; set; }

        public string ErrorMessage { get; set; }
    }

    public sealed class EmbeddedRelayDiagnosticInfo
    {
        public EmbeddedRelayStatus Status { get; set; }

        public IReadOnlyList<string> Lines { get; set; }
    }

    public sealed class EmbeddedRelayLocalInfo
    {
        public string Version { get; set; }

        public string ConfigPath { get; set; }

        public string StatePath { get; set; }

        public string ListenHost { get; set; }

        public int ListenPort { get; set; }

        public string InternalListenHost { get; set; }

        public int InternalListenPort { get; set; }

        public bool InternalTlsRequired { get; set; }

        public string InternalRelayUrl { get; set; }

        public string InternalHealthUrl { get; set; }

        public string PublicHost { get; set; }

        public int PublicPort { get; set; }

        public bool TlsRequired { get; set; }

        public string CertificatePath { get; set; }

        public string CertificateSha256 { get; set; }

        public string RelayHost { get; set; }

        public int RelayPort { get; set; }

        public string RelayUrl { get; set; }

        public string HealthUrl { get; set; }

        public EmbeddedRelayTenant[] Tenants { get; set; }

        public EmbeddedRelaySecret Secret { get; set; }
    }

    public sealed class EmbeddedRelayTenant
    {
        public string TenantId { get; set; }

        public string DisplayName { get; set; }

        public bool Enabled { get; set; }
    }

    public sealed class EmbeddedRelaySecret
    {
        public string TenantId { get; set; }

        public string Value { get; set; }

        public string Source { get; set; }
    }
}
