using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class AutomationCreationContextBuilder
    {
        public static AutomationCreationContext ForThread(
            string threadId,
            AppInteractionMode interactionMode,
            string runtimeProfileId,
            string workspaceId,
            string workspacePath,
            string modelId,
            string reasoningEffort,
            ApprovalMode? approvalMode,
            string officeRuntimeScopeId = null)
        {
            return new AutomationCreationContext
            {
                Kind = AutomationKind.Thread,
                InteractionMode = interactionMode,
                RuntimeProfileId = runtimeProfileId,
                OfficeRuntimeScopeId = officeRuntimeScopeId,
                WorkspaceId = workspaceId,
                WorkspacePath = workspacePath,
                ThreadId = threadId,
                ModelId = modelId,
                ReasoningEffort = reasoningEffort,
                ApprovalMode = approvalMode
            };
        }

        public static AutomationCreationContext ForProjectWorkspace(
            WorkspaceInfo workspace,
            string runtimeProfileId,
            string modelId,
            string reasoningEffort,
            ApprovalMode? approvalMode)
        {
            return new AutomationCreationContext
            {
                Kind = AutomationKind.Project,
                InteractionMode = AppInteractionMode.Coding,
                RuntimeProfileId = runtimeProfileId,
                WorkspaceId = workspace == null ? null : workspace.Id,
                WorkspacePath = workspace == null ? null : workspace.RootPath,
                ModelId = modelId,
                ReasoningEffort = reasoningEffort,
                ApprovalMode = approvalMode
            };
        }

        public static AutomationCreationContext ForOfficeWorkspace(
            WorkspaceInfo workspace,
            string officeRootPath,
            string runtimeProfileId,
            string officeRuntimeScopeId,
            string modelId,
            string reasoningEffort,
            ApprovalMode? approvalMode)
        {
            return new AutomationCreationContext
            {
                Kind = AutomationKind.Office,
                InteractionMode = AppInteractionMode.Office,
                RuntimeProfileId = runtimeProfileId,
                OfficeRuntimeScopeId = officeRuntimeScopeId,
                WorkspaceId = workspace == null ? null : workspace.Id,
                WorkspacePath = workspace == null ? null : workspace.RootPath,
                OfficeRootPath = officeRootPath,
                ModelId = modelId,
                ReasoningEffort = reasoningEffort,
                ApprovalMode = approvalMode
            };
        }
    }
}
