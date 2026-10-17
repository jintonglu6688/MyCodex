using System;

namespace MyCodex.Application.DTOs
{
    public sealed class GitHubRepositoryEntry
    {
        public string NameWithOwner { get; set; }

        public string Description { get; set; }

        public string Visibility { get; set; }

        public bool IsPrivate { get; set; }

        public string DefaultBranch { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public DateTimeOffset? PushedAt { get; set; }

        public long? DiskUsage { get; set; }

        public int? Stars { get; set; }

        public int? Forks { get; set; }

        public int? Watchers { get; set; }

        public int? OpenIssues { get; set; }

        public string HomepageUrl { get; set; }

        public string License { get; set; }

        public string PrimaryLanguage { get; set; }

        public bool IsArchived { get; set; }

        public bool IsFork { get; set; }

        public string Topics { get; set; }

        public string HttpsUrl { get; set; }

        public string SshUrl { get; set; }

        public override string ToString()
        {
            return NameWithOwner ?? string.Empty;
        }
    }
}
