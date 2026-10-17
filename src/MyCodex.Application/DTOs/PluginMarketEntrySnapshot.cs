namespace MyCodex.Application.DTOs
{
    public class PluginMarketEntrySnapshot
    {
        public string MarketId { get; set; }

        public string PluginId { get; set; }

        public string DisplayName { get; set; }

        public string Version { get; set; }

        public string Category { get; set; }

        public string Summary { get; set; }

        public string PackageUrl { get; set; }

        public string ManifestUrl { get; set; }

        public string DetailsUrl { get; set; }

        public string AuthPolicy { get; set; }

        public string Sha256 { get; set; }

        public long SizeBytes { get; set; }
    }
}
