namespace MyCodex.Application.DTOs
{
    public sealed class GitHubTagEntry
    {
        public string Name { get; set; }

        public string CommitSha { get; set; }

        public string ZipballUrl { get; set; }

        public string TarballUrl { get; set; }
    }
}
