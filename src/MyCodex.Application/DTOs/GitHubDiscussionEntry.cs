using System;
using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class GitHubDiscussionEntry
    {
        public GitHubDiscussionEntry()
        {
            Comments = new List<GitHubDiscussionCommentEntry>();
        }

        public int Number { get; set; }

        public string Title { get; set; }

        public string State { get; set; }

        public string Author { get; set; }

        public string Body { get; set; }

        public DateTimeOffset? CreatedAt { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public string Url { get; set; }

        public List<GitHubDiscussionCommentEntry> Comments { get; private set; }
    }
}
