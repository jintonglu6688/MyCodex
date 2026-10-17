namespace MyCodex.Application.DTOs
{
    public sealed class AppServerGitDiffToRemoteResult
    {
        public string Sha { get; set; }

        public string Diff { get; set; }
    }
}
