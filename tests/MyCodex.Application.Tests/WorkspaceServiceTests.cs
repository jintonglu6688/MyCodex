using System;
using System.IO;
using NUnit.Framework;
using MyCodex.Application.Services;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class WorkspaceServiceTests
    {
        [Test]
        public void NormalizeWorkspacePath_RemovesLongPathPrefix()
        {
            var service = new WorkspaceService();
            var path = @"\\?\C:\Temp\Workspace\..\Workspace";

            var normalized = service.NormalizeWorkspacePath(path);

            Assert.That(normalized, Is.EqualTo(Path.GetFullPath(@"C:\Temp\Workspace")));
        }

        [Test]
        public void NormalizeWorkspacePath_PreservesNativeUnixAbsolutePath()
        {
            var service = new WorkspaceService();

            var normalized = service.NormalizeWorkspacePath("/home/tester/../tester/repo/");

            Assert.That(normalized, Is.EqualTo("/home/tester/repo"));
        }

        [Test]
        public void LooksLikeWslMountedWindowsDrivePath_DetectsMountedWindowsDrives()
        {
            Assert.That(WorkspacePathUtilities.LooksLikeWslMountedWindowsDrivePath("/mnt/e/Projects/demo"), Is.True);
            Assert.That(WorkspacePathUtilities.LooksLikeWslMountedWindowsDrivePath("/MNT/c"), Is.True);
            Assert.That(WorkspacePathUtilities.LooksLikeWslMountedWindowsDrivePath("/home/tester/repo"), Is.False);
            Assert.That(WorkspacePathUtilities.LooksLikeWslMountedWindowsDrivePath(@"E:\Projects\demo"), Is.False);
        }

        [Test]
        public void LooksLikeWindowsUncAbsolutePath_DetectsNetworkShares()
        {
            Assert.That(WorkspacePathUtilities.LooksLikeWindowsUncAbsolutePath(@"\\server\share\repo"), Is.True);
            Assert.That(WorkspacePathUtilities.LooksLikeWindowsUncAbsolutePath(@"C:\repo"), Is.False);
            Assert.That(WorkspacePathUtilities.LooksLikeWindowsUncAbsolutePath("/home/tester/repo"), Is.False);
        }

        [Test]
        public void CreateFromPath_UsesInjectedDirectoryAndGitReaders()
        {
            var service = new WorkspaceService();

            var workspace = service.CreateFromPath(
                "/home/tester/repo",
                true,
                path => path == "/home/tester/repo",
                path => new MyCodex.Application.DTOs.WorkspaceGitInfoSnapshot
                {
                    IsGitRepository = true,
                    Branch = "main",
                    Sha = "1234567890abcdef",
                    OriginUrl = "https://example.com/repo.git"
                });

            Assert.That(workspace.RootPath, Is.EqualTo("/home/tester/repo"));
            Assert.That(workspace.Name, Is.EqualTo("repo"));
            Assert.That(workspace.GitBranch, Is.EqualTo("main"));
        }

        [Test]
        public void CreateFromPath_AllowsMissingDirectoryWhenRequireExistingIsFalse()
        {
            var service = new WorkspaceService();
            var workspaceRoot = Path.Combine(Path.GetTempPath(), "MyCodex-Missing-" + Guid.NewGuid().ToString("N"));

            var workspace = service.CreateFromPath(workspaceRoot, false);

            Assert.That(workspace.RootPath, Is.EqualTo(Path.GetFullPath(workspaceRoot)));
            Assert.That(workspace.Name, Is.EqualTo(Path.GetFileName(workspaceRoot)));
            Assert.That(string.IsNullOrWhiteSpace(workspace.Id), Is.False);
        }

        [Test]
        public void CreateFromPath_ThrowsWhenDirectoryIsMissingAndRequireExistingIsTrue()
        {
            var service = new WorkspaceService();
            var workspaceRoot = Path.Combine(Path.GetTempPath(), "MyCodex-Missing-" + Guid.NewGuid().ToString("N"));

            Assert.Throws<DirectoryNotFoundException>(() => service.CreateFromPath(workspaceRoot, true));
        }

        [Test]
        public void ReadGitInfo_ReadsBranchShaAndOriginFromSimpleGitDirectory()
        {
            var service = new WorkspaceService();
            var workspaceRoot = CreateTempDirectory();
            try
            {
                var gitRoot = Path.Combine(workspaceRoot, ".git");
                Directory.CreateDirectory(Path.Combine(gitRoot, "refs", "heads"));
                File.WriteAllText(Path.Combine(gitRoot, "HEAD"), "ref: refs/heads/main");
                File.WriteAllText(Path.Combine(gitRoot, "refs", "heads", "main"), "1234567890abcdef");
                File.WriteAllText(
                    Path.Combine(gitRoot, "config"),
                    "[remote \"origin\"]" + Environment.NewLine +
                    "    url = https://example.com/demo.git" + Environment.NewLine);

                var snapshot = service.ReadGitInfo(workspaceRoot);

                Assert.That(snapshot.IsGitRepository, Is.True);
                Assert.That(snapshot.Branch, Is.EqualTo("main"));
                Assert.That(snapshot.Sha, Is.EqualTo("1234567890abcdef"));
                Assert.That(snapshot.OriginUrl, Is.EqualTo("https://example.com/demo.git"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadGitInfo_ResolvesGitPointerFileWorkspace()
        {
            var service = new WorkspaceService();
            var workspaceRoot = CreateTempDirectory();
            try
            {
                var gitRoot = Path.Combine(workspaceRoot, ".git-data");
                Directory.CreateDirectory(Path.Combine(gitRoot, "refs", "heads"));
                File.WriteAllText(Path.Combine(workspaceRoot, ".git"), "gitdir: .git-data");
                File.WriteAllText(Path.Combine(gitRoot, "HEAD"), "ref: refs/heads/dev");
                File.WriteAllText(Path.Combine(gitRoot, "refs", "heads", "dev"), "fedcba9876543210");
                File.WriteAllText(
                    Path.Combine(gitRoot, "config"),
                    "[remote \"origin\"]" + Environment.NewLine +
                    "    url = https://example.com/worktree.git" + Environment.NewLine);

                var snapshot = service.ReadGitInfo(workspaceRoot);

                Assert.That(snapshot.IsGitRepository, Is.True);
                Assert.That(snapshot.Branch, Is.EqualTo("dev"));
                Assert.That(snapshot.Sha, Is.EqualTo("fedcba9876543210"));
                Assert.That(snapshot.OriginUrl, Is.EqualTo("https://example.com/worktree.git"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadGitInfo_HandlesDetachedHead()
        {
            var service = new WorkspaceService();
            var workspaceRoot = CreateTempDirectory();
            try
            {
                var gitRoot = Path.Combine(workspaceRoot, ".git");
                Directory.CreateDirectory(gitRoot);
                File.WriteAllText(Path.Combine(gitRoot, "HEAD"), "abcdef1234567890");

                var snapshot = service.ReadGitInfo(workspaceRoot);

                Assert.That(snapshot.IsGitRepository, Is.True);
                Assert.That(snapshot.Sha, Is.EqualTo("abcdef1234567890"));
                Assert.That(snapshot.Branch, Is.EqualTo("detached@abcdef12"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        [Test]
        public void ReadGitInfo_ResolvesShaFromPackedRefsInCommonDirectory()
        {
            var service = new WorkspaceService();
            var workspaceRoot = CreateTempDirectory();
            try
            {
                var gitRoot = Path.Combine(workspaceRoot, ".git");
                var commonRoot = Path.Combine(workspaceRoot, ".git-common");
                Directory.CreateDirectory(gitRoot);
                Directory.CreateDirectory(commonRoot);
                File.WriteAllText(Path.Combine(gitRoot, "commondir"), "..\\.git-common");
                File.WriteAllText(Path.Combine(gitRoot, "HEAD"), "ref: refs/heads/release");
                File.WriteAllText(
                    Path.Combine(commonRoot, "packed-refs"),
                    "# pack-refs with: peeled fully-peeled sorted" + Environment.NewLine +
                    "1111111111111111111111111111111111111111 refs/heads/release" + Environment.NewLine);
                File.WriteAllText(
                    Path.Combine(commonRoot, "config"),
                    "[remote \"origin\"]" + Environment.NewLine +
                    "    url = https://example.com/common.git" + Environment.NewLine);

                var snapshot = service.ReadGitInfo(workspaceRoot);

                Assert.That(snapshot.IsGitRepository, Is.True);
                Assert.That(snapshot.Branch, Is.EqualTo("release"));
                Assert.That(snapshot.Sha, Is.EqualTo("1111111111111111111111111111111111111111"));
                Assert.That(snapshot.OriginUrl, Is.EqualTo("https://example.com/common.git"));
            }
            finally
            {
                DeleteDirectory(workspaceRoot);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "MyCodex-Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
