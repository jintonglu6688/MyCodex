using System;
using System.IO;
using System.Linq;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public sealed class TargetStartupSelection
    {
        public TargetProfileSnapshot Target { get; set; }
        public string WorkspacePath { get; set; }
    }

    public static class TargetStartupPlanner
    {
        public static TargetStartupSelection Resolve(TargetCatalogSnapshot catalog, string explicitTargetId, string workspacePath)
        {
            if (catalog == null || catalog.Targets == null)
            {
                throw new InvalidOperationException("The target catalog is unavailable.");
            }

            var requestedId = string.IsNullOrWhiteSpace(explicitTargetId) ? catalog.LastOpenedTargetId : explicitTargetId.Trim();
            if (!TargetMigrationPlanner.IsSafeTargetId(requestedId))
            {
                throw new InvalidOperationException("The requested target ID is invalid.");
            }
            var path = string.IsNullOrWhiteSpace(workspacePath) ? string.Empty : workspacePath.Trim().Trim('"');
            if (path.IndexOf('\0') >= 0 || path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0)
            {
                throw new InvalidOperationException("The workspace path is invalid.");
            }

            string distribution;
            string unixPath;
            var wslUnc = TryParseWslUnc(path, out distribution, out unixPath);
            if (string.IsNullOrWhiteSpace(explicitTargetId) && path.Length > 0)
            {
                if (wslUnc)
                {
                    var wsl = catalog.Targets.SingleOrDefault(t => t.Kind == "Wsl" &&
                        string.Equals(t.WslDistributionName, distribution, StringComparison.OrdinalIgnoreCase));
                    if (wsl == null) throw new InvalidOperationException("No configured target matches this WSL distribution.");
                    requestedId = wsl.Id;
                }
                else if (!WorkspacePathUtilities.LooksLikeUnixAbsolutePath(path))
                {
                    requestedId = "windows";
                }
            }

            var target = catalog.Targets.SingleOrDefault(t => string.Equals(t.Id, requestedId, StringComparison.Ordinal));
            if (target == null) throw new InvalidOperationException("The requested target no longer exists. Select a configured target.");
            if (path.Length > 0)
            {
                if (wslUnc)
                {
                    if (target.Kind != "Wsl" || !string.Equals(target.WslDistributionName, distribution, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("This WSL path does not belong to the selected target.");
                    path = unixPath;
                }
                else if (target.Kind == "Windows")
                {
                    if (WorkspacePathUtilities.LooksLikeUnixAbsolutePath(path))
                        throw new InvalidOperationException("Choose a WSL or SSH target for a Unix workspace path.");
                    path = Path.GetFullPath(path);
                }
                else if (!WorkspacePathUtilities.LooksLikeUnixAbsolutePath(path))
                {
                    throw new InvalidOperationException("A WSL or SSH workspace requires an absolute Unix path.");
                }
            }
            return new TargetStartupSelection { Target = target, WorkspacePath = path };
        }

        private static bool TryParseWslUnc(string path, out string distribution, out string unixPath)
        {
            distribution = null;
            unixPath = null;
            var normalized = path.Replace('/', '\\');
            var prefixes = new[] { @"\\wsl$\", @"\\wsl.localhost\" };
            foreach (var prefix in prefixes)
            {
                if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                var remainder = normalized.Substring(prefix.Length);
                var separator = remainder.IndexOf('\\');
                distribution = separator < 0 ? remainder : remainder.Substring(0, separator);
                if (string.IsNullOrWhiteSpace(distribution)) throw new InvalidOperationException("The WSL distribution is missing.");
                unixPath = separator < 0 ? "/" : "/" + remainder.Substring(separator + 1).Replace('\\', '/');
                return true;
            }
            return false;
        }
    }
}
