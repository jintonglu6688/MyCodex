using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    public sealed class AutomationCreationContext
    {
        public AutomationKind Kind { get; set; }

        public AppInteractionMode InteractionMode { get; set; } = AppInteractionMode.Coding;

        public string RuntimeProfileId { get; set; }

        public string OfficeRuntimeScopeId { get; set; }

        public string WorkspaceId { get; set; }

        public string WorkspacePath { get; set; }

        public string OfficeRootPath { get; set; }

        public string SessionId { get; set; }

        public string ThreadId { get; set; }

        public string ModelId { get; set; }

        public string ReasoningEffort { get; set; }

        public ApprovalMode? ApprovalMode { get; set; }
    }
}
