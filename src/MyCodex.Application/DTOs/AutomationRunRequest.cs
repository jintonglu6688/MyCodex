using System;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.DTOs
{
    public sealed class AutomationRunRequest
    {
        public string AutomationId { get; set; }

        public AutomationTriggerReason TriggerReason { get; set; }

        public DateTime? ScheduledFor { get; set; }
    }
}
