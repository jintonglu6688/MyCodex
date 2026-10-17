namespace MyCodex.Application.DTOs
{
    public sealed class TextFileOpenRequest
    {
        public string FilePath { get; set; }

        public bool IsLocalWindowsFile { get; set; }

        public int? Line { get; set; }

        public int? Column { get; set; }

        public string SourceDescription { get; set; }
    }
}
