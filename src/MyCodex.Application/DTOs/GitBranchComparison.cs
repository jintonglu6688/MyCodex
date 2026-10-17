namespace MyCodex.Application.DTOs
{
    public sealed class GitBranchComparison
    {
        public string CurrentRef { get; set; }

        public string TargetRef { get; set; }

        public int CurrentOnlyCount { get; set; }

        public int TargetOnlyCount { get; set; }
    }
}
