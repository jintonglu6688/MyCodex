namespace MyCodex.Application.DTOs
{
    public sealed class TextFileOpenPreference
    {
        public string Mode { get; set; }

        public string ExternalEditorPath { get; set; }

        public string ExternalEditorArguments { get; set; }
    }
}
