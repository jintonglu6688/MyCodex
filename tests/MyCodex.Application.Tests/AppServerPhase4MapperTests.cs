using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerPhase4MapperTests
    {
        [Test]
        public void BuildPluginSummaries_MapsOfficialInstalledStateAndVersions()
        {
            var result = new Dictionary<string, object>
            {
                {
                    "marketplaces",
                    new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "name", "mycodex-official" },
                            { "path", @"E:\Markets\official.json" },
                            {
                                "plugins",
                                new object[]
                                {
                                    new Dictionary<string, object>
                                    {
                                        { "id", "d-drive-dev-environment@mycodex-official" },
                                        { "name", "d-drive-dev-environment" },
                                        { "version", "1.0.2" },
                                        { "localVersion", "1.0.1" },
                                        { "installed", true },
                                        { "enabled", false }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var plugins = AppServerPluginSkillMapper.BuildPluginSummaries(result);

            Assert.That(plugins, Has.Count.EqualTo(1));
            Assert.That(plugins[0].Installed, Is.True);
            Assert.That(plugins[0].Enabled, Is.False);
            Assert.That(plugins[0].Version, Is.EqualTo("1.0.2"));
            Assert.That(plugins[0].LocalVersion, Is.EqualTo("1.0.1"));
        }

        [Test]
        public void BuildSkillReadResult_ParsesMarkdownFromOfficialAndFallbackShapes()
        {
            var result = new Dictionary<string, object>
            {
                {
                    "skill",
                    new Dictionary<string, object>
                    {
                        { "name", "github" },
                        { "markdown", "# GitHub\nUse GitHub issues." },
                        { "sourceLabel", "Official marketplace" }
                    }
                }
            };

            var parsed = AppServerPluginSkillReadMapper.BuildResult(result);

            Assert.That(parsed.SkillName, Is.EqualTo("github"));
            Assert.That(parsed.Markdown, Is.EqualTo("# GitHub\nUse GitHub issues."));
            Assert.That(parsed.SourceLabel, Is.EqualTo("Official marketplace"));

            var fallback = AppServerPluginSkillReadMapper.BuildResult(new Dictionary<string, object>
            {
                { "skillName", "search" },
                { "contents", "plain text skill" }
            });

            Assert.That(fallback.SkillName, Is.EqualTo("search"));
            Assert.That(fallback.Markdown, Is.EqualTo("plain text skill"));
        }

        [Test]
        public void BuildMarketplaceUpgradeResult_UsesRequestedMarketplaceWhenResponseIsSparse()
        {
            var result = AppServerMarketplaceMapper.BuildUpgradeResult(
                new Dictionary<string, object>
                {
                    { "success", true },
                    { "message", "market refreshed" }
                },
                "Official");

            Assert.That(result.MarketplaceName, Is.EqualTo("Official"));
            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Is.EqualTo("market refreshed"));
        }
    }
}
