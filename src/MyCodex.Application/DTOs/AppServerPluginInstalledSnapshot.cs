using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerPluginInstalledSnapshot
    {
        public AppServerPluginInstalledSnapshot()
        {
            Plugins = new List<AppServerPluginSummary>();
            MarketplaceErrors = new List<AppServerMarketplaceLoadError>();
        }

        public List<AppServerPluginSummary> Plugins { get; set; }

        public List<AppServerMarketplaceLoadError> MarketplaceErrors { get; set; }
    }
}
