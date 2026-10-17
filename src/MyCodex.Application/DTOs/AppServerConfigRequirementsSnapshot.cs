using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerConfigRequirementsSnapshot
    {
        public bool HasRequirements { get; set; }

        public List<string> AllowedApprovalPolicies { get; set; }

        public List<string> AllowedSandboxModes { get; set; }

        public List<string> AllowedWebSearchModes { get; set; }

        public IDictionary<string, bool> FeatureRequirements { get; set; }

        public string EnforceResidency { get; set; }

        public AppServerNewThreadModelDefaults NewThreadModelDefaults { get; set; }
    }
}
