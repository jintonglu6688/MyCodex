namespace MyCodex.Infrastructure.Codex
{
    public sealed class CodexRuntimeUpdateInfo
    {
        public string TargetTriple { get; set; }

        public string InstalledVersion { get; set; }

        public string LatestVersion { get; set; }

        public bool IsLatestInstalled { get; set; }

        internal CodexRuntimePackage Package { get; set; }

        public bool IsUpdateAvailable => !string.IsNullOrWhiteSpace(LatestVersion) &&
            (string.IsNullOrWhiteSpace(InstalledVersion) ||
                Updates.UpdateVersionComparer.Compare(LatestVersion, InstalledVersion) > 0);
    }
}
