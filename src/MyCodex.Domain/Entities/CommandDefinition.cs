using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public sealed class CommandDefinition
    {
        [DataMember]
        public string Name { get; set; }

        [DataMember]
        public string CommandText { get; set; }

        [DataMember]
        public CommandHostPlatform HostPlatform { get; set; } = CommandHostPlatform.Windows;
    }
}
