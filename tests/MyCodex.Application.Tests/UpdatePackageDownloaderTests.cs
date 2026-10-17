using System;
using System.IO;
using System.Reflection;
using System.Threading;
using MyCodex.Infrastructure.Updates;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UpdatePackageDownloaderTests
    {
        [Test]
        public void ResolveSafeDestinationPath_AllowsPlainFileName()
        {
            string directory = TestContext.CurrentContext.WorkDirectory;

            string path = ResolveSafeDestinationPath(directory, "MyCodex.zip");

            Assert.That(path, Is.EqualTo(Path.Combine(directory, "MyCodex.zip")));
        }

        [TestCase("..\\evil.zip")]
        [TestCase("../evil.zip")]
        [TestCase("nested\\evil.zip")]
        [TestCase("nested/evil.zip")]
        [TestCase("C:\\temp\\evil.zip")]
        public void ResolveSafeDestinationPath_RejectsPathLikeFileName(string fileName)
        {
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
                delegate { ResolveSafeDestinationPath(TestContext.CurrentContext.WorkDirectory, fileName); });

            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void DownloadVerifiedAsync_RemovesPackageWhenVerificationFails()
        {
            var rootPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "update-download-" + Guid.NewGuid().ToString("N"));
            var sourcePath = Path.Combine(rootPath, "source.bin");
            var destinationDirectory = Path.Combine(rootPath, "destination");
            var destinationPath = Path.Combine(destinationDirectory, "MyCodex.exe");
            Directory.CreateDirectory(rootPath);
            File.WriteAllText(sourcePath, "invalid package");

            try
            {
                var downloader = new UpdatePackageDownloader();
                var asset = new UpdateAssetReference
                {
                    FileName = "MyCodex.exe",
                    Size = new FileInfo(sourcePath).Length,
                    Sha256 = new string('0', 64)
                };

                Assert.That(
                    () => downloader.DownloadVerifiedAsync(
                            new Uri(sourcePath, UriKind.Absolute),
                            asset,
                            destinationDirectory,
                            CancellationToken.None)
                        .GetAwaiter()
                        .GetResult(),
                    Throws.TypeOf<InvalidOperationException>());
                Assert.That(File.Exists(destinationPath), Is.False);
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
        }

        private static string ResolveSafeDestinationPath(string destinationDirectory, string fileName)
        {
            MethodInfo method = typeof(UpdatePackageDownloader).GetMethod(
                "ResolveSafeDestinationPath",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);

            return (string)method.Invoke(null, new object[] { destinationDirectory, fileName });
        }
    }
}
