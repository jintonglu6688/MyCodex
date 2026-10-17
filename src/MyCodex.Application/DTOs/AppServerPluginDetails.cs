using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerPluginDetails
    {
        public string MarketplaceName { get; set; }

        public string MarketplacePath { get; set; }

        public AppServerPluginSummary Summary { get; set; }

        public string Description { get; set; }

        public List<AppServerPluginSkillInfo> Skills { get; set; }

        public List<AppServerPluginAppInfo> Apps { get; set; }

        public List<string> McpServers { get; set; }
    }
}
