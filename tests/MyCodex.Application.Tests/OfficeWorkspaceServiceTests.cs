using System;
using System.IO;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficeWorkspaceServiceTests
    {
        [Test]
        public void ResolveOfficeRootPath_UsesConfiguredPathWhenProvided()
        {
            var configuredPath = Path.Combine(Path.GetTempPath(), "MyCodexOffice", "..", "MyCodexOffice");
            var service = CreateWindowsService(Path.Combine(Path.GetTempPath(), "Documents"));

            Assert.That(service.ResolveOfficeRootPath(configuredPath), Is.EqualTo(Path.GetFullPath(configuredPath)));
        }

        [Test]
        public void ResolveOfficeRootPath_FallsBackToMyCodexOfficeFolderUnderDocuments()
        {
            var documentsPath = Path.Combine(Path.GetTempPath(), "MyCodexDocuments");
            var service = CreateWindowsService(documentsPath);

            Assert.That(
                service.ResolveOfficeRootPath(" "),
                Is.EqualTo(Path.GetFullPath(Path.Combine(documentsPath, "MyCodex-office"))));
        }

        [Test]
        public void ResolveOfficeRootPath_FallsBackToUserProfileWhenDocumentsIsMissing()
        {
            var userProfilePath = Path.Combine(Path.GetTempPath(), "MyCodexProfile");
            var service = new OfficeWorkspaceService(() => string.Empty, () => userProfilePath);

            Assert.That(
                service.ResolveOfficeRootPath(null),
                Is.EqualTo(Path.GetFullPath(Path.Combine(userProfilePath, "MyCodex-office"))));
        }

        [TestCase(null, "/home/alice", "/home/alice/MyCodex-office")]
        [TestCase("~", "/home/alice", "/home/alice")]
        [TestCase("~/work", "/home/alice", "/home/alice/work")]
        [TestCase("$HOME/work", "/Users/alice", "/Users/alice/work")]
        public void ResolveOfficeRootPath_UnixTargetResolvesHomeExpressions(
            string configuredPath,
            string unixHomePath,
            string expected)
        {
            var service = CreateUnixService();

            Assert.That(
                service.ResolveOfficeRootPath(AppServerHostPlatform.Wsl, configuredPath, unixHomePath),
                Is.EqualTo(expected));
        }

        [Test]
        public void ResolveOfficeRootPath_UnixAbsoluteOverrideDoesNotRequireHome()
        {
            var service = CreateUnixService();

            Assert.That(
                service.ResolveOfficeRootPath(AppServerHostPlatform.RemoteMac, "/srv/office/./docs", null),
                Is.EqualTo("/srv/office/docs"));
        }

        [TestCase("relative/path", "/home/alice")]
        [TestCase(null, null)]
        [TestCase("~/work", "relative/home")]
        public void ResolveOfficeRootPath_UnixRejectsRelativeRootsAndInvalidRequiredHome(
            string configuredPath,
            string unixHomePath)
        {
            var service = CreateUnixService();

            Assert.Throws<ArgumentException>(() => service.ResolveOfficeRootPath(
                AppServerHostPlatform.Wsl,
                configuredPath,
                unixHomePath));
        }

        [Test]
        public void CreateOfficeWorkspace_UsesWindowsSingletonIdentity()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "MyCodexOffice");
            var service = new OfficeWorkspaceService(() => rootPath, () => Path.GetTempPath());

            var workspace = service.CreateOfficeWorkspace(null);

            Assert.That(workspace.Id, Is.EqualTo("office:windows"));
            Assert.That(workspace.Name, Is.EqualTo("Office"));
            Assert.That(workspace.RootPath, Is.EqualTo(Path.GetFullPath(Path.Combine(rootPath, "MyCodex-office"))));
            Assert.That(workspace.Kind, Is.EqualTo(WorkspaceKind.Office));
        }

        [Test]
        public void CreateOfficeWorkspace_DirectoryChangePreservesRuntimeScopedIdentity()
        {
            var service = CreateUnixService();

            var first = service.CreateOfficeWorkspace(" WSL|Ubuntu ", AppServerHostPlatform.Wsl, "/home/alice/Office");
            var moved = service.CreateOfficeWorkspace("wsl|ubuntu", AppServerHostPlatform.Wsl, "/srv/shared/Office");
            var otherRuntime = service.CreateOfficeWorkspace("remote-unix|server-a", AppServerHostPlatform.RemoteMac, "/srv/shared/Office");

            Assert.That(first.Id, Is.EqualTo("office:wsl|ubuntu"));
            Assert.That(moved.Id, Is.EqualTo(first.Id));
            Assert.That(moved.RootPath, Is.EqualTo("/srv/shared/Office"));
            Assert.That(otherRuntime.Id, Is.EqualTo("office:remote-unix|server-a"));
        }

        [Test]
        public void ResolveOfficeWorkspaceForImport_DescendantUsesSingleRuntimeWorkspace()
        {
            var service = CreateUnixService();

            var workspace = service.ResolveOfficeWorkspaceForImport(
                "wsl|ubuntu",
                AppServerHostPlatform.Wsl,
                "/home/alice/Office",
                "/home/alice/Office/generated/assets",
                Array.Empty<WorkspaceInfo>());

            Assert.That(workspace.Id, Is.EqualTo("office:wsl|ubuntu"));
            Assert.That(workspace.RootPath, Is.EqualTo("/home/alice/Office"));
        }

        [Test]
        public void ResolveOfficeWorkspaceForImport_RejectsPathOutsideRootWithPlatformCaseRules()
        {
            var service = CreateUnixService();
            var windowsRoot = Path.Combine(Path.GetTempPath(), "OfficeRoot");

            Assert.DoesNotThrow(() => service.ResolveOfficeWorkspaceForImport(
                "windows",
                AppServerHostPlatform.Windows,
                windowsRoot,
                Path.Combine(windowsRoot.ToUpperInvariant(), "Child"),
                Array.Empty<WorkspaceInfo>()));
            Assert.Throws<ArgumentException>(() => service.ResolveOfficeWorkspaceForImport(
                "wsl|ubuntu",
                AppServerHostPlatform.Wsl,
                "/Office",
                "/office/Child",
                Array.Empty<WorkspaceInfo>()));
        }

        [Test]
        public void ResolveOfficeWorkspaceForImport_WindowsDriveRootAcceptsDescendant()
        {
            var service = CreateWindowsService(Path.GetTempPath());
            var driveRoot = Path.GetPathRoot(Path.GetTempPath());

            Assert.DoesNotThrow(() => service.ResolveOfficeWorkspaceForImport(
                "windows",
                AppServerHostPlatform.Windows,
                driveRoot,
                Path.Combine(driveRoot, "OfficeChild"),
                Array.Empty<WorkspaceInfo>()));
        }

        private static OfficeWorkspaceService CreateWindowsService(string documentsPath)
        {
            return new OfficeWorkspaceService(() => documentsPath, () => Path.GetTempPath());
        }

        private static OfficeWorkspaceService CreateUnixService()
        {
            return new OfficeWorkspaceService(
                () => throw new InvalidOperationException("Windows Documents must not be used."),
                () => throw new InvalidOperationException("Windows profile must not be used."));
        }
    }
}
