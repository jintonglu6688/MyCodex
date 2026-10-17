using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.RemoteHost;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteHostServiceControllerTests
    {
        [Test]
        public async Task StartIfConfiguredAsync_EmbeddedAutoStartStartsRelayBeforeRealtimeChannel()
        {
            var store = new FakeConfigStore(CreateConfiguredSnapshot("Embedded", true));
            var relay = new FakeEmbeddedRelayService { State = EmbeddedRelayRunState.Stopped };
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(store, realtime, relay))
            {
                await controller.StartIfConfiguredAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(relay.StartCount, Is.EqualTo(1));
            Assert.That(realtime.StartCount, Is.EqualTo(1));
        }

        [Test]
        public async Task StartIfConfiguredAsync_EmbeddedAutoStartDisabledDoesNotStartOrConnectStoppedRelay()
        {
            var store = new FakeConfigStore(CreateConfiguredSnapshot("Embedded", false));
            var relay = new FakeEmbeddedRelayService { State = EmbeddedRelayRunState.Stopped };
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(store, realtime, relay))
            {
                await controller.StartIfConfiguredAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(relay.StartCount, Is.EqualTo(0));
            Assert.That(realtime.StartCount, Is.EqualTo(0));
        }

        [Test]
        public async Task StartIfConfiguredAsync_ExternalConnectsWithoutStartingEmbeddedRelay()
        {
            var store = new FakeConfigStore(CreateConfiguredSnapshot("External", false));
            var relay = new FakeEmbeddedRelayService { State = EmbeddedRelayRunState.Stopped };
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(store, realtime, relay))
            {
                await controller.StartIfConfiguredAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(relay.StartCount, Is.EqualTo(0));
            Assert.That(realtime.StartCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RestartIfConfiguredAsync_ExternalStopsPreviouslyRunningEmbeddedRelayBeforeConnecting()
        {
            var store = new FakeConfigStore(CreateConfiguredSnapshot("External", false));
            var relay = new FakeEmbeddedRelayService { State = EmbeddedRelayRunState.Running };
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(store, realtime, relay))
            {
                await controller.RestartIfConfiguredAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(realtime.StopCount, Is.EqualTo(1));
            Assert.That(relay.StopCount, Is.EqualTo(1));
            Assert.That(realtime.StartCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ApplyConfiguredModeAsync_EmbeddedStartsRelayEvenWhenApplicationAutoStartIsDisabled()
        {
            var store = new FakeConfigStore(CreateConfiguredSnapshot("Embedded", false));
            var relay = new FakeEmbeddedRelayService { State = EmbeddedRelayRunState.Stopped };
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(store, realtime, relay))
            {
                await controller.ApplyConfiguredModeAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(realtime.StopCount, Is.EqualTo(1));
            Assert.That(relay.StartCount, Is.EqualTo(1));
            Assert.That(realtime.StartCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ApplyConfiguredModeAsync_EmbeddedAcquiresRunningRelayWithoutStoppingOtherUsers()
        {
            var store = new FakeConfigStore(CreateConfiguredSnapshot("Embedded", false));
            var relay = new FakeEmbeddedRelayService { State = EmbeddedRelayRunState.Running };
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(store, realtime, relay))
            {
                await controller.ApplyConfiguredModeAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(relay.StopCount, Is.EqualTo(0));
            Assert.That(relay.StartCount, Is.EqualTo(1));
            Assert.That(realtime.StartCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ShutdownAsync_StopsRealtimeAndActiveEmbeddedRelay()
        {
            var store = new FakeConfigStore(CreateConfiguredSnapshot("Embedded", true));
            var relay = new FakeEmbeddedRelayService { State = EmbeddedRelayRunState.Running };
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(store, realtime, relay))
            {
                await controller.ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Assert.That(realtime.StopCount, Is.EqualTo(1));
            Assert.That(relay.StopCount, Is.EqualTo(1));
        }

        [Test]
        public async Task StartIfConfiguredAsync_DisabledTargetDoesNotAcquireOrConnect()
        {
            var snapshot = CreateConfiguredSnapshot("Embedded", true);
            snapshot.RemoteEnabled = false;
            var relay = new FakeEmbeddedRelayService { State = EmbeddedRelayRunState.Running };
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(new FakeConfigStore(snapshot), realtime, relay))
            {
                await controller.StartIfConfiguredAsync(CancellationToken.None);
            }

            Assert.That(relay.StartCount, Is.Zero);
            Assert.That(realtime.StartCount, Is.Zero);
        }

        [Test]
        public async Task StartIfConfiguredAsync_RunningRelayStillAcquiresThisTargetsLease()
        {
            var relay = new FakeEmbeddedRelayService { State = EmbeddedRelayRunState.Running };
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(
                new FakeConfigStore(CreateConfiguredSnapshot("Embedded", false)), realtime, relay))
            {
                await controller.StartIfConfiguredAsync(CancellationToken.None);
            }

            Assert.That(relay.StartCount, Is.EqualTo(1));
            Assert.That(relay.StopCount, Is.Zero);
            Assert.That(realtime.StartCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RefreshConfigurationAsync_MetadataChangesDoNotReconnect()
        {
            var snapshot = CreateConfiguredSnapshot("External", false);
            snapshot.RemoteEnabled = true;
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(new FakeConfigStore(snapshot), realtime))
            {
                await controller.StartIfConfiguredAsync(CancellationToken.None);
                snapshot.UpdatedAt = DateTime.UtcNow;
                snapshot.LastRegisteredAt = DateTime.UtcNow;
                snapshot.EmbeddedRelayProcessId = 42;
                await controller.RefreshConfigurationAsync(CancellationToken.None);
                await controller.RefreshConfigurationAsync(CancellationToken.None);
            }

            Assert.That(realtime.StartCount, Is.EqualTo(1));
            Assert.That(realtime.StopCount, Is.Zero);
        }

        [Test]
        public async Task RefreshConfigurationAsync_LegacyNullableEnabledDoesNotReconnectUnchangedSettings()
        {
            var snapshot = CreateConfiguredSnapshot("External", false);
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(new FakeConfigStore(snapshot), realtime))
            {
                await controller.StartIfConfiguredAsync(CancellationToken.None);
                await controller.RefreshConfigurationAsync(CancellationToken.None);
            }

            Assert.That(realtime.StartCount, Is.EqualTo(1));
            Assert.That(realtime.StopCount, Is.Zero);
        }

        [Test]
        public async Task RefreshConfigurationAsync_ConnectionFailureRetriesUntilApplied()
        {
            var snapshot = CreateConfiguredSnapshot("External", false);
            snapshot.RemoteEnabled = true;
            var realtime = new FakeRealtimeClient();
            using (var controller = new RemoteHostServiceController(new FakeConfigStore(snapshot), realtime))
            {
                await controller.StartIfConfiguredAsync(CancellationToken.None);
                snapshot.RelayHost = "changed.example";
                realtime.FailNextStart = true;
                Assert.ThrowsAsync<InvalidOperationException>(() => controller.RefreshConfigurationAsync(CancellationToken.None));
                await controller.RefreshConfigurationAsync(CancellationToken.None);
                await controller.RefreshConfigurationAsync(CancellationToken.None);
            }

            Assert.That(realtime.StartCount, Is.EqualTo(3));
            Assert.That(realtime.StopCount, Is.EqualTo(2));
            Assert.That(realtime.LastConfig.RelayHost, Is.EqualTo("changed.example"));
        }

        [Test]
        public async Task RefreshConfigurationAsync_EmbeddedChangeRetainsLeaseAndRetriesFailedStart()
        {
            var snapshot = CreateConfiguredSnapshot("Embedded", true);
            snapshot.RemoteEnabled = true;
            var realtime = new FakeRealtimeClient();
            var relay = new FakeEmbeddedRelayService();
            using (var controller = new RemoteHostServiceController(new FakeConfigStore(snapshot), realtime, relay))
            {
                await controller.StartIfConfiguredAsync(CancellationToken.None);
                snapshot.EmbeddedRelayPublicHostOverride = "changed.example";
                relay.FailNextStart = true;
                Assert.ThrowsAsync<InvalidOperationException>(() => controller.RefreshConfigurationAsync(CancellationToken.None));
                await controller.RefreshConfigurationAsync(CancellationToken.None);
                await controller.RefreshConfigurationAsync(CancellationToken.None);
            }

            Assert.That(relay.StartCount, Is.EqualTo(3));
            Assert.That(relay.StopCount, Is.Zero);
            Assert.That(realtime.StartCount, Is.EqualTo(2));
        }

        [Test]
        public async Task RefreshConfigurationAsync_DisablingTargetReleasesItsLeaseWithoutReconnect()
        {
            var snapshot = CreateConfiguredSnapshot("Embedded", true);
            snapshot.RemoteEnabled = true;
            var realtime = new FakeRealtimeClient();
            var relay = new FakeEmbeddedRelayService();
            using (var controller = new RemoteHostServiceController(new FakeConfigStore(snapshot), realtime, relay))
            {
                await controller.StartIfConfiguredAsync(CancellationToken.None);
                snapshot.RemoteEnabled = false;
                await controller.RefreshConfigurationAsync(CancellationToken.None);
                await controller.RefreshConfigurationAsync(CancellationToken.None);
            }

            Assert.That(relay.StartCount, Is.EqualTo(1));
            Assert.That(relay.StopCount, Is.EqualTo(1));
            Assert.That(realtime.StartCount, Is.EqualTo(1));
            Assert.That(realtime.StopCount, Is.EqualTo(1));
        }

        private static RemoteServiceConfigSnapshot CreateConfiguredSnapshot(string mode, bool autoStart)
        {
            return new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = mode,
                StartEmbeddedRelayWithApplication = autoStart,
                RelayHost = "relay.example.com",
                RelayPort = 443,
                UseTls = true,
                TenantId = "tenant_a",
                HostId = Guid.NewGuid().ToString("D"),
                EncryptedAccessSecret = "protected"
            };
        }

        private sealed class FakeConfigStore : IRemoteServiceConfigStore
        {
            private readonly RemoteServiceConfigSnapshot _snapshot;

            public FakeConfigStore(RemoteServiceConfigSnapshot snapshot)
            {
                _snapshot = snapshot;
            }

            public RemoteServiceConfigSnapshot Load() { return _snapshot; }
            public void Save(RemoteServiceConfigSnapshot snapshot) { }
            public void ProtectAccessSecret(RemoteServiceConfigSnapshot snapshot, string accessSecret) { }
            public string UnprotectAccessSecret(RemoteServiceConfigSnapshot snapshot) { return "secret"; }
            public void Clear() { }
        }

        private sealed class FakeEmbeddedRelayService : IEmbeddedRelayService
        {
            public EmbeddedRelayRunState State { get; set; }
            public int StartCount { get; private set; }
            public int StopCount { get; private set; }
            public bool FailNextStart { get; set; }

            public EmbeddedRelayStatus GetStatus(RemoteServiceConfigSnapshot snapshot)
            {
                return new EmbeddedRelayStatus { State = State };
            }

            public Task<EmbeddedRelayStartResult> StartAsync(RemoteServiceConfigSnapshot snapshot, CancellationToken cancellationToken)
            {
                StartCount++;
                if (FailNextStart)
                {
                    FailNextStart = false;
                    return Task.FromResult(new EmbeddedRelayStartResult { Success = false, ErrorMessage = "Relay not ready." });
                }
                State = EmbeddedRelayRunState.Running;
                return Task.FromResult(new EmbeddedRelayStartResult
                {
                    Success = true,
                    Status = new EmbeddedRelayStatus { State = State }
                });
            }

            public Task StopAsync(CancellationToken cancellationToken)
            {
                StopCount++;
                State = EmbeddedRelayRunState.Stopped;
                return Task.CompletedTask;
            }

            public EmbeddedRelayDiagnosticInfo GetDiagnostics(RemoteServiceConfigSnapshot snapshot)
            {
                return new EmbeddedRelayDiagnosticInfo { Status = GetStatus(snapshot) };
            }
        }

        private sealed class FakeRealtimeClient : IRelayHostRealtimeClient
        {
            public event EventHandler<RelayRealtimeConnectionStatusEventArgs> StatusChanged
            {
                add { }
                remove { }
            }

            public int StartCount { get; private set; }
            public int StopCount { get; private set; }
            public bool FailNextStart { get; set; }
            public RelayHostConfig LastConfig { get; private set; }

            public Task StartAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
            {
                StartCount++;
                LastConfig = config;
                if (FailNextStart)
                {
                    FailNextStart = false;
                    throw new InvalidOperationException("Connection unavailable.");
                }
                return Task.CompletedTask;
            }

            public Task StopAsync()
            {
                StopCount++;
                return Task.CompletedTask;
            }

            public Task PublishAsync(RemoteRelayEvent remoteEvent, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public void Dispose()
            {
            }
        }
    }
}
