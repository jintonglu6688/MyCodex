using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public sealed class TargetCatalogSnapshot
    {
        [DataMember]
        public int SchemaVersion { get; set; } = 1;

        [DataMember]
        public string LastOpenedTargetId { get; set; }

        [DataMember]
        public List<TargetProfileSnapshot> Targets { get; set; } = new List<TargetProfileSnapshot>();
    }

    [DataContract]
    public sealed class TargetProfileSnapshot
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string Kind { get; set; }

        [DataMember]
        public string RuntimeScopeId { get; set; }

        [DataMember]
        public string SshProfileId { get; set; }

        [DataMember]
        public string WslDistributionName { get; set; }

        [DataMember]
        public List<string> LegacyRuntimeScopeIds { get; set; } = new List<string>();
    }
}
