namespace MyCodex.Application.DTOs
{
    public sealed class AppServerContextInjectionRequest
    {
        public string ThreadId { get; set; }

        public string Source { get; set; }

        public string Text { get; set; }

        public bool Required { get; set; }
    }
}
