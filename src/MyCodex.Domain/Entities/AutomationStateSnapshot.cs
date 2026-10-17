using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class AutomationStateSnapshot
    {
        [DataMember]
        public List<AutomationDefinitionSnapshot> Definitions { get; set; } = new List<AutomationDefinitionSnapshot>();

        [DataMember]
        public List<AutomationRunSnapshot> Runs { get; set; } = new List<AutomationRunSnapshot>();
    }
}
