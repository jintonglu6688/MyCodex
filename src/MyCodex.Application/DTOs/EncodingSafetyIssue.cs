namespace MyCodex.Application.DTOs
{
    public sealed class EncodingSafetyIssue
    {
        public string FilePath { get; set; }

        public int? LineNumber { get; set; }

        public string Reason { get; set; }

        public string Preview { get; set; }
    }
}
