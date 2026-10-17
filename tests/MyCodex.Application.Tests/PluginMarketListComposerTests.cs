using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PluginMarketListComposerTests
    {
        [Test]
        public void ComposeAvailableItems_DeduplicatesEntriesFromSameMarketAndPluginId()
        {
            var plugins = new[]
            {
                new AppServerPluginSummary
                {
                    PluginId = "demo-plugin",
                    Name = "demo-plugin",
                    DisplayName = "Demo Runtime",
                    Installed = false,
                    MarketplaceName = "Official"
                }
            };
            var remoteItems = new[]
            {
                new PluginMarketListItem
                {
                    PluginId = "demo-plugin",
                    DisplayName = "Demo Remote",
                    MarketName = "Official",
                    IsRemoteCatalog = true,
                    RemoteEntry = new PluginMarketEntrySnapshot { PluginId = "demo-plugin" }
                }
            };

            var items = PluginMarketListComposer.ComposeAvailableItems(plugins, remoteItems, "Runtime Market");

            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].IsRemoteCatalog, Is.False);
            Assert.That(items[0].DisplayName, Is.EqualTo("Demo Runtime"));
        }

        [Test]
        public void ComposeAvailableItems_DeduplicatesQualifiedAppServerIdentityAndRemoteCatalogIdentity()
        {
            var plugins = new[]
            {
                new AppServerPluginSummary
                {
                    PluginId = "superpowers@mycodex-official",
                    RemotePluginId = "superpowers",
                    Name = "superpowers",
                    DisplayName = "Superpowers",
                    Installed = false,
                    MarketplaceId = "mycodex-official",
                    MarketplaceName = "MyCodex Official"
                }
            };
            var remoteItems = new[]
            {
                new PluginMarketListItem
                {
                    PluginId = "superpowers",
                    DisplayName = "Superpowers",
                    MarketName = "MyCodex Official",
                    IsRemoteCatalog = true,
                    RemoteEntry = new PluginMarketEntrySnapshot
                    {
                        MarketId = "mycodex-official",
                        PluginId = "superpowers"
                    }
                }
            };

            var items = PluginMarketListComposer.ComposeAvailableItems(plugins, remoteItems, "Runtime Market");

            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].AppServerPlugin, Is.SameAs(plugins[0]));
        }

        [Test]
        public void ComposeAvailableItems_PreservesEntriesWhenPluginIdMatchesButMarketDiffers()
        {
            var plugins = new[]
            {
                new AppServerPluginSummary
                {
                    PluginId = "demo-plugin",
                    Name = "demo-plugin",
                    DisplayName = "Demo Runtime",
                    Installed = false,
                    MarketplaceName = "Runtime"
                }
            };
            var remoteItems = new[]
            {
                new PluginMarketListItem
                {
                    PluginId = "demo-plugin",
                    DisplayName = "Demo Remote",
                    MarketName = "Official",
                    IsRemoteCatalog = true,
                    RemoteEntry = new PluginMarketEntrySnapshot { PluginId = "demo-plugin" }
                }
            };

            var items = PluginMarketListComposer.ComposeAvailableItems(plugins, remoteItems, "Runtime Market");

            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items.Count(item => item.IsRemoteCatalog), Is.EqualTo(1));
            Assert.That(items.Count(item => !item.IsRemoteCatalog), Is.EqualTo(1));
        }

        [Test]
        public void ComposeAvailableItems_RegisteredMarketplaceUsesOnlyAppServerProjection()
        {
            var plugins = new[]
            {
                new AppServerPluginSummary
                {
                    PluginId = "ponytail@mycodex-official",
                    RemotePluginId = "ponytail",
                    Name = "ponytail",
                    MarketplaceId = "mycodex-official",
                    MarketplaceName = "MyCodex Official"
                }
            };
            var remoteItems = new[]
            {
                new PluginMarketListItem
                {
                    PluginId = "superpowers",
                    MarketName = "MyCodex Official",
                    IsRemoteCatalog = true,
                    RemoteEntry = new PluginMarketEntrySnapshot
                    {
                        MarketId = "mycodex-official",
                        PluginId = "superpowers"
                    }
                }
            };

            var items = PluginMarketListComposer.ComposeAvailableItems(plugins, remoteItems, "Runtime Market");

            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].PluginId, Is.EqualTo("ponytail"));
            Assert.That(items[0].IsRemoteCatalog, Is.False);
        }

        [Test]
        public void ComposeAvailableItems_KeepsRemoteEntryWhenPluginAlreadyInstalledFromSameMarket()
        {
            var plugins = new[]
            {
                new AppServerPluginSummary
                {
                    PluginId = "installed-plugin",
                    Name = "installed-plugin",
                    DisplayName = "Installed",
                    Installed = true,
                    MarketplaceName = "Official"
                }
            };
            var remoteItems = new[]
            {
                new PluginMarketListItem
                {
                    PluginId = "installed-plugin",
                    DisplayName = "Remote Installed",
                    MarketName = "Official",
                    IsRemoteCatalog = true,
                    RemoteEntry = new PluginMarketEntrySnapshot { PluginId = "installed-plugin" }
                }
            };

            var items = PluginMarketListComposer.ComposeAvailableItems(plugins, remoteItems, "Runtime Market");

            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].IsRemoteCatalog, Is.True);
            Assert.That(items[0].PluginId, Is.EqualTo("installed-plugin"));
        }

        [Test]
        public void ComposeAvailableItems_AssignsRuntimeMarketNameWhenMarketplaceNameMissing()
        {
            var plugins = new[]
            {
                new AppServerPluginSummary
                {
                    PluginId = "runtime-plugin",
                    Name = "runtime-plugin",
                    DisplayName = "Runtime Plugin",
                    Installed = false
                }
            };

            var items = PluginMarketListComposer.ComposeAvailableItems(plugins, new PluginMarketListItem[0], "Runtime Market");

            Assert.That(items.Select(item => item.MarketName).ToArray(), Is.EqualTo(new[] { "Runtime Market" }));
        }
    }
}
