using MyCodex.Domain.Enums;

namespace MyCodex.Application.Capabilities
{
    public static class OfficeCapabilityPackage
    {
        public const string PackageId = "mycodex.office";
        public const string DisplayName = "MyCodex Office";

        public static CapabilityPackageDescriptor CreateDefault()
        {
            var package = new CapabilityPackageDescriptor
            {
                Id = PackageId,
                DisplayName = DisplayName,
                Version = "1.0",
                Kind = CapabilityPackageKind.Official,
                EnabledByDefault = true
            };

            package.SupportedModes.Add(AppInteractionMode.Office);
            package.SubFeatures.Add(CreateSubFeature("word", "Word", CapabilitySubFeatureState.Enabled));
            package.SubFeatures.Add(CreateSubFeature("spreadsheet", "Spreadsheet", CapabilitySubFeatureState.Enabled));
            package.SubFeatures.Add(CreateSubFeature("presentation", "Presentation", CapabilitySubFeatureState.Conditional));
            package.SubFeatures.Add(CreateSubFeature("preview", "Preview", CapabilitySubFeatureState.Disabled));
            package.SubFeatures.Add(CreateSubFeature("validation", "Validation", CapabilitySubFeatureState.Enabled));
            package.SubFeatures.Add(CreateSubFeature("image-assist", "Image Assist", CapabilitySubFeatureState.Conditional));

            package.Permissions.Add(CapabilityPermission.ReadOfficeRoot);
            package.Permissions.Add(CapabilityPermission.WriteOfficeRoot);
            package.Permissions.Add(CapabilityPermission.CreateEditedCopy);
            package.Permissions.Add(CapabilityPermission.OverwriteWithConfirmation);
            package.Permissions.Add(CapabilityPermission.DeleteWithConfirmation);
            package.Permissions.Add(CapabilityPermission.WriteOutsideOfficeRootWithConfirmation);
            package.Permissions.Add(CapabilityPermission.BulkModifyWithConfirmation);
            package.Permissions.Add(CapabilityPermission.UseMcpTools);
            package.Permissions.Add(CapabilityPermission.GenerateDocuments);
            package.Permissions.Add(CapabilityPermission.GenerateSpreadsheets);
            package.Permissions.Add(CapabilityPermission.GeneratePresentations);

            package.SkillNames.Add("mycodex-office-document");
            package.SkillNames.Add("mycodex-office-spreadsheet");
            package.SkillNames.Add("mycodex-office-quality-check");

            package.DeveloperInstructionFragments.Add(
                "Use enabled Skills, plugins, MCP tools, and CLI tools. Use only tools that are actually enabled and suitable for the task. Choose the tool that best matches the task. Do not prefer a specific Office plugin solely because it is installed.");
            package.DeveloperInstructionFragments.Add(
                "If the user names a tool, prefer it. If it is unavailable, say so briefly and choose the closest reliable available option. Do not pretend that an unavailable tool was used.");
            package.DeveloperInstructionFragments.Add(
                "When no tool is named, prefer a dedicated Skill, plugin, or MCP tool that directly matches the artifact and operation, then a reliable CLI tool, then a suitable general file-processing capability.");
            package.DeveloperInstructionFragments.Add(
                "Use one primary creation or editing tool. Add another tool only when validation, preview, conversion, research, or repair genuinely requires it.");
            package.DeveloperInstructionFragments.Add(
                "Respect enabled third-party plugins, Skills, and MCP tools when the user explicitly requests them or when they are better suited to the document, spreadsheet, presentation, research, or file-management task.");
            package.DeveloperInstructionFragments.Add(
                "For PowerPoint, PPTX, slide deck, or presentation work, use an enabled presentation-specific Skill or plugin, or an MCP tool, when it best matches the task; then import or copy the finished presentation into the current Office working directory before delivery.");
            package.DeveloperInstructionFragments.Add(
                "When OfficeCLI is available, it may be used to create, inspect, preview, validate, or repair Office files when that is the best fit for the task. Do not require OfficeCLI when another enabled Skill, plugin, MCP tool, or CLI tool is a better fit.");
            package.DeveloperInstructionFragments.Add(
                "Use browser workflows only when the task needs external research, fact checking, or source material; do not browse for a simple local document task.");
            package.DeveloperInstructionFragments.Add(
                "Follow the selected tool's instructions, permission boundaries, and validation requirements. If no available tool can reliably produce the requested format, state the limitation; never fake a format by changing an extension or claim that a nonexistent tool completed the work.");

            return package;
        }

        private static CapabilitySubFeature CreateSubFeature(string id, string displayName, CapabilitySubFeatureState state)
        {
            return new CapabilitySubFeature
            {
                Id = id,
                DisplayName = displayName,
                State = state
            };
        }
    }
}
