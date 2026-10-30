using System;
using System.Globalization;

namespace MyCodex.Infrastructure.Updates
{
    public sealed class UpdateManifest
    {
        public string SchemaVersion { get; set; }

        public string Channel { get; set; }

        public string GeneratedAt { get; set; }

        public string PackageBaseUrl { get; set; }

        public UpdateManifestAppPackage App { get; set; }
    }

    public sealed class UpdateManifestAppPackage
    {
        public string Version { get; set; }

        public string MinimumSupportedVersion { get; set; }

        public string ReleaseDate { get; set; }

        public string ReleaseNotes { get; set; }

        public string ReleaseNotesZhCn { get; set; }

        public string ReleaseNotesEnUs { get; set; }

        public UpdateAssetReference Asset { get; set; }
    }

    public static class UpdateVersionComparer
    {
        public static int Compare(string left, string right)
        {
            var leftNormalized = Normalize(left);
            var rightNormalized = Normalize(right);
            Version leftVersion;
            Version rightVersion;
            if (Version.TryParse(leftNormalized, out leftVersion) &&
                Version.TryParse(rightNormalized, out rightVersion))
            {
                return leftVersion.CompareTo(rightVersion);
            }

            return string.Compare(leftNormalized, rightNormalized, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string version)
        {
            var value = (version ?? string.Empty).Trim();
            if (value.StartsWith("v", true, CultureInfo.InvariantCulture))
            {
                value = value.Substring(1);
            }

            var suffixIndex = value.IndexOf('-');
            return suffixIndex > 0 ? value.Substring(0, suffixIndex) : value;
        }
    }
}
