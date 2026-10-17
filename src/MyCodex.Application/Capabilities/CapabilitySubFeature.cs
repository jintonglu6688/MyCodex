namespace MyCodex.Application.Capabilities
{
    public sealed class CapabilitySubFeature
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public CapabilitySubFeatureState State { get; set; }
    }
}
