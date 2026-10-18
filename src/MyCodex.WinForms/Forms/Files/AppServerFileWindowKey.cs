using System;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AppServerFileWindowKey : IEquatable<AppServerFileWindowKey>
    {
        private AppServerFileWindowKey(
            string windowKind,
            string runtimeSource,
            string runtimeProfileId,
            string remoteProfileId,
            AppInteractionMode interactionMode,
            string normalizedPath)
        {
            WindowKind = NormalizeToken(windowKind);
            RuntimeSource = NormalizeToken(runtimeSource);
            RuntimeProfileId = NormalizeToken(runtimeProfileId);
            RemoteProfileId = NormalizeToken(remoteProfileId);
            InteractionMode = interactionMode;
            NormalizedPath = normalizedPath ?? string.Empty;
        }

        public string WindowKind { get; private set; }

        public string RuntimeSource { get; private set; }

        public string RuntimeProfileId { get; private set; }

        public string RemoteProfileId { get; private set; }

        public AppInteractionMode InteractionMode { get; private set; }

        public string NormalizedPath { get; private set; }

        public static AppServerFileWindowKey CreateBrowser(
            string runtimeSource,
            string runtimeProfileId,
            string remoteProfileId,
            AppInteractionMode interactionMode,
            string path)
        {
            return new AppServerFileWindowKey(
                "browser",
                runtimeSource,
                runtimeProfileId,
                remoteProfileId,
                interactionMode,
                NormalizePath(runtimeSource, path));
        }

        public static AppServerFileWindowKey CreatePreview(
            string runtimeSource,
            string runtimeProfileId,
            string remoteProfileId,
            AppInteractionMode interactionMode,
            string path)
        {
            return new AppServerFileWindowKey(
                "preview",
                runtimeSource,
                runtimeProfileId,
                remoteProfileId,
                interactionMode,
                NormalizePath(runtimeSource, path));
        }

        public bool Equals(AppServerFileWindowKey other)
        {
            return other != null &&
                   string.Equals(WindowKind, other.WindowKind, StringComparison.Ordinal) &&
                   string.Equals(RuntimeSource, other.RuntimeSource, StringComparison.Ordinal) &&
                   string.Equals(RuntimeProfileId, other.RuntimeProfileId, StringComparison.Ordinal) &&
                   string.Equals(RemoteProfileId, other.RemoteProfileId, StringComparison.Ordinal) &&
                   InteractionMode == other.InteractionMode &&
                   string.Equals(NormalizedPath, other.NormalizedPath, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AppServerFileWindowKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + WindowKind.GetHashCode();
                hash = hash * 31 + RuntimeSource.GetHashCode();
                hash = hash * 31 + RuntimeProfileId.GetHashCode();
                hash = hash * 31 + RemoteProfileId.GetHashCode();
                hash = hash * 31 + InteractionMode.GetHashCode();
                hash = hash * 31 + NormalizedPath.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return string.Join(
                "|",
                new[]
                {
                    WindowKind,
                    RuntimeSource,
                    RuntimeProfileId,
                    RemoteProfileId,
                    InteractionMode.ToString().ToLowerInvariant(),
                    NormalizedPath
                });
        }

        public bool MatchesScope(AppServerFileWindowRuntimeScope scope)
        {
            return scope != null &&
                   string.Equals(RuntimeSource, NormalizeToken(scope.RuntimeSource), StringComparison.Ordinal) &&
                   string.Equals(RuntimeProfileId, NormalizeToken(scope.RuntimeProfileId), StringComparison.Ordinal) &&
                   string.Equals(RemoteProfileId, NormalizeToken(scope.RemoteProfileId), StringComparison.Ordinal) &&
                   InteractionMode == scope.InteractionMode;
        }

        private static string NormalizeToken(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

        private static string NormalizePath(string runtimeSource, string path)
        {
            var text = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
            if (text.Length == 0)
            {
                return string.Empty;
            }

            if (CodexRuntimeSources.IsRemoteUnixSsh(runtimeSource) ||
                string.Equals(runtimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeUnixPath(text);
            }

            return NormalizeWindowsPath(text);
        }

        private static string NormalizeUnixPath(string path)
        {
            var normalized = path.Replace('\\', '/');
            while (normalized.EndsWith("/", StringComparison.Ordinal) && normalized.Length > 1)
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized;
        }

        private static string NormalizeWindowsPath(string path)
        {
            var normalized = path.Replace('/', '\\');
            while (normalized.EndsWith("\\", StringComparison.Ordinal) && normalized.Length > 3)
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized.ToLowerInvariant();
        }
    }
}
