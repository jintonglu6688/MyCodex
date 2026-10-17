using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerConfigSnapshot
    {
        public AppServerConfigSnapshot()
        {
            Origins = new Dictionary<string, AppServerConfigOrigin>(System.StringComparer.OrdinalIgnoreCase);
            Layers = new List<AppServerConfigLayer>();
            CredentialEnvironmentReferences = new List<AppServerCredentialEnvironmentReference>();
        }

        public string ModelId { get; set; }

        public string ReviewModelId { get; set; }

        public string ModelProviderId { get; set; }

        public string AuthCredentialsStore { get; set; }

        public string ModelReasoningEffort { get; set; }

        public string ApprovalPolicy { get; set; }

        public string SandboxMode { get; set; }

        public long? ModelContextWindow { get; set; }

        public long? ModelAutoCompactTokenLimit { get; set; }

        public string ModelReasoningSummary { get; set; }

        public string ModelVerbosity { get; set; }

        public string ServiceTier { get; set; }

        public string WebSearchMode { get; set; }

        public string WebSearchContextSize { get; set; }

        public bool? ViewImageToolEnabled { get; set; }

        public long? ToolOutputTokenLimit { get; set; }

        public long? ProjectDocMaxBytes { get; set; }

        public Dictionary<string, AppServerConfigOrigin> Origins { get; }

        public List<AppServerConfigLayer> Layers { get; }

        // Names only, extracted from the selected provider; never retain credential values.
        public List<AppServerCredentialEnvironmentReference> CredentialEnvironmentReferences { get; }
        public string CredentialReferenceScope { get; set; }
        public string CredentialReferenceFilePath { get; set; }
        public string CredentialReferenceProviderId { get; set; }
        public bool CredentialReferenceHasProfile { get; set; }
        public bool CredentialReferencesTruncated { get; set; }
    }

    public sealed class AppServerCredentialEnvironmentReference
    {
        public string Field { get; set; }
        public string VariableName { get; set; }
    }

    public class AppServerConfigOrigin
    {
        public string KeyPath { get; set; }

        public string SourceText { get; set; }

        public string SourceType { get; set; }

        public string Version { get; set; }
    }

    public class AppServerConfigLayer
    {
        public string SourceText { get; set; }

        public string SourceType { get; set; }

        public string Version { get; set; }

        public string DisabledReason { get; set; }
    }
}
