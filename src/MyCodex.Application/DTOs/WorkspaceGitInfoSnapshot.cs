namespace MyCodex.Application.DTOs
{
    public sealed class WorkspaceGitInfoSnapshot
    {
        public bool IsGitRepository { get; set; }

        public string OriginUrl { get; set; }

        public string Branch { get; set; }

        public string Sha { get; set; }
    }
}
