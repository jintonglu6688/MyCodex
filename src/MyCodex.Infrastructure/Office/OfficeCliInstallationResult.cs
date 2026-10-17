using MyCodex.Application.Services;

namespace MyCodex.Infrastructure.Office
{
    public sealed class OfficeCliInstallationResult
    {
        public bool Success { get; set; }

        public string Version { get; set; }

        public string Message { get; set; }

        public OfficeCliIntegrationStatus Status { get; set; }
    }
}
