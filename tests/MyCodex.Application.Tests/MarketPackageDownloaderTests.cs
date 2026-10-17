using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Markets;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MarketPackageDownloaderTests
    {
        [Test]
        public void ResolveSafeDestinationPath_RejectsPathTraversal()
        {
            var root = CreateTempDirectory();
            try
            {
                Assert.Throws<InvalidOperationException>(
                    () => MarketPackageDownloader.ResolveSafeDestinationPath(root, "..\\evil.zip", "Test", "package"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void DownloadAsync_DeletesPartialFileWhenHashMismatch()
        {
            var root = CreateTempDirectory();
            var writtenPath = string.Empty;
            try
            {
                var downloader = new MarketPackageDownloader((uri, destinationPath, cancellationToken) =>
                {
                    writtenPath = destinationPath;
                    File.WriteAllText(destinationPath, "payload");
                    return Task.CompletedTask;
                });

                var spec = new MarketPackageDownloadSpec
                {
                    PackageUrl = "https://example.invalid/package.zip",
                    FallbackId = "package",
                    PackageKind = "package",
                    ResourceKeyPrefix = "TestMarketPackageDownloader",
                    Sha256 = new string('0', 64)
                };

                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await downloader.DownloadAsync(spec, root, CancellationToken.None));
                Assert.That(File.Exists(writtenPath), Is.False);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "MyCodex-MarketPackageDownloaderTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
