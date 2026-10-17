using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerFileSystemChangedEvent
    {
        public AppServerFileSystemChangedEvent()
        {
            ChangedPaths = new List<string>();
        }

        public string WatchId { get; set; }

        public List<string> ChangedPaths { get; set; }
    }
}
