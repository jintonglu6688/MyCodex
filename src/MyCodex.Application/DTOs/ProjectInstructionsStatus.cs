namespace MyCodex.Application.DTOs
{
    public sealed class ProjectInstructionsStatus
    {
        public string WorkspacePath { get; set; }

        public string TargetPath { get; set; }

        public bool Exists { get; set; }

        public bool CanSave { get; set; }

        public string ExistingContent { get; set; }

        public string UnsupportedReason { get; set; }
    }
}
