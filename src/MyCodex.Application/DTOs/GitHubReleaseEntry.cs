using System;

namespace MyCodex.Application.DTOs
{
    public sealed class GitHubReleaseEntry
    {
        public string TagName { get; set; }

        public string Name { get; set; }

        public bool IsDraft { get; set; }

        public bool IsPrerelease { get; set; }

        public bool IsLatest { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }
    }
}
