using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SharedRelayCorruptionTests
    {
        private string _root;
        private string _path;

        [Test]
        public void RecoveryProbe_RegisteredOwnersAreCheckedWithoutSendingWindowRequests()
        {
            var records = Path.Combine(_root, "targets", ".instances");
            Directory.CreateDirectory(records);
            var path = Path.Combine(records, "MyCodex.Target.test.json");
            var record = TargetInstanceSecurity.Capture("recovery-test", "windows");
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(record));
            Assert.That(TargetInstanceCoordinator.HasOtherRegisteredUsers(_root, 0), Is.True);
            Assert.That(TargetInstanceCoordinator.HasOtherRegisteredUsers(_root, record.ProcessId), Is.False);
            record.StartTimeUtcTicks++;
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(record));
            Assert.That(TargetInstanceCoordinator.HasOtherRegisteredUsers(_root, 0), Is.False);
            record.StartTimeUtcTicks = 0;
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(record));
            Assert.That(SharedRelayOwnershipRecovery.GetBlockReason(_root, name => false, identity => false,
                () => new int[0]), Is.EqualTo("RegisteredTarget"));
        }

        [TestCase("MyCodex")]
        [TestCase("MyCodex.Dev")]
        [TestCase("mycodex-relay")]
        public void RecoveryProbe_RefusesAnyOtherTargetHelperOrRelay(string processName)
        {
            Assert.That(SharedRelayOwnershipRecovery.GetBlockReason(_root,
                name => name == processName, identity => false, () => new int[0]),
                Is.EqualTo("OtherProcess:" + processName));
        }

        [Test]
        public void RecoveryProbe_RefusesLiveRecordedRelayAndOccupiedPorts()
        {
            var config = new SharedRelayConfigStore(_root);
            var snapshot = config.Load();
            snapshot.EmbeddedRelayProcessId = 42;
            snapshot.EmbeddedRelayProcessExecutablePath = "custom-relay.exe";
            snapshot.EmbeddedRelayProcessStartTimeUtcTicks = 123;
            snapshot.EmbeddedRelayInternalPort = 41234;
            config.Save(snapshot);
            Assert.That(SharedRelayOwnershipRecovery.GetBlockReason(_root, name => false, identity => true,
                () => new int[0]), Is.EqualTo("RecordedRelayAlive"));
            foreach (var port in new[] { 38443, 41234 })
                Assert.That(SharedRelayOwnershipRecovery.GetBlockReason(_root, name => false, identity => false,
                    () => new[] { port }), Is.EqualTo("RelayPortOccupied"));
            Assert.That(SharedRelayOwnershipRecovery.GetBlockReason(_root, name => false, identity => false,
                () => new int[0]), Is.Null);
        }

        [Test]
        public void Ownership_UnknownInspectionPreservesZeroFilledRecord()
        {
            File.WriteAllBytes(_path, new byte[516]);
            var store = new SharedRelayStateStore(_root, identity => true,
                () => { throw new UnauthorizedAccessException(); });
            Assert.Throws<InvalidDataException>(() => store.WithLock(state => true, CancellationToken.None));
            Assert.That(File.ReadAllBytes(_path), Is.EqualTo(new byte[516]));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(_path), "*.corrupt-*"), Is.Empty);
        }

        [Test]
        public async Task Ownership_ConcurrentRecoveryRetainsEveryNewLeaseAndOnlyOneBackup()
        {
            File.WriteAllBytes(_path, new byte[516]);
            var stores = Enumerable.Range(0, 5).Select(_ => new SharedRelayStateStore(_root, identity => true, () => null)).ToArray();
            await Task.WhenAll(stores.Select((store, index) => Task.Run(() => store.WithLock(state =>
            {
                state.Leases.Add(new SharedRelayLease { Id = index.ToString(), User = new SharedRelayProcessRecord
                {
                    ProcessId = 42, ExecutablePath = "client.exe", StartTimeUtcTicks = 123
                } });
                return true;
            }, CancellationToken.None))));
            Assert.That(stores[0].WithLock(state => state.Leases.Count, CancellationToken.None), Is.EqualTo(5));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(_path), "*.corrupt-*"), Has.Length.EqualTo(1));
        }

        [Test]
        public void Ownership_ZeroFilledRecordRecoversAndAllowsPairingConfigurationSave()
        {
            var ownership = new SharedRelayStateStore(_root, identity => true, () => null);
            var store = new SeparatedRemoteServiceConfigStore(Path.Combine(_root, "config", "relay-service.json"),
                Path.Combine(_root, "config", "remote-service.json"), ownership);
            var snapshot = store.Load();
            snapshot.HostId = Guid.NewGuid().ToString();
            snapshot.EncryptedAccessSecret = "protected-original";
            store.Save(snapshot);
            var hostId = snapshot.HostId;
            File.WriteAllBytes(_path, new byte[516]);

            snapshot.ThisPcName = "pairing retry";
            store.Save(snapshot);

            var saved = store.Load();
            Assert.That(saved.ThisPcName, Is.EqualTo("pairing retry"));
            Assert.That(saved.HostId, Is.EqualTo(hostId));
            Assert.That(saved.EncryptedAccessSecret, Is.EqualTo("protected-original"));
            var backups = Directory.GetFiles(Path.GetDirectoryName(_path), "shared-host.json.corrupt-*");
            Assert.That(backups, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllBytes(backups[0]), Is.EqualTo(new byte[516]));
            Assert.That(ownership.WithLock(state => state.Leases.Count, CancellationToken.None), Is.Zero);
        }

        [Test]
        public void Ownership_ActiveUsersBlockRecoveryUntilTheyHaveExited()
        {
            File.WriteAllBytes(_path, new byte[516]);
            var blocked = true;
            var store = new SharedRelayStateStore(_root, identity => true, () => blocked ? "OtherProcess" : null);
            var called = false;
            Assert.Throws<InvalidDataException>(() => store.WithLock(state => called = true, CancellationToken.None));
            Assert.That(called, Is.False);
            Assert.That(File.ReadAllBytes(_path), Is.EqualTo(new byte[516]));
            blocked = false;
            Assert.That(store.WithLock(state => state.Leases.Count, CancellationToken.None), Is.Zero);
        }

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "relay-corrupt-" + Guid.NewGuid().ToString("N"));
            _path = Path.Combine(_root, "Relay", "shared-host.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        [TestCase("\0\0")]
        [TestCase("")]
        [TestCase("{\"Leases\":[")]
        [TestCase("{\"Leases\":null}")]
        [TestCase("{\"Host\":{\"ProcessId\":42}}")]
        public void Ownership_RejectsCorruptionWithoutRunningActionsOrReplacingEvidence(string contents)
        {
            File.WriteAllText(_path, contents, new UTF8Encoding(false));
            var original = File.ReadAllBytes(_path);
            var called = false;
            var store = new SharedRelayStateStore(_root, identity => true, () => "OtherProcess");
            Assert.Throws<InvalidDataException>(() => store.WithLock(state => called = true, CancellationToken.None));
            Assert.That(called, Is.False);
            Assert.That(File.ReadAllBytes(_path), Is.EqualTo(original));
        }

        [Test]
        public void Ownership_ExtraBomIsRecoveredWithoutDroppingLiveLeases()
        {
            File.WriteAllText(_path, "\ufeff\ufeff{\"Leases\":[{\"Id\":\"live\",\"User\":{\"ProcessId\":42,\"ExecutablePath\":\"client.exe\",\"StartTimeUtcTicks\":123}}]}", new UTF8Encoding(false));
            var store = new SharedRelayStateStore(_root, identity => true, () => "OtherProcess");
            Assert.That(store.WithLock(state => state.Leases[0].Id, CancellationToken.None), Is.EqualTo("live"));
            Assert.That(store.WithLock(state => state.Leases.Count, CancellationToken.None), Is.EqualTo(1));
        }

        [Test]
        public async Task Service_CorruptionBecomesFailedStatusAndStartResultAndDoesNotBreakDisposal()
        {
            File.WriteAllText(_path, "\0x", new UTF8Encoding(false));
            var service = new SharedEmbeddedRelayService(new AppConfigService(_root));
            try
            {
                var status = service.GetStatus(null);
                Assert.That(status.State, Is.EqualTo(EmbeddedRelayRunState.Failed));
                Assert.That(status.Message, Is.Not.Empty);
                Assert.That(service.GetDiagnostics(null).Status.State, Is.EqualTo(EmbeddedRelayRunState.Failed));
                var result = await service.StartAsync(null, CancellationToken.None);
                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorMessage, Is.EqualTo(status.Message));
                Assert.ThrowsAsync<InvalidDataException>(async () => await service.StopAsync(CancellationToken.None));
                Assert.DoesNotThrow(() => service.Dispose());
                Assert.DoesNotThrow(() => service.Dispose());
                Assert.That(File.ReadAllBytes(_path), Is.EqualTo(new byte[] { 0, 120 }));
            }
            finally
            {
                // The pre-fix implementation cannot dispose while the evidence is corrupt.
                File.Delete(_path);
                service.Dispose();
            }
        }

        [TestCase("\0private-token-do-not-log", 1, "U+0000")]
        [TestCase("{\"Error\":\"private-token-do-not-log\",", 0, "U+007B")]
        public void Ownership_DiagnosticsDescribeCorruptionWithoutLoggingFileContents(string contents, int nulCount, string firstCodePoint)
        {
            File.WriteAllText(_path, contents, new UTF8Encoding(false));
            var output = new StringWriter();
            using (var listener = new TextWriterTraceListener(output))
            {
                Trace.Listeners.Add(listener);
                try
                {
                    var store = new SharedRelayStateStore(_root);
                    var error = Assert.Throws<InvalidDataException>(() => store.WithLock(state => true, CancellationToken.None));
                    Assert.Throws<InvalidDataException>(() => store.WithLock(state => true, CancellationToken.None));
                    Trace.Flush();
                    var log = output.ToString();
                    Assert.That(log, Does.Contain("[RemoteRelay] OwnershipReadFailed"));
                    Assert.That(log, Does.Contain("NulCount=" + nulCount));
                    Assert.That(log, Does.Contain("FirstCodePoint=" + firstCodePoint));
                    Assert.That(log, Does.Contain("Sha256="));
                    Assert.That(log.Split(new[] { "OwnershipReadFailed" }, StringSplitOptions.None).Length, Is.EqualTo(2));
                    Assert.That(log + error.ToString(), Does.Not.Contain("private-token-do-not-log"));
                }
                finally
                {
                    Trace.Listeners.Remove(listener);
                }
            }
        }

        [Test]
        public void Service_CancelledStartPreservesCancellationEvenWhenCleanupCannotReadOwnership()
        {
            File.WriteAllText(_path, "\0x", new UTF8Encoding(false));
            var service = new SharedEmbeddedRelayService(new AppConfigService(_root));
            try
            {
                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    Assert.CatchAsync<OperationCanceledException>(async () => await service.StartAsync(null, cancellation.Token));
                    Assert.That(File.ReadAllBytes(_path), Is.EqualTo(new byte[] { 0, 120 }));
                }
            }
            finally
            {
                File.Delete(_path);
                service.Dispose();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void PhoneConnection_OpensWithVisibleFailureWhenOwnershipIsCorrupt()
        {
            File.WriteAllText(_path, "\0x", new UTF8Encoding(false));
            var config = new AppConfigService(_root);
            var service = new SharedEmbeddedRelayService(config);
            try
            {
                using (var form = new RemoteServiceManagementForm(new JsonRemoteServiceConfigStore(config),
                    new RelayHostClient(), null, embeddedRelayService: service,
                    bindingStore: new JsonRemoteBindingStore(Path.Combine(_root, "bindings.json")), targetOnly: true))
                {
                    Assert.That(form.ServerStateTextForTests, Is.EqualTo(service.GetStatus(null).Message));
                    Assert.That(form.InitialFocusControlForTests.AccessibleName, Is.Not.Empty);
                }
            }
            finally
            {
                File.Delete(_path);
                service.Dispose();
            }
        }
    }
}
