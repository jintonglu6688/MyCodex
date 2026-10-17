using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class ExternalAgentConfigMigrationItem
    {
        public ExternalAgentConfigMigrationItem()
        {
            Details = new ExternalAgentConfigMigrationDetails();
            RawItem = new Dictionary<string, object>();
        }

        public ExternalAgentConfigMigrationItemType ItemType { get; set; }

        public string RawItemType { get; set; }

        public string Description { get; set; }

        public string Cwd { get; set; }

        public ExternalAgentConfigMigrationDetails Details { get; set; }

        public IDictionary<string, object> RawItem { get; set; }
    }
}
