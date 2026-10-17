namespace MyCodex.Application.DTOs
{
    public sealed class GitCommitHistoryEntry
    {
        public string FullSha { get; set; }

        public string ShortSha { get; set; }

        public string AuthorName { get; set; }

        public string AuthorDateText { get; set; }

        public string Subject { get; set; }
    }
}
