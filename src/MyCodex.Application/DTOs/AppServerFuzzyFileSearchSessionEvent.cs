using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerFuzzyFileSearchSessionEvent
    {
        public AppServerFuzzyFileSearchSessionEvent()
        {
            Files = new List<AppServerFuzzyFileSearchResult>();
        }

        public string SessionId { get; set; }

        public string Query { get; set; }

        public List<AppServerFuzzyFileSearchResult> Files { get; set; }

        public bool IsCompleted { get; set; }
    }
}
