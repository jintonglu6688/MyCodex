using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerFuzzyFileSearchRequest
    {
        public AppServerFuzzyFileSearchRequest()
        {
            Roots = new List<string>();
        }

        public string Query { get; set; }

        public List<string> Roots { get; set; }

        public string CancellationToken { get; set; }
    }
}
