using System.Runtime.Serialization;
using MyCodex.Shared.Constants;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class UpdateSettingsSnapshot
    {
        [DataMember]
        public string Channel { get; set; } = "stable";

        [DataMember]
        public string ManifestUrl { get; set; } = AppConstants.DefaultUpdateManifestUrl;
    }
}
