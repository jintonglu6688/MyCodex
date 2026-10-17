using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerMcpToolCallResult
    {
        public AppServerMcpToolCallResult()
        {
            ContentItems = new List<string>();
        }

        public List<string> ContentItems { get; private set; }

        public string StructuredContentJson { get; set; }

        public string MetaJson { get; set; }

        public bool IsError { get; set; }

        public string DisplayText { get; set; }
    }
}
