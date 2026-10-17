using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AuthenticationMcpSnapshot
    {
        public string Version { get; set; }
        public List<AuthenticationMcpServer> Servers { get; set; } = new List<AuthenticationMcpServer>();
    }

    public sealed class AuthenticationMcpServer
    {
        public string Id { get; set; }
        public bool Enabled { get; set; }
        public Dictionary<string, object> Config { get; set; }
    }
}
