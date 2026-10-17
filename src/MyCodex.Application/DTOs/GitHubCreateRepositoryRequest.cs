namespace MyCodex.Application.DTOs
{
    public sealed class GitHubCreateRepositoryRequest
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public bool IsPrivate { get; set; }

        public bool AddReadme { get; set; }

        public string SourceDirectory { get; set; }

        public string RemoteName { get; set; }
    }
}
