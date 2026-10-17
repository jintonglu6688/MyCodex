using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class ExternalAgentConfigPluginMigration
    {
        public ExternalAgentConfigPluginMigration()
        {
            PluginNames = new List<string>();
        }

        public string MarketplaceName { get; set; }

        public List<string> PluginNames { get; set; }
    }

    public sealed class ExternalAgentConfigSessionMigration
    {
        public string Path { get; set; }

        public string Cwd { get; set; }

        public string Title { get; set; }
    }

    public sealed class ExternalAgentConfigNamedMigration
    {
        public string Name { get; set; }
    }
}
