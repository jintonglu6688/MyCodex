using System;
using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class GitHubReleaseDetailEntry
    {
        public GitHubReleaseDetailEntry()
        {
            Assets = new List<GitHubReleaseAssetEntry>();
        }

        public string TagName { get; set; }

        public string Name { get; set; }

        public bool IsDraft { get; set; }

        public bool IsPrerelease { get; set; }

        public string Author { get; set; }

        public string Body { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public string Url { get; set; }

        public List<GitHubReleaseAssetEntry> Assets { get; private set; }
    }
}
