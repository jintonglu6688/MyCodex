using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerFileWindowKeyTests
    {
        [Test]
        public void BrowserKey_NormalizesWindowsPathCaseAndSeparators()
        {
            var first = AppServerFileWindowKey.CreateBrowser(
                "Bundled",
                "windows",
                null,
                AppInteractionMode.Coding,
                @"E:\Repo\src");
            var second = AppServerFileWindowKey.CreateBrowser(
                "Bundled",
                "windows",
                null,
                AppInteractionMode.Coding,
                @"e:/repo/src/");

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.ToString(), Does.Contain(@"browser|bundled|windows||coding|e:\repo\src"));
        }

        [Test]
        public void PreviewKey_KeepsUnixPathCaseSensitive()
        {
            var lower = AppServerFileWindowKey.CreatePreview(
                "Wsl",
                "wsl-default",
                null,
                AppInteractionMode.Coding,
                "/home/demo/readme.md");
            var upper = AppServerFileWindowKey.CreatePreview(
                "Wsl",
                "wsl-default",
                null,
                AppInteractionMode.Coding,
                "/home/demo/README.md");

            Assert.That(upper, Is.Not.EqualTo(lower));
        }

        [Test]
        public void SamePathInDifferentRuntimeScopes_DoesNotMatch()
        {
            var wsl = AppServerFileWindowKey.CreateBrowser(
                "Wsl",
                "wsl-profile",
                null,
                AppInteractionMode.Coding,
                "/home/demo/project");
            var remote = AppServerFileWindowKey.CreateBrowser(
                "RemoteUnixSsh",
                "remote-profile",
                "prod",
                AppInteractionMode.Coding,
                "/home/demo/project");

            Assert.That(remote, Is.Not.EqualTo(wsl));
        }

        [Test]
        public void EmptyWindowsBrowserPath_RepresentsDriveRoot()
        {
            var key = AppServerFileWindowKey.CreateBrowser(
                "Bundled",
                "windows",
                null,
                AppInteractionMode.Coding,
                string.Empty);

            Assert.That(key.NormalizedPath, Is.EqualTo(string.Empty));
            Assert.That(key.WindowKind, Is.EqualTo("browser"));
        }

        [TestCase("Bundled", "windows", null)]
        [TestCase("Wsl", "wsl-profile", null)]
        [TestCase("RemoteMacSsh", "remote-profile", "mac")]
        [TestCase("RemoteUnixSsh", "remote-profile", "unix")]
        public void MatchesScope_CoversSupportedRuntimeFamilies(
            string runtimeSource,
            string runtimeProfileId,
            string remoteProfileId)
        {
            var key = AppServerFileWindowKey.CreateBrowser(
                runtimeSource,
                runtimeProfileId,
                remoteProfileId,
                AppInteractionMode.Coding,
                "/repo");

            Assert.That(key.MatchesScope(new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = runtimeSource.ToUpperInvariant(),
                RuntimeProfileId = runtimeProfileId.ToUpperInvariant(),
                RemoteProfileId = remoteProfileId == null ? null : remoteProfileId.ToUpperInvariant(),
                InteractionMode = AppInteractionMode.Coding
            }), Is.True);
            Assert.That(key.MatchesScope(new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = runtimeSource,
                RuntimeProfileId = runtimeProfileId + "-other",
                RemoteProfileId = remoteProfileId,
                InteractionMode = AppInteractionMode.Coding
            }), Is.False);
        }
    }
}
