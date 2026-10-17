using System;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteServiceConfigPlannerTests
    {
        [Test]
        public void EnsureDefaults_GeneratesStableHostIdAndDisplayName()
        {
            var snapshot = new RemoteServiceConfigSnapshot();

            RemoteServiceConfigPlanner.EnsureDefaults(snapshot, "WORKSTATION");
            var firstHostId = snapshot.HostId;
            RemoteServiceConfigPlanner.EnsureDefaults(snapshot, "OTHER");

            Assert.That(firstHostId, Is.Not.Empty);
            Guid parsedHostId;
            Assert.That(Guid.TryParseExact(firstHostId, "D", out parsedHostId), Is.True);
            Assert.That(firstHostId, Is.EqualTo(firstHostId.ToLowerInvariant()));
            Assert.That(snapshot.HostId, Is.EqualTo(firstHostId));
            Assert.That(snapshot.ThisPcName, Is.EqualTo("WORKSTATION"));
            Assert.That(snapshot.RelayPort, Is.EqualTo(443));
            Assert.That(snapshot.UseTls, Is.True);
        }

        [Test]
        public void EnsureDefaults_DiscardsNonCanonicalLegacyHostId()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                HostId = "host_legacy"
            };

            RemoteServiceConfigPlanner.EnsureDefaults(snapshot, "WORKSTATION");

            Guid parsedHostId;
            Assert.That(snapshot.HostId, Is.Not.EqualTo("host_legacy"));
            Assert.That(Guid.TryParseExact(snapshot.HostId, "D", out parsedHostId), Is.True);
            Assert.That(snapshot.HostId, Is.EqualTo(snapshot.HostId.ToLowerInvariant()));
        }

        [Test]
        public void EnsureDefaults_SetsRemoteServiceModeToEmbedded()
        {
            var snapshot = new RemoteServiceConfigSnapshot();

            RemoteServiceConfigPlanner.EnsureDefaults(snapshot, "WORKSTATION");

            Assert.That(snapshot.RemoteServiceMode, Is.EqualTo("Embedded"));
        }

        [Test]
        public void Validate_ForEmbeddedModeBeforeStartAllowsEmptyServerFields()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "Embedded"
            };

            var result = RemoteServiceConfigPlanner.Validate(snapshot);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Messages, Is.Empty);
        }

        [Test]
        public void Validate_ForEmbeddedModeRequiresInternalCredentialsAfterServiceStart()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "Embedded",
                RelayHost = "127.0.0.1",
                RelayPort = 38443,
                TenantId = "tenant_local",
                EncryptedAccessSecret = "encrypted-secret"
            };

            var result = RemoteServiceConfigPlanner.Validate(snapshot);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Messages, Is.Empty);
        }

        [Test]
        public void Validate_ForExternalModeKeepsExistingRequiredFields()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "External"
            };

            var result = RemoteServiceConfigPlanner.Validate(snapshot);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Messages, Does.Contain("Service host is required."));
            Assert.That(result.Messages, Does.Contain("Tenant ID is required."));
            Assert.That(result.Messages, Does.Contain("Access secret is required."));
        }

        [Test]
        public void BuildRelayHostConfig_UsesUnprotectedSecret()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RelayHost = "relay.example.com",
                RelayPort = 443,
                UseTls = true,
                TenantId = "tenant_a",
                HostId = "host_a",
                ThisPcName = "Windows",
                AccessSecretSuffix = "1234"
            };

            var config = RemoteServiceConfigPlanner.BuildRelayHostConfig(snapshot, "tenant-secret-1234");

            Assert.That(config.RelayHost, Is.EqualTo("relay.example.com"));
            Assert.That(config.RelayPort, Is.EqualTo(443));
            Assert.That(config.TlsRequired, Is.True);
            Assert.That(config.TenantId, Is.EqualTo("tenant_a"));
            Assert.That(config.TenantSecret, Is.EqualTo("tenant-secret-1234"));
        }

        [Test]
        public void BuildRelayHostConfig_CarriesRelayIdentityPins()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RelayHost = "relay.example.com",
                RelayPort = 443,
                UseTls = true,
                TenantId = "tenant_a",
                RelaySigningPublicKey = "relay-signing-key",
                RelaySigningKeyFingerprint = "relay-signing-fingerprint"
            };

            var config = RemoteServiceConfigPlanner.BuildRelayHostConfig(snapshot, "tenant-secret");

            Assert.That(config.RelaySigningPublicKey, Is.EqualTo("relay-signing-key"));
            Assert.That(config.RelaySigningKeyFingerprint, Is.EqualTo("relay-signing-fingerprint"));
        }

        [Test]
        public void BuildRelayHostConfig_EmbeddedUsesLoopbackAndCarriesFingerprint()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "Embedded",
                RelayHost = "192.168.1.50",
                RelayPort = 38443,
                UseTls = true,
                TenantId = "tenant_local",
                EmbeddedRelayCertificateSha256 = new string('b', 64),
                EmbeddedRelayInternalHost = "127.0.0.1",
                EmbeddedRelayInternalPort = 39221,
                EmbeddedRelayInternalUseTls = false
            };

            var config = RemoteServiceConfigPlanner.BuildRelayHostConfig(snapshot, "secret");

            Assert.That(config.RelayHost, Is.EqualTo("192.168.1.50"));
            Assert.That(config.ConnectionHost, Is.EqualTo("127.0.0.1"));
            Assert.That(config.ConnectionPort, Is.EqualTo(39221));
            Assert.That(config.ConnectionTlsRequired, Is.False);
            Assert.That(config.CertificateSha256, Is.EqualTo(new string('b', 64)));
        }

        [Test]
        public void BuildRelayHostConfig_ExternalUsesPublishedHostAndNoPin()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "External",
                RelayHost = "relay.example.com",
                RelayPort = 443,
                UseTls = true,
                TenantId = "tenant_external",
                EmbeddedRelayCertificateSha256 = new string('c', 64)
            };

            var config = RemoteServiceConfigPlanner.BuildRelayHostConfig(snapshot, "secret");

            Assert.That(config.ConnectionHost, Is.Null);
            Assert.That(config.ConnectionPort, Is.EqualTo(0));
            Assert.That(config.CertificateSha256, Is.Null);
        }

        [Test]
        public void EnsureDefaults_MigratesLegacyExternalFieldsIntoExternalProfile()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "External",
                RelayHost = "relay.example.com",
                RelayPort = 443,
                UseTls = true,
                TenantId = "tenant_external",
                EncryptedAccessSecret = "protected-external"
            };

            RemoteServiceConfigPlanner.EnsureDefaults(snapshot, "WORKSTATION");

            Assert.That(snapshot.SchemaVersion, Is.EqualTo(RemoteServiceConfigPlanner.CurrentSchemaVersion));
            Assert.That(snapshot.ExternalProfile.RelayHost, Is.EqualTo("relay.example.com"));
            Assert.That(snapshot.ExternalProfile.TenantId, Is.EqualTo("tenant_external"));
            Assert.That(snapshot.ExternalProfile.EncryptedAccessSecret, Is.EqualTo("protected-external"));
            Assert.That(snapshot.EmbeddedProfile.RelayHost, Is.Null);
        }

        [Test]
        public void SelectMode_PreservesPreviousProfileAndProjectsSelectedProfile()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "External",
                RelayHost = "relay.example.com",
                RelayPort = 443,
                UseTls = true,
                TenantId = "tenant_external",
                EncryptedAccessSecret = "protected-external"
            };
            RemoteServiceConfigPlanner.EnsureDefaults(snapshot, "WORKSTATION");
            snapshot.EmbeddedProfile = new RemoteServiceProfileSnapshot
            {
                RelayHost = "192.168.1.50",
                RelayPort = 38443,
                UseTls = true,
                TenantId = "tenant_embedded",
                EncryptedAccessSecret = "protected-embedded"
            };

            RemoteServiceConfigPlanner.SelectMode(snapshot, "Embedded");

            Assert.That(snapshot.RelayHost, Is.EqualTo("192.168.1.50"));
            Assert.That(snapshot.TenantId, Is.EqualTo("tenant_embedded"));
            Assert.That(snapshot.ExternalProfile.RelayHost, Is.EqualTo("relay.example.com"));
            Assert.That(snapshot.ExternalProfile.EncryptedAccessSecret, Is.EqualTo("protected-external"));
        }

        [TestCase(false, false, false, false)]
        [TestCase(true, false, false, true)]
        public void BuildButtonState_EnablesActionsFromConfigAndSelection(bool configured, bool registered, bool deviceSelected, bool canSave)
        {
            var state = RemoteServiceConfigPlanner.BuildButtonState(configured, registered, false, deviceSelected, false);

            Assert.That(state.CanSave, Is.EqualTo(canSave));
            Assert.That(state.CanRegister, Is.EqualTo(configured));
            Assert.That(state.CanCreateInvite, Is.EqualTo(configured && registered));
            Assert.That(state.CanRevokeDevice, Is.EqualTo(deviceSelected));
        }
    }
}
