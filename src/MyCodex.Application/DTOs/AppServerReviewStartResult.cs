namespace MyCodex.Application.DTOs
{
    public sealed class AppServerReviewStartResult
    {
        public string ReviewThreadId { get; set; }

        public AppServerThreadSummary CreatedThread { get; set; }

        public string TurnId { get; set; }

        public string ConfiguredModelId { get; set; }

        public string ConfiguredReasoningEffort { get; set; }

        public string WorkingDirectory { get; set; }
    }
}
