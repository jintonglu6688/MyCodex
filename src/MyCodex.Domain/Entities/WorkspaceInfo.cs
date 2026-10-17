using System;
using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class WorkspaceInfo
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string RootPath { get; set; }

        [DataMember]
        public string GitBranch { get; set; }

        [DataMember]
        public WorkspaceKind Kind { get; set; } = WorkspaceKind.Coding;

        [DataMember]
        public DateTime LastOpenedAt { get; set; }
    }
}
