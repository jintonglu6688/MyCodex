using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerMcpResourceReadResult
    {
        public AppServerMcpResourceReadResult()
        {
            Contents = new List<AppServerMcpResourceContent>();
        }

        public List<AppServerMcpResourceContent> Contents { get; private set; }
    }
}
