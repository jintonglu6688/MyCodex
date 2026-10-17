namespace MyCodex.Application.DTOs
{
    public sealed class AppServerFileSystemEntry
    {
        public string FileName { get; set; }

        public bool IsDirectory { get; set; }

        public bool IsFile { get; set; }
    }
}
