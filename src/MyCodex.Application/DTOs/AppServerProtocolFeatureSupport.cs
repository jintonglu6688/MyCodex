namespace MyCodex.Application.DTOs
{
    public static class AppServerProtocolFeatureSupport
    {
        public static bool IsSupported(AppServerProtocolCapabilities capabilities, AppServerProtocolFeature feature)
        {
            if (capabilities == null)
            {
                return true;
            }

            switch (feature)
            {
                case AppServerProtocolFeature.PluginManagement:
                    return capabilities.SupportsPluginManagement;
                case AppServerProtocolFeature.Skills:
                    return capabilities.SupportsSkills;
                case AppServerProtocolFeature.McpManagement:
                    return capabilities.SupportsMcpManagement;
                case AppServerProtocolFeature.McpOAuth:
                    return capabilities.SupportsMcpOAuth;
                case AppServerProtocolFeature.ModelProviderCapabilities:
                    return capabilities.SupportsModelProviderCapabilities;
                case AppServerProtocolFeature.ExperimentalFeatureList:
                    return capabilities.SupportsExperimentalApi;
                case AppServerProtocolFeature.ConfigRequirements:
                    return capabilities.SupportsConfigRequirements;
                case AppServerProtocolFeature.ExternalAgentConfigImport:
                    return capabilities.SupportsExternalAgentConfigImport;
                case AppServerProtocolFeature.FileSystem:
                    return capabilities.SupportsFileSystem;
                case AppServerProtocolFeature.FuzzyFileSearch:
                    return capabilities.SupportsFuzzyFileSearch;
                case AppServerProtocolFeature.WindowsSandbox:
                    return capabilities.SupportsWindowsSandbox;
                default:
                    return false;
            }
        }
    }
}
