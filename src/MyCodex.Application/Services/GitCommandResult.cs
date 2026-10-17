namespace MyCodex.Application.Services
{
    public sealed class GitCommandResult
    {
        public int ExitCode { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }

        public bool Success
        {
            get { return ExitCode == 0; }
        }
    }
}
