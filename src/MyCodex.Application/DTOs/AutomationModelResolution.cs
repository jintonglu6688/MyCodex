namespace MyCodex.Application.DTOs
{
    public sealed class AutomationModelResolution
    {
        public string ConfiguredModelId { get; set; }

        public string ConfiguredReasoningEffort { get; set; }

        public string ModelId { get; set; }

        public string ReasoningEffort { get; set; }

        public bool UsedFallback { get; set; }

        public string Message { get; set; }
    }
}
