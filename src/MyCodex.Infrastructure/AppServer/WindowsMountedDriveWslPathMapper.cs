using System;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class WindowsMountedDriveWslPathMapper : IAppServerPathMapper
    {
        private const string LongPathPrefix = @"\\?\";

        public AppServerHostPlatform HostPlatform
        {
            get { return AppServerHostPlatform.Wsl; }
        }

        public bool TryMapToServerPath(string clientPath, out string serverPath)
        {
            serverPath = null;
            var path = NormalizeClientInput(clientPath);
            if (!IsWindowsDriveAbsolutePath(path))
            {
                return false;
            }

            var drive = char.ToLowerInvariant(path[0]);
            var rest = path.Substring(2).Replace('\\', '/');
            if (rest.Length == 0)
            {
                rest = "/";
            }

            if (rest[0] != '/')
            {
                rest = "/" + rest;
            }

            serverPath = "/mnt/" + drive + rest;
            return true;
        }

        public bool TryMapToClientPath(string serverPath, out string clientPath)
        {
            clientPath = null;
            var path = NormalizeServerInput(serverPath);
            if (!IsWslMountedDrivePath(path))
            {
                return false;
            }

            var drive = char.ToUpperInvariant(path[5]);
            var rest = path.Length == 6 ? string.Empty : path.Substring(7);
            clientPath = drive + @":\" + rest.Replace('/', '\\');
            return true;
        }

        private static string NormalizeClientInput(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalized = path.Trim();
            if (normalized.StartsWith(LongPathPrefix, StringComparison.Ordinal) &&
                normalized.Length > LongPathPrefix.Length + 2 &&
                char.IsLetter(normalized[LongPathPrefix.Length]) &&
                normalized[LongPathPrefix.Length + 1] == ':')
            {
                normalized = normalized.Substring(LongPathPrefix.Length);
            }

            return normalized;
        }

        private static string NormalizeServerInput(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        }

        private static bool IsWindowsDriveAbsolutePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.Length >= 3 &&
                   char.IsLetter(path[0]) &&
                   path[1] == ':' &&
                   (path[2] == '\\' || path[2] == '/');
        }

        private static bool IsWslMountedDrivePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.Length >= 6 &&
                   path[0] == '/' &&
                   string.Equals(path.Substring(1, 4), "mnt/", StringComparison.OrdinalIgnoreCase) &&
                   char.IsLetter(path[5]) &&
                   (path.Length == 6 || path[6] == '/');
        }
    }
}
