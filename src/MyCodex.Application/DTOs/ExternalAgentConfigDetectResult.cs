using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class ExternalAgentConfigDetectResult
    {
        public ExternalAgentConfigDetectResult()
        {
            Items = new List<ExternalAgentConfigMigrationItem>();
        }

        public List<ExternalAgentConfigMigrationItem> Items { get; set; }
    }
}
