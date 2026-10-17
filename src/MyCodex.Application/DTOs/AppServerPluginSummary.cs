using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerPluginSummary
    {
        public string MarketplaceName { get; set; }

        public string MarketplaceId { get; set; }

        public string MarketplacePath { get; set; }

        public string PluginId { get; set; }

        public string RemotePluginId { get; set; }

        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string ShortDescription { get; set; }

        public string LongDescription { get; set; }

        public string DeveloperName { get; set; }

        public string Category { get; set; }

        public string SourceType { get; set; }

        public string SourcePath { get; set; }

        public bool Installed { get; set; }

        public bool Enabled { get; set; }

        public string Version { get; set; }

        public string LocalVersion { get; set; }

        public string InstallPolicy { get; set; }

        public string InstallPolicySource { get; set; }

        public string AuthPolicy { get; set; }

        public string WebsiteUrl { get; set; }

        public string PrivacyPolicyUrl { get; set; }

        public string TermsOfServiceUrl { get; set; }

        public string ComposerIconPath { get; set; }

        public string LogoPath { get; set; }

        public string BrandColor { get; set; }

        public List<string> Capabilities { get; set; }

        public List<string> DefaultPrompts { get; set; }
    }
}
