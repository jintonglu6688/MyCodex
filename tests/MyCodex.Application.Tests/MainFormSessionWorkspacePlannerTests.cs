using System.Collections.Generic;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormSessionWorkspacePlannerTests
    {
        [Test]
        public void ResolveWorkspace_PrefersSessionWorkspaceOverAutomationRunWorkspace()
        {
            var sessionWorkspace = Workspace("session-workspace", @"E:\Repos\Session", WorkspaceKind.Coding);
            var runWorkspace = Workspace("run-workspace", @"E:\Repos\Run", WorkspaceKind.Coding);
            var run = new AutomationRunSnapshot { WorkspaceId = runWorkspace.Id };
            var session = new ChatSession { WorkspaceId = sessionWorkspace.Id };

            var result = MainFormSessionWorkspacePlanner.ResolveWorkspace(
                run,
                session,
                null,
                id => id == sessionWorkspace.Id ? sessionWorkspace : id == runWorkspace.Id ? runWorkspace : null,
                path => null,
                path => null,
                path => false);

            Assert.That(result, Is.SameAs(sessionWorkspace));
        }

        [Test]
        public void ResolveWorkspace_PrefersRunWorkspaceOverPathFallback()
        {
            var runWorkspace = Workspace("run-workspace", @"E:\Repos\Run", WorkspaceKind.Coding);
            var pathWorkspace = Workspace("path-workspace", @"E:\Repos\Path", WorkspaceKind.Coding);
            var run = new AutomationRunSnapshot
            {
                WorkspaceId = runWorkspace.Id,
                WorkspacePath = pathWorkspace.RootPath
            };

            var result = MainFormSessionWorkspacePlanner.ResolveWorkspace(
                run,
                null,
                null,
                id => id == runWorkspace.Id ? runWorkspace : null,
                path => pathWorkspace,
                path => null,
                path => false);

            Assert.That(result, Is.SameAs(runWorkspace));
        }

        [Test]
        public void ResolveWorkspace_UsesPathFallbackForExistingWorkspaceWithWindowsPathNormalization()
        {
            var existingWorkspace = Workspace("path-workspace", @"E:\Repos\Project", WorkspaceKind.Coding);
            var workspaces = new[] { existingWorkspace };

            var result = MainFormSessionWorkspacePlanner.ResolveWorkspace(
                null,
                null,
                @"e:\repos\project\..\Project\",
                id => null,
                path => MainFormSessionWorkspacePlanner.FindWorkspaceByPath(workspaces, path),
                path => null,
                path => false);

            Assert.That(result, Is.SameAs(existingWorkspace));
            Assert.That(MainFormSessionWorkspacePlanner.ArePathsEqual(existingWorkspace.RootPath, @"e:\repos\project\..\Project\"), Is.True);
        }

        [Test]
        public void ResolveWorkspace_ReturnsNullForRemoteUnixPath()
        {
            var localWorkspace = Workspace("local", @"E:\Repos\Project", WorkspaceKind.Coding);
            var result = MainFormSessionWorkspacePlanner.ResolveWorkspace(
                new AutomationRunSnapshot { WorkspacePath = "/home/user/project" },
                null,
                null,
                id => null,
                path => localWorkspace,
                path => Workspace("office", path, WorkspaceKind.Office),
                path => true);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void ResolveWorkspace_UsesInjectedOfficeResolverForOfficePath()
        {
            var officeWorkspace = Workspace("office-reports", @"E:\Documents\MyCodex-office\Reports", WorkspaceKind.Office);
            var result = MainFormSessionWorkspacePlanner.ResolveWorkspace(
                null,
                null,
                officeWorkspace.RootPath,
                id => null,
                path => null,
                path => officeWorkspace,
                path => path == officeWorkspace.RootPath);

            Assert.That(result, Is.SameAs(officeWorkspace));
        }

        [Test]
        public void ResolveWorkspace_ReturnsNullForBlankFallbackPath()
        {
            var result = MainFormSessionWorkspacePlanner.ResolveWorkspace(
                null,
                null,
                "  ",
                id => null,
                path => Workspace("path", path, WorkspaceKind.Coding),
                path => Workspace("office", path, WorkspaceKind.Office),
                path => true);

            Assert.That(result, Is.Null);
        }

        [TestCase(AppInteractionMode.Office)]
        [TestCase(AppInteractionMode.Coding)]
        public void ResolveInteractionMode_PrefersSessionMode(AppInteractionMode mode)
        {
            var result = MainFormSessionWorkspacePlanner.ResolveInteractionMode(
                new ChatSession { Mode = mode },
                Workspace("office", @"E:\Documents\MyCodex-office", WorkspaceKind.Office),
                @"E:\Documents\MyCodex-office",
                path => mode != AppInteractionMode.Office);

            Assert.That(result, Is.EqualTo(mode));
        }

        [Test]
        public void ResolveInteractionMode_ReturnsOfficeForOfficeWorkspace()
        {
            var result = MainFormSessionWorkspacePlanner.ResolveInteractionMode(
                null,
                Workspace("office", @"E:\Documents\MyCodex-office", WorkspaceKind.Office),
                null,
                path => false);

            Assert.That(result, Is.EqualTo(AppInteractionMode.Office));
        }

        [Test]
        public void ResolveInteractionMode_ReturnsOfficeForOfficePath()
        {
            var result = MainFormSessionWorkspacePlanner.ResolveInteractionMode(
                null,
                null,
                @"E:\Documents\MyCodex-office\Reports",
                path => true);

            Assert.That(result, Is.EqualTo(AppInteractionMode.Office));
        }

        [Test]
        public void ResolveInteractionMode_DefaultsToCoding()
        {
            var result = MainFormSessionWorkspacePlanner.ResolveInteractionMode(
                null,
                null,
                @"E:\Repos\Project",
                path => false);

            Assert.That(result, Is.EqualTo(AppInteractionMode.Coding));
        }

        [Test]
        public void ResolveWorkspacePath_ReturnsNullForBlankFallbackPath()
        {
            Assert.That(MainFormSessionWorkspacePlanner.ResolveWorkspacePath(null, "  "), Is.Null);
        }

        private static WorkspaceInfo Workspace(string id, string rootPath, WorkspaceKind kind)
        {
            return new WorkspaceInfo
            {
                Id = id,
                Name = id,
                RootPath = rootPath,
                Kind = kind
            };
        }
    }
}
