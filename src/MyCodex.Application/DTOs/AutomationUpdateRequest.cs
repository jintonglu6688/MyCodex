using System.Collections.Generic;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.DTOs
{
    public sealed class AutomationUpdateRequest
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public AutomationKind Kind { get; set; }

        public string Prompt { get; set; }

        public List<AutomationInputItemSnapshot> InputItems { get; set; }

        public AutomationScheduleSnapshot Schedule { get; set; }

        public AutomationContextSnapshot Context { get; set; }

        public string ModelId { get; set; }

        public string ReasoningEffort { get; set; }

        public ApprovalMode? ApprovalMode { get; set; }

        public bool AllowNoFindingsAutoArchive { get; set; }
    }
}
