using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerPluginSkillMapper
    {
        internal static List<AppServerSkillsSnapshot> BuildSkillsSnapshots(
            IDictionary<string, object> result,
            Func<string, string> mapServerPathToClient,
            Func<string, string, string, string> resolveCurrentPluginSkillPath)
        {
            var snapshots = new List<AppServerSkillsSnapshot>();
            foreach (var item in GetArray(result, "data"))
            {
                var entry = item as IDictionary<string, object>;
                if (entry == null)
                {
                    continue;
                }

                snapshots.Add(new AppServerSkillsSnapshot
                {
                    WorkspacePath = mapServerPathToClient(GetString(entry, "cwd")),
                    Skills = BuildSkillInfos(entry, mapServerPathToClient, resolveCurrentPluginSkillPath),
                    Errors = BuildSkillErrors(entry, mapServerPathToClient)
                });
            }

            return snapshots
                .OrderBy(item => item.WorkspacePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static List<AppServerPluginSummary> BuildPluginSummaries(IDictionary<string, object> result)
        {
            var plugins = new List<AppServerPluginSummary>();
            foreach (var item in GetArray(result, "marketplaces"))
            {
                var marketplace = item as IDictionary<string, object>;
                if (marketplace == null)
                {
                    continue;
                }

                var marketplaceInterface = GetDictionary(marketplace, "interface");
                var marketplaceId = GetString(marketplace, "name");
                var marketplaceName = GetString(marketplaceInterface, "displayName") ?? marketplaceId;
                var marketplacePath = GetString(marketplace, "path");
                foreach (var pluginItem in GetArray(marketplace, "plugins"))
                {
                    var plugin = pluginItem as IDictionary<string, object>;
                    var summary = BuildPluginSummary(plugin, marketplaceName, marketplaceId, marketplacePath);
                    if (summary != null)
                    {
                        plugins.Add(summary);
                    }
                }
            }

            return plugins
                .OrderBy(item => item.MarketplaceName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.DisplayName ?? item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PluginId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static AppServerPluginInstalledSnapshot BuildInstalledPluginSnapshot(
            IDictionary<string, object> result)
        {
            return new AppServerPluginInstalledSnapshot
            {
                Plugins = BuildPluginSummaries(result),
                MarketplaceErrors = BuildMarketplaceLoadErrors(result)
            };
        }

        private static List<AppServerMarketplaceLoadError> BuildMarketplaceLoadErrors(
            IDictionary<string, object> result)
        {
            var errors = new List<AppServerMarketplaceLoadError>();
            foreach (var item in GetArray(result, "marketplaceLoadErrors"))
            {
                var error = item as IDictionary<string, object>;
                if (error == null)
                {
                    continue;
                }

                errors.Add(new AppServerMarketplaceLoadError
                {
                    MarketplaceName = GetString(error, "marketplacePath"),
                    Message = GetString(error, "message")
                });
            }

            return errors;
        }

        internal static AppServerPluginDetails BuildPluginDetails(
            IDictionary<string, object> result,
            Func<string, string> mapServerPathToClient,
            Func<string, string, string, string> resolveCurrentPluginSkillPath)
        {
            var plugin = GetDictionary(result, "plugin");
            var marketplaceId = GetString(plugin, "marketplaceName");
            var marketplaceName = GetString(plugin, "marketplaceDisplayName") ?? marketplaceId;
            var marketplacePath = GetString(plugin, "marketplacePath");
            var summary = BuildPluginSummary(GetDictionary(plugin, "summary"), marketplaceName, marketplaceId, marketplacePath) ??
                          new AppServerPluginSummary
                          {
                              MarketplaceName = marketplaceName,
                              MarketplaceId = marketplaceId,
                              MarketplacePath = marketplacePath
                          };

            return new AppServerPluginDetails
            {
                MarketplaceName = marketplaceName,
                MarketplacePath = marketplacePath,
                Summary = summary,
                Description = GetString(plugin, "description"),
                Skills = BuildPluginSkillInfos(plugin, mapServerPathToClient, resolveCurrentPluginSkillPath),
                Apps = BuildPluginApps(plugin, "apps"),
                McpServers = BuildStringList(plugin, "mcpServers")
            };
        }

        internal static List<AppServerPluginAppInfo> BuildPluginApps(IDictionary<string, object> source, string key)
        {
            var result = new List<AppServerPluginAppInfo>();
            foreach (var item in GetArray(source, key))
            {
                var app = item as IDictionary<string, object>;
                if (app == null)
                {
                    continue;
                }

                result.Add(new AppServerPluginAppInfo
                {
                    Id = GetString(app, "id"),
                    Name = GetString(app, "name"),
                    Description = GetString(app, "description"),
                    InstallUrl = GetString(app, "installUrl"),
                    NeedsAuth = GetNullableBool(app, "needsAuth") ??
                                string.Equals(key, "appsNeedingAuth", StringComparison.Ordinal)
                });
            }

            return result
                .OrderBy(item => item.Name ?? item.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static AppServerPluginSummary BuildPluginSummary(
            IDictionary<string, object> plugin,
            string marketplaceName,
            string marketplaceId,
            string marketplacePath)
        {
            if (plugin == null)
            {
                return null;
            }

            var pluginInterface = GetDictionary(plugin, "interface");
            var source = GetDictionary(plugin, "source");
            var name = GetString(plugin, "name");
            var displayName = GetString(pluginInterface, "displayName");

            return new AppServerPluginSummary
            {
                MarketplaceName = marketplaceName,
                MarketplaceId = marketplaceId,
                MarketplacePath = marketplacePath,
                PluginId = GetString(plugin, "id"),
                RemotePluginId = GetString(plugin, "remotePluginId"),
                Name = name,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
                ShortDescription = GetString(pluginInterface, "shortDescription") ?? GetString(plugin, "shortDescription"),
                LongDescription = GetString(pluginInterface, "longDescription"),
                DeveloperName = GetString(pluginInterface, "developerName"),
                Category = GetString(pluginInterface, "category"),
                SourceType = GetString(source, "type"),
                SourcePath = GetString(source, "path"),
                Installed = GetNullableBool(plugin, "installed") ?? false,
                Enabled = GetNullableBool(plugin, "enabled") ?? false,
                Version = GetString(plugin, "version"),
                LocalVersion = GetString(plugin, "localVersion"),
                InstallPolicy = GetString(plugin, "installPolicy"),
                InstallPolicySource = GetString(plugin, "installPolicySource"),
                AuthPolicy = GetString(plugin, "authPolicy"),
                WebsiteUrl = GetString(pluginInterface, "websiteUrl"),
                PrivacyPolicyUrl = GetString(pluginInterface, "privacyPolicyUrl"),
                TermsOfServiceUrl = GetString(pluginInterface, "termsOfServiceUrl"),
                ComposerIconPath = GetString(pluginInterface, "composerIcon"),
                LogoPath = GetString(pluginInterface, "logo"),
                BrandColor = GetString(pluginInterface, "brandColor"),
                Capabilities = BuildStringList(pluginInterface, "capabilities"),
                DefaultPrompts = BuildStringList(pluginInterface, "defaultPrompt")
            };
        }

        private static List<AppServerSkillInfo> BuildSkillInfos(
            IDictionary<string, object> entry,
            Func<string, string> mapServerPathToClient,
            Func<string, string, string, string> resolveCurrentPluginSkillPath)
        {
            var result = new List<AppServerSkillInfo>();
            foreach (var item in GetArray(entry, "skills"))
            {
                var skill = item as IDictionary<string, object>;
                if (skill == null)
                {
                    continue;
                }

                var skillInterface = GetDictionary(skill, "interface");
                var dependencies = GetDictionary(skill, "dependencies");
                var name = GetString(skill, "name");
                var displayName = GetString(skillInterface, "displayName");
                var mappedPath = mapServerPathToClient(GetString(skill, "path"));
                var resolvedPath = resolveCurrentPluginSkillPath(mappedPath, "skills/list", name);

                result.Add(new AppServerSkillInfo
                {
                    Name = name,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
                    ShortDescription = GetString(skillInterface, "shortDescription") ?? GetString(skill, "shortDescription"),
                    Description = GetString(skill, "description"),
                    Enabled = GetNullableBool(skill, "enabled") ?? false,
                    Path = resolvedPath,
                    Scope = GetString(skill, "scope"),
                    DefaultPrompt = GetString(skillInterface, "defaultPrompt"),
                    BrandColor = GetString(skillInterface, "brandColor"),
                    ToolDependencies = BuildSkillToolDependencies(dependencies)
                });
            }

            return result
                .OrderBy(item => item.DisplayName ?? item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Path ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<AppServerPluginSkillInfo> BuildPluginSkillInfos(
            IDictionary<string, object> source,
            Func<string, string> mapServerPathToClient,
            Func<string, string, string, string> resolveCurrentPluginSkillPath)
        {
            var result = new List<AppServerPluginSkillInfo>();
            foreach (var item in GetArray(source, "skills"))
            {
                var skill = item as IDictionary<string, object>;
                if (skill == null)
                {
                    continue;
                }

                var skillInterface = GetDictionary(skill, "interface");
                var name = GetString(skill, "name");
                var displayName = GetString(skillInterface, "displayName");
                var mappedPath = mapServerPathToClient(GetString(skill, "path"));
                var resolvedPath = resolveCurrentPluginSkillPath(mappedPath, "plugin/details", name);

                result.Add(new AppServerPluginSkillInfo
                {
                    Name = name,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
                    ShortDescription = GetString(skillInterface, "shortDescription") ?? GetString(skill, "shortDescription"),
                    Description = GetString(skill, "description"),
                    Path = resolvedPath,
                    Enabled = GetNullableBool(skill, "enabled") ?? false,
                    DefaultPrompt = GetString(skillInterface, "defaultPrompt")
                });
            }

            return result
                .OrderBy(item => item.DisplayName ?? item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Path ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<AppServerSkillError> BuildSkillErrors(
            IDictionary<string, object> entry,
            Func<string, string> mapServerPathToClient)
        {
            var result = new List<AppServerSkillError>();
            foreach (var item in GetArray(entry, "errors"))
            {
                var error = item as IDictionary<string, object>;
                if (error == null)
                {
                    continue;
                }

                result.Add(new AppServerSkillError
                {
                    Path = mapServerPathToClient(GetString(error, "path")),
                    Message = GetString(error, "message")
                });
            }

            return result
                .OrderBy(item => item.Path ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Message ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<AppServerSkillToolDependency> BuildSkillToolDependencies(IDictionary<string, object> dependencies)
        {
            var result = new List<AppServerSkillToolDependency>();
            foreach (var item in GetArray(dependencies, "tools"))
            {
                var tool = item as IDictionary<string, object>;
                if (tool == null)
                {
                    continue;
                }

                result.Add(new AppServerSkillToolDependency
                {
                    Type = GetString(tool, "type"),
                    Value = GetString(tool, "value"),
                    Description = GetString(tool, "description"),
                    Command = GetString(tool, "command"),
                    Transport = GetString(tool, "transport"),
                    Url = GetString(tool, "url")
                });
            }

            return result;
        }

        private static List<string> BuildStringList(IDictionary<string, object> source, string key)
        {
            return BuildStringList(GetArray(source, key));
        }

        private static List<string> BuildStringList(IEnumerable<object> items)
        {
            var result = new List<string>();
            foreach (var item in items ?? Enumerable.Empty<object>())
            {
                var text = item == null ? null : Convert.ToString(item);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                text = text.Trim();
                if (!result.Any(existing => string.Equals(existing, text, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(text);
                }
            }

            return result;
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetDictionary(source, key);
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetString(source, key);
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetArray(source, key);
        }

        private static bool? GetNullableBool(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetNullableBool(source, key);
        }
    }
}
