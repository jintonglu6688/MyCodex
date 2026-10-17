namespace MyCodex.Application.DTOs
{
    public sealed class GitHubCreateRepositoryResult
    {
        public bool Success { get; set; }

        public int ExitCode { get; set; }

        public GitHubRepositoryEntry Repository { get; set; }

        public string Output { get; set; }
    }
}
