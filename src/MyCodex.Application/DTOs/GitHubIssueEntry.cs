using System;

namespace MyCodex.Application.DTOs
{
    public sealed class GitHubIssueEntry
    {
        public int Number { get; set; }

        public string Title { get; set; }

        public string State { get; set; }

        public string Author { get; set; }

        public int Comments { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public string Url { get; set; }
    }
}
