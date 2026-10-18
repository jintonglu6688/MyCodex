using System;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Constants;

namespace MyCodex.WinForms.Forms
{
    internal static class AppServerFileWindowLocalPathPolicy
    {
        public static bool IsLocalWindowsFile(AppServerFileWindowRuntimeScope scope, string path)
        {
            string localPath;
            return TryGetLocalWindowsFilePath(scope, path, out localPath);
        }

        public static bool TryGetLocalWindowsFilePath(AppServerFileWindowRuntimeScope scope, string path, out string localPath)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                CodexRuntimeSources.IsRemoteUnixSsh(scope != null ? scope.RuntimeSource : null))
            {
                localPath = null;
                return false;
            }

            var normalizedPath = path.Trim();
            var runtimeSource = scope != null ? scope.RuntimeSource : null;
            if (WorkspacePathUtilities.LooksLikeWindowsDriveAbsolutePath(normalizedPath) ||
                WorkspacePathUtilities.LooksLikeWindowsUncAbsolutePath(normalizedPath))
            {
                localPath = normalizedPath;
                return true;
            }

            if (string.Equals(runtimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase))
            {
                var mapper = new WindowsMountedDriveWslPathMapper();
                if (mapper.TryMapToClientPath(normalizedPath, out localPath))
                {
                    return true;
                }
            }

            localPath = null;
            return false;
        }
    }
}
