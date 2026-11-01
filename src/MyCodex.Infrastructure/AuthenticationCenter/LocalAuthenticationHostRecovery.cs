using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal static class LocalAuthenticationHostRecovery
    {
        // Caller holds the shared store's lifecycle gate throughout validation, shutdown and restart.
        internal static async Task StopIdleResidentAsync(IDictionary<string, object> status, string home, string store,
            bool hasOtherUsers, Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> request,
            CancellationToken token)
        {
            if (hasOtherUsers) throw new AuthenticationCenterException("backend_version_mismatch");
            try
            {
                if (!SamePath(JsonValueReader.GetString(status, "codexHome"), home) ||
                    !SamePath(JsonValueReader.GetString(status, "dataDir"), store))
                    throw new AuthenticationCenterException("backend_version_mismatch");
                var artifact = AuthenticationHostArtifact.LoadForUpdate(NormalizePath(JsonValueReader.GetString(status, "executablePath")));
                artifact.ValidateIdentity(status);
            }
            catch (Exception)
            {
                // An unknown resident is never permission to stop a service or mutate its configuration.
                throw new AuthenticationCenterException("backend_version_mismatch");
            }
            token.ThrowIfCancellationRequested();
            var result = await request("backend/shutdown", new Dictionary<string, object> { { "forUpdate", true } }, token).ConfigureAwait(false);
            if (!result.TryGetValue("status", out var value) || !Equals(value, "shutting_down"))
                throw new AuthenticationCenterException("invalid_response");
        }

        private static bool SamePath(string first, string second)
        {
            return string.Equals(NormalizePath(first), NormalizePath(second), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new AuthenticationCenterException("backend_version_mismatch");
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)) path = path.Substring(4);
            if (!WorkspacePathUtilities.LooksLikeWindowsDriveAbsolutePath(path) && !WorkspacePathUtilities.LooksLikeWindowsUncAbsolutePath(path))
                throw new AuthenticationCenterException("backend_version_mismatch");
            return Path.GetFullPath(path).TrimEnd('\\');
        }
    }
}
