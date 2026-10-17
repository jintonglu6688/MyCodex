using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public static class ExternalAgentConfigImportPolicy
    {
        public static bool CanImport(ExternalAgentConfigMigrationItem item)
        {
            if (item == null)
            {
                return false;
            }

            switch (item.ItemType)
            {
                case ExternalAgentConfigMigrationItemType.AgentsMd:
                case ExternalAgentConfigMigrationItemType.Skills:
                case ExternalAgentConfigMigrationItemType.Plugins:
                case ExternalAgentConfigMigrationItemType.McpServerConfig:
                case ExternalAgentConfigMigrationItemType.Subagents:
                case ExternalAgentConfigMigrationItemType.Hooks:
                case ExternalAgentConfigMigrationItemType.Commands:
                case ExternalAgentConfigMigrationItemType.Sessions:
                    return true;
                default:
                    return false;
            }
        }

        public static List<ExternalAgentConfigMigrationItem> FilterImportable(
            IEnumerable<ExternalAgentConfigMigrationItem> items)
        {
            return items == null
                ? new List<ExternalAgentConfigMigrationItem>()
                : items.Where(CanImport).ToList();
        }
    }
}
