namespace MyCodex.Application.DTOs
{
    public sealed class AppServerMcpResourceSummary
    {
        public string Uri { get; set; }

        public string Name { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public string MimeType { get; set; }

        public bool IsTemplate { get; set; }
    }
}
