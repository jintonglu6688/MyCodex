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
    public sealed class AppServerMarketplaceRegistrationServiceTests
    {
        [Test]
        public async Task RegisterMarketplaceAsync_RefreshesCatalogAndInstalledObservationWithoutInstallingPlugin()
        {
            var client = new RecordingPluginClient
            {
                AddResult = new AppServerMarketplaceAddResult
                {
                    MarketplaceName = "mycodex-official",
                    AlreadyAdded = false
                },
                Plugins = new List<AppServerPluginSummary>
                {
                    new AppServerPluginSummary
                    {
                        MarketplaceId = "mycodex-official",
                        PluginId = "superpowers@mycodex-official",
                        Name = "superpowers"
                    }
                },
                InstalledSnapshots = new Queue<AppServerPluginInstalledSnapshot>(new[]
                {
                    new AppServerPluginInstalledSnapshot(),
                    new AppServerPluginInstalledSnapshot
                    {
                        Plugins = new List<AppServerPluginSummary>
                        {
                            new AppServerPluginSummary
                            {
                                MarketplaceId = "mycodex-official",
                                PluginId = "d-drive-dev-environment@mycodex-official",
                                Name = "d-drive-dev-environment",
                                Installed = true
                            },
                            new AppServerPluginSummary
                            {
                                MarketplaceId = "mycodex-official",
                                PluginId = "ponytail@mycodex-official",
                                Name = "ponytail",
                                Installed = true
                            }
                        }
                    }
                })
            };

            var result = await AppServerMarketplaceRegistrationService.RegisterMarketplaceAsync(
                client,
                "https://market.example.com/git/mycodex-official.git",
                "mycodex-official",
                CancellationToken.None);

            Assert.That(client.Calls, Is.EqualTo(new[] { "installed", "add", "list", "installed" }));
            Assert.That(result.MarketplaceName, Is.EqualTo("mycodex-official"));
            Assert.That(result.RediscoveredInstalledCount, Is.EqualTo(2));
            Assert.That(result.CatalogPlugins, Has.Count.EqualTo(1));
            Assert.That(result.InstalledPlugins, Has.Count.EqualTo(2));
        }

        private sealed class RecordingPluginClient : IAppServerPluginClient
        {
            public List<string> Calls { get; } = new List<string>();
            public string AddedSource { get; private set; }
            public AppServerMarketplaceAddResult AddResult { get; set; }
            public List<AppServerPluginSummary> Plugins { get; set; }
            public Queue<AppServerPluginInstalledSnapshot> InstalledSnapshots { get; set; }

            public Task<AppServerMarketplaceAddResult> AddMarketplaceAsync(string source, CancellationToken cancellationToken = default(CancellationToken))
            {
                Calls.Add("add");
                AddedSource = source;
                return Task.FromResult(AddResult);
            }

            public Task RemoveMarketplaceAsync(string marketplaceName, CancellationToken cancellationToken = default(CancellationToken))
            {
                throw new System.NotSupportedException();
            }

            public Task<List<AppServerPluginSummary>> ListPluginsAsync(CancellationToken cancellationToken)
            {
                Calls.Add("list");
                return Task.FromResult(Plugins);
            }

            public Task<List<AppServerPluginSummary>> ListPluginsAsync() { throw new System.NotSupportedException(); }
            public Task<AppServerPluginInstalledSnapshot> GetInstalledPluginsAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                Calls.Add("installed");
                return Task.FromResult(InstalledSnapshots == null || InstalledSnapshots.Count == 0
                    ? new AppServerPluginInstalledSnapshot()
                    : InstalledSnapshots.Dequeue());
            }
            public Task<AppServerPluginDetails> ReadPluginAsync(string marketplacePath, string pluginName) { throw new System.NotSupportedException(); }
            public Task<AppServerPluginSkillReadResult> ReadPluginSkillAsync(string remoteMarketplaceName, string remotePluginId, string skillName, CancellationToken cancellationToken = default(CancellationToken)) { throw new System.NotSupportedException(); }
            public Task<AppServerMarketplaceUpgradeResult> UpgradeMarketplaceAsync(string marketplaceName, CancellationToken cancellationToken = default(CancellationToken)) { throw new System.NotSupportedException(); }
            public Task<AppServerPluginInstallResult> InstallPluginAsync(string marketplacePath, string pluginName) { throw new System.NotSupportedException(); }
            public Task<AppServerPluginInstallResult> InstallRemotePluginAsync(string remoteMarketplaceName, string remotePluginId) { throw new System.NotSupportedException(); }
            public Task UninstallPluginAsync(string pluginId) { throw new System.NotSupportedException(); }
        }
    }
}
