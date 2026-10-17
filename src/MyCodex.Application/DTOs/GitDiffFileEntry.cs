namespace MyCodex.Application.DTOs
{
    public sealed class GitDiffFileEntry
    {
        public string FilePath { get; set; }

        public string OriginalFilePath { get; set; }

        public string ChangeKind { get; set; }

        public string DiffText { get; set; }

        public int AddedLineCount { get; set; }

        public int DeletedLineCount { get; set; }
    }
}
