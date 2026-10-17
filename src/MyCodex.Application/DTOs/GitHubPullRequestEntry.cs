using System;

namespace MyCodex.Application.DTOs
{
    public sealed class GitHubPullRequestEntry
    {
        public int Number { get; set; }

        public string Title { get; set; }

        public string State { get; set; }

        public string Author { get; set; }

        public bool IsDraft { get; set; }

        public string ReviewDecision { get; set; }

        public int Comments { get; set; }

        public string BaseBranch { get; set; }

        public string HeadBranch { get; set; }

        public DateTimeOffset? MergedAt { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public string Url { get; set; }
    }
}
