using MyCodex.Domain.Enums;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerProtocolCapabilities
    {
        public string RuntimeSource { get; set; }

        public string RuntimeVersion { get; set; }

        public AppServerHostPlatform HostPlatform { get; set; }

        public string PlatformOs { get; set; }

        public string UserAgent { get; set; }

        public string ProtocolProfile { get; set; }

        public bool IsLegacyRuntime { get; set; }

        public bool SupportsModernAppServerProtocol { get; set; }

        public bool SupportsExperimentalApi { get; set; }

        public bool SupportsPluginManagement { get; set; }

        public bool SupportsPluginCatalogObservation { get; set; }

        public bool SupportsInstalledPluginObservation { get; set; }

        public bool SupportsMarketplaceRegistration { get; set; }

        public bool SupportsPluginMutation { get; set; }

        public bool SupportsSkills { get; set; }

        public bool SupportsMcpOAuth { get; set; }

        public bool SupportsMcpManagement { get; set; }

        public bool SupportsMcpElicitation { get; set; }

        public bool SupportsFileSystemApi { get; set; }

        public bool SupportsFileSystem { get; set; }

        public bool SupportsFuzzyFileSearch { get; set; }

        public bool SupportsWindowsSandbox { get; set; }

        public bool SupportsConfigRequirements { get; set; }

        public bool SupportsExternalAgentConfigImport { get; set; }

        public bool SupportsModelProviderCapabilities { get; set; }

        public bool SupportsCommandExecIo { get; set; }

        public bool SupportsThreadForkLastTurnId { get; set; }

        public bool SupportsThreadHistoryPagination { get; set; }
        public bool SupportsRealtimeTimeline { get; set; }

        public bool SupportsSubagentThreadFilters { get; set; }

        public bool SupportsCollabAgentToolCall { get; set; }

        public bool SupportsBrowserUseIntegration { get; set; }
    }
}
