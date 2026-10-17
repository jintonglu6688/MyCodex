namespace MyCodex.Application.DTOs
{
    public sealed class AppServerModerationMetadataEvent
    {
        public string ThreadId { get; set; }

        public string TurnId { get; set; }

        public string Summary { get; set; }

        public string Details { get; set; }
    }
}
