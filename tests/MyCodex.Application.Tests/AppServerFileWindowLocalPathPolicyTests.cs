using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerFileWindowLocalPathPolicyTests
    {
        [TestCase(CodexRuntimeSources.Bundled)]
        [TestCase(CodexRuntimeSources.System)]
        [TestCase(CodexRuntimeSources.Custom)]
        [TestCase(null)]
        public void IsLocalWindowsFile_ReturnsTrue_ForWindowsPathsInLocalRuntime(string runtimeSource)
        {
            var scope = new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = runtimeSource,
                InteractionMode = AppInteractionMode.Coding
            };

            Assert.That(AppServerFileWindowLocalPathPolicy.IsLocalWindowsFile(scope, @"E:\Repo\a.txt"), Is.True);
            Assert.That(AppServerFileWindowLocalPathPolicy.IsLocalWindowsFile(scope, @"\\server\share\a.txt"), Is.True);
        }

        [TestCase(CodexRuntimeSources.RemoteMacSsh)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh)]
        public void IsLocalWindowsFile_ReturnsFalse_ForRemoteRuntimeEvenWhenPathLooksWindows(string runtimeSource)
        {
            var scope = new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = runtimeSource,
                InteractionMode = AppInteractionMode.Coding
            };

            Assert.That(AppServerFileWindowLocalPathPolicy.IsLocalWindowsFile(scope, @"E:\Repo\a.txt"), Is.False);
        }

        [Test]
        public void IsLocalWindowsFile_ReturnsTrue_ForWindowsMountedWslPath()
        {
            var scope = new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = CodexRuntimeSources.Wsl,
                InteractionMode = AppInteractionMode.Coding
            };

            Assert.That(AppServerFileWindowLocalPathPolicy.IsLocalWindowsFile(scope, @"E:\Repo\a.txt"), Is.True);
            Assert.That(AppServerFileWindowLocalPathPolicy.IsLocalWindowsFile(scope, "/mnt/e/Repo/a.txt"), Is.True);
        }

        [Test]
        public void TryGetLocalWindowsFilePath_MapsWslMountedDrivePath()
        {
            var scope = new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = CodexRuntimeSources.Wsl,
                InteractionMode = AppInteractionMode.Coding
            };

            var mapped = AppServerFileWindowLocalPathPolicy.TryGetLocalWindowsFilePath(
                scope,
                "/mnt/e/Repo/a.txt",
                out var localPath);

            Assert.That(mapped, Is.True);
            Assert.That(localPath, Is.EqualTo(@"E:\Repo\a.txt"));
        }

        [Test]
        public void IsLocalWindowsFile_ReturnsFalse_ForUnixPathInLocalRuntime()
        {
            var scope = new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = CodexRuntimeSources.Bundled,
                InteractionMode = AppInteractionMode.Coding
            };

            Assert.That(AppServerFileWindowLocalPathPolicy.IsLocalWindowsFile(scope, "/home/user/a.txt"), Is.False);
        }

        [Test]
        public void IsLocalWindowsFile_ReturnsFalse_ForWslHomePath()
        {
            var scope = new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = CodexRuntimeSources.Wsl,
                InteractionMode = AppInteractionMode.Coding
            };

            Assert.That(AppServerFileWindowLocalPathPolicy.IsLocalWindowsFile(scope, "/home/user/a.txt"), Is.False);
        }
    }
}
