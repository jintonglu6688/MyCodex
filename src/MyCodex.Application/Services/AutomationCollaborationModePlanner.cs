using MyCodex.Application.Capabilities;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public sealed class AutomationCollaborationModePlanner
    {
        public AppServerCollaborationMode Build(AutomationDefinitionSnapshot definition)
        {
            if (definition == null || definition.Kind != AutomationKind.Office)
            {
                return null;
            }

            var context = definition.Context ?? new AutomationContextSnapshot();
            return new AppServerCollaborationMode
            {
                Mode = "default",
                ModelId = definition.ModelId,
                ReasoningEffort = definition.ReasoningEffort,
                DeveloperInstructions = OfficeDeveloperInstructionComposer.Compose(
                    context.OfficeRootPath,
                    context.WorkspacePath)
            };
        }
    }
}
