namespace MyCodex.Application.DTOs
{
    public sealed class GitCloneRequest
    {
        public string RepositoryUrl { get; set; }

        public string TargetParentDirectory { get; set; }

        public string TargetFolderName { get; set; }

        public bool RecurseSubmodules { get; set; }
    }
}
