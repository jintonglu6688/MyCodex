namespace MyCodex.Application.DTOs
{
    public sealed class TextFileExternalEditorCommand
    {
        public string ExecutablePath { get; set; }

        public string Arguments { get; set; }

        public bool UseShellExecute { get; set; }
    }
}
