using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public sealed class OfficeRuntimeStateSnapshot
    {
        [DataMember]
        public string RuntimeScopeId { get; set; }

        [DataMember]
        public List<WorkspaceInfo> RecentWorkspaces { get; set; } = new List<WorkspaceInfo>();

        [DataMember]
        public string CurrentWorkspaceId { get; set; }

        [DataMember]
        public string CurrentSessionId { get; set; }
    }
}
