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
        public void CreateDirectoryAsync_CreatesChildDirectory()
        {
            using (var temp = new TemporaryDirectory())
            {
                var service = new LocalRuntimeFileSystemOperations();

                var result = service.CreateDirectoryAsync(temp.Path, "docs").GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(Directory.Exists(Path.Combine(temp.Path, "docs")), Is.True);
            }
        }

        [Test]
        public void CreateDirectoryAsync_DoesNotTreatExistingDirectoryAsCreated()
        {
            using (var temp = new TemporaryDirectory())
            {
                var directoryPath = Path.Combine(temp.Path, "docs");
                Directory.CreateDirectory(directoryPath);
                var service = new LocalRuntimeFileSystemOperations();

                var result = service.CreateDirectoryAsync(temp.Path, "docs").GetAwaiter().GetResult();

                Assert.That(result.Success, Is.False);
                Assert.That(Directory.Exists(directoryPath), Is.True);
            }
        }

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

        [Test]
        public void CreateFileAsync_CreatesEmptyFileInParentDirectory()
        {
            using (var temp = new TemporaryDirectory())
            {
                var service = new LocalRuntimeFileSystemOperations();

                var result = service.CreateFileAsync(temp.Path, "notes.md").GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(File.Exists(Path.Combine(temp.Path, "notes.md")), Is.True);
                Assert.That(new FileInfo(Path.Combine(temp.Path, "notes.md")).Length, Is.EqualTo(0));
            }
        }

        [Test]
        public void CreateFileAsync_DoesNotOverwriteExistingFile()
        {
            using (var temp = new TemporaryDirectory())
            {
                var filePath = Path.Combine(temp.Path, "notes.md");
                File.WriteAllText(filePath, "existing");
                var service = new LocalRuntimeFileSystemOperations();

                var result = service.CreateFileAsync(temp.Path, "notes.md").GetAwaiter().GetResult();

                Assert.That(result.Success, Is.False);
                Assert.That(File.ReadAllText(filePath), Is.EqualTo("existing"));
            }
        }

        [Test]
        public void RenameAsync_RenamesFileWithinSameParent()
        {
            using (var temp = new TemporaryDirectory())
            {
                File.WriteAllText(Path.Combine(temp.Path, "old.txt"), "hello");
                var service = new LocalRuntimeFileSystemOperations();

                var result = service.RenameAsync(Path.Combine(temp.Path, "old.txt"), "new.txt").GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(File.Exists(Path.Combine(temp.Path, "old.txt")), Is.False);
                Assert.That(File.Exists(Path.Combine(temp.Path, "new.txt")), Is.True);
            }
        }

        [Test]
        public void RenameAsync_RenamesDirectoryWithinSameParent()
        {
            using (var temp = new TemporaryDirectory())
            {
                Directory.CreateDirectory(Path.Combine(temp.Path, "old"));
                var service = new LocalRuntimeFileSystemOperations();

                var result = service.RenameAsync(Path.Combine(temp.Path, "old"), "new").GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(Directory.Exists(Path.Combine(temp.Path, "old")), Is.False);
                Assert.That(Directory.Exists(Path.Combine(temp.Path, "new")), Is.True);
            }
        }

        [Test]
        public void DeleteAsync_DeletesFile()
        {
            using (var temp = new TemporaryDirectory())
            {
                var filePath = Path.Combine(temp.Path, "notes.md");
                File.WriteAllText(filePath, "hello");
                var service = new LocalRuntimeFileSystemOperations();

                var result = service.DeleteAsync(filePath, true).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(File.Exists(filePath), Is.False);
            }
        }

        [Test]
        public void DeleteAsync_DeletesDirectoryRecursively()
        {
            using (var temp = new TemporaryDirectory())
            {
                var directoryPath = Path.Combine(temp.Path, "docs");
                Directory.CreateDirectory(directoryPath);
                File.WriteAllText(Path.Combine(directoryPath, "notes.md"), "hello");
                var service = new LocalRuntimeFileSystemOperations();

                var result = service.DeleteAsync(directoryPath, true).GetAwaiter().GetResult();

                Assert.That(result.Success, Is.True);
                Assert.That(Directory.Exists(directoryPath), Is.False);
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
