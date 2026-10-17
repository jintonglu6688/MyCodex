using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SharedRelayLifecycleTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "shared-relay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        [Test]
        public async Task Ownership_SerializesConcurrentLeasesAndReleasesOnlyTheRequestedUser()
        {
            var stores = Enumerable.Range(0, 8).Select(_ => new SharedRelayStateStore(_root, identity => identity != null)).ToArray();
            await Task.WhenAll(stores.Select((store, index) => Task.Run(() => store.WithLock(state =>
            {
                state.Leases.Add(new SharedRelayLease
                {
                    Id = index.ToString(),
                    User = new SharedRelayProcessRecord { ProcessId = 42, ExecutablePath = "client.exe", StartTimeUtcTicks = 123 }
                });
                return true;
            }, CancellationToken.None))));

            stores[0].WithLock(state =>
            {
                Assert.That(state.Leases.Count, Is.EqualTo(8));
                state.Leases.RemoveAll(lease => lease.Id == "0");
                return true;
            }, CancellationToken.None);
            Assert.That(stores[1].WithLock(state => state.Leases.Count, CancellationToken.None), Is.EqualTo(7));
        }

        [Test]
        public void Ownership_RejectsReusedPidWrongPathAndWrongStartTime()
        {
            SharedRelayProcessRecord current;
            using (var process = Process.GetCurrentProcess()) current = SharedRelayProcessRecord.Capture(process);
            var store = new SharedRelayStateStore(_root);
            store.WithLock(state =>
            {
                state.Leases.Add(new SharedRelayLease { Id = "valid", User = current });
                state.Leases.Add(new SharedRelayLease { Id = "reused", User = new SharedRelayProcessRecord
                {
                    ProcessId = current.ProcessId, ExecutablePath = current.ExecutablePath,
                    StartTimeUtcTicks = current.StartTimeUtcTicks + 1
                } });
                state.Leases.Add(new SharedRelayLease { Id = "other-path", User = new SharedRelayProcessRecord
                {
                    ProcessId = current.ProcessId, ExecutablePath = current.ExecutablePath + ".other",
                    StartTimeUtcTicks = current.StartTimeUtcTicks
                } });
                return true;
            }, CancellationToken.None);

            Assert.That(store.WithLock(state => state.Leases.Select(lease => lease.Id).ToArray(), CancellationToken.None),
                Is.EqualTo(new[] { "valid" }));
        }

        [Test]
        public void ServiceConfig_LoadsExistingCredentialsAndNeverCreatesTargetIdentity()
        {
            var path = Path.Combine(_root, "config", "relay-service.json");
            var target = Path.Combine(_root, "target", "remote-service.json");
            var original = new JsonRemoteServiceConfigStore(path, target);
            var shared = original.LoadShared();
            shared.RemoteServiceMode = "Embedded";
            shared.EncryptedAccessSecret = "original-protected-secret";
            shared.EmbeddedRelayCertificateSha256 = "original-certificate";
            shared.RelaySigningPublicKey = "original-public-key";
            shared.RelaySigningKeyFingerprint = "original-fingerprint";
            original.SaveShared(shared);

            var serviceStore = new SharedRelayConfigStore(_root);
            var snapshot = serviceStore.Load();
            Assert.That(snapshot.HostId, Is.Null);
            Assert.That(snapshot.EncryptedAccessSecret, Is.EqualTo("original-protected-secret"));
            snapshot.EmbeddedRelayProcessId = 42;
            serviceStore.Save(snapshot);

            var saved = original.LoadShared();
            Assert.That(saved.EncryptedAccessSecret, Is.EqualTo(shared.EncryptedAccessSecret));
            Assert.That(saved.EmbeddedRelayCertificateSha256, Is.EqualTo(shared.EmbeddedRelayCertificateSha256));
            Assert.That(saved.RelaySigningPublicKey, Is.EqualTo(shared.RelaySigningPublicKey));
            Assert.That(saved.RelaySigningKeyFingerprint, Is.EqualTo(shared.RelaySigningKeyFingerprint));
            Assert.That(saved.EmbeddedRelayProcessId, Is.EqualTo(42));
            Assert.That(File.Exists(target), Is.False);
            Assert.That(File.Exists(Path.Combine(_root, "Relay", "unused-target-identity.json")), Is.False);
            Assert.Throws<InvalidOperationException>(() => serviceStore.Save(new RemoteServiceConfigSnapshot()));
        }

        [Test]
        public void Ownership_UnknownProcessInspectionAndMalformedLeaseDoNotGrantShutdownPermission()
        {
            var writer = new SharedRelayStateStore(_root, identity => true);
            writer.WithLock(state =>
            {
                state.Leases.Add(new SharedRelayLease { Id = "unknown", User = new SharedRelayProcessRecord
                {
                    ProcessId = 42, ExecutablePath = "client.exe", StartTimeUtcTicks = 123
                } });
                return true;
            }, CancellationToken.None);
            var record = Path.Combine(_root, "Relay", "shared-host.json");
            var original = File.ReadAllText(record);
            var called = false;
            var inaccessible = new SharedRelayStateStore(_root, identity => { throw new UnauthorizedAccessException(); });
            Assert.Throws<UnauthorizedAccessException>(() => inaccessible.WithLock(state => called = true, CancellationToken.None));
            Assert.That(called, Is.False);
            Assert.That(File.ReadAllText(record), Is.EqualTo(original));

            File.WriteAllText(record, "{\"Leases\":[{\"Id\":\"broken\",\"User\":null}]}");
            Assert.Throws<InvalidDataException>(() => writer.WithLock(state => called = true, CancellationToken.None));
            Assert.That(called, Is.False);
        }

        [Test]
        public void ServiceConfig_DoesNotOverwriteSavedTlsWithVirtualTargetDefaults()
        {
            var store = new JsonRemoteServiceConfigStore(Path.Combine(_root, "config", "relay-service.json"),
                Path.Combine(_root, "target", "remote-service.json"));
            var shared = store.LoadShared();
            shared.RemoteServiceMode = "Embedded";
            shared.UseTls = false;
            store.SaveShared(shared);
            var serviceStore = new SharedRelayConfigStore(_root);
            var snapshot = serviceStore.Load();
            RemoteServiceConfigPlanner.EnsureDefaults(snapshot, "test");
            Assert.That(snapshot.UseTls, Is.False);
        }

        [Test]
        public void Ownership_LiveUserFromConflictingInstallationIsNotRemoved()
        {
            var store = new SharedRelayStateStore(_root, identity => true);
            store.WithLock(state =>
            {
                state.InstallationExecutable = "first.exe";
                state.Leases.Add(new SharedRelayLease { Id = "other-installation", User = new SharedRelayProcessRecord
                {
                    ProcessId = 42, ExecutablePath = "second.exe", StartTimeUtcTicks = 123
                } });
                return true;
            }, CancellationToken.None);
            var path = Path.Combine(_root, "Relay", "shared-host.json");
            var original = File.ReadAllText(path);
            Assert.Throws<InvalidDataException>(() => store.WithLock(state => state.Leases.Count, CancellationToken.None));
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        }

        [Test]
        public void ServiceConfig_PreservesExternalProfileWhileUpdatingEmbeddedProcess()
        {
            var store = new JsonRemoteServiceConfigStore(Path.Combine(_root, "config", "relay-service.json"),
                Path.Combine(_root, "target", "remote-service.json"));
            var shared = store.LoadShared();
            shared.RemoteServiceMode = "External";
            shared.SchemaVersion = RemoteServiceConfigPlanner.CurrentSchemaVersion;
            shared.RelayHost = "external.example";
            shared.EncryptedAccessSecret = "external-secret";
            shared.ExternalProfile = new RemoteServiceProfileSnapshot { RelayHost = "external.example", EncryptedAccessSecret = "external-secret" };
            shared.EmbeddedProfile = new RemoteServiceProfileSnapshot { RelayHost = "local.example", EncryptedAccessSecret = "local-secret" };
            store.SaveShared(shared);

            var serviceStore = new SharedRelayConfigStore(_root);
            var snapshot = serviceStore.Load();
            Assert.That(snapshot.EncryptedAccessSecret, Is.EqualTo("local-secret"));
            snapshot.EmbeddedRelayProcessId = 42;
            serviceStore.Save(snapshot);

            var saved = store.LoadShared();
            Assert.That(saved.RemoteServiceMode, Is.EqualTo("External"));
            Assert.That(saved.EncryptedAccessSecret, Is.EqualTo("external-secret"));
            Assert.That(saved.EmbeddedProfile.EncryptedAccessSecret, Is.EqualTo("local-secret"));
            Assert.That(saved.EmbeddedRelayProcessId, Is.EqualTo(42));
        }

        [Test]
        public void ExistingRelayWithoutSharedSecretIsNotReinitialized()
        {
            var layout = EmbeddedRelayDirectoryLayout.Create(_root);
            Directory.CreateDirectory(layout.RootDirectory);
            File.WriteAllText(layout.ConfigPath, "preserve me");
            Assert.Throws<InvalidOperationException>(() => SharedEmbeddedRelayHost.EnsureInitializationIsSafe(
                _root, new RemoteServiceConfigSnapshot()));
            Assert.That(File.ReadAllText(layout.ConfigPath), Is.EqualTo("preserve me"));
        }
    }
}
