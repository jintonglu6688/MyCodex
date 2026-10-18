using System;
using System.Collections.Generic;
using System.IO;

namespace MyCodex.Application.Services
{
    public static class WorkspacePathUtilities
    {
        public static bool LooksLikeUnixAbsolutePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.TrimStart().StartsWith("/", StringComparison.Ordinal);
        }

        public static bool LooksLikeWindowsDriveAbsolutePath(string path)
        {
            var trimmedPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            return !string.IsNullOrWhiteSpace(trimmedPath) &&
                   trimmedPath.Length >= 3 &&
                   char.IsLetter(trimmedPath[0]) &&
                   trimmedPath[1] == ':' &&
                   (trimmedPath[2] == '\\' || trimmedPath[2] == '/');
        }

        public static bool LooksLikeWindowsUncAbsolutePath(string path)
        {
            var trimmedPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            return !string.IsNullOrWhiteSpace(trimmedPath) &&
                   trimmedPath.Length >= 5 &&
                   trimmedPath[0] == '\\' &&
                   trimmedPath[1] == '\\' &&
                   trimmedPath[2] != '\\';
        }

        public static bool LooksLikeWslMountedWindowsDrivePath(string path)
        {
            var trimmedPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            return !string.IsNullOrWhiteSpace(trimmedPath) &&
                   trimmedPath.Length >= 6 &&
                   trimmedPath[0] == '/' &&
                   string.Equals(trimmedPath.Substring(1, 4), "mnt/", StringComparison.OrdinalIgnoreCase) &&
                   char.IsLetter(trimmedPath[5]) &&
                   (trimmedPath.Length == 6 || trimmedPath[6] == '/');
        }

        public static string NormalizeUnixAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var trimmedPath = path.Trim().Trim('"').Replace('\\', '/');
            if (!LooksLikeUnixAbsolutePath(trimmedPath))
            {
                return trimmedPath;
            }

            var parts = new List<string>();
            foreach (var part in trimmedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    if (parts.Count > 0)
                    {
                        parts.RemoveAt(parts.Count - 1);
                    }

                    continue;
                }

                parts.Add(part);
            }

            return parts.Count == 0 ? "/" : "/" + string.Join("/", parts.ToArray());
        }

        /// <summary>
        /// Normalizes a Unix absolute path with NormalizeUnixAbsolutePath and any other path with Path.GetFullPath.
        /// Returns an empty string for blank input and the trimmed input when the path cannot be resolved.
        /// </summary>
        public static string NormalizeLocalOrUnixPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                if (LooksLikeUnixAbsolutePath(path))
                {
                    return NormalizeUnixAbsolutePath(path);
                }

                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        public static string GetWorkspaceName(string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return string.Empty;
            }

            var trimmedPath = normalizedPath.TrimEnd('\\', '/');
            if (trimmedPath.Length == 0)
            {
                return normalizedPath;
            }

            var separatorIndex = Math.Max(trimmedPath.LastIndexOf('\\'), trimmedPath.LastIndexOf('/'));
            var name = separatorIndex >= 0 ? trimmedPath.Substring(separatorIndex + 1) : trimmedPath;
            return string.IsNullOrWhiteSpace(name) ? normalizedPath : name;
        }
    }
}
