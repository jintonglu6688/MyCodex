using System;

namespace MyCodex.Application.DTOs
{
    public sealed class GitHubReleaseAssetEntry
    {
        public string Name { get; set; }

        public string Label { get; set; }

        public string ContentType { get; set; }

        public long? Size { get; set; }

        public int DownloadCount { get; set; }

        public string State { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public string Url { get; set; }
    }
}
