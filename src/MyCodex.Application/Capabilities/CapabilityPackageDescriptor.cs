using System.Collections.Generic;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Capabilities
{
    public sealed class CapabilityPackageDescriptor
    {
        public CapabilityPackageDescriptor()
        {
            SupportedModes = new List<AppInteractionMode>();
            SubFeatures = new List<CapabilitySubFeature>();
            Permissions = new List<CapabilityPermission>();
            DeveloperInstructionFragments = new List<string>();
            SkillNames = new List<string>();
        }

        public string Id { get; set; }

        public string DisplayName { get; set; }

        public string Version { get; set; }

        public CapabilityPackageKind Kind { get; set; }

        public bool EnabledByDefault { get; set; }

        public List<AppInteractionMode> SupportedModes { get; private set; }

        public List<CapabilitySubFeature> SubFeatures { get; private set; }

        public List<CapabilityPermission> Permissions { get; private set; }

        public List<string> DeveloperInstructionFragments { get; private set; }

        public List<string> SkillNames { get; private set; }
    }
}
