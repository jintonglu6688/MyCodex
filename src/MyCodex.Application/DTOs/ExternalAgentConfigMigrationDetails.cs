using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class ExternalAgentConfigMigrationDetails
    {
        public ExternalAgentConfigMigrationDetails()
        {
            Plugins = new List<ExternalAgentConfigPluginMigration>();
            Sessions = new List<ExternalAgentConfigSessionMigration>();
            McpServers = new List<ExternalAgentConfigNamedMigration>();
            Hooks = new List<ExternalAgentConfigNamedMigration>();
            Subagents = new List<ExternalAgentConfigNamedMigration>();
            Commands = new List<ExternalAgentConfigNamedMigration>();
        }

        public List<ExternalAgentConfigPluginMigration> Plugins { get; set; }

        public List<ExternalAgentConfigSessionMigration> Sessions { get; set; }

        public List<ExternalAgentConfigNamedMigration> McpServers { get; set; }

        public List<ExternalAgentConfigNamedMigration> Hooks { get; set; }

        public List<ExternalAgentConfigNamedMigration> Subagents { get; set; }

        public List<ExternalAgentConfigNamedMigration> Commands { get; set; }
    }
}
