using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class AutomationContextSnapshot
    {
        [DataMember]
        public AppInteractionMode InteractionMode { get; set; } = AppInteractionMode.Coding;

        [DataMember]
        public string RuntimeProfileId { get; set; }

        [DataMember]
        public string OfficeRuntimeScopeId { get; set; }

        [DataMember]
        public string WorkspaceId { get; set; }

        [DataMember]
        public string SessionId { get; set; }

        [DataMember]
        public string ThreadId { get; set; }

        [DataMember]
        public string WorkspacePath { get; set; }

        [DataMember]
        public string OfficeRootPath { get; set; }
    }
}
