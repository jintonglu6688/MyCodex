namespace MyCodex.Application.DTOs
{
    public sealed class AppServerFileSystemMetadata
    {
        public bool IsDirectory { get; set; }

        public bool IsFile { get; set; }

        public bool IsSymlink { get; set; }

        public long CreatedAtMs { get; set; }

        public long ModifiedAtMs { get; set; }
    }
}
