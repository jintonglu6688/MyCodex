namespace MyCodex.Application.DTOs
{
    public sealed class GitSshTestResult
    {
        public bool Succeeded { get; set; }

        public int ExitCode { get; set; }

        public string Output { get; set; }

        public string Details { get; set; }
    }
}
