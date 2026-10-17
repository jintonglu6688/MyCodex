using System.Runtime.Serialization;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.DTOs
{
    // Only runtime editor fields cross target IPC. Preferences and remote identity remain owned by the target.
    [DataContract]
    public sealed class TargetRuntimeSettings
    {
        [DataMember(EmitDefaultValue = false)]
        public string ExpectedRuntimeRevision { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool BundledRuntimeUpdated { get; set; }

        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string CodexRuntimeSource { get; set; }

        [DataMember]
        public string CodexCustomExecutablePath { get; set; }

        [DataMember]
        public string WindowsShellPreference { get; set; }

        [DataMember]
        public WslAppServerConfigSnapshot WslAppServer { get; set; }

        [DataMember]
        public RemoteMacSshConfigSnapshot RemoteSsh { get; set; }
    }
}
