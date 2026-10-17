namespace MyCodex.Application.DTOs
{
    public sealed class AppServerMcpResourceContent
    {
        public string Uri { get; set; }

        public string MimeType { get; set; }

        public string Text { get; set; }

        public string Blob { get; set; }

        public string MetaSummary { get; set; }

        public bool IsBlob
        {
            get { return !string.IsNullOrEmpty(Blob); }
        }
    }
}
