namespace MyCodex.Application.DTOs
{
    public sealed class AppServerContextInjectionResult
    {
        public bool Injected { get; set; }

        public bool ShouldFallbackToUserMessage { get; set; }

        public string ErrorMessage { get; set; }
    }
}
