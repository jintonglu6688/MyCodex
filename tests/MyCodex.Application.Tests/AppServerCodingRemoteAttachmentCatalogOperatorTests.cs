using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCodingRemoteAttachmentCatalogOperatorTests
    {
        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void DefaultConstructor_UsesTheSameAppServerFacadeForPluginAndSkillObservations()
        {
            var source = RepositorySource.ReadSourceFile("AppServerCodingRemoteAttachmentCatalogOperator.cs");

            Assert.That(source, Does.Contain(": this(appStateStore, appServerClient, AppInteractionMode.Coding)"));
        }

        [Test]
        public async Task ListPluginAttachmentsAsync_FiltersInstalledPluginsWithPathsAndSortsEnabledFirst()
        {
            var appServerClient = new FakeAttachmentClient();
            appServerClient.Plugins.Add(new AppServerPluginSummary
            {
                Name = "disabled-plugin",
                DisplayName = "Disabled Plugin",
                SourcePath = @"E:\Plugins\disabled",
                MarketplaceName = "personal",
                Installed = true,
                Enabled = false
            });
            appServerClient.Plugins.Add(new AppServerPluginSummary
            {
                Name = "enabled-plugin",
                DisplayName = "Enabled Plugin",
                SourcePath = @"E:\Plugins\enabled",
                MarketplaceName = "official",
                ShortDescription = "Enabled summary",
                Installed = true,
                Enabled = true
            });
            appServerClient.Plugins.Add(new AppServerPluginSummary
            {
                Name = "missing-path",
                DisplayName = "Missing Path",
                Installed = true,
                Enabled = true
            });
            appServerClient.Plugins.Add(new AppServerPluginSummary
            {
                Name = "not-installed",
                DisplayName = "Not Installed",
                SourcePath = @"E:\Plugins\not-installed",
                Installed = false,
                Enabled = true
            });
            var operatorUnderTest = new AppServerCodingRemoteAttachmentCatalogOperator(
                CreateStore(),
                appServerClient,
                appServerClient);

            var result = await operatorUnderTest.ListPluginAttachmentsAsync("session-a", CancellationToken.None);

            Assert.That(result.SessionId, Is.EqualTo("session-a"));
            Assert.That(result.Items, Has.Count.EqualTo(1));
            Assert.That(result.Items[0].Type, Is.EqualTo("mention"));
            Assert.That(result.Items[0].Name, Is.EqualTo("enabled-plugin"));
            Assert.That(result.Items[0].DisplayName, Is.EqualTo("Enabled Plugin"));
            Assert.That(result.Items[0].Path, Is.EqualTo(@"E:\Plugins\enabled"));
            Assert.That(result.Items[0].Enabled, Is.True);
            Assert.That(result.Items[0].Installed, Is.True);
            Assert.That(result.Items[0].SourceText, Is.EqualTo("official"));
            Assert.That(result.Items[0].Summary, Is.EqualTo("Enabled summary"));
        }

        [Test]
        public async Task ListSkillAttachmentsAsync_UsesSessionWorkspaceFiltersPathsAndSortsEnabledFirst()
        {
            var appServerClient = new FakeAttachmentClient();
            appServerClient.SkillSnapshots.Add(new AppServerSkillsSnapshot
            {
                WorkspacePath = @"E:\Repos\A",
                Skills = new List<AppServerSkillInfo>
                {
                    new AppServerSkillInfo
                    {
                        Name = "disabled-skill",
                        DisplayName = "Disabled Skill",
                        Path = @"E:\Skills\disabled",
                        Scope = "plugin",
                        Enabled = false
                    },
                    new AppServerSkillInfo
                    {
                        Name = "enabled-skill",
                        DisplayName = "Enabled Skill",
                        ShortDescription = "Enabled skill summary",
                        Path = @"E:\Skills\enabled",
                        Scope = "user",
                        Enabled = true
                    },
                    new AppServerSkillInfo
                    {
                        Name = "missing-path",
                        DisplayName = "Missing Path",
                        Enabled = true
                    }
                }
            });
            var operatorUnderTest = new AppServerCodingRemoteAttachmentCatalogOperator(
                CreateStore(),
                appServerClient,
                appServerClient);

            var result = await operatorUnderTest.ListSkillAttachmentsAsync("session-a", true, CancellationToken.None);

            Assert.That(appServerClient.LastSkillWorkspacePaths, Is.EqualTo(new[] { @"E:\Repos\A" }));
            Assert.That(appServerClient.LastSkillForceReload, Is.True);
            Assert.That(result.SessionId, Is.EqualTo("session-a"));
            Assert.That(result.WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(result.Items, Has.Count.EqualTo(2));
            Assert.That(result.Items[0].Type, Is.EqualTo("skill"));
            Assert.That(result.Items[0].Name, Is.EqualTo("enabled-skill"));
            Assert.That(result.Items[0].DisplayName, Is.EqualTo("Enabled Skill"));
            Assert.That(result.Items[0].Path, Is.EqualTo(@"E:\Skills\enabled"));
            Assert.That(result.Items[0].Enabled, Is.True);
            Assert.That(result.Items[0].SourceText, Is.EqualTo("user"));
            Assert.That(result.Items[0].Scope, Is.EqualTo("user"));
            Assert.That(result.Items[0].Summary, Is.EqualTo("Enabled skill summary"));
            Assert.That(result.Items[1].Name, Is.EqualTo("disabled-skill"));
        }

        [Test]
        public async Task ListSkillAttachmentsAsync_OfficeModeUsesOfficeWorkspacePath()
        {
            var appServerClient = new FakeAttachmentClient();
            appServerClient.SkillSnapshots.Add(new AppServerSkillsSnapshot
            {
                WorkspacePath = @"E:\Documents\MyCodex-office\Reports",
                Skills = new List<AppServerSkillInfo>
                {
                    new AppServerSkillInfo
                    {
                        Name = "office-skill",
                        DisplayName = "Office Skill",
                        Path = @"E:\Skills\office",
                        Scope = "user",
                        Enabled = true
                    }
                }
            });
            var operatorUnderTest = new AppServerCodingRemoteAttachmentCatalogOperator(
                CreateOfficeStore(),
                appServerClient,
                appServerClient,
                AppInteractionMode.Office);

            var result = await operatorUnderTest.ListSkillAttachmentsAsync("office-session", true, CancellationToken.None);

            Assert.That(appServerClient.LastSkillWorkspacePaths, Is.EqualTo(new[] { @"E:\Documents\MyCodex-office\Reports" }));
            Assert.That(result.SessionId, Is.EqualTo("office-session"));
            Assert.That(result.WorkspaceId, Is.EqualTo("office:reports"));
            Assert.That(result.Items.Single().Name, Is.EqualTo("office-skill"));
        }

        private static FakeAppStateStore CreateStore()
        {
            return CreateStore("windows");
        }

        private static FakeAppStateStore CreateStore(string runtimeProfileId)
        {
            return new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    RecentWorkspaces =
                    {
                        new WorkspaceInfo
                        {
                            Id = "workspace-a",
                            RootPath = @"E:\Repos\A",
                            Kind = WorkspaceKind.Coding
                        }
                    },
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            RuntimeProfileId = runtimeProfileId
                        }
                    }
                }
            };
        }

        private static FakeAppStateStore CreateOfficeStore()
        {
            return new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    RecentWorkspaces =
                    {
                        new WorkspaceInfo
                        {
                            Id = "office:reports",
                            RootPath = @"E:\Documents\MyCodex-office\Reports",
                            Kind = WorkspaceKind.Office
                        },
                        new WorkspaceInfo
                        {
                            Id = "workspace-a",
                            RootPath = @"E:\Repos\A",
                            Kind = WorkspaceKind.Coding
                        }
                    },
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "office-session",
                            WorkspaceId = "office:reports",
                            Mode = AppInteractionMode.Office,
                            RuntimeProfileId = "windows"
                        }
                    }
                }
            };
        }

        private sealed class FakeAppStateStore : IAppStateStore
        {
            public AppStateSnapshot Snapshot { get; set; }

            public AppStateSnapshot Load()
            {
                return Snapshot ?? new AppStateSnapshot();
            }

            public void Save(AppStateSnapshot snapshot)
            {
                Snapshot = snapshot;
            }
        }

        private sealed class FakeAttachmentClient : IAppServerPluginClient, IAppServerSkillClient
        {
            public FakeAttachmentClient()
            {
                Plugins = new List<AppServerPluginSummary>();
                SkillSnapshots = new List<AppServerSkillsSnapshot>();
            }

            public List<AppServerPluginSummary> Plugins { get; }

            public List<AppServerSkillsSnapshot> SkillSnapshots { get; }

            public IReadOnlyCollection<string> LastSkillWorkspacePaths { get; private set; }

            public bool LastSkillForceReload { get; private set; }

            public Task<List<AppServerPluginSummary>> ListPluginsAsync()
            {
                return Task.FromResult(Plugins);
            }

            public Task<List<AppServerPluginSummary>> ListPluginsAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(Plugins);
            }

            public Task<AppServerPluginInstalledSnapshot> GetInstalledPluginsAsync(
                CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(new AppServerPluginInstalledSnapshot());
            }

            public Task<List<AppServerSkillsSnapshot>> ListSkillsAsync(IReadOnlyCollection<string> workspacePaths, bool forceReload)
            {
                LastSkillWorkspacePaths = workspacePaths;
                LastSkillForceReload = forceReload;
                return Task.FromResult(SkillSnapshots);
            }

            public Task<bool> WriteSkillEnabledAsync(string path, bool enabled) { throw new System.NotSupportedException(); }
            public Task<AppServerPluginDetails> ReadPluginAsync(string marketplacePath, string pluginName) { throw new System.NotSupportedException(); }
            public Task<AppServerPluginSkillReadResult> ReadPluginSkillAsync(string remoteMarketplaceName, string remotePluginId, string skillName, CancellationToken cancellationToken = default(CancellationToken)) { throw new System.NotSupportedException(); }
            public Task<AppServerMarketplaceUpgradeResult> UpgradeMarketplaceAsync(string marketplaceName, CancellationToken cancellationToken = default(CancellationToken)) { throw new System.NotSupportedException(); }
            public Task<AppServerMarketplaceAddResult> AddMarketplaceAsync(string source, CancellationToken cancellationToken = default(CancellationToken)) { throw new System.NotSupportedException(); }
            public Task RemoveMarketplaceAsync(string marketplaceName, CancellationToken cancellationToken = default(CancellationToken)) { throw new System.NotSupportedException(); }
            public Task<AppServerPluginInstallResult> InstallPluginAsync(string marketplacePath, string pluginName) { throw new System.NotSupportedException(); }
            public Task<AppServerPluginInstallResult> InstallRemotePluginAsync(string remoteMarketplaceName, string remotePluginId) { throw new System.NotSupportedException(); }
            public Task UninstallPluginAsync(string pluginId) { throw new System.NotSupportedException(); }
        }
    }
}
