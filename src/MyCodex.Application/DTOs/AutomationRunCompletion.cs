using System;
using System.Collections.Generic;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.DTOs
{
    public sealed class AutomationRunCompletion
    {
        public AutomationRunStatus Status { get; set; }

        public DateTime CompletedAt { get; set; }

        public string TurnId { get; set; }

        public string Summary { get; set; }

        public string ErrorMessage { get; set; }

        public IReadOnlyList<string> GeneratedArtifacts { get; set; }

        public IReadOnlyList<string> ChangedFiles { get; set; }
    }
}
