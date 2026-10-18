using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    public class PluginMarketListItem
    {
        public string MarketName { get; set; }

        public string PluginId { get; set; }

        public string DisplayName { get; set; }

        public string Summary { get; set; }

        public string AppServerSource { get; set; }

        public bool IsRemoteCatalog { get; set; }

        public PluginMarketAvailabilityStatus AvailabilityStatus { get; set; }

        public string InstalledVersion { get; set; }

        public AppServerPluginSummary AppServerPlugin { get; set; }

        public PluginMarketEntrySnapshot RemoteEntry { get; set; }
    }
}
