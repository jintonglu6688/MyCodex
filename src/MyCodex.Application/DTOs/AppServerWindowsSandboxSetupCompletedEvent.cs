namespace MyCodex.Application.DTOs
{
    public sealed class AppServerWindowsSandboxSetupCompletedEvent
    {
        public AppServerWindowsSandboxSetupMode Mode { get; set; }

        public string RawMode { get; set; }

        public bool Success { get; set; }

        public string Error { get; set; }
    }
}
