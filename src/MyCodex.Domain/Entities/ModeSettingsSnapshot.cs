using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public sealed class ModeSettingsSnapshot
    {
        [DataMember]
        public AppInteractionMode DefaultStartupMode { get; set; } = AppInteractionMode.Coding;

        [DataMember]
        public string OfficeRootPath { get; set; }
    }
}
