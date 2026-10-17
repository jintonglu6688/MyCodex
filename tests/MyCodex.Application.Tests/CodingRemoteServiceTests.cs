using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class CodingRemoteServiceTests
    {
        [Test]
        public async Task ListWorkspacesAsync_ReturnsOnlyCodingWorkspacesFromAppState()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    RecentWorkspaces =
                    {
                        new WorkspaceInfo
                        {
                            Id = "workspace-a",
                            Name = "Repo A",
                            RootPath = @"E:\Repos\A",
                            Kind = WorkspaceKind.Coding,
                            LastOpenedAt = new DateTime(2026, 6, 7, 8, 0, 0, DateTimeKind.Utc)
                        },
                        new WorkspaceInfo
                        {
                            Id = "office-a",
                            Name = "Office",
                            RootPath = @"E:\Docs",
                            Kind = WorkspaceKind.Office,
                            LastOpenedAt = new DateTime(2026, 6, 7, 9, 0, 0, DateTimeKind.Utc)
                        }
                    }
                }
            };
            var service = new CodingRemoteService(store);

            var workspaces = await service.ListWorkspacesAsync(CancellationToken.None);

            Assert.That(workspaces, Has.Count.EqualTo(1));
            Assert.That(workspaces[0].WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(workspaces[0].DisplayName, Is.EqualTo("Repo A"));
            Assert.That(workspaces[0].Path, Is.EqualTo(@"E:\Repos\A"));
            Assert.That(workspaces[0].LastUsedAt, Is.EqualTo(new DateTime(2026, 6, 7, 8, 0, 0, DateTimeKind.Utc)));
            Assert.That(workspaces[0].Kind, Is.EqualTo(CodingRemoteKinds.Coding));
        }

        [Test]
        public async Task ListWorkspacesAsync_OfficeModeEnsuresDefaultAndReturnsOnlyOfficeWorkspaces()
        {
            var rootPath = CreateUniqueDirectoryPath("OfficeRemoteList");
            var categoryPath = Path.Combine(rootPath, "Reports");
            Directory.CreateDirectory(categoryPath);
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    RecentWorkspaces =
                    {
                        new WorkspaceInfo
                        {
                            Id = "workspace-a",
                            Name = "Repo A",
                            RootPath = @"E:\Repos\A",
                            Kind = WorkspaceKind.Coding
                        },
                        new WorkspaceInfo
                        {
                            Id = "office:reports",
                            Name = "Reports",
                            RootPath = categoryPath,
                            Kind = WorkspaceKind.Office,
                            LastOpenedAt = new DateTime(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc)
                        }
                    },
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "office-session",
                            WorkspaceId = "office:reports",
                            Mode = AppInteractionMode.Office
                        }
                    }
                }
            };
            var service = CodingRemoteService.CreateOffice(
                store,
                new FakeAppConfigStore(new AppConfigSnapshot()),
                new OfficeWorkspaceService(() => null, () => null),
                modeSettingsLoader: config => Task.FromResult(new ModeSettingsSnapshot { OfficeRootPath = rootPath }));

            var workspaces = await service.ListWorkspacesAsync(CancellationToken.None);

            Assert.That(Directory.Exists(rootPath), Is.True);
            Assert.That(workspaces, Has.Count.EqualTo(1));
            Assert.That(workspaces.Single().Kind, Is.EqualTo(CodingRemoteKinds.Office));
            Assert.That(workspaces.Single().DisplayName, Is.EqualTo("Office"));
            Assert.That(workspaces.Single().Path, Is.EqualTo(rootPath));
            Assert.That(workspaces.Single().WorkspaceId, Is.EqualTo("office:windows"));
            Assert.That(store.Snapshot.RecentWorkspaces.Count(workspace => workspace.Kind == WorkspaceKind.Office), Is.EqualTo(1));
            Assert.That(store.Snapshot.Sessions.Single().WorkspaceId, Is.EqualTo("office:windows"));
        }

        [Test]
        public async Task OfficeRuntimeFileSystem_WslAndSshUseSingleTargetWorkspacePaths()
        {
            var fileSystem = new FakeOfficeRuntimeFileSystem { HomeDirectory = "/home/alice" };
            var wslConfig = new AppConfigSnapshot
            {
                CodexRuntimeSource = MyCodex.Shared.Constants.CodexRuntimeSources.Wsl,
                WslAppServer = new WslAppServerConfigSnapshot { DistributionName = "Ubuntu" }
            };
            var wslStore = new FakeAppStateStore();
            var wslService = CodingRemoteService.CreateOffice(
                wslStore,
                new FakeAppConfigStore(wslConfig),
                new OfficeWorkspaceService(() => throw new InvalidOperationException(), () => throw new InvalidOperationException()),
                officeRuntimeScopeService: new OfficeRuntimeScopeService(),
                officeRuntimeFileSystem: fileSystem,
                modeSettingsLoader: config => Task.FromResult(new ModeSettingsSnapshot { OfficeRootPath = "~/office" }));

            var wslWorkspaces = await wslService.ListWorkspacesAsync(CancellationToken.None);

            Assert.That(wslWorkspaces.Single().Path, Is.EqualTo("/home/alice/office"));
            Assert.That(wslWorkspaces.Single().WorkspaceId, Is.EqualTo("office:wsl|ubuntu"));
            Assert.That(fileSystem.EnsuredPaths, Does.Contain("/home/alice/office"));

            var sshConfig = new AppConfigSnapshot
            {
                CodexRuntimeSource = MyCodex.Shared.Constants.CodexRuntimeSources.RemoteUnixSsh,
                SelectedRemoteUnixSshProfileId = "work",
                RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                {
                    new RemoteUnixSshProfileConfigSnapshot { Id = "work", Ssh = new RemoteMacSshConfigSnapshot() }
                }
            };
            var sshStore = new FakeAppStateStore();
            var sshService = CodingRemoteService.CreateOffice(
                sshStore,
                new FakeAppConfigStore(sshConfig),
                new OfficeWorkspaceService(() => throw new InvalidOperationException(), () => throw new InvalidOperationException()),
                officeRuntimeScopeService: new OfficeRuntimeScopeService(),
                officeRuntimeFileSystem: fileSystem,
                modeSettingsLoader: config => Task.FromResult(new ModeSettingsSnapshot { OfficeRootPath = "/srv/office" }));

            var sshWorkspaces = await sshService.ListWorkspacesAsync(CancellationToken.None);

            Assert.That(sshWorkspaces.Single().Path, Is.EqualTo("/srv/office"));
            Assert.That(sshWorkspaces.Single().WorkspaceId, Is.EqualTo("office:remote-unix|work"));
            Assert.That(fileSystem.EnsuredPaths, Does.Contain("/srv/office"));
        }

        [Test]
        public async Task ListSessionsAsync_ReturnsMainCodingSessionsForWorkspaceWithWindowsListFields()
        {
            var createdAt = new DateTime(2026, 6, 7, 7, 0, 0, DateTimeKind.Utc);
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "old",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            Title = "Old",
                            Preview = "old preview",
                            CreatedAt = createdAt.AddHours(-2),
                            LastActiveAt = new DateTime(2026, 6, 7, 8, 0, 0, DateTimeKind.Utc)
                        },
                        new ChatSession
                        {
                            Id = "new",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            ThreadId = "thread-new",
                            Title = "Mainline | New",
                            Preview = "new preview",
                            CreatedAt = createdAt,
                            LastActiveAt = new DateTime(2026, 6, 7, 9, 0, 0, DateTimeKind.Utc)
                        },
                        new ChatSession
                        {
                            Id = "subagent",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            Title = "Subagent",
                            ParentThreadId = "thread-new",
                            IsSubAgent = true,
                            SubAgentNickname = "Ampere",
                            SubAgentRole = "reviewer",
                            LastActiveAt = new DateTime(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc)
                        },
                        new ChatSession
                        {
                            Id = "office",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Office,
                            Title = "Office",
                            LastActiveAt = new DateTime(2026, 6, 7, 10, 0, 0, DateTimeKind.Utc)
                        }
                    }
                }
            };
            var service = new CodingRemoteService(store);

            var sessions = await service.ListSessionsAsync("workspace-a", CancellationToken.None);

            Assert.That(sessions.Select(session => session.SessionId).ToArray(), Is.EqualTo(new[] { "new", "old" }));
            Assert.That(sessions[0].Title, Is.EqualTo("New"));
            Assert.That(sessions[0].CreatedAt, Is.EqualTo(createdAt));
            Assert.That(sessions[0].BranchKind, Is.EqualTo(CodingRemoteSessionBranchKinds.Mainline));
            Assert.That(sessions[0].SubAgentCount, Is.EqualTo(1));
            Assert.That(sessions[0].SubAgents, Has.Count.EqualTo(1));
            Assert.That(sessions[0].SubAgents[0].SessionId, Is.EqualTo("subagent"));
            Assert.That(sessions[0].SubAgents[0].Title, Is.EqualTo("Subagent"));
            Assert.That(sessions[0].SubAgents[0].SubAgentName, Is.EqualTo("Ampere"));
            Assert.That(sessions[0].SubAgents[0].SubAgentRole, Is.EqualTo("reviewer"));
            Assert.That(sessions[0].LastMessagePreview, Is.EqualTo("new preview"));
            Assert.That(sessions[0].State, Is.EqualTo(CodingRemoteSessionStates.Idle));
        }

        [Test]
        public async Task ListMessagesAsync_WithoutMessageReaderReturnsEmptyPageForKnownSession()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding
                        }
                    }
                }
            };
            var service = new CodingRemoteService(store);

            var page = await service.ListMessagesAsync(
                new CodingRemoteMessagesListRequest
                {
                    SessionId = "session-a",
                    Limit = 50
                },
                CancellationToken.None);

            Assert.That(page.SessionId, Is.EqualTo("session-a"));
            Assert.That(page.Messages, Is.Empty);
            Assert.That(page.NextCursor, Is.Null);
        }

        private static string CreateUniqueDirectoryPath(string prefix)
        {
            return Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                prefix + "-" + Guid.NewGuid().ToString("N"));
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

        private sealed class FakeAppConfigStore : IAppConfigStore
        {
            public FakeAppConfigStore(AppConfigSnapshot snapshot)
            {
                Snapshot = snapshot;
            }

            public AppConfigSnapshot Snapshot { get; set; }

            public AppConfigSnapshot Load()
            {
                return Snapshot ?? new AppConfigSnapshot();
            }

            public void Save(AppConfigSnapshot snapshot)
            {
                Snapshot = snapshot;
            }
        }

        private sealed class FakeOfficeRuntimeFileSystem : IOfficeRuntimeFileSystem
        {
            private readonly HashSet<string> _directories = new HashSet<string>(StringComparer.Ordinal);

            public string HomeDirectory { get; set; }

            public List<string> EnsuredPaths { get; } = new List<string>();

            public Task<string> ResolveHomeDirectoryAsync(AppConfigSnapshot config)
            {
                return Task.FromResult(HomeDirectory);
            }

            public Task<bool> DirectoryExistsAsync(AppConfigSnapshot config, string path)
            {
                return Task.FromResult(_directories.Contains(path));
            }

            public Task<RuntimeFileSystemOperationResult> EnsureDirectoryAsync(AppConfigSnapshot config, string path)
            {
                EnsuredPaths.Add(path);
                _directories.Add(path);
                return Task.FromResult(RuntimeFileSystemOperationResult.Ok(string.Empty));
            }

            public Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(AppConfigSnapshot config, string path)
            {
                return Task.FromResult((IReadOnlyList<RuntimeFileSystemEntrySnapshot>)new RuntimeFileSystemEntrySnapshot[0]);
            }
        }

    }
}
