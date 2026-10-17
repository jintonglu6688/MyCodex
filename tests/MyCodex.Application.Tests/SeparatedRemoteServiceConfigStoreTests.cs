using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
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
    public sealed class SeparatedRemoteServiceConfigStoreTests
    {
        private string _root;
        private string _sharedPath;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "separated-remote", Guid.NewGuid().ToString("N"));
            _sharedPath = Path.Combine(_root, "config", "relay-service.json");
        }

        [Test]
        public void SaveAndLoad_RetainsEmbeddedTransportAndCngAssociationInSeparateFiles()
        {
            var store = CreateStore("wsl");
            var old = CreateConfiguredSnapshot();
            store.ProtectAccessSecret(old, "tenant-secret-1234");
            store.Save(old);

            var loaded = store.Load();
            var transport = RemoteServiceConfigPlanner.BuildRelayHostConfig(loaded, store.UnprotectAccessSecret(loaded));
            Assert.That(loaded.HostId, Is.EqualTo(old.HostId));
            Assert.That(CngRemoteIdentityStore.GetSigningKeyName(loaded.HostId),
                Is.EqualTo(CngRemoteIdentityStore.GetSigningKeyName(old.HostId)));
            Assert.That(CngRemoteIdentityStore.GetAgreementKeyName(loaded.HostId),
                Is.EqualTo(CngRemoteIdentityStore.GetAgreementKeyName(old.HostId)));
            Assert.That(loaded.ThisPcName, Is.EqualTo("Original WSL"));
            Assert.That(loaded.RemoteEnabled, Is.True);
            Assert.That(loaded.LastRegisteredAt, Is.EqualTo(old.LastRegisteredAt));
            Assert.That(transport.TenantSecret, Is.EqualTo("tenant-secret-1234"));
            Assert.That(transport.ConnectionHost, Is.EqualTo("127.0.0.1"));
            Assert.That(transport.ConnectionPort, Is.EqualTo(39221));
            Assert.That(transport.ConnectionTlsRequired, Is.False);
            Assert.That(transport.CertificateSha256, Is.EqualTo(new string('a', 64)));
            Assert.That(transport.RelaySigningPublicKey, Is.EqualTo("signing-key"));
            Assert.That(loaded.ExternalProfile.EncryptedAccessSecret, Is.EqualTo("protected-external"));
            Assert.That(loaded.EmbeddedRelayProcessId, Is.EqualTo(1234));
            Assert.That(loaded.EmbeddedRelayProcessStartTimeUtcTicks, Is.EqualTo(638877312001234567L));

            var sharedJson = File.ReadAllText(_sharedPath);
            var identityJson = File.ReadAllText(IdentityPath("wsl"));
            Assert.That(sharedJson, Does.Not.Contain(old.HostId));
            Assert.That(sharedJson, Does.Not.Contain("ThisPcName"));
            Assert.That(sharedJson, Does.Not.Contain("RemoteEnabled"));
            Assert.That(identityJson, Does.Not.Contain("EncryptedAccessSecret"));
            Assert.That(identityJson, Does.Not.Contain("EmbeddedRelay"));
            Assert.That(identityJson, Does.Not.Contain("signing-key"));
        }

        [Test]
        public void Save_StaleTargetRenamePreservesNewProcessAndCredentials()
        {
            var first = CreateStore("wsl");
            first.Save(CreateConfiguredSnapshot());
            var staleStore = CreateStore("wsl");
            var stale = staleStore.Load();
            var owner = first.Load();
            owner.EmbeddedRelayProcessId = 6789;
            owner.EmbeddedRelayProcessStartTimeUtcTicks++;
            owner.EncryptedAccessSecret = "new-protected-secret";
            owner.RelaySigningPublicKey = "new-signing-key";
            first.Save(owner);

            stale.ThisPcName = "Renamed WSL";
            staleStore.Save(stale);
            stale.RemoteEnabled = false;
            staleStore.Save(stale);

            var loaded = first.Load();
            Assert.That(loaded.ThisPcName, Is.EqualTo("Renamed WSL"));
            Assert.That(loaded.RemoteEnabled, Is.False);
            Assert.That(loaded.EmbeddedRelayProcessId, Is.EqualTo(6789));
            Assert.That(loaded.EmbeddedRelayProcessStartTimeUtcTicks, Is.EqualTo(owner.EmbeddedRelayProcessStartTimeUtcTicks));
            Assert.That(loaded.EncryptedAccessSecret, Is.EqualTo("new-protected-secret"));
            Assert.That(loaded.EmbeddedProfile.EncryptedAccessSecret, Is.EqualTo("new-protected-secret"));
            Assert.That(loaded.RelaySigningPublicKey, Is.EqualTo("new-signing-key"));
        }

        [Test]
        public void ConcurrentServiceUpdatesAndTargetRenameKeepBothResults()
        {
            var owner = CreateStore("owner");
            owner.Save(CreateConfiguredSnapshot());
            var other = CreateStore("other");
            other.Save(new RemoteServiceConfigSnapshot
            {
                HostId = "e1497f3b-76a0-4c18-870f-a2d6863e2f63",
                ThisPcName = "Other",
                RemoteEnabled = false
            });

            Task.WaitAll(
                Task.Run(() =>
                {
                    for (var i = 1; i <= 20; i++)
                    {
                        var snapshot = owner.Load();
                        snapshot.EmbeddedRelayProcessId = 2000 + i;
                        owner.Save(snapshot);
                    }
                }),
                Task.Run(() =>
                {
                    for (var i = 1; i <= 20; i++)
                    {
                        var snapshot = other.Load();
                        snapshot.ThisPcName = "Other " + i;
                        other.Save(snapshot);
                    }
                }));

            Assert.That(owner.Load().EmbeddedRelayProcessId, Is.EqualTo(2020));
            Assert.That(other.Load().EmbeddedRelayProcessId, Is.EqualTo(2020));
            Assert.That(other.Load().ThisPcName, Is.EqualTo("Other 20"));
            Assert.That(owner.Load().ThisPcName, Is.EqualTo("Original WSL"));
        }

        [Test]
        public void Save_IndependentSharedEditsMergeAndSameFieldConflictDoesNotWrite()
        {
            var first = CreateStore("windows");
            first.Save(CreateConfiguredSnapshot());
            var second = CreateStore("windows");
            var a = first.Load();
            var b = second.Load();
            a.RelaySigningPublicKey = "fresh-pin";
            first.Save(a);
            b.RelayHost = "new-public.example.com";
            second.Save(b);
            Assert.That(first.Load().EmbeddedProfile.RelaySigningPublicKey, Is.EqualTo("fresh-pin"));
            Assert.That(first.Load().RelayHost, Is.EqualTo("new-public.example.com"));

            a = first.Load();
            b = second.Load();
            a.RelayPort = 40001;
            first.Save(a);
            b.RelayPort = 40002;
            var sharedBefore = File.ReadAllBytes(_sharedPath);
            var identityBefore = File.ReadAllBytes(IdentityPath("windows"));
            Assert.Throws<InvalidOperationException>(() => second.Save(b));
            Assert.That(File.ReadAllBytes(_sharedPath), Is.EqualTo(sharedBefore));
            Assert.That(File.ReadAllBytes(IdentityPath("windows")), Is.EqualTo(identityBefore));
        }

        [Test]
        public void Save_NewTargetCannotRotateInitializedSharedServiceOrCopyIdentity()
        {
            var originalStore = CreateStore("wsl");
            var original = CreateConfiguredSnapshot();
            originalStore.Save(original);
            var sharedBefore = File.ReadAllBytes(_sharedPath);
            var newStore = CreateStore("ssh");
            var identity = newStore.LoadTargetIdentity();
            Assert.That(identity.RemoteEnabled, Is.False);
            Assert.That(identity.HostId, Is.Null);
            newStore.Save(new RemoteServiceConfigSnapshot
            {
                HostId = "e1497f3b-76a0-4c18-870f-a2d6863e2f63",
                ThisPcName = "SSH",
                RemoteEnabled = false,
                TenantId = "unrelated-tenant",
                EncryptedAccessSecret = "unrelated-secret"
            });

            Assert.That(File.ReadAllBytes(_sharedPath), Is.EqualTo(sharedBefore));
            Assert.That(originalStore.Load().HostId, Is.EqualTo(original.HostId));
            Assert.That(newStore.Load().HostId, Is.Not.EqualTo(original.HostId));
            Assert.That(newStore.Load().EncryptedAccessSecret, Is.EqualTo(original.EncryptedAccessSecret));
            Assert.That(newStore.Load().RemoteEnabled, Is.False);
        }

        [Test]
        public void ExplicitSettingsSavesUpdateOnlyTheirOwnedFile()
        {
            var a = CreateStore("a");
            a.Save(CreateConfiguredSnapshot());
            var b = CreateStore("b");
            b.Save(new RemoteServiceConfigSnapshot
            {
                HostId = "e1497f3b-76a0-4c18-870f-a2d6863e2f63",
                ThisPcName = "Second",
                RemoteEnabled = false
            });
            var aBefore = File.ReadAllBytes(IdentityPath("a"));
            var bBefore = File.ReadAllBytes(IdentityPath("b"));
            var shared = a.LoadShared();
            shared.EmbeddedRelayInternalPort = 39222;
            a.SaveShared(shared);
            Assert.That(File.ReadAllBytes(IdentityPath("a")), Is.EqualTo(aBefore));
            Assert.That(File.ReadAllBytes(IdentityPath("b")), Is.EqualTo(bBefore));

            var sharedBefore = File.ReadAllBytes(_sharedPath);
            var identity = b.LoadTargetIdentity();
            identity.ThisPcName = "Renamed";
            identity.RemoteEnabled = true;
            b.SaveTargetIdentity(identity);
            Assert.That(File.ReadAllBytes(_sharedPath), Is.EqualTo(sharedBefore));
            Assert.That(a.Load().ThisPcName, Is.EqualTo("Original WSL"));
            Assert.That(b.Load().ThisPcName, Is.EqualTo("Renamed"));
        }

        [Test]
        public void NewTargetDefaultsPreserveAnExistingNonTlsService()
        {
            var originalStore = CreateStore("original");
            var snapshot = CreateConfiguredSnapshot();
            snapshot.UseTls = false;
            originalStore.Save(snapshot);
            var sharedBefore = File.ReadAllBytes(_sharedPath);
            var newStore = CreateStore("new");
            var newTarget = newStore.Load();
            RemoteServiceConfigPlanner.EnsureDefaults(newTarget, "New");
            newStore.Save(newTarget);
            Assert.That(newStore.Load().UseTls, Is.False);
            Assert.That(File.ReadAllBytes(_sharedPath), Is.EqualTo(sharedBefore));
            Assert.That(newStore.Load().RemoteEnabled, Is.False);
        }

        [Test]
        public void SharedSettingsUpdatesKeepActiveAndInactiveProfilesConsistent()
        {
            var store = CreateStore("a");
            store.Save(CreateConfiguredSnapshot());
            var shared = store.LoadShared();
            shared.RelayHost = "changed.example.com";
            store.SaveShared(shared);
            Assert.That(store.Load().EmbeddedProfile.RelayHost, Is.EqualTo("changed.example.com"));

            shared = store.LoadShared();
            shared.EmbeddedProfile.RelayPort = 42001;
            store.SaveShared(shared);
            Assert.That(store.Load().RelayPort, Is.EqualTo(42001));

            shared = store.LoadShared();
            shared.RemoteServiceMode = "External";
            store.SaveShared(shared);
            var loaded = store.Load();
            Assert.That(loaded.RelayHost, Is.EqualTo("external.example.com"));
            Assert.That(loaded.EmbeddedProfile.RelayHost, Is.EqualTo("changed.example.com"));
            Assert.That(loaded.EmbeddedProfile.RelayPort, Is.EqualTo(42001));
            Assert.That(loaded.ExternalProfile.EncryptedAccessSecret, Is.EqualTo("protected-external"));
        }

        [Test]
        public void ExistingIdentityCannotBeChangedByRenameOrBlindSnapshot()
        {
            var store = CreateStore("a");
            store.Save(CreateConfiguredSnapshot());
            var before = File.ReadAllBytes(IdentityPath("a"));
            var loaded = store.Load();
            loaded.HostId = Guid.NewGuid().ToString("D");
            Assert.Throws<InvalidOperationException>(() => store.Save(loaded));
            Assert.That(File.ReadAllBytes(IdentityPath("a")), Is.EqualTo(before));
            Assert.Throws<InvalidOperationException>(() => store.SaveShared(new SharedRelayServiceConfigSnapshot
            {
                EncryptedAccessSecret = "replacement"
            }));
            Assert.That(store.Load().EncryptedAccessSecret, Is.EqualTo("protected-secret"));
        }

        [Test]
        public void InvalidPersistedDataIsReportedAndNeverReplacedByDefaults()
        {
            var store = CreateStore("a");
            store.Save(CreateConfiguredSnapshot());
            File.WriteAllText(_sharedPath, "{broken");
            Assert.Throws<SerializationException>(() => store.Load());
            Assert.Throws<SerializationException>(() => store.Save(CreateConfiguredSnapshot()));
            Assert.That(File.ReadAllText(_sharedPath), Is.EqualTo("{broken"));
        }

        [Test]
        public void Clear_OnlyRemovesThisTargetIdentity()
        {
            var store = CreateStore("a");
            store.Save(CreateConfiguredSnapshot());
            var sharedBefore = File.ReadAllBytes(_sharedPath);
            store.Clear();
            Assert.That(File.Exists(IdentityPath("a")), Is.False);
            Assert.That(File.ReadAllBytes(_sharedPath), Is.EqualTo(sharedBefore));
            Assert.That(store.LoadTargetIdentity().RemoteEnabled, Is.False);
        }

        [Test]
        public void AppConfigService_ExplicitTargetRootUsesSharedServicePath()
        {
            var appConfig = new AppConfigService(
                Path.Combine(_root, "targets", "windows"),
                Path.Combine(_root, "install"),
                "MyCodex",
                _root);
            var store = new JsonRemoteServiceConfigStore(appConfig);
            store.Save(CreateConfiguredSnapshot());
            Assert.That(File.Exists(_sharedPath), Is.True);
            Assert.That(File.ReadAllText(appConfig.GetRemoteServiceConfigFilePath()), Does.Not.Contain("RelayHost"));
        }

        [Test]
        public void LegacyFileRoundTripKeepsOldFieldsAndIndependentSettingsEntryPoints()
        {
            var appConfig = new AppConfigService(Path.Combine(_root, "legacy"));
            appConfig.EnsureConfigDirectory();
            var path = appConfig.GetRemoteServiceConfigFilePath();
            File.WriteAllText(path, "{\"HostId\":\"8248f523-d733-4f91-a606-f8ed8b98c127\",\"ThisPcName\":\"Old\",\"RemoteServiceMode\":\"Embedded\",\"RelayHost\":\"192.168.1.50\",\"RelayPort\":38443,\"TenantId\":\"tenant_local\",\"EncryptedAccessSecret\":\"protected-secret\",\"EmbeddedRelayInternalHost\":\"127.0.0.1\",\"EmbeddedRelayInternalPort\":39221,\"EmbeddedRelayCertificateSha256\":\"pin\"}");
            var store = new JsonRemoteServiceConfigStore(appConfig);
            var loaded = store.Load();
            Assert.That(loaded.RemoteEnabled, Is.Null);
            store.Save(loaded);
            Assert.That(File.ReadAllText(path), Does.Contain("HostId"));
            Assert.That(File.Exists(Path.Combine(appConfig.GetSharedDataDirectory(), "config", "relay-service.json")), Is.False);

            var identity = store.LoadTargetIdentity();
            Assert.That(identity.RemoteEnabled, Is.True);
            identity.ThisPcName = "Renamed";
            store.SaveTargetIdentity(identity);
            var shared = store.LoadShared();
            shared.EmbeddedRelayProcessId = 1234;
            store.SaveShared(shared);
            var reloaded = store.Load();
            Assert.That(reloaded.HostId, Is.EqualTo(loaded.HostId));
            Assert.That(reloaded.ThisPcName, Is.EqualTo("Renamed"));
            Assert.That(reloaded.EncryptedAccessSecret, Is.EqualTo("protected-secret"));
            Assert.That(reloaded.EmbeddedRelayInternalPort, Is.EqualTo(39221));
            Assert.That(reloaded.EmbeddedRelayCertificateSha256, Is.EqualTo("pin"));
            Assert.That(reloaded.EmbeddedRelayProcessId, Is.EqualTo(1234));
        }

        [Test]
        public void Projection_PreservesEveryLegacyMemberAndClonesModeProfiles()
        {
            var legacy = CreateConfiguredSnapshot();
            legacy.EmbeddedRelayLastStartedAt = DateTime.UtcNow;
            legacy.CreatedAt = DateTime.UtcNow.AddDays(-1);
            legacy.UpdatedAt = DateTime.UtcNow;
            legacy.RemoteEnabled = false;
            RemoteServiceConfigPlanner.CaptureActiveProfile(legacy);
            var shared = RemoteServiceConfigProjection.SplitShared(legacy);
            var identity = RemoteServiceConfigProjection.SplitTargetIdentity(legacy);
            var composed = RemoteServiceConfigProjection.Compose(shared, identity);
            foreach (var property in typeof(RemoteServiceConfigSnapshot).GetProperties())
            {
                var original = property.GetValue(legacy);
                var actual = property.GetValue(composed);
                if (property.PropertyType == typeof(RemoteServiceProfileSnapshot))
                {
                    foreach (var profileProperty in typeof(RemoteServiceProfileSnapshot).GetProperties())
                    {
                        Assert.That(profileProperty.GetValue(actual), Is.EqualTo(profileProperty.GetValue(original)),
                            property.Name + "." + profileProperty.Name);
                    }
                }
                else
                {
                    Assert.That(actual, Is.EqualTo(original), property.Name);
                }
            }

            shared.ExternalProfile.EncryptedAccessSecret = "another";
            Assert.That(legacy.ExternalProfile.EncryptedAccessSecret, Is.EqualTo("protected-external"));
            Assert.That(composed.ExternalProfile.EncryptedAccessSecret, Is.EqualTo("protected-external"));
        }

        [TestCase(1)]
        [TestCase(2)]
        public void OtherLiveUsersBlockSharedChangesButAllowTargetRenameAndToggle(int userCount)
        {
            var ownership = new SharedRelayStateStore(_root, record => true);
            var store = new SeparatedRemoteServiceConfigStore(_sharedPath, IdentityPath("a"), ownership);
            store.Save(CreateConfiguredSnapshot());
            SharedRelayProcessRecord current;
            using (var process = Process.GetCurrentProcess()) current = SharedRelayProcessRecord.Capture(process);
            ownership.WithLock(state =>
            {
                for (var i = 0; i < userCount; i++)
                {
                    state.Leases.Add(new SharedRelayLease
                    {
                        Id = "other-" + i,
                        User = new SharedRelayProcessRecord
                        {
                            ProcessId = current.ProcessId + 100 + i,
                            ExecutablePath = current.ExecutablePath,
                            StartTimeUtcTicks = current.StartTimeUtcTicks
                        }
                    });
                }

                return true;
            }, CancellationToken.None);

            var sharedBefore = File.ReadAllBytes(_sharedPath);
            var identity = store.Load();
            identity.ThisPcName = "Renamed";
            identity.RemoteEnabled = false;
            store.Save(identity);
            Assert.That(store.Load().ThisPcName, Is.EqualTo("Renamed"));
            Assert.That(store.Load().RemoteEnabled, Is.False);
            Assert.That(File.ReadAllBytes(_sharedPath), Is.EqualTo(sharedBefore));
            var identityBefore = File.ReadAllBytes(IdentityPath("a"));

            identity.RelayHost = "other.example.com";
            Assert.Throws<InvalidOperationException>(() => store.Save(identity));
            Assert.That(File.ReadAllBytes(_sharedPath), Is.EqualTo(sharedBefore));
            Assert.That(File.ReadAllBytes(IdentityPath("a")), Is.EqualTo(identityBefore));
            var shared = store.LoadShared();
            shared.RelayPort++;
            Assert.Throws<InvalidOperationException>(() => store.SaveShared(shared));
            Assert.That(File.ReadAllBytes(_sharedPath), Is.EqualTo(sharedBefore));
        }

        [Test]
        public void SingleOwnLiveUserMaySaveSharedSettings()
        {
            var ownership = new SharedRelayStateStore(_root);
            var store = new SeparatedRemoteServiceConfigStore(_sharedPath, IdentityPath("a"), ownership);
            store.Save(CreateConfiguredSnapshot());
            SharedRelayProcessRecord current;
            using (var process = Process.GetCurrentProcess()) current = SharedRelayProcessRecord.Capture(process);
            ownership.WithLock(state =>
            {
                state.Leases.Add(new SharedRelayLease { Id = "own", User = current });
                return true;
            }, CancellationToken.None);
            var shared = store.LoadShared();
            shared.RelayPort = 44001;
            store.SaveShared(shared);
            Assert.That(store.Load().RelayPort, Is.EqualTo(44001));
        }

        [Test]
        public void ServiceOwnerCanSaveMetadataUnderLifecycleGateWithMultipleUsers()
        {
            var ownership = new SharedRelayStateStore(_root, record => true);
            var store = new SeparatedRemoteServiceConfigStore(_sharedPath, IdentityPath("a"), ownership);
            store.Save(CreateConfiguredSnapshot());
            var identityBefore = File.ReadAllBytes(IdentityPath("a"));
            ownership.WithLock(state =>
            {
                state.Leases.Add(new SharedRelayLease
                {
                    Id = "other",
                    User = new SharedRelayProcessRecord { ProcessId = 42, ExecutablePath = "other.exe", StartTimeUtcTicks = 123 }
                });
                var shared = store.LoadShared();
                shared.EmbeddedRelayProcessId = 9876;
                store.SaveSharedFromService(shared);
                return true;
            }, CancellationToken.None);
            Assert.That(store.Load().EmbeddedRelayProcessId, Is.EqualTo(9876));
            Assert.That(File.ReadAllBytes(IdentityPath("a")), Is.EqualTo(identityBefore));
        }

        private JsonRemoteServiceConfigStore CreateStore(string target)
        {
            return new JsonRemoteServiceConfigStore(_sharedPath, IdentityPath(target));
        }

        private string IdentityPath(string target)
        {
            return Path.Combine(_root, "targets", target, "config", "remote-service.json");
        }

        private static RemoteServiceConfigSnapshot CreateConfiguredSnapshot()
        {
            return new RemoteServiceConfigSnapshot
            {
                HostId = "8248f523-d733-4f91-a606-f8ed8b98c127",
                ThisPcName = "Original WSL",
                LastRegisteredAt = new DateTime(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc),
                RemoteServiceMode = "Embedded",
                RelayHost = "192.168.1.50",
                RelayPort = 38443,
                UseTls = true,
                TenantId = "tenant_local",
                EncryptedAccessSecret = "protected-secret",
                AccessSecretSuffix = "cret",
                EmbeddedRelayEnabled = true,
                EmbeddedRelayConfigPath = @"C:\App\relay\relay.toml",
                EmbeddedRelayStatePath = @"C:\App\relay\state.db",
                EmbeddedRelayProcessId = 1234,
                EmbeddedRelayProcessExecutablePath = @"C:\App\relay\mycodex-relay.exe",
                EmbeddedRelayProcessStartTimeUtcTicks = 638877312001234567L,
                EmbeddedRelayCertificateSha256 = new string('a', 64),
                EmbeddedRelayInternalHost = "127.0.0.1",
                EmbeddedRelayInternalPort = 39221,
                EmbeddedRelayInternalUseTls = false,
                RelaySigningPublicKey = "signing-key",
                RelaySigningKeyFingerprint = "signing-fingerprint",
                EmbeddedRelayPublicHostOverride = "relay.example.com",
                EmbeddedRelayPublicPortOverride = 443,
                StartEmbeddedRelayWithApplication = true,
                ExternalProfile = new RemoteServiceProfileSnapshot
                {
                    RelayHost = "external.example.com",
                    RelayPort = 443,
                    UseTls = true,
                    TenantId = "external_tenant",
                    EncryptedAccessSecret = "protected-external"
                }
            };
        }
    }
}
