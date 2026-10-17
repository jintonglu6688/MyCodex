using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCodingRemoteSessionCreatorTests
    {
        [Test]
        public async Task CreateSessionAsync_AddsCurrentCodingSessionForWorkspace()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    CurrentWorkspaceId = "workspace-old",
                    CurrentSessionId = "session-old",
                    CurrentCodingWorkspaceId = "workspace-old",
                    CurrentCodingSessionId = "session-old",
                    RecentWorkspaces =
                    {
                        new WorkspaceInfo
                        {
                            Id = "workspace-a",
                            Name = "Repo A",
                            RootPath = @"E:\Repos\A",
                            Kind = WorkspaceKind.Coding
                        }
                    },
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-old",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            Title = "Repo A",
                            RuntimeProfileId = "windows"
                        }
                    },
                    RuntimeProfiles =
                    {
                        new RuntimeProfileStateSnapshot
                        {
                            RuntimeProfileId = "windows",
                            CurrentWorkspaceId = "workspace-old",
                            CurrentSessionId = "session-old"
                        }
                    }
                }
            };
            var creator = new AppServerCodingRemoteSessionCreator(store);

            var result = await creator.CreateSessionAsync(
                new CodingRemoteSessionCreateRequest
                {
                    WorkspaceId = "workspace-a",
                    Title = "Custom title"
                },
                CancellationToken.None);

            Assert.That(result.WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(result.Title, Is.EqualTo("Custom title"));
            Assert.That(store.Snapshot.Sessions.First().Title, Is.EqualTo("Custom title"));
            Assert.That(result.BranchKind, Is.EqualTo(CodingRemoteSessionBranchKinds.Mainline));
            Assert.That(result.State, Is.EqualTo(CodingRemoteSessionStates.Idle));
            Assert.That(store.Snapshot.Sessions.First().Id, Is.EqualTo(result.SessionId));
            Assert.That(store.Snapshot.Sessions.First().ThreadId, Is.EqualTo(string.Empty));
            Assert.That(store.Snapshot.CurrentWorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(store.Snapshot.CurrentSessionId, Is.EqualTo(result.SessionId));
            Assert.That(store.Snapshot.CurrentCodingWorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(store.Snapshot.CurrentCodingSessionId, Is.EqualTo(result.SessionId));
            Assert.That(store.Snapshot.RuntimeProfiles[0].CurrentWorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(store.Snapshot.RuntimeProfiles[0].CurrentSessionId, Is.EqualTo(result.SessionId));
        }

        [Test]
        public async Task CreateSessionAsync_OfficeModeAddsCurrentOfficeSessionForCategory()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    CurrentCodingWorkspaceId = "workspace-old",
                    CurrentCodingSessionId = "session-old",
                    CurrentOfficeWorkspaceId = "office-old",
                    CurrentOfficeSessionId = "office-session-old",
                    RecentWorkspaces =
                    {
                        new WorkspaceInfo
                        {
                            Id = "office:reports",
                            Name = "Reports",
                            RootPath = @"E:\Documents\MyCodex-office\Reports",
                            Kind = WorkspaceKind.Office
                        },
                        new WorkspaceInfo
                        {
                            Id = "workspace-a",
                            Name = "Repo A",
                            RootPath = @"E:\Repos\A",
                            Kind = WorkspaceKind.Coding
                        }
                    },
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "office-session-old",
                            WorkspaceId = "office:reports",
                            Mode = AppInteractionMode.Office,
                            Title = "Old Office"
                        }
                    }
                }
            };
            var creator = new AppServerCodingRemoteSessionCreator(store, AppInteractionMode.Office);

            var result = await creator.CreateSessionAsync(
                new CodingRemoteSessionCreateRequest
                {
                    WorkspaceId = "office:reports",
                    Title = "Report draft"
                },
                CancellationToken.None);

            Assert.That(result.WorkspaceId, Is.EqualTo("office:reports"));
            Assert.That(result.Title, Is.EqualTo("Report draft"));
            Assert.That(store.Snapshot.Sessions.First().Mode, Is.EqualTo(AppInteractionMode.Office));
            Assert.That(store.Snapshot.CurrentInteractionMode, Is.EqualTo(AppInteractionMode.Office));
            Assert.That(store.Snapshot.CurrentWorkspaceId, Is.EqualTo("office:reports"));
            Assert.That(store.Snapshot.CurrentSessionId, Is.EqualTo(result.SessionId));
            Assert.That(store.Snapshot.CurrentOfficeWorkspaceId, Is.EqualTo("office:reports"));
            Assert.That(store.Snapshot.CurrentOfficeSessionId, Is.EqualTo(result.SessionId));
            Assert.That(store.Snapshot.CurrentCodingWorkspaceId, Is.EqualTo("workspace-old"));
            Assert.That(store.Snapshot.CurrentCodingSessionId, Is.EqualTo("session-old"));
        }

        [Test]
        public void CreateSessionAsync_MissingWorkspaceThrowsWorkspaceNotFound()
        {
            var creator = new AppServerCodingRemoteSessionCreator(new FakeAppStateStore());

            var ex = Assert.ThrowsAsync<CodingRemoteOperationException>(async () =>
                await creator.CreateSessionAsync(
                    new CodingRemoteSessionCreateRequest { WorkspaceId = "missing" },
                    CancellationToken.None));

            Assert.That(ex.ErrorCode, Is.EqualTo(MyCodex.Application.RemoteRelay.RemoteErrorCodes.WorkspaceNotFound));
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
    }
}
