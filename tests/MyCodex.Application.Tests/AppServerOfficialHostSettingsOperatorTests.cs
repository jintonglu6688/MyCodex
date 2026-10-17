using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerOfficialHostSettingsOperatorTests
    {
        [Test]
        public async Task GetAsync_AddsUserSettingsWithoutWritingOrReadingCredentials()
        {
            var client = new ConfigClient();
            client.Snapshot.ModelVerbosity = "low";
            client.Snapshot.Origins["model_verbosity"] = new AppServerConfigOrigin { SourceType = "user" };
            client.Snapshot.ServiceTier = "priority";
            client.Snapshot.Origins["service_tier"] = new AppServerConfigOrigin { SourceType = "mdm" };
            var core = new CoreSettingsOperator();

            var result = await new AppServerOfficialHostSettingsOperator(core, client).GetAsync(CancellationToken.None);

            Assert.That(result.Scope.RuntimeDisplayName, Is.EqualTo("Current target"));
            Assert.That(result.Official.ModelVerbosity, Is.EqualTo("low"));
            Assert.That(result.Official.ServiceTier, Is.Null);
            Assert.That(client.Reads, Is.EqualTo(1));
            Assert.That(client.Writes, Is.Zero);
            Assert.That(core.Updates, Is.Zero);
        }

        [Test]
        public async Task UpdateAsync_WritesFreshUserVersionAndReloadsThenReturnsActualSettings()
        {
            var client = new ConfigClient();
            var core = new CoreSettingsOperator();
            var result = await new AppServerOfficialHostSettingsOperator(core, client).UpdateAsync(
                OfficialRequest("medium"), CancellationToken.None);

            Assert.That(result.Updated, Is.True);
            Assert.That(result.Settings.Official.ModelVerbosity, Is.EqualTo("medium"));
            Assert.That(client.Edits.Select(edit => edit.KeyPath), Is.EqualTo(new[] { "model_verbosity" }));
            Assert.That(client.ExpectedVersion, Is.EqualTo("user-version"));
            Assert.That(client.Reload, Is.True);
            Assert.That(core.Updates, Is.Zero);
        }

        [Test]
        public async Task UpdateAsync_NoOpDoesNotWrite()
        {
            var client = new ConfigClient();
            var result = await new AppServerOfficialHostSettingsOperator(new CoreSettingsOperator(), client)
                .UpdateAsync(OfficialRequest(null), CancellationToken.None);

            Assert.That(result.Updated, Is.False);
            Assert.That(client.Writes, Is.Zero);
        }

        [Test]
        public void UpdateAsync_MissingVersionCannotWrite()
        {
            var client = new ConfigClient();
            client.Snapshot.Layers.Clear();
            var settings = new AppServerOfficialHostSettingsOperator(new CoreSettingsOperator(), client);

            var error = Assert.ThrowsAsync<CodingRemoteOperationException>(
                async () => await settings.UpdateAsync(OfficialRequest("medium"), CancellationToken.None));

            Assert.That(error.ErrorCode, Is.EqualTo(RemoteErrorCodes.OperationFailed));
            Assert.That(client.Writes, Is.Zero);
        }

        [Test]
        public void UpdateAsync_InvalidChoiceCannotWrite()
        {
            var client = new ConfigClient();
            var settings = new AppServerOfficialHostSettingsOperator(new CoreSettingsOperator(), client);

            var error = Assert.ThrowsAsync<CodingRemoteOperationException>(
                async () => await settings.UpdateAsync(OfficialRequest("invalid"), CancellationToken.None));

            Assert.That(error.ErrorCode, Is.EqualTo(RemoteErrorCodes.InvalidCommand));
            Assert.That(client.Writes, Is.Zero);
        }

        [Test]
        public void UpdateAsync_ConflictingWriteDoesNotReturnSuccessOrRetry()
        {
            var client = new ConfigClient { Conflict = true };
            var settings = new AppServerOfficialHostSettingsOperator(new CoreSettingsOperator(), client);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await settings.UpdateAsync(OfficialRequest("medium"), CancellationToken.None));

            Assert.That(client.Writes, Is.EqualTo(1));
            Assert.That(client.Snapshot.ModelVerbosity, Is.Null);
        }

        [Test]
        public void UpdateAsync_MixedSectionsAreRejectedBeforeAnyChanges()
        {
            var client = new ConfigClient();
            var core = new CoreSettingsOperator();
            var settings = new AppServerOfficialHostSettingsOperator(core, client);
            var request = OfficialRequest("medium");
            request.Defaults = new HostSettingsDefaults { ModelId = "new-model" };

            var error = Assert.ThrowsAsync<CodingRemoteOperationException>(
                async () => await settings.UpdateAsync(request, CancellationToken.None));

            Assert.That(error.ErrorCode, Is.EqualTo(RemoteErrorCodes.InvalidCommand));
            Assert.That(core.Updates, Is.Zero);
            Assert.That(client.Reads, Is.Zero);
            Assert.That(client.Writes, Is.Zero);
        }

        [Test]
        public async Task UpdateAsync_DefaultsStillUseExistingOperatorAndIncludeAdvancedSettings()
        {
            var client = new ConfigClient();
            var core = new CoreSettingsOperator();
            var request = new HostSettingsUpdateRequest { Defaults = new HostSettingsDefaults { ModelId = "new-model" } };
            var result = await new AppServerOfficialHostSettingsOperator(core, client).UpdateAsync(request, CancellationToken.None);

            Assert.That(core.LastRequest, Is.SameAs(request));
            Assert.That(result.Updated, Is.True);
            Assert.That(result.Settings.Official, Is.Not.Null);
            Assert.That(client.Writes, Is.Zero);
            Assert.That(client.Reads, Is.EqualTo(1));
        }

        private static HostSettingsUpdateRequest OfficialRequest(string verbosity)
        {
            return new HostSettingsUpdateRequest { Official = new OfficialCodexSettingsSelection { ModelVerbosity = verbosity } };
        }

        private sealed class CoreSettingsOperator : IHostSettingsOperator
        {
            public int Updates;
            public HostSettingsUpdateRequest LastRequest;
            public Task<HostSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(new HostSettingsSnapshot
                {
                    Scope = new HostSettingsScope { RuntimeDisplayName = "Current target" },
                    Defaults = new HostSettingsDefaults { ModelId = "model", ApprovalMode = "auto" }
                });
            }
            public async Task<HostSettingsUpdateResult> UpdateAsync(HostSettingsUpdateRequest request, CancellationToken cancellationToken)
            {
                Updates++;
                LastRequest = request;
                return new HostSettingsUpdateResult { Updated = true, Settings = await GetAsync(cancellationToken) };
            }
        }

        private sealed class ConfigClient : IAppServerConfigClient
        {
            public readonly AppServerConfigSnapshot Snapshot = new AppServerConfigSnapshot();
            public int Reads;
            public int Writes;
            public bool Reload;
            public bool Conflict;
            public string ExpectedVersion;
            public IReadOnlyCollection<AppServerConfigWriteEdit> Edits;
            public ConfigClient()
            {
                Snapshot.Layers.Add(new AppServerConfigLayer { SourceType = "user", Version = "user-version" });
            }
            public Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers)
            {
                return ReadConfigAsync(workspacePath, includeLayers, CancellationToken.None);
            }
            public Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.That(workspacePath, Is.Null);
                Assert.That(includeLayers, Is.True);
                Reads++;
                return Task.FromResult(Snapshot);
            }
            public Task WriteConfigBatchAsync(IReadOnlyCollection<AppServerConfigWriteEdit> edits, string filePath = null,
                string expectedVersion = null, bool reloadUserConfig = false)
            {
                Writes++;
                ExpectedVersion = expectedVersion;
                Reload = reloadUserConfig;
                Edits = edits;
                Assert.That(filePath, Is.Null);
                if (Conflict) { throw new InvalidOperationException("version conflict"); }
                Snapshot.ModelVerbosity = (string)edits.Single().Value;
                Snapshot.Origins["model_verbosity"] = new AppServerConfigOrigin { SourceType = "user" };
                return Task.CompletedTask;
            }
            public Task WriteConfigValueAsync(string keyPath, object value) => throw new NotSupportedException();
            public Task<AppServerModelProviderCapabilities> ReadModelProviderCapabilitiesAsync() => throw new NotSupportedException();
            public Task<AppServerConfigRequirementsSnapshot> ReadConfigRequirementsAsync() => throw new NotSupportedException();
            public Task<List<AppServerModelOption>> ListModelsAsync() => throw new NotSupportedException();
            public Task<List<AppServerModelOption>> ListModelsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
            public Task<AppServerPermissionProfileListResult> ListPermissionProfilesAsync(string workspacePath = null,
                int? limit = null, string cursor = null, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        }
    }
}
