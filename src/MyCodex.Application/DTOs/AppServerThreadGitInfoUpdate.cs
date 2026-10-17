namespace MyCodex.Application.DTOs
{
    public sealed class AppServerThreadGitInfoUpdate
    {
        public bool HasOriginUrl { get; set; }

        public string OriginUrl { get; set; }

        public bool HasBranch { get; set; }

        public string Branch { get; set; }

        public bool HasSha { get; set; }

        public string Sha { get; set; }

        public bool HasAnyUpdates
        {
            get
            {
                return HasOriginUrl || HasBranch || HasSha;
            }
        }
    }
}
