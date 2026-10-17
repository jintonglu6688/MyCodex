using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerFuzzyFileSearchResult
    {
        public AppServerFuzzyFileSearchResult()
        {
            Indices = new List<uint>();
        }

        public string FileName { get; set; }

        public string MatchType { get; set; }

        public string Path { get; set; }

        public string Root { get; set; }

        public uint Score { get; set; }

        public List<uint> Indices { get; set; }
    }
}
