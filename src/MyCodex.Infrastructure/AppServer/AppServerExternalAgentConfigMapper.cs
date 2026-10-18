using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerExternalAgentConfigMapper
    {
        internal static ExternalAgentConfigDetectResult BuildDetectResult(IDictionary<string, object> result)
        {
            var detectResult = new ExternalAgentConfigDetectResult();
            foreach (var itemObject in GetArray(result, "items"))
            {
                var item = itemObject as IDictionary<string, object>;
                if (item == null)
                {
                    continue;
                }

                detectResult.Items.Add(BuildMigrationItem(item));
            }

            return detectResult;
        }

        internal static List<IDictionary<string, object>> ToProtocolMigrationItems(
            IEnumerable<ExternalAgentConfigMigrationItem> items)
        {
            var result = new List<IDictionary<string, object>>();
            if (items == null)
            {
                return result;
            }

            foreach (var item in ExternalAgentConfigImportPolicy.FilterImportable(items))
            {
                if (!HasMatchingRawItemType(item))
                {
                    continue;
                }

                result.Add(item.RawItem != null && item.RawItem.Count > 0
                    ? item.RawItem
                    : BuildProtocolMigrationItem(item));
            }

            return result;
        }

        private static bool HasMatchingRawItemType(ExternalAgentConfigMigrationItem item)
        {
            if (item == null)
            {
                return true;
            }

            var rawItemType = AppServerJsonValueReader.GetString(item.RawItem, "itemType");
            if (string.IsNullOrWhiteSpace(rawItemType))
            {
                rawItemType = item.RawItemType;
            }

            return string.IsNullOrWhiteSpace(rawItemType) || MapItemType(rawItemType) == item.ItemType;
        }

        private static ExternalAgentConfigMigrationItem BuildMigrationItem(IDictionary<string, object> item)
        {
            var rawType = AppServerJsonValueReader.GetString(item, "itemType");
            return new ExternalAgentConfigMigrationItem
            {
                ItemType = MapItemType(rawType),
                RawItemType = rawType,
                Description = AppServerJsonValueReader.GetString(item, "description"),
                Cwd = AppServerJsonValueReader.GetString(item, "cwd"),
                Details = BuildDetails(GetDictionary(item, "details")),
                RawItem = new Dictionary<string, object>(item, StringComparer.Ordinal)
            };
        }

        private static IDictionary<string, object> BuildProtocolMigrationItem(ExternalAgentConfigMigrationItem item)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            result["itemType"] = string.IsNullOrWhiteSpace(item.RawItemType)
                ? MapItemType(item.ItemType)
                : item.RawItemType;
            result["description"] = item.Description ?? string.Empty;
            result["cwd"] = item.Cwd;
            return result;
        }

        private static ExternalAgentConfigMigrationDetails BuildDetails(IDictionary<string, object> details)
        {
            var result = new ExternalAgentConfigMigrationDetails();
            if (details == null)
            {
                return result;
            }

            result.Plugins = GetArray(details, "plugins")
                .Select(item => BuildPlugin(item as IDictionary<string, object>))
                .Where(item => item != null)
                .ToList();
            result.Sessions = GetArray(details, "sessions")
                .Select(item => BuildSession(item as IDictionary<string, object>))
                .Where(item => item != null)
                .ToList();
            result.McpServers = GetArray(details, "mcpServers")
                .Select(item => BuildNamed(item as IDictionary<string, object>))
                .Where(item => item != null)
                .ToList();
            result.Hooks = GetArray(details, "hooks")
                .Select(item => BuildNamed(item as IDictionary<string, object>))
                .Where(item => item != null)
                .ToList();
            result.Subagents = GetArray(details, "subagents")
                .Select(item => BuildNamed(item as IDictionary<string, object>))
                .Where(item => item != null)
                .ToList();
            result.Commands = GetArray(details, "commands")
                .Select(item => BuildNamed(item as IDictionary<string, object>))
                .Where(item => item != null)
                .ToList();
            return result;
        }

        private static ExternalAgentConfigPluginMigration BuildPlugin(IDictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            return new ExternalAgentConfigPluginMigration
            {
                MarketplaceName = AppServerJsonValueReader.GetString(source, "marketplaceName"),
                PluginNames = GetStringList(source, "pluginNames")
            };
        }

        private static ExternalAgentConfigSessionMigration BuildSession(IDictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            return new ExternalAgentConfigSessionMigration
            {
                Path = AppServerJsonValueReader.GetString(source, "path"),
                Cwd = AppServerJsonValueReader.GetString(source, "cwd"),
                Title = AppServerJsonValueReader.GetString(source, "title")
            };
        }

        private static ExternalAgentConfigNamedMigration BuildNamed(IDictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            var name = AppServerJsonValueReader.GetString(source, "name");
            return string.IsNullOrWhiteSpace(name)
                ? null
                : new ExternalAgentConfigNamedMigration { Name = name.Trim() };
        }

        private static ExternalAgentConfigMigrationItemType MapItemType(string rawType)
        {
            switch ((rawType ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "AGENTS_MD":
                    return ExternalAgentConfigMigrationItemType.AgentsMd;
                case "CONFIG":
                    return ExternalAgentConfigMigrationItemType.Config;
                case "SKILLS":
                    return ExternalAgentConfigMigrationItemType.Skills;
                case "PLUGINS":
                    return ExternalAgentConfigMigrationItemType.Plugins;
                case "MCP_SERVER_CONFIG":
                    return ExternalAgentConfigMigrationItemType.McpServerConfig;
                case "SUBAGENTS":
                    return ExternalAgentConfigMigrationItemType.Subagents;
                case "HOOKS":
                    return ExternalAgentConfigMigrationItemType.Hooks;
                case "COMMANDS":
                    return ExternalAgentConfigMigrationItemType.Commands;
                case "SESSIONS":
                    return ExternalAgentConfigMigrationItemType.Sessions;
                default:
                    return ExternalAgentConfigMigrationItemType.Unknown;
            }
        }

        private static string MapItemType(ExternalAgentConfigMigrationItemType itemType)
        {
            switch (itemType)
            {
                case ExternalAgentConfigMigrationItemType.AgentsMd:
                    return "AGENTS_MD";
                case ExternalAgentConfigMigrationItemType.Config:
                    return "CONFIG";
                case ExternalAgentConfigMigrationItemType.Skills:
                    return "SKILLS";
                case ExternalAgentConfigMigrationItemType.Plugins:
                    return "PLUGINS";
                case ExternalAgentConfigMigrationItemType.McpServerConfig:
                    return "MCP_SERVER_CONFIG";
                case ExternalAgentConfigMigrationItemType.Subagents:
                    return "SUBAGENTS";
                case ExternalAgentConfigMigrationItemType.Hooks:
                    return "HOOKS";
                case ExternalAgentConfigMigrationItemType.Commands:
                    return "COMMANDS";
                case ExternalAgentConfigMigrationItemType.Sessions:
                    return "SESSIONS";
                default:
                    return "UNKNOWN";
            }
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null &&
                source.TryGetValue(key, out value) &&
                value is IDictionary<string, object>
                    ? (IDictionary<string, object>)value
                    : null;
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null || value is string)
            {
                return Enumerable.Empty<object>();
            }

            var array = value as IEnumerable<object>;
            return array ?? Enumerable.Empty<object>();
        }

        private static List<string> GetStringList(IDictionary<string, object> source, string key)
        {
            var result = new List<string>();
            foreach (var item in GetArray(source, key))
            {
                var text = item == null ? null : Convert.ToString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(text.Trim());
                }
            }

            return result;
        }
    }
}
