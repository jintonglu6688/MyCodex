using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class GitDiffSnapshot
    {
        public GitDiffSnapshot()
        {
            Files = new List<GitDiffFileEntry>();
        }

        public string WorkspacePath { get; set; }

        public bool IsGitRepository { get; set; }

        public List<GitDiffFileEntry> Files { get; }
    }
}
