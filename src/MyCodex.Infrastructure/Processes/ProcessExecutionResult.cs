namespace MyCodex.Infrastructure.Processes
{
    public sealed class ProcessExecutionResult
    {
        public int ExitCode { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }
    }
}
