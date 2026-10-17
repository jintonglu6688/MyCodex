using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MyCodexOfficialMarketplaceBootstrapTests
    {
        [Test]
        public async Task MissingBuiltInMarketplace_RegistersPinnedSourceAndRediscoversInstalledPlugins()
        {
            var client = new RecordingPluginClient
            {
                CatalogSnapshots = new Queue<List<AppServerPluginSummary>>(new[]
                {
                    new List<AppServerPluginSummary>(),
                    new List<AppServerPluginSummary>
                    {
                        new AppServerPluginSummary
                        {
                            MarketplaceId = "mycodex-official",
                            PluginId = "superpowers@mycodex-official",
                            Name = "superpowers"
                        }
                    }
                }),
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
                                PluginId = "superpowers@mycodex-official",
                                Name = "superpowers",
                                Installed = true,
                                Enabled = true
                            }
                        }
                    }
                })
            };

            var result = await MyCodexOfficialMarketplaceBootstrap.EnsureRegisteredAsync(
                client,
                new AppServerProtocolCapabilities
                {
                    RuntimeSource = CodexRuntimeSources.Bundled,
                    SupportsMarketplaceRegistration = true
                },
                CancellationToken.None);

            Assert.That(client.Calls, Is.EqualTo(new[] { "list", "installed", "add", "list", "installed" }));
            Assert.That(client.AddedSource, Is.EqualTo("https://market.jtl.world/git/mycodex-official.git"));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.RediscoveredInstalledCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ConflictingBuiltInMarketplaceSource_IsRemovedAndPinnedSourceIsRegistered()
        {
            var client = new RecordingPluginClient
            {
                AddFailuresRemaining = 1,
                AddFailureMessage =
                    "marketplace 'mycodex-official' is already added from a different source; remove it before adding this source",
                CatalogSnapshots = new Queue<List<AppServerPluginSummary>>(new[]
                {
                    new List<AppServerPluginSummary>(),
                    new List<AppServerPluginSummary>
                    {
                        new AppServerPluginSummary
                        {
                            MarketplaceId = "mycodex-official",
                            PluginId = "superpowers@mycodex-official",
                            Name = "superpowers"
                        }
                    }
                }),
                InstalledSnapshots = new Queue<AppServerPluginInstalledSnapshot>(new[]
                {
                    new AppServerPluginInstalledSnapshot(),
                    new AppServerPluginInstalledSnapshot(),
                    new AppServerPluginInstalledSnapshot
                    {
                        Plugins = new List<AppServerPluginSummary>
                        {
                            new AppServerPluginSummary
                            {
                                MarketplaceId = "mycodex-official",
                                PluginId = "superpowers@mycodex-official",
                                Name = "superpowers",
                                Installed = true
                            }
                        }
                    }
                })
            };

            var result = await MyCodexOfficialMarketplaceBootstrap.EnsureRegisteredAsync(
                client,
                CreateCapabilities(CodexRuntimeSources.System, true),
                CancellationToken.None);

            Assert.That(
                client.Calls,
                Is.EqualTo(new[] { "list", "installed", "add", "remove:mycodex-official", "installed", "add", "list", "installed" }));
            Assert.That(result, Is.Not.Null);
            Assert.That(result.RediscoveredInstalledCount, Is.EqualTo(1));
            Assert.That(result.InstalledPlugins, Has.Count.EqualTo(1));
        }

        [Test]
        public void OtherRegistrationFailure_DoesNotRemoveOrRetryMarketplace()
        {
            var client = new RecordingPluginClient
            {
                AddFailuresRemaining = 1,
                AddFailureMessage = "marketplace source could not be loaded",
                CatalogSnapshots = new Queue<List<AppServerPluginSummary>>(new[]
                {
                    new List<AppServerPluginSummary>()
                }),
                InstalledSnapshots = new Queue<AppServerPluginInstalledSnapshot>(new[]
                {
                    new AppServerPluginInstalledSnapshot()
                })
            };

            Assert.That(
                async () => await MyCodexOfficialMarketplaceBootstrap.EnsureRegisteredAsync(
                    client,
                    CreateCapabilities(CodexRuntimeSources.System, true),
                    CancellationToken.None),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(client.Calls, Is.EqualTo(new[] { "list", "installed", "add" }));
        }

        [Test]
        public async Task ExistingBuiltInMarketplace_DoesNotRegisterAgain()
        {
            var client = new RecordingPluginClient
            {
                CatalogSnapshots = new Queue<List<AppServerPluginSummary>>(new[]
                {
                    new List<AppServerPluginSummary>
                    {
                        new AppServerPluginSummary
                        {
                            MarketplaceId = "mycodex-official",
                            PluginId = "ponytail@mycodex-official",
                            Name = "ponytail"
                        }
                    }
                })
            };

            var result = await MyCodexOfficialMarketplaceBootstrap.EnsureRegisteredAsync(
                client,
                CreateCapabilities(CodexRuntimeSources.Bundled, true),
                CancellationToken.None);

            Assert.That(result, Is.Null);
            Assert.That(client.Calls, Is.EqualTo(new[] { "list" }));
            Assert.That(client.AddedSource, Is.Null);
        }

        [Test]
        public async Task QualifiedBuiltInPluginIdentity_DoesNotRegisterAgainWhenMarketplaceFieldIsMissing()
        {
            var client = new RecordingPluginClient
            {
                CatalogSnapshots = new Queue<List<AppServerPluginSummary>>(new[]
                {
                    new List<AppServerPluginSummary>
                    {
                        new AppServerPluginSummary
                        {
                            PluginId = "ponytail@mycodex-official",
                            Name = "ponytail"
                        }
                    }
                })
            };

            var result = await MyCodexOfficialMarketplaceBootstrap.EnsureRegisteredAsync(
                client,
                CreateCapabilities(CodexRuntimeSources.Bundled, true),
                CancellationToken.None);

            Assert.That(result, Is.Null);
            Assert.That(client.Calls, Is.EqualTo(new[] { "list" }));
        }

        [Test]
        public async Task UnvalidatedRuntime_DoesNotProbeOrRegisterMarketplace()
        {
            var client = new RecordingPluginClient();

            var result = await MyCodexOfficialMarketplaceBootstrap.EnsureRegisteredAsync(
                client,
                CreateCapabilities(CodexRuntimeSources.Custom, false),
                CancellationToken.None);

            Assert.That(result, Is.Null);
            Assert.That(client.Calls, Is.Empty);
            Assert.That(client.AddedSource, Is.Null);
        }

        [Test]
        public async Task ValidatedRemoteUnixTarget_UsesTheSamePinnedRegistrationPath()
        {
            var client = new RecordingPluginClient
            {
                CatalogSnapshots = new Queue<List<AppServerPluginSummary>>(new[]
                {
                    new List<AppServerPluginSummary>(),
                    new List<AppServerPluginSummary>()
                }),
                InstalledSnapshots = new Queue<AppServerPluginInstalledSnapshot>(new[]
                {
                    new AppServerPluginInstalledSnapshot(),
                    new AppServerPluginInstalledSnapshot()
                })
            };

            var result = await MyCodexOfficialMarketplaceBootstrap.EnsureRegisteredAsync(
                client,
                CreateCapabilities(CodexRuntimeSources.RemoteUnixSsh, true),
                CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(client.AddedSource, Is.EqualTo("https://market.jtl.world/git/mycodex-official.git"));
        }

        [Test]
        public async Task ValidatedWslTarget_UsesTheSamePinnedRegistrationPath()
        {
            var client = new RecordingPluginClient
            {
                CatalogSnapshots = new Queue<List<AppServerPluginSummary>>(new[]
                {
                    new List<AppServerPluginSummary>(),
                    new List<AppServerPluginSummary>()
                }),
                InstalledSnapshots = new Queue<AppServerPluginInstalledSnapshot>(new[]
                {
                    new AppServerPluginInstalledSnapshot(),
                    new AppServerPluginInstalledSnapshot()
                })
            };

            var result = await MyCodexOfficialMarketplaceBootstrap.EnsureRegisteredAsync(
                client,
                CreateCapabilities(CodexRuntimeSources.Wsl, true),
                CancellationToken.None);

            Assert.That(result, Is.Not.Null);
            Assert.That(client.AddedSource, Is.EqualTo("https://market.jtl.world/git/mycodex-official.git"));
        }

        [Test]
        public void MainForm_RunsBuiltInMarketplaceBootstrapAtStartupAndTargetReloadButNotReconnect()
        {
            var runtimeConnection = File.ReadAllText(GetSourcePath("MainForm.RuntimeConnection.cs"));
            var runtimeSettings = File.ReadAllText(GetSourcePath("MainForm.RuntimeSettings.cs"));
            var bootstrap = File.ReadAllText(GetSourcePath("MainForm.PluginMarketplaceBootstrap.cs"));
            var initialize = ExtractMethod(runtimeConnection, "private async Task InitializeAppAsync()");
            var reconnect = ExtractMethod(runtimeConnection, "private async Task<bool> TryRecoverAppServerConnectionAsync(");
            var ensureAction = ExtractMethod(runtimeConnection, "private async Task<bool> EnsureAppServerAvailableForActionAsync(");
            var restart = ExtractMethod(runtimeSettings, "private async Task RestartAppServerForRuntimeSettingsAsync(");

            Assert.That(initialize, Does.Contain("await EnsureMyCodexOfficialMarketplaceRegisteredAsync();"));
            Assert.That(restart, Does.Contain("await EnsureMyCodexOfficialMarketplaceRegisteredAsync();"));
            Assert.That(reconnect, Does.Not.Contain("EnsureMyCodexOfficialMarketplaceRegisteredAsync"));
            Assert.That(ensureAction, Does.Not.Contain("EnsureMyCodexOfficialMarketplaceRegisteredAsync"));
            Assert.That(bootstrap, Does.Contain("MyCodexOfficialMarketplaceBootstrap.EnsureRegisteredAsync("));
            Assert.That(bootstrap, Does.Contain("TimeSpan.FromSeconds(30)"));
            Assert.That(bootstrap, Does.Contain("catch (Exception)"));
            Assert.That(bootstrap, Does.Not.Contain("MessageBox.Show"));
            Assert.That(bootstrap, Does.Not.Contain("ex.Message"));
        }

        private static string GetSourcePath(string fileName)
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MyCodex.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "Repository root not found.");
            return Path.Combine(directory.FullName, "src", "MyCodex.WinForms", "Forms", fileName);
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "Method not found: " + signature);
            var brace = source.IndexOf('{', start);
            Assert.That(brace, Is.GreaterThanOrEqualTo(0), "Method body not found: " + signature);
            var depth = 0;
            for (var index = brace; index < source.Length; index++)
            {
                if (source[index] == '{')
                {
                    depth++;
                }
                else if (source[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return source.Substring(start, index - start + 1);
                    }
                }
            }

            Assert.Fail("Method body is incomplete: " + signature);
            return string.Empty;
        }

        private static AppServerProtocolCapabilities CreateCapabilities(string runtimeSource, bool supportsRegistration)
        {
            return new AppServerProtocolCapabilities
            {
                RuntimeSource = runtimeSource,
                SupportsMarketplaceRegistration = supportsRegistration
            };
        }

        private sealed class RecordingPluginClient : IAppServerPluginClient
        {
            public List<string> Calls { get; } = new List<string>();

            public string AddedSource { get; private set; }

            public int AddFailuresRemaining { get; set; }

            public string AddFailureMessage { get; set; }

            public Queue<List<AppServerPluginSummary>> CatalogSnapshots { get; set; }

            public Queue<AppServerPluginInstalledSnapshot> InstalledSnapshots { get; set; }

            public Task<List<AppServerPluginSummary>> ListPluginsAsync(CancellationToken cancellationToken)
            {
                Calls.Add("list");
                return Task.FromResult(CatalogSnapshots == null || CatalogSnapshots.Count == 0
                    ? new List<AppServerPluginSummary>()
                    : CatalogSnapshots.Dequeue());
            }

            public Task<List<AppServerPluginSummary>> ListPluginsAsync()
            {
                return ListPluginsAsync(CancellationToken.None);
            }

            public Task<AppServerPluginInstalledSnapshot> GetInstalledPluginsAsync(
                CancellationToken cancellationToken = default(CancellationToken))
            {
                Calls.Add("installed");
                return Task.FromResult(InstalledSnapshots == null || InstalledSnapshots.Count == 0
                    ? new AppServerPluginInstalledSnapshot()
                    : InstalledSnapshots.Dequeue());
            }

            public Task<AppServerMarketplaceAddResult> AddMarketplaceAsync(
                string source,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                Calls.Add("add");
                AddedSource = source;
                if (AddFailuresRemaining > 0)
                {
                    AddFailuresRemaining--;
                    throw new InvalidOperationException(AddFailureMessage);
                }

                return Task.FromResult(new AppServerMarketplaceAddResult
                {
                    MarketplaceName = "mycodex-official",
                    AlreadyAdded = false
                });
            }

            public Task RemoveMarketplaceAsync(
                string marketplaceName,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                Calls.Add("remove:" + marketplaceName);
                return Task.CompletedTask;
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
