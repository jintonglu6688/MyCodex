using System.Runtime.Serialization;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public sealed class VoiceChatSettings
    {
        [DataMember(EmitDefaultValue = false)]
        public string Voice { get; set; }

        [DataMember]
        public bool FasterSpeech { get; set; }

        [DataMember(EmitDefaultValue = false)]
        public VoiceOutputLevel? OutputLevel { get; set; }
    }

    public enum VoiceOutputLevel
    {
        Original = 100,
        Boost = 150,
        StrongBoost = 200
    }
}
