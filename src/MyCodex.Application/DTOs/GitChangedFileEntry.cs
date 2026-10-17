namespace MyCodex.Application.DTOs
{
    public sealed class GitChangedFileEntry
    {
        public string FilePath { get; set; }

        public string OriginalFilePath { get; set; }

        public string ChangeKind { get; set; }

        public string StatusGroup { get; set; }

        public bool IsStaged { get; set; }

        public bool IsUnstaged { get; set; }

        public bool IsUntracked { get; set; }

        public bool IsConflicted { get; set; }

        public bool HasStagedChanges { get; set; }

        public bool HasUnstagedChanges { get; set; }

        public bool CanStage { get; set; }

        public bool CanUnstage { get; set; }

        public bool CanDiscard { get; set; }

        public int AddedLineCount { get; set; }

        public int DeletedLineCount { get; set; }

        public bool HasLineStats { get; set; }

        public string DiffText { get; set; }

        public string StagedDiffText { get; set; }

        public string UnstagedDiffText { get; set; }
    }
}
