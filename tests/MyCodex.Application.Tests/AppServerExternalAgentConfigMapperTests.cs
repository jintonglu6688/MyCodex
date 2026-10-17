using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerExternalAgentConfigMapperTests
    {
        [Test]
        public void BuildDetectResult_MapsMigrationItemsAndDetails()
        {
            var result = AppServerExternalAgentConfigMapper.BuildDetectResult(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    {
                        "items",
                        new object[]
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "itemType", "SUBAGENTS" },
                                { "description", "Claude subagents" },
                                { "cwd", @"E:\Repo" },
                                {
                                    "details",
                                    new Dictionary<string, object>(StringComparer.Ordinal)
                                    {
                                        {
                                            "subagents",
                                            new object[]
                                            {
                                                new Dictionary<string, object>(StringComparer.Ordinal) { { "name", "reviewer" } }
                                            }
                                        },
                                        {
                                            "plugins",
                                            new object[]
                                            {
                                                new Dictionary<string, object>(StringComparer.Ordinal)
                                                {
                                                    { "marketplaceName", "local" },
                                                    { "pluginNames", new object[] { "plugin-a", "plugin-b" } }
                                                }
                                            }
                                        },
                                        {
                                            "sessions",
                                            new object[]
                                            {
                                                new Dictionary<string, object>(StringComparer.Ordinal)
                                                {
                                                    { "path", @"C:\Users\me\.claude\sessions\a.jsonl" },
                                                    { "cwd", @"E:\Repo" },
                                                    { "title", "Imported session" }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                });

            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(result.Items[0].ItemType, Is.EqualTo(ExternalAgentConfigMigrationItemType.Subagents));
            Assert.That(result.Items[0].RawItemType, Is.EqualTo("SUBAGENTS"));
            Assert.That(result.Items[0].Description, Is.EqualTo("Claude subagents"));
            Assert.That(result.Items[0].Cwd, Is.EqualTo(@"E:\Repo"));
            Assert.That(result.Items[0].Details.Subagents[0].Name, Is.EqualTo("reviewer"));
            Assert.That(result.Items[0].Details.Plugins[0].MarketplaceName, Is.EqualTo("local"));
            Assert.That(result.Items[0].Details.Plugins[0].PluginNames, Is.EqualTo(new[] { "plugin-a", "plugin-b" }));
            Assert.That(result.Items[0].Details.Sessions[0].Title, Is.EqualTo("Imported session"));
            Assert.That(result.Items[0].RawItem.ContainsKey("itemType"), Is.True);
        }

        [Test]
        public void ToProtocolMigrationItems_PreservesRawItemForImport()
        {
            var raw = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "itemType", "COMMANDS" },
                { "description", "commands" },
                { "cwd", null }
            };

            var items = AppServerExternalAgentConfigMapper.ToProtocolMigrationItems(
                new[]
                {
                    new ExternalAgentConfigMigrationItem
                    {
                        ItemType = ExternalAgentConfigMigrationItemType.Commands,
                        RawItemType = "COMMANDS",
                        Description = "commands",
                        RawItem = raw
                    }
                });

            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0], Is.SameAs(raw));
        }

        [Test]
        public void ToProtocolMigrationItems_RejectsBroadConfigAndUnknownItems()
        {
            var items = AppServerExternalAgentConfigMapper.ToProtocolMigrationItems(
                new[]
                {
                    new ExternalAgentConfigMigrationItem
                    {
                        ItemType = ExternalAgentConfigMigrationItemType.Config,
                        RawItemType = "CONFIG",
                        RawItem = new Dictionary<string, object> { { "itemType", "CONFIG" } }
                    },
                    new ExternalAgentConfigMigrationItem
                    {
                        ItemType = ExternalAgentConfigMigrationItemType.Unknown,
                        RawItemType = "FUTURE_ITEM",
                        RawItem = new Dictionary<string, object> { { "itemType", "FUTURE_ITEM" } }
                    },
                    new ExternalAgentConfigMigrationItem
                    {
                        ItemType = (ExternalAgentConfigMigrationItemType)999,
                        RawItemType = "FUTURE_ITEM",
                        RawItem = new Dictionary<string, object> { { "itemType", "FUTURE_ITEM" } }
                    },
                    new ExternalAgentConfigMigrationItem
                    {
                        ItemType = (ExternalAgentConfigMigrationItemType)999,
                        RawItemType = "FUTURE_ITEM",
                        RawItem = new Dictionary<string, object>()
                    },
                    new ExternalAgentConfigMigrationItem
                    {
                        ItemType = ExternalAgentConfigMigrationItemType.Commands,
                        RawItemType = "COMMANDS",
                        RawItem = new Dictionary<string, object> { { "itemType", "CONFIG" } }
                    },
                    new ExternalAgentConfigMigrationItem
                    {
                        ItemType = ExternalAgentConfigMigrationItemType.Commands,
                        RawItemType = "CONFIG",
                        RawItem = new Dictionary<string, object>()
                    },
                    new ExternalAgentConfigMigrationItem
                    {
                        ItemType = ExternalAgentConfigMigrationItemType.AgentsMd,
                        RawItemType = "AGENTS_MD",
                        RawItem = new Dictionary<string, object> { { "itemType", "AGENTS_MD" } }
                    }
                });

            Assert.That(items.Select(item => item["itemType"]), Is.EqualTo(new object[] { "AGENTS_MD" }));
        }
    }
}
