using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IAutomationService
    {
        AutomationDefinitionSnapshot Create(AutomationCreationRequest request);

        AutomationDefinitionSnapshot Update(AutomationUpdateRequest request);

        void Disable(string automationId);

        void Enable(string automationId);

        void Delete(string automationId);

        void DeleteRun(string runId);
    }
}
