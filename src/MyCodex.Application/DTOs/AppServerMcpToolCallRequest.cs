using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerMcpToolCallRequest
    {
        public string ThreadId { get; set; }

        public string Server { get; set; }

        public string Tool { get; set; }

        public IDictionary<string, object> Arguments { get; set; }

        public IDictionary<string, object> Meta { get; set; }
    }
}
