namespace MyCodex.Application.DTOs
{
    public sealed class GitBranchEntry
    {
        public string Name { get; set; }

        public bool IsCurrent { get; set; }

        public bool IsRemote { get; set; }

        public string Upstream { get; set; }

        public string TrackingStatus { get; set; }

        public int? AheadCount { get; set; }

        public int? BehindCount { get; set; }
    }
}
