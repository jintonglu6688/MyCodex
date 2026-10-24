using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    // Zero and null values mean the defaults: the official voice and pace, unchanged volume, F8 held to talk.
    [DataContract]
    public sealed class VoiceChatSettings
    {
        [DataMember(EmitDefaultValue = false)]
        public string Voice { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public bool FasterSpeech { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int OutputVolumePercent { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public int TalkVirtualKey { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public VoiceTalkMode TalkMode { get; set; }
    }
}
