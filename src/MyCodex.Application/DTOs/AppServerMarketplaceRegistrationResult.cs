using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerMarketplaceRegistrationResult
    {
        public AppServerMarketplaceRegistrationResult()
        {
            CatalogPlugins = new List<AppServerPluginSummary>();
            InstalledPlugins = new List<AppServerPluginSummary>();
        }

        public string MarketplaceName { get; set; }

        public int RediscoveredInstalledCount { get; set; }

        public List<AppServerPluginSummary> CatalogPlugins { get; set; }

        public List<AppServerPluginSummary> InstalledPlugins { get; set; }
    }
}
