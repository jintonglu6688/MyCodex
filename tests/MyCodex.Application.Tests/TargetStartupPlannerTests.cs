using System;
using System.Collections.Generic;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetStartupPlannerTests
    {
        [Test]
        public void WindowsWorkspace_SelectsWindowsEvenWhenLastOpenedWasRemote()
        {
            var catalog = Catalog("ssh-build");
            var selection = TargetStartupPlanner.Resolve(catalog, null, @"E:\Projects\site");

            Assert.That(selection.Target.Id, Is.EqualTo("windows"));
            Assert.That(selection.WorkspacePath, Is.EqualTo(@"E:\Projects\site"));
            Assert.That(catalog.LastOpenedTargetId, Is.EqualTo("ssh-build"));
        }

        [TestCase(@"\\wsl$\Ubuntu\home\alice\repo", "wsl-ubuntu", "/home/alice/repo")]
        [TestCase(@"\\wsl.localhost\Debian\home\alice\repo", "wsl-debian", "/home/alice/repo")]
        [TestCase(@"\\WSL.LOCALHOST\ubuntu\home\alice\repo with spaces", "wsl-ubuntu", "/home/alice/repo with spaces")]
        [TestCase(@"\\wsl$\Ubuntu", "wsl-ubuntu", "/")]
        public void WslUnc_SelectsMappedConfiguredDistribution(string workspace, string targetId, string unixPath)
        {
            var selection = TargetStartupPlanner.Resolve(Catalog("windows"), null, workspace);

            Assert.That(selection.Target.Id, Is.EqualTo(targetId));
            Assert.That(selection.WorkspacePath, Is.EqualTo(unixPath));
        }

        [TestCase("windows", "/home/alice/repo")]
        [TestCase("windows", @"\\wsl$\Ubuntu\home\alice\repo")]
        [TestCase("wsl-debian", @"\\wsl$\Ubuntu\home\alice\repo")]
        [TestCase("ssh-build", @"\\wsl$\Ubuntu\home\alice\repo")]
        [TestCase("wsl-ubuntu", @"E:\Projects\site")]
        [TestCase("ssh-build", @"E:\Projects\site")]
        public void ExplicitTargetWithIncompatibleWorkspace_RejectsInsteadOfRedirecting(string targetId, string workspace)
        {
            Assert.Throws<InvalidOperationException>(() => TargetStartupPlanner.Resolve(Catalog("windows"), targetId, workspace));
        }

        [Test]
        public void UnixWorkspaceWithWindowsLastOpened_RejectsWithoutGuessingRemoteTarget()
        {
            Assert.Throws<InvalidOperationException>(() => TargetStartupPlanner.Resolve(Catalog("windows"), null, "/home/alice/repo"));
        }

        [TestCase("wsl-ubuntu")]
        [TestCase("ssh-build")]
        public void UnixWorkspace_RetainsExplicitOrLastOpenedRemoteTarget(string targetId)
        {
            var catalog = Catalog(targetId);
            var explicitSelection = TargetStartupPlanner.Resolve(catalog, targetId, "/home/alice/repo");
            var restoredSelection = TargetStartupPlanner.Resolve(catalog, null, "/home/alice/repo");

            Assert.That(explicitSelection.Target.Id, Is.EqualTo(targetId));
            Assert.That(restoredSelection.Target.Id, Is.EqualTo(targetId));
            Assert.That(restoredSelection.WorkspacePath, Is.EqualTo("/home/alice/repo"));
        }

        [Test]
        public void NoWorkspace_ReopensSavedTargetAndExplicitSelectionWins()
        {
            var catalog = Catalog("ssh-build");
            Assert.That(TargetStartupPlanner.Resolve(catalog, null, null).Target.Id, Is.EqualTo("ssh-build"));
            var explicitSelection = TargetStartupPlanner.Resolve(catalog, "windows", null);
            Assert.That(explicitSelection.Target.Id, Is.EqualTo("windows"));
            Assert.That(explicitSelection.WorkspacePath, Is.Empty);
        }

        [Test]
        public void UnconfiguredWslDistributionAndMissingUncDistribution_Reject()
        {
            Assert.Throws<InvalidOperationException>(() => TargetStartupPlanner.Resolve(Catalog("windows"), null, @"\\wsl$\Fedora\home\alice"));
            Assert.Throws<InvalidOperationException>(() => TargetStartupPlanner.Resolve(Catalog("windows"), null, @"\\wsl$\\home\alice"));
        }

        [Test]
        public void DeletedTarget_RejectsWithoutOpeningWindows()
        {
            Assert.Throws<InvalidOperationException>(() => TargetStartupPlanner.Resolve(Catalog("deleted-target"), null, null));
            Assert.Throws<InvalidOperationException>(() => TargetStartupPlanner.Resolve(Catalog("windows"), "deleted-target", @"E:\Projects\site"));
        }

        [TestCase("../windows")]
        [TestCase("windows/other")]
        [TestCase(@"windows\other")]
        [TestCase("CON")]
        public void UnsafeTargetId_Rejects(string targetId)
        {
            Assert.Throws<InvalidOperationException>(() => TargetStartupPlanner.Resolve(Catalog("windows"), targetId, null));
        }

        [TestCase("/home/alice\0repo")]
        [TestCase("/home/alice\rrepo")]
        [TestCase("/home/alice\nrepo")]
        public void InvalidWorkspaceCharacters_Reject(string workspace)
        {
            Assert.Throws<InvalidOperationException>(() => TargetStartupPlanner.Resolve(Catalog("ssh-build"), "ssh-build", workspace));
        }

        private static TargetCatalogSnapshot Catalog(string lastOpened)
        {
            return new TargetCatalogSnapshot
            {
                LastOpenedTargetId = lastOpened,
                Targets = new List<TargetProfileSnapshot>
                {
                    new TargetProfileSnapshot { Id = "windows", Kind = "Windows", Name = "Windows", RuntimeScopeId = "windows" },
                    new TargetProfileSnapshot { Id = "wsl-ubuntu", Kind = "Wsl", Name = "Ubuntu", WslDistributionName = "Ubuntu", RuntimeScopeId = "wsl|ubuntu" },
                    new TargetProfileSnapshot { Id = "wsl-debian", Kind = "Wsl", Name = "Debian", WslDistributionName = "Debian", RuntimeScopeId = "wsl|debian" },
                    new TargetProfileSnapshot { Id = "ssh-build", Kind = "Ssh", Name = "Build", RuntimeScopeId = "remote-unix|build", SshProfileId = "build" }
                }
            };
        }
    }
}
