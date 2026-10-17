using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerMcpServerStatus
    {
        public AppServerMcpServerStatus()
        {
            Resources = new List<AppServerMcpResourceSummary>();
            ResourceTemplates = new List<AppServerMcpResourceSummary>();
        }

        public string ServerName { get; set; }

        public string Status { get; set; }

        public string AuthStatus { get; set; }

        public bool? Enabled { get; set; }

        public int? ToolCount { get; set; }

        public int? ResourceCount { get; set; }

        public int? ResourceTemplateCount { get; set; }

        public List<AppServerMcpResourceSummary> Resources { get; private set; }

        public List<AppServerMcpResourceSummary> ResourceTemplates { get; private set; }

        public string SourceSummary { get; set; }

        public string SourceDetail { get; set; }

        public Dictionary<string, object> ConfigValues { get; set; }

        public bool SupportsOAuth { get; set; }

        public bool? IsRequired { get; set; }

        public string OAuthResource { get; set; }

        public string EnabledToolsText { get; set; }

        public string DisabledToolsText { get; set; }

        public string EnvironmentText { get; set; }

        public string ErrorText { get; set; }

        public string DetailText { get; set; }
    }
}
