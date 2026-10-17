namespace MyCodex.Application.DTOs
{
    public sealed class GitHubRepositorySearchRequest
    {
        public string Keyword { get; set; }

        public string Language { get; set; }

        public string Topic { get; set; }

        public string License { get; set; }

        public int? MinStars { get; set; }

        public int? MaxStars { get; set; }

        public int? MinForks { get; set; }

        public int? MaxForks { get; set; }

        public int? MinOpenIssues { get; set; }

        public int? MaxOpenIssues { get; set; }

        public bool ExcludeForks { get; set; }

        public bool ExcludeArchived { get; set; }

        public bool PublicOnly { get; set; }

        public string Sort { get; set; }

        public string Order { get; set; }

        public int Limit { get; set; }
    }
}
