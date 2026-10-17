namespace MyCodex.Application.DTOs
{
    public sealed class GitHubContributorEntry
    {
        public string Login { get; set; }

        public string Type { get; set; }

        public int Contributions { get; set; }

        public string Url { get; set; }
    }
}
