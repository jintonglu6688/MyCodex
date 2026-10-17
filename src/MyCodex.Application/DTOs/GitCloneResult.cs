namespace MyCodex.Application.DTOs
{
    public sealed class GitCloneResult
    {
        public bool Success { get; set; }

        public int ExitCode { get; set; }

        public string TargetPath { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }
    }
}
