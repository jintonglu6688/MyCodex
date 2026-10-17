using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class GitRepositoryStatusSnapshot
    {
        public GitRepositoryStatusSnapshot()
        {
            Files = new List<GitChangedFileEntry>();
        }

        public string WorkspacePath { get; set; }

        public bool IsGitRepository { get; set; }

        public bool CanInitializeRepository { get; set; }

        public string StatusErrorMessage { get; set; }

        public string Branch { get; set; }

        public string HeadSha { get; set; }

        public string OriginUrl { get; set; }

        public string Upstream { get; set; }

        public int? AheadCount { get; set; }

        public int? BehindCount { get; set; }

        public bool HasConflicts { get; set; }

        public bool IsDetachedHead { get; set; }

        public bool IsCherryPickInProgress { get; set; }

        public bool IsRevertInProgress { get; set; }

        public bool IsMergeInProgress { get; set; }

        public bool IsRebaseInProgress { get; set; }

        public bool HasSequencerOperation
        {
            get { return IsCherryPickInProgress || IsRevertInProgress; }
        }

        public bool HasMergeOrRebaseOperation
        {
            get { return IsMergeInProgress || IsRebaseInProgress; }
        }

        public int StashCount { get; set; }

        public int StagedCount { get; set; }

        public int UnstagedCount { get; set; }

        public int UntrackedCount { get; set; }

        public int ConflictedCount { get; set; }

        public List<GitChangedFileEntry> Files { get; private set; }
    }
}
