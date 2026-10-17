using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerInstalledPluginObservationServiceTests
    {
        [Test]
        public async Task LoadAsync_BundledContract_UsesInstalledObservationOnly()
        {
            var client = new RecordingPluginClient();
            client.InstalledSnapshot.Plugins.Add(new AppServerPluginSummary
            {
                Name = "installed-plugin",
                MarketplaceName = "official",
                Installed = true
            });
            client.Catalog.Add(new AppServerPluginSummary
            {
                Name = "catalog-only",
                MarketplaceName = "official",
                Installed = false
            });

            var result = await AppServerInstalledPluginObservationService.LoadAsync(
                client,
                new AppServerProtocolCapabilities { SupportsInstalledPluginObservation = true },
                CancellationToken.None);

            Assert.That(client.InstalledCalls, Is.EqualTo(1));
            Assert.That(client.ListCalls, Is.EqualTo(0));
            Assert.That(result.Plugins, Has.Count.EqualTo(1));
            Assert.That(result.Plugins[0].Name, Is.EqualTo("installed-plugin"));
        }

        [Test]
        public async Task LoadAsync_WithoutInstalledContract_UsesOfficialCatalogInstalledFlagsOnly()
        {
            var client = new RecordingPluginClient();
            client.Catalog.Add(new AppServerPluginSummary
            {
                Name = "installed-plugin",
                MarketplaceName = "official",
                Installed = true
            });
            client.Catalog.Add(new AppServerPluginSummary
            {
                Name = "available-plugin",
                MarketplaceName = "official",
                Installed = false
            });

            var result = await AppServerInstalledPluginObservationService.LoadAsync(
                client,
                new AppServerProtocolCapabilities { SupportsInstalledPluginObservation = false },
                CancellationToken.None);

            Assert.That(client.InstalledCalls, Is.EqualTo(0));
            Assert.That(client.ListCalls, Is.EqualTo(1));
            Assert.That(result.Plugins, Has.Count.EqualTo(1));
            Assert.That(result.Plugins[0].Name, Is.EqualTo("installed-plugin"));
            Assert.That(result.MarketplaceErrors, Is.Empty);
        }

        private sealed class RecordingPluginClient : IAppServerPluginClient
        {
            public RecordingPluginClient()
            {
                Catalog = new List<AppServerPluginSummary>();
                InstalledSnapshot = new AppServerPluginInstalledSnapshot();
            }

            public List<AppServerPluginSummary> Catalog { get; }

            public AppServerPluginInstalledSnapshot InstalledSnapshot { get; }

            public int ListCalls { get; private set; }

            public int InstalledCalls { get; private set; }

            public Task<List<AppServerPluginSummary>> ListPluginsAsync()
            {
                return ListPluginsAsync(CancellationToken.None);
            }

            public Task<List<AppServerPluginSummary>> ListPluginsAsync(CancellationToken cancellationToken)
            {
                ListCalls++;
                return Task.FromResult(Catalog);
            }

            public Task<AppServerPluginInstalledSnapshot> GetInstalledPluginsAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                InstalledCalls++;
                return Task.FromResult(InstalledSnapshot);
            }

            public Task<AppServerPluginDetails> ReadPluginAsync(string marketplacePath, string pluginName) { throw new NotSupportedException(); }
            public Task<AppServerPluginSkillReadResult> ReadPluginSkillAsync(string remoteMarketplaceName, string remotePluginId, string skillName, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }
            public Task<AppServerMarketplaceUpgradeResult> UpgradeMarketplaceAsync(string marketplaceName, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }
            public Task<AppServerMarketplaceAddResult> AddMarketplaceAsync(string source, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }
            public Task RemoveMarketplaceAsync(string marketplaceName, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }
            public Task<AppServerPluginInstallResult> InstallPluginAsync(string marketplacePath, string pluginName) { throw new NotSupportedException(); }
            public Task<AppServerPluginInstallResult> InstallRemotePluginAsync(string remoteMarketplaceName, string remotePluginId) { throw new NotSupportedException(); }
            public Task UninstallPluginAsync(string pluginId) { throw new NotSupportedException(); }
        }
    }
}
