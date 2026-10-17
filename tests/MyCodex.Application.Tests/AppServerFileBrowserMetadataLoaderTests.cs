using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerFileBrowserMetadataLoaderTests
    {
        [Test]
        public async Task LoadAsync_LimitsEachBatchToEightAndReportsProgress()
        {
            var client = new FakeFileSystemClient();
            var loader = new AppServerFileBrowserMetadataLoader(client);
            var batches = new List<IReadOnlyDictionary<string, string>>();
            var paths = Enumerable.Range(1, 17).Select(index => "/repo/file" + index).ToList();

            await loader.LoadAsync(paths, batch => batches.Add(batch));

            CollectionAssert.AreEqual(new[] { 8, 8, 1 }, batches.Select(batch => batch.Count));
            Assert.That(client.MetadataRequests, Has.Count.EqualTo(17));
        }

        [Test]
        public async Task LoadAsync_ReusesCacheUntilPathIsInvalidatedAndPrunesDeletedPaths()
        {
            var client = new FakeFileSystemClient();
            var loader = new AppServerFileBrowserMetadataLoader(client);
            var paths = new[] { "/repo/a", "/repo/b" };

            await loader.LoadAsync(paths, batch => { });
            await loader.LoadAsync(paths, batch => { });
            loader.Invalidate(new[] { "/repo/a" });
            await loader.LoadAsync(paths, batch => { });
            loader.Prune(new[] { "/repo/a" });
            await loader.LoadAsync(paths, batch => { });

            CollectionAssert.AreEqual(
                new[] { "/repo/a", "/repo/b", "/repo/a", "/repo/b" },
                client.MetadataRequests);
        }

        [Test]
        public async Task LoadAsync_IsolatesSingleItemFailure()
        {
            var client = new FakeFileSystemClient { FailingPath = "/repo/b" };
            var loader = new AppServerFileBrowserMetadataLoader(client);
            var values = new Dictionary<string, string>();

            await loader.LoadAsync(
                new[] { "/repo/a", "/repo/b", "/repo/c" },
                batch =>
                {
                    foreach (var pair in batch)
                    {
                        values[pair.Key] = pair.Value;
                    }
                });

            Assert.That(values["/repo/a"], Is.Not.EqualTo(AppServerFileBrowserMetadataLoader.UnavailableValue));
            Assert.That(values["/repo/b"], Is.EqualTo(AppServerFileBrowserMetadataLoader.UnavailableValue));
            Assert.That(values["/repo/c"], Is.Not.EqualTo(AppServerFileBrowserMetadataLoader.UnavailableValue));
        }

        [Test]
        public void FormatModifiedTime_UsesLocalTimeWithoutSeconds()
        {
            var unixMilliseconds = new DateTimeOffset(2026, 7, 16, 8, 9, 10, TimeSpan.Zero).ToUnixTimeMilliseconds();
            var expected = DateTimeOffset
                .FromUnixTimeMilliseconds(unixMilliseconds)
                .LocalDateTime
                .ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var actual = AppServerFileBrowserMetadataLoader.FormatModifiedTime(unixMilliseconds);

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(actual, Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$"));
            Assert.That(
                AppServerFileBrowserMetadataLoader.FormatModifiedTime(0),
                Is.EqualTo(AppServerFileBrowserMetadataLoader.UnavailableValue));
        }

        private sealed class FakeFileSystemClient : IAppServerFileSystemClient
        {
            public readonly List<string> MetadataRequests = new List<string>();

            public string FailingPath { get; set; }

            public Task<AppServerFileSystemMetadata> GetMetadataAsync(string path)
            {
                MetadataRequests.Add(path);
                if (string.Equals(path, FailingPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("metadata failed");
                }

                return Task.FromResult(new AppServerFileSystemMetadata { ModifiedAtMs = 1 });
            }

            public Task<List<AppServerFileSystemEntry>> ReadDirectoryAsync(string path) { throw new NotSupportedException(); }
            public Task<AppServerFileSystemReadResult> ReadFileAsync(string path) { throw new NotSupportedException(); }
            public Task WriteFileAsync(string path, byte[] data) { throw new NotSupportedException(); }
            public Task CreateDirectoryAsync(string path, bool? recursive = null) { throw new NotSupportedException(); }
            public Task RemoveAsync(string path, bool? recursive = null, bool? force = null) { throw new NotSupportedException(); }
            public Task CopyAsync(string sourcePath, string destinationPath, bool recursive) { throw new NotSupportedException(); }
            public Task<AppServerFileSystemWatchResult> WatchFileSystemAsync(string path, string watchId) { throw new NotSupportedException(); }
            public Task UnwatchFileSystemAsync(string watchId) { throw new NotSupportedException(); }
        }
    }
}
