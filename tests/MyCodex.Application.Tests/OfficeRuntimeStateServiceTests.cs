using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficeRuntimeStateServiceTests
    {
        [Test]
        public void GetOrCreateState_DeduplicatesActiveScopeAndPreservesInactiveScopes()
        {
            var snapshot = new AppStateSnapshot
            {
                OfficeRuntimeStates = new List<OfficeRuntimeStateSnapshot>
                {
                    new OfficeRuntimeStateSnapshot
                    {
                        RuntimeScopeId = " WSL|Ubuntu ",
                        RecentWorkspaces = new List<WorkspaceInfo>
                        {
                            Workspace("office:wsl|ubuntu:first", "/office/First", "First")
                        },
                        CurrentWorkspaceId = "office:wsl|ubuntu:first"
                    },
                    new OfficeRuntimeStateSnapshot
                    {
                        RuntimeScopeId = "wsl|ubuntu",
                        RecentWorkspaces = new List<WorkspaceInfo>
                        {
                            Workspace("office:wsl|ubuntu:second", "/office/Second", "Second")
                        },
                        CurrentSessionId = "second-session"
                    },
                    new OfficeRuntimeStateSnapshot
                    {
                        RuntimeScopeId = "windows",
                        RecentWorkspaces = new List<WorkspaceInfo>
                        {
                            Workspace("office:windows:inactive", @"C:\Office", "Windows")
                        }
                    }
                }
            };
            var service = new OfficeRuntimeStateService();

            var active = service.GetOrCreateState(snapshot, "wsl|ubuntu");

            Assert.That(snapshot.OfficeRuntimeStates, Has.Count.EqualTo(2));
            Assert.That(active.RuntimeScopeId, Is.EqualTo("wsl|ubuntu"));
            Assert.That(active.RecentWorkspaces.Select(item => item.Name),
                Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(active.CurrentWorkspaceId, Is.EqualTo("office:wsl|ubuntu:first"));
            Assert.That(active.CurrentSessionId, Is.EqualTo("second-session"));
            Assert.That(snapshot.OfficeRuntimeStates.Single(item => item.RuntimeScopeId == "windows")
                .RecentWorkspaces[0].Name, Is.EqualTo("Windows"));
        }

        [Test]
        public void MigrateLegacyState_RewritesWindowsWorkspaceAndLinkedSessionWithoutMetadataLoss()
        {
            var workspaceTime = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Local);
            var createdAt = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Local);
            var activeAt = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Local);
            const string legacyWorkspaceId = @"office:C:\Office\Reports";
            var snapshot = new AppStateSnapshot
            {
                CurrentOfficeWorkspaceId = legacyWorkspaceId,
                CurrentOfficeSessionId = "office-session",
                RecentWorkspaces = new List<WorkspaceInfo>
                {
                    new WorkspaceInfo
                    {
                        Id = legacyWorkspaceId,
                        Name = "Reports",
                        RootPath = @"C:\Office\Reports",
                        Kind = WorkspaceKind.Office,
                        LastOpenedAt = workspaceTime
                    }
                },
                Sessions = new List<ChatSession>
                {
                    new ChatSession
                    {
                        Id = "office-session",
                        WorkspaceId = legacyWorkspaceId,
                        Mode = AppInteractionMode.Office,
                        ThreadId = "thread-1",
                        Title = "Quarterly report",
                        CreatedAt = createdAt,
                        LastActiveAt = activeAt,
                        IsSubAgent = true,
                        ParentThreadId = "parent-thread"
                    }
                }
            };
            var service = new OfficeRuntimeStateService();

            var active = service.MigrateLegacyState(
                snapshot,
                new OfficeRuntimeTarget("windows", AppServerHostPlatform.Windows, CodexRuntimeSources.Bundled));

            var expectedId = new OfficeWorkspaceService().CreateOfficeWorkspace(
                "windows",
                AppServerHostPlatform.Windows,
                @"C:\Office\Reports").Id;
            Assert.That(active.RuntimeScopeId, Is.EqualTo("windows"));
            Assert.That(active.RecentWorkspaces, Has.Count.EqualTo(1));
            Assert.That(active.RecentWorkspaces[0].Id, Is.EqualTo(expectedId));
            Assert.That(active.RecentWorkspaces[0].Name, Is.EqualTo("Reports"));
            Assert.That(active.RecentWorkspaces[0].LastOpenedAt, Is.EqualTo(workspaceTime));
            Assert.That(active.CurrentWorkspaceId, Is.EqualTo(expectedId));
            Assert.That(active.CurrentSessionId, Is.EqualTo("office-session"));
            Assert.That(snapshot.CurrentOfficeWorkspaceId, Is.EqualTo(expectedId));
            Assert.That(snapshot.Sessions[0].WorkspaceId, Is.EqualTo(expectedId));
            Assert.That(snapshot.Sessions[0].OfficeRuntimeScopeId, Is.EqualTo("windows"));
            Assert.That(snapshot.Sessions[0].Id, Is.EqualTo("office-session"));
            Assert.That(snapshot.Sessions[0].ThreadId, Is.EqualTo("thread-1"));
            Assert.That(snapshot.Sessions[0].Title, Is.EqualTo("Quarterly report"));
            Assert.That(snapshot.Sessions[0].CreatedAt, Is.EqualTo(createdAt));
            Assert.That(snapshot.Sessions[0].LastActiveAt, Is.EqualTo(activeAt));
            Assert.That(snapshot.Sessions[0].ParentThreadId, Is.EqualTo("parent-thread"));
        }

        [Test]
        public void MigrateLegacyState_UnixEndpointUsesActiveUnixScope()
        {
            const string legacyId = "office:/home/alice/MyCodex-office/Docs";
            var snapshot = CreateLegacySnapshot(legacyId, "/home/alice/MyCodex-office/Docs");
            var service = new OfficeRuntimeStateService();

            var active = service.MigrateLegacyState(
                snapshot,
                new OfficeRuntimeTarget("wsl|ubuntu", AppServerHostPlatform.Wsl, CodexRuntimeSources.Wsl));

            Assert.That(active.RuntimeScopeId, Is.EqualTo("wsl|ubuntu"));
            Assert.That(active.RecentWorkspaces, Has.Count.EqualTo(1));
            Assert.That(active.RecentWorkspaces[0].Id, Is.EqualTo("office:wsl|ubuntu"));
            Assert.That(snapshot.Sessions[0].OfficeRuntimeScopeId, Is.EqualTo("wsl|ubuntu"));
        }

        [Test]
        public void MigrateLegacyState_WindowsLookingPathDefaultsToWindowsEvenOnUnixEndpoint()
        {
            const string legacyId = @"office:C:\Office\Docs";
            var snapshot = CreateLegacySnapshot(legacyId, @"C:\Office\Docs");
            var service = new OfficeRuntimeStateService();

            service.MigrateLegacyState(
                snapshot,
                new OfficeRuntimeTarget("remote-unix|profile-a", AppServerHostPlatform.RemoteMac, CodexRuntimeSources.RemoteUnixSsh));

            var windowsState = snapshot.OfficeRuntimeStates.SingleOrDefault(
                item => item.RuntimeScopeId == "windows");
            Assert.That(windowsState, Is.Not.Null);
            Assert.That(windowsState.RecentWorkspaces[0].Id, Is.EqualTo("office:windows"));
            Assert.That(snapshot.Sessions[0].OfficeRuntimeScopeId, Is.EqualTo("windows"));
            Assert.That(snapshot.OfficeRuntimeStates.Single(item => item.RuntimeScopeId == "remote-unix|profile-a")
                .RecentWorkspaces, Is.Empty);
        }

        [Test]
        public void MigrateLegacyState_CollapsesCategoriesAndKeepsAllSessions()
        {
            var snapshot = new AppStateSnapshot
            {
                CurrentOfficeWorkspaceId = "office:/home/alice/Office/Reports",
                RecentWorkspaces = new List<WorkspaceInfo>
                {
                    Workspace("office:/home/alice/Office/Reports", "/home/alice/Office/Reports", "Reports"),
                    Workspace("office:/home/alice/Office/Templates", "/home/alice/Office/Templates", "Templates")
                },
                Sessions = new List<ChatSession>
                {
                    new ChatSession { Id = "reports", WorkspaceId = "office:/home/alice/Office/Reports", Mode = AppInteractionMode.Office },
                    new ChatSession { Id = "templates", WorkspaceId = "office:/home/alice/Office/Templates", Mode = AppInteractionMode.Office }
                }
            };
            var service = new OfficeRuntimeStateService();

            var active = service.MigrateLegacyState(
                snapshot,
                new OfficeRuntimeTarget("wsl|ubuntu", AppServerHostPlatform.Wsl, CodexRuntimeSources.Wsl));

            Assert.That(active.RecentWorkspaces, Has.Count.EqualTo(1));
            Assert.That(active.RecentWorkspaces[0].Id, Is.EqualTo("office:wsl|ubuntu"));
            Assert.That(snapshot.Sessions.Select(session => session.WorkspaceId).Distinct(),
                Is.EqualTo(new[] { "office:wsl|ubuntu" }));
            Assert.That(snapshot.Sessions.Select(session => session.Id),
                Is.EquivalentTo(new[] { "reports", "templates" }));
        }

        private static AppStateSnapshot CreateLegacySnapshot(string workspaceId, string rootPath)
        {
            return new AppStateSnapshot
            {
                CurrentOfficeWorkspaceId = workspaceId,
                CurrentOfficeSessionId = "office-session",
                RecentWorkspaces = new List<WorkspaceInfo>
                {
                    Workspace(workspaceId, rootPath, "Docs")
                },
                Sessions = new List<ChatSession>
                {
                    new ChatSession
                    {
                        Id = "office-session",
                        WorkspaceId = workspaceId,
                        Mode = AppInteractionMode.Office
                    }
                }
            };
        }

        private static WorkspaceInfo Workspace(string id, string rootPath, string name)
        {
            return new WorkspaceInfo
            {
                Id = id,
                Name = name,
                RootPath = rootPath,
                Kind = WorkspaceKind.Office,
                LastOpenedAt = new DateTime(2026, 7, 1, 8, 0, 0)
            };
        }
    }
}
