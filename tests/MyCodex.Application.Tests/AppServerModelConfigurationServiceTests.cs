using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerModelConfigurationServiceTests
    {
        [Test]
        public async Task Refresh_ReReadsExternalConfigurationAndInvalidatesCatalogOnVersionChange()
        {
            var client = new ConfigClient();
            var service = new AppServerModelConfigurationService(client);
            var first = await service.RefreshAsync("windows", null, false);
            Assert.That(first.GlobalConfig.ModelId, Is.EqualTo("model-1"));
            await service.RefreshAsync("windows", null, false);
            Assert.That(client.ModelReads, Is.EqualTo(1));
            client.Version = "2";
            var changed = await service.RefreshAsync("windows", null, false);
            Assert.That(changed.GlobalConfig.ModelId, Is.EqualTo("model-2"));
            Assert.That(changed.GlobalConfig.ModelReasoningEffort, Is.EqualTo("high"));
            Assert.That(client.ModelReads, Is.EqualTo(2));
            Assert.That(client.Writes, Is.Zero);
        }

        [TestCase("windows", "C:\\project")]
        [TestCase("wsl|ubuntu", "/home/user/project")]
        [TestCase("remote-unix|mac", "/Users/user/project")]
        [TestCase("remote-unix|linux", "/home/user/project")]
        public async Task Refresh_UsesTargetAndWorkspaceScope(string scope, string path)
        {
            var client = new ConfigClient();
            var service = new AppServerModelConfigurationService(client);
            var observed = await service.RefreshAsync(scope, path, false);
            Assert.That(observed.WorkspaceConfig.ModelId, Is.EqualTo(path));
            Assert.That(service.GetObserved(scope, null).WorkspaceConfig.ModelId, Is.EqualTo("model-1"));
            Assert.That(service.GetObserved("other", path), Is.Null);
            Assert.That(service.GetObserved(scope, path.ToUpperInvariant()), Is.Null);
            Assert.That(client.Paths, Is.EqualTo(new[] { null, path }));
        }

        [Test]
        public async Task ConcurrentRefreshesForSameScopeShareOneRead()
        {
            var client = new ConfigClient();
            var service = new AppServerModelConfigurationService(client);
            client.ReadGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = service.RefreshAsync("windows", null, false);
            var second = service.RefreshAsync("windows", null, false);
            Assert.That(second, Is.SameAs(first));
            client.ReadGate.SetResult(true);
            await Task.WhenAll(first, second);
            Assert.That(client.Paths, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task InvalidationDiscardsOldInFlightObservation()
        {
            var client = new ConfigClient();
            var service = new AppServerModelConfigurationService(client);
            client.ReadGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var pending = service.RefreshAsync("old", null, false);
            await client.ReadStarted.Task;
            service.Invalidate();
            client.ReadGate.SetResult(true);
            Assert.ThrowsAsync<OperationCanceledException>(async () => await pending);
            Assert.That(service.GetObserved("old", null), Is.Null);
            Assert.That(await service.RefreshAsync("new", null, false), Is.Not.Null);
        }

        [Test]
        public async Task FailedRefreshDoesNotReturnStaleSettings()
        {
            var client = new ConfigClient();
            var service = new AppServerModelConfigurationService(client);
            await service.RefreshAsync("windows", null, false);
            client.FailReads = true;
            Assert.ThrowsAsync<InvalidOperationException>(async () => await service.RefreshAsync("windows", null, false));
            Assert.That(service.GetObserved("windows", null), Is.Null);
        }

        [Test]
        public async Task Write_UsesObservedUserVersionAndOnlyModelKeys()
        {
            var client = new ConfigClient();
            var service = new AppServerModelConfigurationService(client);
            var observed = await service.RefreshAsync("windows", null, false);
            await service.WriteAsync(observed.GlobalConfig, "chosen", null);
            Assert.That(client.ExpectedVersion, Is.EqualTo("1"));
            Assert.That(client.Reload, Is.True);
            Assert.That(client.Edits.Select(edit => edit.KeyPath), Is.EqualTo(new[] { "model", "model_reasoning_effort" }));
            Assert.That(client.Edits[0].Value, Is.EqualTo("chosen"));
            Assert.That(client.Edits[1].Value, Is.Null);
            Assert.That(service.GetObserved("windows", null), Is.Null);
        }

        [Test]
        public async Task Write_ConflictingVersionIsNotRetriedWithNewVersion()
        {
            var client = new ConfigClient();
            var service = new AppServerModelConfigurationService(client);
            var observed = await service.RefreshAsync("windows", null, false);
            client.Version = "2";
            Assert.ThrowsAsync<InvalidOperationException>(async () => await service.WriteAsync(observed.GlobalConfig, "chosen", "low"));
            Assert.That(client.ExpectedVersion, Is.EqualTo("1"));
            Assert.That(client.Writes, Is.EqualTo(1));
        }

        private sealed class ConfigClient : IAppServerConfigClient
        {
            public string Version = "1";
            public int ModelReads;
            public int Writes;
            public bool FailReads;
            public bool Reload;
            public string ExpectedVersion;
            public List<AppServerConfigWriteEdit> Edits;
            public List<string> Paths = new List<string>();
            public TaskCompletionSource<bool> ReadGate;
            public TaskCompletionSource<bool> ReadStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public async Task<AppServerConfigSnapshot> ReadConfigAsync(string path, bool layers)
            {
                Paths.Add(path);
                ReadStarted.TrySetResult(true);
                if (ReadGate != null) { await ReadGate.Task; }
                if (FailReads) { throw new InvalidOperationException("read failed"); }
                var config = new AppServerConfigSnapshot
                {
                    ModelId = path ?? "model-" + Version,
                    ModelProviderId = "provider",
                    ModelReasoningEffort = "high"
                };
                config.Layers.Add(new AppServerConfigLayer { SourceType = "user", SourceText = "config.toml", Version = Version });
                return config;
            }

            public Task<AppServerConfigSnapshot> ReadConfigAsync(string path, bool layers, CancellationToken token) => ReadConfigAsync(path, layers);
            public Task<List<AppServerModelOption>> ListModelsAsync()
            {
                ModelReads++;
                return Task.FromResult(new List<AppServerModelOption> { new AppServerModelOption { ModelId = "recommended", IsDefault = true } });
            }
            public Task<List<AppServerModelOption>> ListModelsAsync(CancellationToken token) => ListModelsAsync();
            public Task WriteConfigBatchAsync(IReadOnlyCollection<AppServerConfigWriteEdit> edits, string filePath = null,
                string expectedVersion = null, bool reloadUserConfig = false)
            {
                Writes++;
                ExpectedVersion = expectedVersion;
                Reload = reloadUserConfig;
                Edits = edits.ToList();
                if (Version != expectedVersion) { throw new InvalidOperationException("version conflict"); }
                return Task.CompletedTask;
            }
            public Task WriteConfigValueAsync(string keyPath, object value) => throw new NotSupportedException();
            public Task<AppServerModelProviderCapabilities> ReadModelProviderCapabilitiesAsync() => throw new NotSupportedException();
            public Task<AppServerConfigRequirementsSnapshot> ReadConfigRequirementsAsync() => throw new NotSupportedException();
            public Task<AppServerPermissionProfileListResult> ListPermissionProfilesAsync(string workspacePath = null,
                int? limit = null, string cursor = null, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        }
    }
}
