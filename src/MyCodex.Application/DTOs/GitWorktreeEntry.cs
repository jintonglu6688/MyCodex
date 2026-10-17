namespace MyCodex.Application.DTOs
{
    public sealed class GitWorktreeEntry
    {
        public string Path { get; set; }

        public string Head { get; set; }

        public string Branch { get; set; }

        public bool IsBare { get; set; }

        public bool IsDetached { get; set; }

        public bool IsPrunable { get; set; }

        public string PruneReason { get; set; }
    }
}
