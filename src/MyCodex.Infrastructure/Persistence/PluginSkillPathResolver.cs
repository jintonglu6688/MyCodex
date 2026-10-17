using System;
using System.IO;
using System.Linq;

namespace MyCodex.Infrastructure.Persistence
{
    public static class PluginSkillPathResolver
    {
        private const string SkillFileName = "SKILL.md";
        private const string PluginManifestRelativePath = ".codex-plugin/plugin.json";

        public static bool TryResolveCurrentPath(string originalPath, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(originalPath))
            {
                return false;
            }

            var trimmedPath = originalPath.Trim();
            PluginCachePathParts parts;
            if (!TryParsePluginCachePath(NormalizeSeparators(trimmedPath), out parts))
            {
                if (PathExists(trimmedPath))
                {
                    resolvedPath = trimmedPath;
                    return true;
                }

                return false;
            }

            if (IsCurrentPluginCachePath(trimmedPath, parts))
            {
                resolvedPath = trimmedPath;
                return true;
            }

            return parts.IsSkillPath
                ? TryResolveSkillPath(parts, out resolvedPath)
                : TryResolvePluginRootPath(parts, out resolvedPath);
        }

        private static bool IsCurrentPluginCachePath(string path, PluginCachePathParts parts)
        {
            if (parts == null || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (parts.IsSkillPath)
            {
                return File.Exists(Path.Combine(path, SkillFileName));
            }

            return File.Exists(Path.Combine(path, ToNativeRelativePath(PluginManifestRelativePath)));
        }

        private static bool TryResolveSkillPath(PluginCachePathParts parts, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(parts.PluginRootParent) ||
                string.IsNullOrWhiteSpace(parts.SkillRelativePath))
            {
                return false;
            }

            foreach (var candidateRoot in EnumerateCandidateRoots(parts.PluginRootParent))
            {
                var candidate = Path.Combine(candidateRoot, "skills", ToNativeRelativePath(parts.SkillRelativePath));
                if (File.Exists(Path.Combine(candidate, SkillFileName)))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolvePluginRootPath(PluginCachePathParts parts, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(parts.PluginRootParent))
            {
                return false;
            }

            foreach (var candidateRoot in EnumerateCandidateRoots(parts.PluginRootParent))
            {
                if (File.Exists(Path.Combine(candidateRoot, ToNativeRelativePath(PluginManifestRelativePath))))
                {
                    resolvedPath = candidateRoot;
                    return true;
                }
            }

            return false;
        }

        private static string[] EnumerateCandidateRoots(string pluginRootParent)
        {
            try
            {
                if (!Directory.Exists(pluginRootParent))
                {
                    return new string[0];
                }

                return Directory.GetDirectories(pluginRootParent)
                    .OrderByDescending(Directory.GetLastWriteTimeUtc)
                    .ThenByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return new string[0];
            }
        }

        private static bool TryParsePluginCachePath(string normalizedPath, out PluginCachePathParts parts)
        {
            parts = null;
            const string Marker = "/plugins/cache/";
            var markerIndex = normalizedPath.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return false;
            }

            var prefix = normalizedPath.Substring(0, markerIndex + Marker.Length);
            var remainder = normalizedPath.Substring(markerIndex + Marker.Length);
            var segments = remainder.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3)
            {
                return false;
            }

            var pluginRootParent = ToNativeAbsolutePath(prefix + segments[0] + "/" + segments[1]);
            var isSkillPath = segments.Length >= 5 &&
                string.Equals(segments[3], "skills", StringComparison.OrdinalIgnoreCase);
            var skillRelativePath = isSkillPath
                ? string.Join("/", segments.Skip(4).ToArray())
                : null;

            parts = new PluginCachePathParts(pluginRootParent, isSkillPath, skillRelativePath);
            return true;
        }

        private static bool PathExists(string path)
        {
            try
            {
                return File.Exists(path) || Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeSeparators(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        private static string ToNativeAbsolutePath(string normalizedPath)
        {
            return normalizedPath.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string ToNativeRelativePath(string normalizedPath)
        {
            return normalizedPath.Replace('/', Path.DirectorySeparatorChar);
        }

        private sealed class PluginCachePathParts
        {
            public PluginCachePathParts(string pluginRootParent, bool isSkillPath, string skillRelativePath)
            {
                PluginRootParent = pluginRootParent;
                IsSkillPath = isSkillPath;
                SkillRelativePath = skillRelativePath;
            }

            public string PluginRootParent { get; private set; }

            public bool IsSkillPath { get; private set; }

            public string SkillRelativePath { get; private set; }
        }
    }
}
