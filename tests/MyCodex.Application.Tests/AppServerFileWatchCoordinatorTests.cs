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
    public sealed class AppServerFileWatchCoordinatorTests
    {
        [Test]
        public async Task WatchAsync_RegistersWatchAndDispatchesChangeByWatchId()
        {
            var client = new FakeFileSystemClient();
            var target = new FakeWatchTarget(@"E:\Repo");
            var coordinator = new AppServerFileWatchCoordinator(client);

            var watchId = await coordinator.WatchAsync(target);
            await coordinator.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent
            {
                WatchId = watchId,
                ChangedPaths = new List<string> { @"E:\Repo\AGENTS.md" }
            });

            Assert.That(client.WatchedPaths.Single(), Is.EqualTo(@"E:\Repo"));
            Assert.That(target.ChangeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task UnwatchAsync_RemovesRegistrationAndCallsClient()
        {
            var client = new FakeFileSystemClient();
            var target = new FakeWatchTarget("/home/demo");
            var coordinator = new AppServerFileWatchCoordinator(client);

            var watchId = await coordinator.WatchAsync(target);
            await coordinator.UnwatchAsync(watchId);
            await coordinator.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent
            {
                WatchId = watchId
            });

            Assert.That(client.UnwatchedIds.Single(), Is.EqualTo(watchId));
            Assert.That(target.ChangeCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ClearAsync_UnwatchesAllRegisteredTargets()
        {
            var client = new FakeFileSystemClient();
            var coordinator = new AppServerFileWatchCoordinator(client);

            await coordinator.WatchAsync(new FakeWatchTarget("/one"));
            await coordinator.WatchAsync(new FakeWatchTarget("/two"));
            await coordinator.ClearAsync();

            Assert.That(client.UnwatchedIds.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ReplaceWatchAsync_UnwatchesOnlyMatchingTarget()
        {
            var client = new FakeFileSystemClient();
            var coordinator = new AppServerFileWatchCoordinator(client);
            var browser = new FakeWatchTarget("/repo");
            var preview = new FakeWatchTarget("/repo/readme.md");

            var browserWatchId = await coordinator.WatchAsync(browser);
            var previewWatchId = await coordinator.WatchAsync(preview);
            browser.WatchPath = "/repo/src";
            var replacementWatchId = await coordinator.ReplaceWatchAsync(browser);
            await coordinator.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent
            {
                WatchId = previewWatchId
            });

            Assert.That(client.UnwatchedIds, Is.EqualTo(new[] { browserWatchId }));
            Assert.That(client.WatchedPaths, Is.EqualTo(new[] { "/repo", "/repo/readme.md", "/repo/src" }));
            Assert.That(replacementWatchId, Is.Not.Null);
            Assert.That(preview.ChangeCount, Is.EqualTo(1));
        }

        private sealed class FakeWatchTarget : IAppServerFileWatchTarget
        {
            public FakeWatchTarget(string watchPath)
            {
                WatchPath = watchPath;
            }

            public string WatchPath { get; set; }

            public int ChangeCount { get; private set; }

            public string WatchUnavailableMessage { get; private set; }

            public Task HandleFileSystemChangedAsync(AppServerFileSystemChangedEvent change)
            {
                ChangeCount++;
                return Task.CompletedTask;
            }

            public void HandleWatchUnavailable(string message)
            {
                WatchUnavailableMessage = message;
            }
        }

        private sealed class FakeFileSystemClient : IAppServerFileSystemClient
        {
            public readonly List<string> WatchedPaths = new List<string>();
            public readonly List<string> UnwatchedIds = new List<string>();

            public Task<AppServerFileSystemWatchResult> WatchFileSystemAsync(string path, string watchId)
            {
                WatchedPaths.Add(path);
                return Task.FromResult(new AppServerFileSystemWatchResult { Path = path });
            }

            public Task UnwatchFileSystemAsync(string watchId)
            {
                UnwatchedIds.Add(watchId);
                return Task.CompletedTask;
            }

            public Task<List<AppServerFileSystemEntry>> ReadDirectoryAsync(string path)
            {
                return Task.FromResult(new List<AppServerFileSystemEntry>());
            }

            public Task<AppServerFileSystemReadResult> ReadFileAsync(string path)
            {
                return Task.FromResult(new AppServerFileSystemReadResult());
            }

            public Task<AppServerFileSystemMetadata> GetMetadataAsync(string path)
            {
                return Task.FromResult(new AppServerFileSystemMetadata());
            }

            public Task WriteFileAsync(string path, byte[] data)
            {
                return Task.CompletedTask;
            }

            public Task CreateDirectoryAsync(string path, bool? recursive = null)
            {
                return Task.CompletedTask;
            }

            public Task RemoveAsync(string path, bool? recursive = null, bool? force = null)
            {
                return Task.CompletedTask;
            }

            public Task CopyAsync(string sourcePath, string destinationPath, bool recursive)
            {
                return Task.CompletedTask;
            }
        }
    }
}
