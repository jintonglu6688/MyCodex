using System;

namespace MyCodex.Application.DTOs
{
    public sealed class RuntimeFileSystemEntrySnapshot
    {
        public string Name { get; set; }

        public string FullPath { get; set; }

        public bool IsDirectory { get; set; }

        public bool IsHidden { get; set; }

        public long? Size { get; set; }

        public DateTime? ModifiedAt { get; set; }
    }
}
