namespace MyCodex.Application.DTOs
{
    public sealed class AppServerWindowsSandboxReadiness
    {
        public AppServerWindowsSandboxReadinessStatus Status { get; set; }

        public string RawStatus { get; set; }

        public bool IsReady
        {
            get { return Status == AppServerWindowsSandboxReadinessStatus.Ready; }
        }
    }
}
