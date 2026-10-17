using System;
using System.IO;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteServiceConfigStoreTests
    {
        [Test]
        public void ProtectAccessSecret_StoresSuffixAndRoundTripsSecret()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "remote-service-store");
            var store = new JsonRemoteServiceConfigStore(new AppConfigService(root));
            var snapshot = new RemoteServiceConfigSnapshot();

            store.ProtectAccessSecret(snapshot, "tenant-secret-1234");
            store.Save(snapshot);

            var reloaded = store.Load();
            Assert.That(reloaded.AccessSecretSuffix, Is.EqualTo("1234"));
            Assert.That(reloaded.EncryptedAccessSecret, Is.Not.Empty);
            Assert.That(reloaded.EncryptedAccessSecret, Does.Not.Contain("tenant-secret-1234"));
            Assert.That(store.UnprotectAccessSecret(reloaded), Is.EqualTo("tenant-secret-1234"));
        }

        [Test]
        public void Load_WhenFileMissing_ReturnsDefaultSnapshot()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "remote-service-missing");
            var store = new JsonRemoteServiceConfigStore(new AppConfigService(root));

            var snapshot = store.Load();

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.RelayPort, Is.EqualTo(0));
            Assert.That(snapshot.EncryptedAccessSecret, Is.Null);
        }

        [Test]
        public void Clear_RemovesSavedConfig()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "remote-service-clear");
            var appConfig = new AppConfigService(root);
            var store = new JsonRemoteServiceConfigStore(appConfig);
            store.Save(new RemoteServiceConfigSnapshot { RelayHost = "relay.example.com" });

            store.Clear();

            Assert.That(File.Exists(appConfig.GetRemoteServiceConfigFilePath()), Is.False);
        }

        [Test]
        public void SaveAndLoad_PreservesEmbeddedRelayProcessIdentity()
        {
            var root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "remote-service-process-identity",
                Guid.NewGuid().ToString("N"));
            var store = new JsonRemoteServiceConfigStore(new AppConfigService(root));
            var snapshot = new RemoteServiceConfigSnapshot
            {
                EmbeddedRelayProcessId = 4242,
                EmbeddedRelayProcessExecutablePath = @"C:\App\relay\mycodex-relay.exe",
                EmbeddedRelayProcessStartTimeUtcTicks = 638877312001234567L
            };

            store.Save(snapshot);

            var reloaded = store.Load();
            Assert.That(reloaded.EmbeddedRelayProcessId, Is.EqualTo(4242));
            Assert.That(reloaded.EmbeddedRelayProcessExecutablePath, Is.EqualTo(@"C:\App\relay\mycodex-relay.exe"));
            Assert.That(reloaded.EmbeddedRelayProcessStartTimeUtcTicks, Is.EqualTo(638877312001234567L));
        }

        [Test]
        public void SaveAndLoad_PreservesEmbeddedRelayCertificateFingerprint()
        {
            var root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "remote-service-certificate-fingerprint",
                Guid.NewGuid().ToString("N"));
            var store = new JsonRemoteServiceConfigStore(new AppConfigService(root));
            var fingerprint = new string('e', 64);

            store.Save(new RemoteServiceConfigSnapshot
            {
                EmbeddedRelayCertificateSha256 = fingerprint,
                EmbeddedRelayPublicHostOverride = "a.jtl.world",
                EmbeddedRelayPublicPortOverride = 443,
                EmbeddedRelayInternalHost = "127.0.0.1",
                EmbeddedRelayInternalPort = 39221,
                EmbeddedRelayInternalUseTls = false
            });

            var loaded = store.Load();
            Assert.That(loaded.EmbeddedRelayCertificateSha256, Is.EqualTo(fingerprint));
            Assert.That(loaded.EmbeddedRelayPublicHostOverride, Is.EqualTo("a.jtl.world"));
            Assert.That(loaded.EmbeddedRelayPublicPortOverride, Is.EqualTo(443));
            Assert.That(loaded.EmbeddedRelayInternalHost, Is.EqualTo("127.0.0.1"));
            Assert.That(loaded.EmbeddedRelayInternalPort, Is.EqualTo(39221));
            Assert.That(loaded.EmbeddedRelayInternalUseTls, Is.False);
        }

        [Test]
        public void SaveAndLoad_PreservesEmbeddedAndExternalProfilesIndependently()
        {
            var root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "remote-service-profiles",
                Guid.NewGuid().ToString("N"));
            var store = new JsonRemoteServiceConfigStore(new AppConfigService(root));
            var snapshot = new RemoteServiceConfigSnapshot
            {
                SchemaVersion = 2,
                RemoteServiceMode = "Embedded",
                StartEmbeddedRelayWithApplication = true,
                RelayHost = "192.168.1.50",
                RelayPort = 38443,
                UseTls = true,
                TenantId = "tenant_embedded",
                EncryptedAccessSecret = "protected-embedded",
                ExternalProfile = new RemoteServiceProfileSnapshot
                {
                    RelayHost = "relay.example.com",
                    RelayPort = 443,
                    UseTls = true,
                    TenantId = "tenant_external",
                    EncryptedAccessSecret = "protected-external"
                }
            };

            store.Save(snapshot);

            var loaded = store.Load();
            Assert.That(loaded.StartEmbeddedRelayWithApplication, Is.True);
            Assert.That(loaded.EmbeddedProfile.RelayHost, Is.EqualTo("192.168.1.50"));
            Assert.That(loaded.EmbeddedProfile.TenantId, Is.EqualTo("tenant_embedded"));
            Assert.That(loaded.EmbeddedProfile.EncryptedAccessSecret, Is.EqualTo("protected-embedded"));
            Assert.That(loaded.ExternalProfile.RelayHost, Is.EqualTo("relay.example.com"));
            Assert.That(loaded.ExternalProfile.TenantId, Is.EqualTo("tenant_external"));
            Assert.That(loaded.ExternalProfile.EncryptedAccessSecret, Is.EqualTo("protected-external"));
        }
    }
}

