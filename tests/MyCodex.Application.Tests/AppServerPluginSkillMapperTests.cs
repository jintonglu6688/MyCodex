using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerPluginSkillMapperTests
    {
        [Test]
        public void BuildPluginSummaries_OrdersByMarketplaceDisplayNamePluginDisplayNameAndPluginId()
        {
            var result = new Dictionary<string, object>
            {
                {
                    "marketplaces",
                    new object[]
                    {
                        Marketplace(
                            "beta-market",
                            "Beta",
                            @"E:\Markets\beta",
                            Plugin("plugin-b", "remote-b", "zeta", "Zeta", "market", false, true),
                            Plugin("plugin-a", "remote-a", "alpha", "Alpha", "market", true, false)),
                        Marketplace(
                            "alpha-market",
                            "Alpha",
                            @"E:\Markets\alpha",
                            Plugin("plugin-c", "remote-c", "gamma", "Gamma", "market", true, true))
                    }
                }
            };

            var summaries = AppServerPluginSkillMapper.BuildPluginSummaries(result);

            Assert.That(summaries.Select(item => item.PluginId).ToArray(), Is.EqualTo(new[] { "plugin-c", "plugin-a", "plugin-b" }));
            Assert.That(summaries[0].MarketplaceName, Is.EqualTo("Alpha"));
            Assert.That(summaries[0].MarketplaceId, Is.EqualTo("alpha-market"));
            Assert.That(summaries[1].DisplayName, Is.EqualTo("Alpha"));
            Assert.That(summaries[1].SourcePath, Is.EqualTo("market"));
            Assert.That(summaries[1].InstallPolicySource, Is.EqualTo("IMPLICIT_CANONICAL_APP"));
        }

        [Test]
        public void BuildSkillsSnapshots_MapsWorkspaceAndSkillPathsAndResolvesPluginCachePaths()
        {
            var result = new Dictionary<string, object>
            {
                {
                    "data",
                    new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "cwd", "/srv/workspace" },
                            {
                                "skills",
                                new object[]
                                {
                                    Skill("repo-skill", "Repo Skill", "/srv/workspace/.codex/skills/repo", true),
                                    Skill("plugin-skill", null, "/srv/plugin-cache/old/skills/plugin", false)
                                }
                            },
                            {
                                "errors",
                                new object[]
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "path", "/srv/workspace/bad-skill" },
                                        { "message", "bad skill" }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var snapshots = AppServerPluginSkillMapper.BuildSkillsSnapshots(
                result,
                path => path == null ? null : path.Replace("/srv", @"E:\Server").Replace('/', '\\'),
                (path, operation, name) => name == "plugin-skill" ? path.Replace("old", "current") : path);

            Assert.That(snapshots, Has.Count.EqualTo(1));
            Assert.That(snapshots[0].WorkspacePath, Is.EqualTo(@"E:\Server\workspace"));
            Assert.That(snapshots[0].Skills.Select(item => item.Name).ToArray(), Is.EqualTo(new[] { "plugin-skill", "repo-skill" }));
            Assert.That(snapshots[0].Skills[0].Path, Is.EqualTo(@"E:\Server\plugin-cache\current\skills\plugin"));
            Assert.That(snapshots[0].Skills[1].DisplayName, Is.EqualTo("Repo Skill"));
            Assert.That(snapshots[0].Errors[0].Path, Is.EqualTo(@"E:\Server\workspace\bad-skill"));
        }

        [Test]
        public void BuildPluginDetails_MapsPluginSkillPathsAndOrdersApps()
        {
            var result = new Dictionary<string, object>
            {
                {
                    "plugin",
                    new Dictionary<string, object>
                    {
                        { "marketplaceName", "Official" },
                        { "marketplacePath", @"E:\Markets\official" },
                        { "description", "Full description" },
                        { "summary", Plugin("plugin-one", "remote-one", "github", "GitHub", "market", true, true) },
                        {
                            "skills",
                            new object[]
                            {
                                Skill("publish", "Publish", "/srv/plugins/github/skills/publish", true)
                            }
                        },
                        {
                            "apps",
                            new object[]
                            {
                                App("z-app", "Z App", true),
                                App("a-app", "A App", false)
                            }
                        },
                        { "mcpServers", new object[] { "server-b", "server-a" } }
                    }
                }
            };

            var details = AppServerPluginSkillMapper.BuildPluginDetails(
                result,
                path => path == null ? null : path.Replace("/srv", @"E:\Server").Replace('/', '\\'),
                (path, operation, name) => path + "|" + operation + "|" + name);

            Assert.That(details.MarketplaceName, Is.EqualTo("Official"));
            Assert.That(details.Description, Is.EqualTo("Full description"));
            Assert.That(details.Summary.PluginId, Is.EqualTo("plugin-one"));
            Assert.That(details.Skills, Has.Count.EqualTo(1));
            Assert.That(details.Skills[0].Path, Is.EqualTo(@"E:\Server\plugins\github\skills\publish|plugin/details|publish"));
            Assert.That(details.Apps.Select(item => item.Id).ToArray(), Is.EqualTo(new[] { "a-app", "z-app" }));
            Assert.That(details.McpServers, Is.EqualTo(new[] { "server-b", "server-a" }));
        }

        [Test]
        public void BuildPluginApps_MapsAppsNeedingAuth()
        {
            var result = new Dictionary<string, object>
            {
                {
                    "appsNeedingAuth",
                    new object[]
                    {
                        App("chat", "Chat", true),
                        App("calendar", "Calendar", false)
                    }
                }
            };

            var apps = AppServerPluginSkillMapper.BuildPluginApps(result, "appsNeedingAuth");

            Assert.That(apps.Select(item => item.Id).ToArray(), Is.EqualTo(new[] { "calendar", "chat" }));
            Assert.That(apps[1].NeedsAuth, Is.True);
        }

        [Test]
        public void BuildPluginApps_TreatsAppsNeedingAuthEntriesAsNeedingAuthByDefault()
        {
            var result = new Dictionary<string, object>
            {
                {
                    "appsNeedingAuth",
                    new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "id", "calendar" },
                            { "name", "Calendar" },
                            { "installUrl", "https://example.test/auth" }
                        }
                    }
                }
            };

            var apps = AppServerPluginSkillMapper.BuildPluginApps(result, "appsNeedingAuth");

            Assert.That(apps, Has.Count.EqualTo(1));
            Assert.That(apps[0].NeedsAuth, Is.True);
        }

        private static Dictionary<string, object> Marketplace(
            string name,
            string displayName,
            string path,
            params Dictionary<string, object>[] plugins)
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "path", path },
                { "interface", new Dictionary<string, object> { { "displayName", displayName } } },
                { "plugins", plugins.Cast<object>().ToArray() }
            };
        }

        private static Dictionary<string, object> Plugin(
            string id,
            string remotePluginId,
            string name,
            string displayName,
            string sourcePath,
            bool installed,
            bool enabled)
        {
            return new Dictionary<string, object>
            {
                { "id", id },
                { "remotePluginId", remotePluginId },
                { "name", name },
                { "installed", installed },
                { "enabled", enabled },
                { "installPolicy", "optional" },
                { "installPolicySource", "IMPLICIT_CANONICAL_APP" },
                { "authPolicy", "optional" },
                {
                    "interface",
                    new Dictionary<string, object>
                    {
                        { "displayName", displayName },
                        { "shortDescription", "short" },
                        { "longDescription", "long" },
                        { "developerName", "dev" },
                        { "category", "tools" },
                        { "capabilities", new object[] { "cap-b", "cap-a", "cap-a" } },
                        { "defaultPrompt", new object[] { "prompt" } }
                    }
                },
                { "source", new Dictionary<string, object> { { "type", "market" }, { "path", sourcePath } } }
            };
        }

        private static Dictionary<string, object> Skill(string name, string displayName, string path, bool enabled)
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "path", path },
                { "enabled", enabled },
                { "scope", "workspace" },
                { "description", "description" },
                {
                    "interface",
                    new Dictionary<string, object>
                    {
                        { "displayName", displayName },
                        { "shortDescription", "short" },
                        { "defaultPrompt", "prompt" },
                        { "brandColor", "#123456" }
                    }
                },
                {
                    "dependencies",
                    new Dictionary<string, object>
                    {
                        {
                            "tools",
                            new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    { "type", "command" },
                                    { "value", "git" },
                                    { "description", "Git CLI" },
                                    { "command", "git" },
                                    { "transport", "stdio" },
                                    { "url", "https://example.com" }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static Dictionary<string, object> App(string id, string name, bool needsAuth)
        {
            return new Dictionary<string, object>
            {
                { "id", id },
                { "name", name },
                { "description", name + " description" },
                { "installUrl", "https://example.com/" + id },
                { "needsAuth", needsAuth }
            };
        }
    }
}
