using System;
using System.IO;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class LocalRuntimeFileSystemOperationsTests
    {
        [Test]
        public void EnsureDirectoryAsync_CreatesNestedDirectoryAndIsIdempotent()
        {
            using (var temp = new TemporaryDirectory())
            {
                var directoryPath = Path.Combine(temp.Path, "office", "docs");
                var service = new LocalRuntimeFileSystemOperations();

                var first = service.EnsureDirectoryAsync(directoryPath).GetAwaiter().GetResult();
                var second = service.EnsureDirectoryAsync(directoryPath).GetAwaiter().GetResult();

                Assert.That(first.Success, Is.True);
                Assert.That(second.Success, Is.True);
                Assert.That(Directory.Exists(directoryPath), Is.True);
            }
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MyCodexTests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; private set; }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
        }
    }
}
