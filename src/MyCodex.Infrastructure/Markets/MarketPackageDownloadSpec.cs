namespace MyCodex.Infrastructure.Markets
{
    internal sealed class MarketPackageDownloadSpec
    {
        public string PackageUrl { get; set; }

        public string FallbackId { get; set; }

        public long SizeBytes { get; set; }

        public string Sha256 { get; set; }

        public string ResourceKeyPrefix { get; set; }

        public string PackageKind { get; set; }
    }
}
