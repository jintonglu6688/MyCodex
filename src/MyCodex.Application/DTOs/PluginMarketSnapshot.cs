using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class PluginMarketSnapshot
    {
        public PluginMarketSnapshot()
        {
            Plugins = new List<PluginMarketEntrySnapshot>();
        }

        public string MarketId { get; set; }

        public string DisplayName { get; set; }

        public string ManifestUrl { get; set; }

        public string AppServerSource { get; set; }

        public string DetailsUrl { get; set; }

        public List<PluginMarketEntrySnapshot> Plugins { get; set; }
    }
}
