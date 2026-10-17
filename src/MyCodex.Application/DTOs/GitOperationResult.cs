namespace MyCodex.Application.DTOs
{
    public sealed class GitOperationResult
    {
        public bool Success { get; set; }

        public int ExitCode { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }

        public string DisplayMessage { get; set; }
    }
}
