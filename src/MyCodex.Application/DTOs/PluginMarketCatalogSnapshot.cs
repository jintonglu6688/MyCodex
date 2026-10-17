using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class PluginMarketCatalogSnapshot
    {
        public PluginMarketCatalogSnapshot()
        {
            Markets = new List<PluginMarketSnapshot>();
        }

        public string IndexUrl { get; set; }

        public List<PluginMarketSnapshot> Markets { get; set; }
    }
}
