namespace MyCodex.Application.DTOs
{
    public sealed class GitCommitFileEntry
    {
        public string FilePath { get; set; }

        public string OriginalFilePath { get; set; }

        public string ChangeKind { get; set; }
    }
}
