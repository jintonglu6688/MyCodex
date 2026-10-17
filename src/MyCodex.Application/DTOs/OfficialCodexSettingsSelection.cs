namespace MyCodex.Application.DTOs
{
    public sealed class OfficialCodexSettingsSelection
    {
        public string ModelReasoningSummary { get; set; }

        public string ModelVerbosity { get; set; }

        public string ServiceTier { get; set; }

        public string WebSearchMode { get; set; }

        public string WebSearchContextSize { get; set; }

        public bool? ViewImageToolEnabled { get; set; }

        public long? ToolOutputTokenLimit { get; set; }

        public long? ProjectDocMaxBytes { get; set; }
    }
}
