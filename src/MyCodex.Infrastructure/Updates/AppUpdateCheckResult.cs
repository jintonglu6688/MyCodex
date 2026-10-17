namespace MyCodex.Infrastructure.Updates
{
    public sealed class AppUpdateCheckResult
    {
        public string CurrentVersion { get; set; }

        public string LatestVersion { get; set; }

        public bool IsUpdateAvailable { get; set; }

        public bool IsConfigured { get; set; }

        public string ReleaseNotes { get; set; }

        public string ReleaseNotesZhCn { get; set; }

        public string ReleaseNotesEnUs { get; set; }

        public UpdateManifest Manifest { get; set; }

        public UpdateManifestAppPackage Package { get; set; }
    }
}
