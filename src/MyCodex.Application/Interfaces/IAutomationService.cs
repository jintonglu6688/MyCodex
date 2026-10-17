using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IAutomationService
    {
        IReadOnlyList<AutomationDefinitionSnapshot> ListDefinitions();

        IReadOnlyList<AutomationRunSnapshot> ListRuns();

        AutomationDefinitionSnapshot Create(AutomationCreationRequest request);

        AutomationDefinitionSnapshot Update(AutomationUpdateRequest request);

        void Disable(string automationId);

        void Enable(string automationId);

        void Delete(string automationId);

        void MarkRunRead(string runId);

        void ArchiveRun(string runId);

        void DeleteRun(string runId);
    }
}
