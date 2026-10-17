using System;
using System.IO;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerPathUtilities
    {
        internal static string NormalizeDisplayPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            const string windowsLongPathPrefix = @"\\?\";
            return path.StartsWith(windowsLongPathPrefix, StringComparison.Ordinal)
                ? path.Substring(windowsLongPathPrefix.Length)
                : path;
        }

        internal static string NormalizeLocalPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Path.GetFullPath(path.Trim().Trim('"'));
        }

        internal static bool LooksLikeUnixAbsolutePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.TrimStart().StartsWith("/", StringComparison.Ordinal);
        }
    }
}
