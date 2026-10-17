using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public sealed class RuntimeProfileConfigSnapshot
    {
        [DataMember]
        public string RuntimeProfileId { get; set; }

        [DataMember]
        public string DefaultModelId { get; set; }

        [DataMember]
        public string DefaultReasoningEffort { get; set; }

        [DataMember]
        public ApprovalMode? DefaultApprovalMode { get; set; }

    }
}
