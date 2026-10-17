using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class GitHubRepositorySearchResult
    {
        public long? TotalCount { get; set; }

        public bool IncompleteResults { get; set; }

        public IReadOnlyList<GitHubRepositoryEntry> Items { get; set; }
    }
}

