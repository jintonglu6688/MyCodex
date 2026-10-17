using MyCodex.Domain.Entities;

namespace MyCodex.Application.DTOs
{
    public sealed class PendingAutomationAssignment
    {
        public AutomationDefinitionSnapshot Definition { get; set; }

        public string ReservedTargetId { get; set; }
    }
}
