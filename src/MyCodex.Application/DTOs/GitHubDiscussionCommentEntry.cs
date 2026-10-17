using System;

namespace MyCodex.Application.DTOs
{
    public sealed class GitHubDiscussionCommentEntry
    {
        public string Author { get; set; }

        public string Body { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public string Url { get; set; }
    }
}
