namespace MyCodex.Application.DTOs
{
    public sealed class AppServerTokenUsageBreakdown
    {
        public long TotalTokens { get; set; }

        public long InputTokens { get; set; }

        public long CachedInputTokens { get; set; }

        public long OutputTokens { get; set; }

        public long ReasoningOutputTokens { get; set; }
    }

    public sealed class AppServerThreadTokenUsageSnapshot
    {
        public AppServerThreadTokenUsageSnapshot()
        {
            Total = new AppServerTokenUsageBreakdown();
            Last = new AppServerTokenUsageBreakdown();
        }

        public AppServerTokenUsageBreakdown Total { get; set; }

        public AppServerTokenUsageBreakdown Last { get; set; }

        public long? ModelContextWindow { get; set; }
    }
}
