namespace MyCodex.Application.DTOs
{
    public sealed class GitHubBranchEntry
    {
        public string Name { get; set; }

        public string CommitSha { get; set; }

        public bool IsProtected { get; set; }
    }
}
