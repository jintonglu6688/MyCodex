using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Skills
{
    internal enum SkillSourceKind
    {
        Unknown,
        System,
        User,
        Workspace,
        Admin,
        Plugin
    }

    internal sealed class SkillSourceInfo
    {
        public SkillSourceInfo(
            SkillSourceKind kind,
            string displayText,
            string pluginDisplayName,
            string marketplaceName,
            string sourcePath)
        {
            Kind = kind;
            DisplayText = displayText ?? string.Empty;
            PluginDisplayName = pluginDisplayName ?? string.Empty;
            MarketplaceName = marketplaceName ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
        }

        public SkillSourceKind Kind { get; private set; }

        public string DisplayText { get; private set; }

        public string PluginDisplayName { get; private set; }

        public string MarketplaceName { get; private set; }

        public string SourcePath { get; private set; }
    }

    internal static class SkillSourceResolver
    {
        public static SkillSourceInfo Resolve(
            AppServerSkillInfo skill,
            string workspacePath,
            IEnumerable<AppServerPluginSummary> plugins)
        {
            if (skill == null)
            {
                return CreateSimple(SkillSourceKind.Unknown);
            }

            var skillPath = PortableSkillPath.Normalize(ResolveSkillPath(skill.Path));
            var plugin = FindPluginBySourcePath(skillPath, plugins);
            if (plugin != null)
            {
                return CreatePlugin(
                    GetPluginDisplayName(plugin),
                    plugin.MarketplaceName,
                    plugin.SourcePath);
            }

            string marketplaceName;
            string pluginName;
            string pluginRootPath;
            if (TryInferPluginFromCachePath(skillPath, out marketplaceName, out pluginName, out pluginRootPath))
            {
                return CreatePlugin(pluginName, marketplaceName, pluginRootPath);
            }

            if (IsCodexSystemSkillPath(skillPath))
            {
                return CreateSimple(SkillSourceKind.System);
            }

            if (IsWorkspaceSkillPath(skillPath, workspacePath))
            {
                return CreateSimple(SkillSourceKind.Workspace);
            }

            if (IsCodexUserSkillPath(skillPath))
            {
                return CreateSimple(SkillSourceKind.User);
            }

            switch ((skill.Scope ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "plugin":
                    return CreatePlugin(null, null, null);
                case "system":
                    return CreateSimple(SkillSourceKind.System);
                case "repo":
                    return CreateSimple(SkillSourceKind.Workspace);
                case "user":
                    return CreateSimple(SkillSourceKind.User);
                case "admin":
                    return CreateSimple(SkillSourceKind.Admin);
                default:
                    return CreateSimple(SkillSourceKind.Unknown);
            }
        }

        private static AppServerPluginSummary FindPluginBySourcePath(
            string skillPath,
            IEnumerable<AppServerPluginSummary> plugins)
        {
            if (string.IsNullOrWhiteSpace(skillPath) || plugins == null)
            {
                return null;
            }

            return plugins
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.SourcePath))
                .Select(item => new
                {
                    Plugin = item,
                    Root = PortableSkillPath.Normalize(item.SourcePath)
                })
                .Where(item => IsPathUnderRoot(skillPath, item.Root))
                .OrderByDescending(item => item.Root.Length)
                .Select(item => item.Plugin)
                .FirstOrDefault();
        }

        private static bool TryInferPluginFromCachePath(
            string skillPath,
            out string marketplaceName,
            out string pluginName,
            out string pluginRootPath)
        {
            marketplaceName = null;
            pluginName = null;
            pluginRootPath = null;

            if (string.IsNullOrWhiteSpace(skillPath))
            {
                return false;
            }

            var marker = "/.codex/plugins/cache/";
            var markerIndex = skillPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return false;
            }

            var prefix = skillPath.Substring(0, markerIndex + marker.Length);
            var remainder = skillPath.Substring(markerIndex + marker.Length);
            var parts = remainder.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4 || !string.Equals(parts[3], "skills", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            marketplaceName = parts[0];
            pluginName = parts[1];
            pluginRootPath = prefix + parts[0] + "/" + parts[1] + "/" + parts[2];
            return true;
        }

        private static bool IsCodexSystemSkillPath(string skillPath)
        {
            return ContainsSegmentSequence(skillPath, ".codex", "skills", ".system");
        }

        private static bool IsCodexUserSkillPath(string skillPath)
        {
            return ContainsSegmentSequence(skillPath, ".codex", "skills") &&
                   !ContainsSegmentSequence(skillPath, ".codex", "skills", ".system");
        }

        private static bool IsWorkspaceSkillPath(string skillPath, string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(skillPath) || string.IsNullOrWhiteSpace(workspacePath))
            {
                return false;
            }

            var workspaceSkillsRoot = PortableSkillPath.Normalize(CombinePortable(workspacePath, ".codex", "skills"));
            return IsPathUnderRoot(skillPath, workspaceSkillsRoot);
        }

        private static bool ContainsSegmentSequence(string path, params string[] expectedSegments)
        {
            if (string.IsNullOrWhiteSpace(path) || expectedSegments == null || expectedSegments.Length == 0)
            {
                return false;
            }

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index <= segments.Length - expectedSegments.Length; index++)
            {
                var matches = true;
                for (var offset = 0; offset < expectedSegments.Length; offset++)
                {
                    if (!string.Equals(segments[index + offset], expectedSegments[offset], StringComparison.OrdinalIgnoreCase))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPathUnderRoot(string path, string root)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(root.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveSkillPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalized = PortableSkillPath.Normalize(path);
            return normalized.EndsWith("/" + SkillFileEditor.SkillFileName, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(0, normalized.Length - SkillFileEditor.SkillFileName.Length - 1)
                : normalized;
        }

        private static string CombinePortable(params string[] parts)
        {
            return string.Join(
                "/",
                (parts ?? new string[0])
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .Select((part, index) => index == 0
                        ? part.Trim().TrimEnd('\\', '/')
                        : part.Trim().Trim('\\', '/')));
        }

        private static string GetPluginDisplayName(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return null;
            }

            return FirstNonEmpty(plugin.DisplayName, plugin.Name, plugin.PluginId);
        }

        private static SkillSourceInfo CreateSimple(SkillSourceKind kind)
        {
            return new SkillSourceInfo(kind, GetSimpleDisplayText(kind), null, null, null);
        }

        private static SkillSourceInfo CreatePlugin(string pluginDisplayName, string marketplaceName, string sourcePath)
        {
            pluginDisplayName = FirstNonEmpty(pluginDisplayName);
            marketplaceName = FirstNonEmpty(marketplaceName);

            string displayText;
            if (!string.IsNullOrWhiteSpace(pluginDisplayName) && !string.IsNullOrWhiteSpace(marketplaceName))
            {
                displayText = string.Format(
                    UiLocalization.Text("SkillSourceResolver.Source.PluginWithMarketplace", "Plugin: {0} ({1})"),
                    pluginDisplayName,
                    marketplaceName);
            }
            else if (!string.IsNullOrWhiteSpace(pluginDisplayName))
            {
                displayText = string.Format(
                    UiLocalization.Text("SkillSourceResolver.Source.PluginWithName", "Plugin: {0}"),
                    pluginDisplayName);
            }
            else
            {
                displayText = UiLocalization.Text("SkillSourceResolver.Source.Plugin", "Plugin");
            }

            return new SkillSourceInfo(
                SkillSourceKind.Plugin,
                displayText,
                pluginDisplayName,
                marketplaceName,
                sourcePath);
        }

        private static string GetSimpleDisplayText(SkillSourceKind kind)
        {
            switch (kind)
            {
                case SkillSourceKind.System:
                    return UiLocalization.Text("SkillSourceResolver.Source.System", "System");
                case SkillSourceKind.User:
                    return UiLocalization.Text("SkillSourceResolver.Source.User", "User");
                case SkillSourceKind.Workspace:
                    return UiLocalization.Text("SkillSourceResolver.Source.Workspace", "Workspace");
                case SkillSourceKind.Admin:
                    return UiLocalization.Text("SkillSourceResolver.Source.Admin", "Admin");
                default:
                    return UiLocalization.Text("SkillSourceResolver.Source.Unknown", "Unknown");
            }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return null;
            }

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }
    }
}
