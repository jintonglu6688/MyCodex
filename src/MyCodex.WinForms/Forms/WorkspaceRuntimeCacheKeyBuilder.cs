using System;
using MyCodex.Application.Services;

namespace MyCodex.WinForms.Forms
{
    internal static class WorkspaceRuntimeCacheKeyBuilder
    {
        internal static string Build(string executionScopeId, string path)
        {
            var scope = string.IsNullOrWhiteSpace(executionScopeId)
                ? "windows"
                : executionScopeId.Trim().ToLowerInvariant();
            var normalizedPath = (path ?? string.Empty).Trim();
            if (!WorkspacePathUtilities.LooksLikeUnixAbsolutePath(normalizedPath))
            {
                normalizedPath = normalizedPath.ToUpperInvariant();
            }

            return scope + "|" + normalizedPath;
        }
    }
}
