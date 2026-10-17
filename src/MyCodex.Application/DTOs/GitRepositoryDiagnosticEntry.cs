namespace MyCodex.Application.DTOs
{
    public sealed class GitRepositoryDiagnosticEntry
    {
        public string Severity { get; set; }

        public string Title { get; set; }

        public string Detail { get; set; }

        public string Suggestion { get; set; }
    }
}
