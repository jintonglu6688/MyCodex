namespace MyCodex.Application.DTOs
{
    public sealed class AppServerHookInfo
    {
        public string Key { get; set; }

        public string EventName { get; set; }

        public string HandlerType { get; set; }

        public string Matcher { get; set; }

        public string Source { get; set; }

        public string SourcePath { get; set; }

        public string PluginId { get; set; }

        public string Command { get; set; }

        public string McpServer { get; set; }

        public string McpTool { get; set; }

        public long? TimeoutSec { get; set; }

        public bool Enabled { get; set; }

        public bool IsManaged { get; set; }

        public string CurrentHash { get; set; }

        public string TrustStatus { get; set; }
    }
}
