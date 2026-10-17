namespace MyCodex.Infrastructure.Updates
{
    public sealed class UpdateAssetReference
    {
        public string Url { get; set; }

        public string FileName { get; set; }

        public string Sha256 { get; set; }

        public long? Size { get; set; }
    }
}
