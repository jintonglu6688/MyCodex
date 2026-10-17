using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class AutomationDefinitionSnapshot
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Description { get; set; }

        [DataMember]
        public AutomationKind Kind { get; set; }

        [DataMember]
        public bool Enabled { get; set; }

        [DataMember]
        public AutomationScheduleSnapshot Schedule { get; set; } = new AutomationScheduleSnapshot();

        [DataMember]
        public string Prompt { get; set; }

        [DataMember]
        public List<AutomationInputItemSnapshot> InputItems { get; set; } = new List<AutomationInputItemSnapshot>();

        [DataMember]
        public AutomationContextSnapshot Context { get; set; } = new AutomationContextSnapshot();

        [DataMember]
        public string ModelId { get; set; }

        [DataMember]
        public string ReasoningEffort { get; set; }

        [DataMember]
        public ApprovalMode? ApprovalMode { get; set; }

        [DataMember]
        public bool AllowNoFindingsAutoArchive { get; set; }

        [DataMember]
        public DateTime CreatedAt { get; set; }

        [DataMember]
        public DateTime UpdatedAt { get; set; }

        [DataMember]
        public DateTime? LastRunAt { get; set; }

        [DataMember]
        public DateTime? NextRunAt { get; set; }
    }
}
