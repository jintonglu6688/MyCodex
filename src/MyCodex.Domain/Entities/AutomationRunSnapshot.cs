using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class AutomationRunSnapshot
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string AutomationId { get; set; }

        [DataMember]
        public AutomationRunStatus Status { get; set; }

        [DataMember]
        public DateTime? ScheduledFor { get; set; }

        [DataMember]
        public DateTime? StartedAt { get; set; }

        [DataMember]
        public DateTime? CompletedAt { get; set; }

        [DataMember]
        public AutomationTriggerReason TriggerReason { get; set; }

        [DataMember]
        public string ThreadId { get; set; }

        [DataMember]
        public string TurnId { get; set; }

        [DataMember]
        public string WorkspaceId { get; set; }

        [DataMember]
        public string WorkspacePath { get; set; }

        [DataMember]
        public string ConfiguredModelId { get; set; }

        [DataMember]
        public string ConfiguredReasoningEffort { get; set; }

        [DataMember]
        public string ActualModelId { get; set; }

        [DataMember]
        public string ActualReasoningEffort { get; set; }

        [DataMember]
        public string ModelResolutionMessage { get; set; }

        [DataMember]
        public string Summary { get; set; }

        [DataMember]
        public string ErrorMessage { get; set; }

        [DataMember]
        public bool HasFindings { get; set; }

        [DataMember]
        public bool NeedsAttention { get; set; }

        [DataMember]
        public bool Unread { get; set; }

        [DataMember]
        public bool Archived { get; set; }

        [DataMember]
        public List<string> GeneratedArtifacts { get; set; } = new List<string>();

        [DataMember]
        public List<string> ChangedFiles { get; set; } = new List<string>();
    }
}
