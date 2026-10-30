using System;
using System.IO;
using System.Threading;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.Updates;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class UpdateManifestClientTests
    {
        // Old clients still read the runtimes entries; this client must ignore them.
        [Test]
        public void Parse_ReadsAppAndIgnoresLegacyRuntimeEntries()
        {
            var manifest = new UpdateManifestClient("https://gitcode.com/demo/mycodex/raw/main/updates/stable.json")
                .Parse(
                    "{" +
                    "\"schemaVersion\":\"1\"," +
                    "\"channel\":\"stable\"," +
                    "\"packageBaseUrl\":\"https://gitcode.com/demo/mycodex/releases/download/v3.1.0/\"," +
                    "\"app\":{\"version\":\"3.1.0\",\"releaseNotes\":{\"zh-CN\":\"中文日志\",\"en-US\":\"English notes\"},\"asset\":{\"url\":\"MyCodex.exe\",\"sha256\":\"sha256:abc\",\"size\":123}}," +
                    "\"runtimes\":[{\"source\":\"Bundled\",\"version\":\"0.125.0\",\"targetTriple\":\"x86_64-pc-windows-msvc\",\"asset\":{\"url\":\"codex.tgz\",\"fileName\":\"codex.tgz\",\"sha256\":\"def\"}}]" +
                    "}");

            Assert.That(manifest.Channel, Is.EqualTo("stable"));
            Assert.That(manifest.App.Version, Is.EqualTo("3.1.0"));
            Assert.That(manifest.App.ReleaseNotesZhCn, Is.EqualTo("中文日志"));
            Assert.That(manifest.App.ReleaseNotesEnUs, Is.EqualTo("English notes"));
            Assert.That(manifest.App.ReleaseNotes, Is.Null);
            Assert.That(manifest.App.Asset.Sha256, Is.EqualTo("abc"));
        }

        [Test]
        public void ResolveAssetUri_AllowsAbsoluteOrPackageBaseRelativeUrls()
        {
            var client = new UpdateManifestClient("https://gitcode.com/demo/mycodex/raw/main/updates/stable.json");
            var manifest = new UpdateManifest
            {
                PackageBaseUrl = "https://cdn.example.com/mycodex/"
            };

            var absolute = client.ResolveAssetUri(
                new UpdateAssetReference { Url = "https://example.com/file.exe" },
                manifest);
            var relative = client.ResolveAssetUri(
                new UpdateAssetReference { Url = "packages/file.exe" },
                manifest);

            Assert.That(absolute.ToString(), Is.EqualTo("https://example.com/file.exe"));
            Assert.That(relative.ToString(), Is.EqualTo("https://cdn.example.com/mycodex/packages/file.exe"));
        }

        [TestCase("http://example.com/file.exe")]
        [TestCase("ftp://example.com/file.exe")]
        [TestCase(@"\\fileserver\share\file.exe")]
        public void ResolveAssetUri_RejectsInsecureAbsoluteUrls(string url)
        {
            var client = new UpdateManifestClient("https://gitcode.com/demo/mycodex/raw/main/updates/stable.json");

            Assert.That(
                () => client.ResolveAssetUri(new UpdateAssetReference { Url = url }, new UpdateManifest()),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void ResolveAssetUri_RejectsRelativeUrlUnderHttpPackageBase()
        {
            var client = new UpdateManifestClient("https://gitcode.com/demo/mycodex/raw/main/updates/stable.json");

            Assert.That(
                () => client.ResolveAssetUri(
                    new UpdateAssetReference { Url = "packages/file.exe" },
                    new UpdateManifest { PackageBaseUrl = "http://cdn.example.com/mycodex/" }),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void LoadAsync_RejectsHttpManifestUrl()
        {
            var client = new UpdateManifestClient("http://example.invalid/stable.json");

            Assert.That(
                () => client.LoadAsync(CancellationToken.None).GetAwaiter().GetResult(),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Parse_AllowsLegacyStringReleaseNotes()
        {
            var manifest = new UpdateManifestClient("https://gitcode.com/demo/mycodex/raw/main/updates/stable.json")
                .Parse("{\"app\":{\"version\":\"3.1.0\",\"releaseNotes\":\"legacy notes\",\"asset\":{\"url\":\"MyCodex.exe\",\"sha256\":\"abc\"}}}");

            Assert.That(manifest.App.ReleaseNotes, Is.EqualTo("legacy notes"));
            Assert.That(manifest.App.ReleaseNotesZhCn, Is.Null);
            Assert.That(manifest.App.ReleaseNotesEnUs, Is.Null);
        }

        [Test]
        public void AppUpdateService_CheckAndDownload_UsesManifestAsset()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var packagePath = Path.Combine(rootPath, "MyCodex_v3.1.0_Setup.exe");
                File.WriteAllText(packagePath, "installer");
                var sha256 = UpdatePackageDownloader.ComputeSha256Hex(packagePath);
                var manifestPath = Path.Combine(rootPath, "stable.json");
                File.WriteAllText(
                    manifestPath,
                    "{" +
                    "\"app\":{\"version\":\"3.1.0\",\"asset\":{\"url\":\"" + new Uri(packagePath).AbsoluteUri + "\",\"fileName\":\"MyCodex_v3.1.0_Setup.exe\",\"sha256\":\"" + sha256 + "\"}}" +
                    "}");

                var service = new AppUpdateService(
                    new AppConfigService(Path.Combine(rootPath, "appdata")),
                    new UpdateSettingsSnapshot { ManifestUrl = new Uri(manifestPath).AbsoluteUri });

                var check = service.CheckAsync("3.0.0", CancellationToken.None).GetAwaiter().GetResult();
                var download = service.DownloadAsync(check, CancellationToken.None).GetAwaiter().GetResult();

                Assert.That(check.IsUpdateAvailable, Is.True);
                Assert.That(File.Exists(download.InstallerPath), Is.True);
                Assert.That(UpdatePackageDownloader.ComputeSha256Hex(download.InstallerPath), Is.EqualTo(sha256));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void VersionComparer_HandlesVPrefixAndPrereleaseSuffix()
        {
            Assert.That(UpdateVersionComparer.Compare("v3.1.0", "3.0.9"), Is.GreaterThan(0));
            Assert.That(UpdateVersionComparer.Compare("3.1.0-beta.1", "3.1.0"), Is.EqualTo(0));
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "MyCodex-UpdateManifestTests-" + Guid.NewGuid().ToString("N"));
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
