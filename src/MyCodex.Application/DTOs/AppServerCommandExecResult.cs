namespace MyCodex.Application.DTOs
{
    public class AppServerCommandExecResult
    {
        public int ExitCode { get; set; }

        public string StdOut { get; set; }

        public string StdErr { get; set; }
    }
}
