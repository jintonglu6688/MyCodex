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
            foreach (var item in AppServerJsonValueReader.GetArray(result, "data"))
            {
                var entry = item as IDictionary<string, object>;
                if (entry == null)
                {
                    continue;
                }

                snapshots.Add(new AppServerSkillsSnapshot
                {
                    WorkspacePath = mapServerPathToClient(AppServerJsonValueReader.GetString(entry, "cwd")),
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
            foreach (var item in AppServerJsonValueReader.GetArray(result, "marketplaces"))
            {
                var marketplace = item as IDictionary<string, object>;
                if (marketplace == null)
                {
                    continue;
                }

                var marketplaceInterface = AppServerJsonValueReader.GetDictionary(marketplace, "interface");
                var marketplaceId = AppServerJsonValueReader.GetString(marketplace, "name");
                var marketplaceName = AppServerJsonValueReader.GetString(marketplaceInterface, "displayName") ?? marketplaceId;
                var marketplacePath = AppServerJsonValueReader.GetString(marketplace, "path");
                foreach (var pluginItem in AppServerJsonValueReader.GetArray(marketplace, "plugins"))
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
            foreach (var item in AppServerJsonValueReader.GetArray(result, "marketplaceLoadErrors"))
            {
                var error = item as IDictionary<string, object>;
                if (error == null)
                {
                    continue;
                }

                errors.Add(new AppServerMarketplaceLoadError
                {
                    MarketplaceName = AppServerJsonValueReader.GetString(error, "marketplacePath"),
                    Message = AppServerJsonValueReader.GetString(error, "message")
                });
            }

            return errors;
        }

        internal static AppServerPluginDetails BuildPluginDetails(
            IDictionary<string, object> result,
            Func<string, string> mapServerPathToClient,
            Func<string, string, string, string> resolveCurrentPluginSkillPath)
        {
            var plugin = AppServerJsonValueReader.GetDictionary(result, "plugin");
            var marketplaceId = AppServerJsonValueReader.GetString(plugin, "marketplaceName");
            var marketplaceName = AppServerJsonValueReader.GetString(plugin, "marketplaceDisplayName") ?? marketplaceId;
            var marketplacePath = AppServerJsonValueReader.GetString(plugin, "marketplacePath");
            var summary = BuildPluginSummary(AppServerJsonValueReader.GetDictionary(plugin, "summary"), marketplaceName, marketplaceId, marketplacePath) ??
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
                Description = AppServerJsonValueReader.GetString(plugin, "description"),
                Skills = BuildPluginSkillInfos(plugin, mapServerPathToClient, resolveCurrentPluginSkillPath),
                Apps = BuildPluginApps(plugin, "apps"),
                McpServers = BuildStringList(plugin, "mcpServers")
            };
        }

        internal static List<AppServerPluginAppInfo> BuildPluginApps(IDictionary<string, object> source, string key)
        {
            var result = new List<AppServerPluginAppInfo>();
            foreach (var item in AppServerJsonValueReader.GetArray(source, key))
            {
                var app = item as IDictionary<string, object>;
                if (app == null)
                {
                    continue;
                }

                result.Add(new AppServerPluginAppInfo
                {
                    Id = AppServerJsonValueReader.GetString(app, "id"),
                    Name = AppServerJsonValueReader.GetString(app, "name"),
                    Description = AppServerJsonValueReader.GetString(app, "description"),
                    InstallUrl = AppServerJsonValueReader.GetString(app, "installUrl"),
                    NeedsAuth = AppServerJsonValueReader.GetNullableBool(app, "needsAuth") ??
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

            var pluginInterface = AppServerJsonValueReader.GetDictionary(plugin, "interface");
            var source = AppServerJsonValueReader.GetDictionary(plugin, "source");
            var name = AppServerJsonValueReader.GetString(plugin, "name");
            var displayName = AppServerJsonValueReader.GetString(pluginInterface, "displayName");

            return new AppServerPluginSummary
            {
                MarketplaceName = marketplaceName,
                MarketplaceId = marketplaceId,
                MarketplacePath = marketplacePath,
                PluginId = AppServerJsonValueReader.GetString(plugin, "id"),
                RemotePluginId = AppServerJsonValueReader.GetString(plugin, "remotePluginId"),
                Name = name,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
                ShortDescription = AppServerJsonValueReader.GetString(pluginInterface, "shortDescription") ?? AppServerJsonValueReader.GetString(plugin, "shortDescription"),
                LongDescription = AppServerJsonValueReader.GetString(pluginInterface, "longDescription"),
                DeveloperName = AppServerJsonValueReader.GetString(pluginInterface, "developerName"),
                Category = AppServerJsonValueReader.GetString(pluginInterface, "category"),
                SourceType = AppServerJsonValueReader.GetString(source, "type"),
                SourcePath = AppServerJsonValueReader.GetString(source, "path"),
                Installed = AppServerJsonValueReader.GetNullableBool(plugin, "installed") ?? false,
                Enabled = AppServerJsonValueReader.GetNullableBool(plugin, "enabled") ?? false,
                Version = AppServerJsonValueReader.GetString(plugin, "version"),
                LocalVersion = AppServerJsonValueReader.GetString(plugin, "localVersion"),
                InstallPolicy = AppServerJsonValueReader.GetString(plugin, "installPolicy"),
                InstallPolicySource = AppServerJsonValueReader.GetString(plugin, "installPolicySource"),
                AuthPolicy = AppServerJsonValueReader.GetString(plugin, "authPolicy"),
                WebsiteUrl = AppServerJsonValueReader.GetString(pluginInterface, "websiteUrl"),
                PrivacyPolicyUrl = AppServerJsonValueReader.GetString(pluginInterface, "privacyPolicyUrl"),
                TermsOfServiceUrl = AppServerJsonValueReader.GetString(pluginInterface, "termsOfServiceUrl"),
                ComposerIconPath = AppServerJsonValueReader.GetString(pluginInterface, "composerIcon"),
                LogoPath = AppServerJsonValueReader.GetString(pluginInterface, "logo"),
                BrandColor = AppServerJsonValueReader.GetString(pluginInterface, "brandColor"),
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
            foreach (var item in AppServerJsonValueReader.GetArray(entry, "skills"))
            {
                var skill = item as IDictionary<string, object>;
                if (skill == null)
                {
                    continue;
                }

                var skillInterface = AppServerJsonValueReader.GetDictionary(skill, "interface");
                var dependencies = AppServerJsonValueReader.GetDictionary(skill, "dependencies");
                var name = AppServerJsonValueReader.GetString(skill, "name");
                var displayName = AppServerJsonValueReader.GetString(skillInterface, "displayName");
                var mappedPath = mapServerPathToClient(AppServerJsonValueReader.GetString(skill, "path"));
                var resolvedPath = resolveCurrentPluginSkillPath(mappedPath, "skills/list", name);

                result.Add(new AppServerSkillInfo
                {
                    Name = name,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
                    ShortDescription = AppServerJsonValueReader.GetString(skillInterface, "shortDescription") ?? AppServerJsonValueReader.GetString(skill, "shortDescription"),
                    Description = AppServerJsonValueReader.GetString(skill, "description"),
                    Enabled = AppServerJsonValueReader.GetNullableBool(skill, "enabled") ?? false,
                    Path = resolvedPath,
                    Scope = AppServerJsonValueReader.GetString(skill, "scope"),
                    DefaultPrompt = AppServerJsonValueReader.GetString(skillInterface, "defaultPrompt"),
                    BrandColor = AppServerJsonValueReader.GetString(skillInterface, "brandColor"),
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
            foreach (var item in AppServerJsonValueReader.GetArray(source, "skills"))
            {
                var skill = item as IDictionary<string, object>;
                if (skill == null)
                {
                    continue;
                }

                var skillInterface = AppServerJsonValueReader.GetDictionary(skill, "interface");
                var name = AppServerJsonValueReader.GetString(skill, "name");
                var displayName = AppServerJsonValueReader.GetString(skillInterface, "displayName");
                var mappedPath = mapServerPathToClient(AppServerJsonValueReader.GetString(skill, "path"));
                var resolvedPath = resolveCurrentPluginSkillPath(mappedPath, "plugin/details", name);

                result.Add(new AppServerPluginSkillInfo
                {
                    Name = name,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
                    ShortDescription = AppServerJsonValueReader.GetString(skillInterface, "shortDescription") ?? AppServerJsonValueReader.GetString(skill, "shortDescription"),
                    Description = AppServerJsonValueReader.GetString(skill, "description"),
                    Path = resolvedPath,
                    Enabled = AppServerJsonValueReader.GetNullableBool(skill, "enabled") ?? false,
                    DefaultPrompt = AppServerJsonValueReader.GetString(skillInterface, "defaultPrompt")
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
            foreach (var item in AppServerJsonValueReader.GetArray(entry, "errors"))
            {
                var error = item as IDictionary<string, object>;
                if (error == null)
                {
                    continue;
                }

                result.Add(new AppServerSkillError
                {
                    Path = mapServerPathToClient(AppServerJsonValueReader.GetString(error, "path")),
                    Message = AppServerJsonValueReader.GetString(error, "message")
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
            foreach (var item in AppServerJsonValueReader.GetArray(dependencies, "tools"))
            {
                var tool = item as IDictionary<string, object>;
                if (tool == null)
                {
                    continue;
                }

                result.Add(new AppServerSkillToolDependency
                {
                    Type = AppServerJsonValueReader.GetString(tool, "type"),
                    Value = AppServerJsonValueReader.GetString(tool, "value"),
                    Description = AppServerJsonValueReader.GetString(tool, "description"),
                    Command = AppServerJsonValueReader.GetString(tool, "command"),
                    Transport = AppServerJsonValueReader.GetString(tool, "transport"),
                    Url = AppServerJsonValueReader.GetString(tool, "url")
                });
            }

            return result;
        }

        private static List<string> BuildStringList(IDictionary<string, object> source, string key)
        {
            return BuildStringList(AppServerJsonValueReader.GetArray(source, key));
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
    }
}
