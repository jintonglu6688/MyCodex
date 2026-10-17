using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteServiceConnectionWorkflowTests
    {
        [Test]
        public async Task SaveOnlyAsync_PersistsSecretWithoutCallingRelay()
        {
            var store = new FakeRemoteServiceConfigStore();
            var client = new FakeRelayHostClient();
            var workflow = new RemoteServiceConnectionWorkflow(store, client);
            var snapshot = CreateValidSnapshot();

            var result = await workflow.SaveOnlyAsync(snapshot, "tenant-secret", "WORKSTATION", CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Stage, Is.EqualTo(RemoteServiceConnectionStage.Saved));
            Assert.That(store.ProtectedSecret, Is.EqualTo("tenant-secret"));
            Assert.That(client.Calls, Is.Empty);
        }

        [Test]
        public async Task ConnectAndRegisterAsync_PinsMetadataBeforeEnrollAndList()
        {
            var store = new FakeRemoteServiceConfigStore();
            var client = new FakeRelayHostClient();
            var workflow = new RemoteServiceConnectionWorkflow(store, client);
            var snapshot = CreateValidSnapshot();

            var identity = CreateIdentity();
            var result = await workflow.ConnectAndRegisterAsync(
                snapshot,
                "tenant-secret",
                "WORKSTATION",
                identity,
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Stage, Is.EqualTo(RemoteServiceConnectionStage.Registered));
            Assert.That(client.Calls, Is.EqualTo(new[] { "GetMetadata", "EnrollHost", "ListDevices" }));
            Assert.That(store.Snapshot.RelaySigningPublicKey, Is.EqualTo(client.SigningPublicKey));
            Assert.That(store.Snapshot.RelaySigningKeyFingerprint, Is.EqualTo(client.SigningKeyFingerprint));
            Assert.That(client.ListDevicesConfig.RelaySigningPublicKey, Is.EqualTo(client.SigningPublicKey));
            Assert.That(client.ListDevicesConfig.RelaySigningKeyFingerprint, Is.EqualTo(client.SigningKeyFingerprint));
            Assert.That(store.Snapshot.LastRegisteredAt.HasValue, Is.True);
            Assert.That(client.LastIdentity, Is.SameAs(identity));
        }

        [Test]
        public async Task ConnectAndRegisterAsync_ForListFailure_ReturnsConnectionFailedWithoutRegistering()
        {
            var store = new FakeRemoteServiceConfigStore();
            var client = new FakeRelayHostClient { ListDevicesException = new InvalidOperationException("tenant-secret leaked") };
            var workflow = new RemoteServiceConnectionWorkflow(store, client);

            var result = await workflow.ConnectAndRegisterAsync(
                CreateValidSnapshot(),
                "tenant-secret",
                "WORKSTATION",
                CreateIdentity(),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Stage, Is.EqualTo(RemoteServiceConnectionStage.TestConnection));
            Assert.That(result.Error.Message, Does.Contain("tenant-secret"));
            Assert.That(client.Calls, Is.EqualTo(new[] { "GetMetadata", "EnrollHost", "ListDevices" }));
            Assert.That(store.LastSavedRelaySigningPublicKey, Is.EqualTo(client.SigningPublicKey));
            Assert.That(store.LastSavedRelaySigningKeyFingerprint, Is.EqualTo(client.SigningKeyFingerprint));
        }

        [Test]
        public async Task ConnectAndRegisterAsync_ForMetadataFailure_PreservesExistingPins()
        {
            var store = new FakeRemoteServiceConfigStore();
            var client = new FakeRelayHostClient
            {
                MetadataException = new InvalidOperationException("metadata failed")
            };
            var workflow = new RemoteServiceConnectionWorkflow(store, client);
            var snapshot = CreateValidSnapshot();
            snapshot.RelaySigningPublicKey = "existing-key";
            snapshot.RelaySigningKeyFingerprint = "existing-fingerprint";

            var result = await workflow.ConnectAndRegisterAsync(
                snapshot,
                "tenant-secret",
                "WORKSTATION",
                CreateIdentity(),
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(client.Calls, Is.EqualTo(new[] { "GetMetadata" }));
            Assert.That(store.Snapshot.RelaySigningPublicKey, Is.EqualTo("existing-key"));
            Assert.That(store.Snapshot.RelaySigningKeyFingerprint, Is.EqualTo("existing-fingerprint"));
        }

        private static RemoteServiceConfigSnapshot CreateValidSnapshot()
        {
            return new RemoteServiceConfigSnapshot
            {
                RelayHost = "relay.example.com",
                RelayPort = 443,
                UseTls = true,
                TenantId = "tenant_a",
                HostId = "host_a",
                ThisPcName = "Windows"
            };
        }

        private static RemoteEndpointIdentity CreateIdentity()
        {
            return new RemoteEndpointIdentity
            {
                IdentityId = "host_a",
                KeyVersion = 1,
                SigningPublicKey = "signing-key",
                AgreementPublicKey = "agreement-key"
            };
        }

        private sealed class FakeRemoteServiceConfigStore : IRemoteServiceConfigStore
        {
            public RemoteServiceConfigSnapshot Snapshot = new RemoteServiceConfigSnapshot();

            public string ProtectedSecret { get; private set; }

            public string LastSavedRelaySigningPublicKey { get; private set; }

            public string LastSavedRelaySigningKeyFingerprint { get; private set; }

            public RemoteServiceConfigSnapshot Load()
            {
                return Snapshot;
            }

            public void Save(RemoteServiceConfigSnapshot snapshot)
            {
                Snapshot = snapshot;
                LastSavedRelaySigningPublicKey = snapshot.RelaySigningPublicKey;
                LastSavedRelaySigningKeyFingerprint = snapshot.RelaySigningKeyFingerprint;
            }

            public void ProtectAccessSecret(RemoteServiceConfigSnapshot snapshot, string accessSecret)
            {
                ProtectedSecret = accessSecret;
                snapshot.EncryptedAccessSecret = string.IsNullOrWhiteSpace(accessSecret) ? null : "protected";
                snapshot.AccessSecretSuffix = "cret";
            }

            public string UnprotectAccessSecret(RemoteServiceConfigSnapshot snapshot)
            {
                return string.IsNullOrWhiteSpace(snapshot.EncryptedAccessSecret) ? null : "tenant-secret";
            }

            public void Clear()
            {
                Snapshot = new RemoteServiceConfigSnapshot();
            }
        }

        private sealed class FakeRelayHostClient : IRelayHostClient
        {
            public readonly List<string> Calls = new List<string>();

            public Exception ListDevicesException;

            public Exception MetadataException;

            public string SigningPublicKey = "relay-signing-key";

            public string SigningKeyFingerprint = "relay-signing-fingerprint";

            public RemoteEndpointIdentity LastIdentity { get; private set; }

            public RelayHostConfig ListDevicesConfig { get; private set; }

            public Task<RelayMetadata> GetMetadataAsync(RelayHostConfig config, CancellationToken cancellationToken)
            {
                Calls.Add("GetMetadata");
                if (MetadataException != null)
                {
                    throw MetadataException;
                }

                return Task.FromResult(new RelayMetadata
                {
                    ProtocolVersion = 1,
                    SigningPublicKey = SigningPublicKey,
                    SigningKeyFingerprint = SigningKeyFingerprint
                });
            }

            public Task<RelayHostRegistrationResult> EnrollHostAsync(RelayHostConfig config, RemoteEndpointIdentity identity, CancellationToken cancellationToken)
            {
                Calls.Add("EnrollHost");
                LastIdentity = identity;
                return Task.FromResult(new RelayHostRegistrationResult { TenantId = config.TenantId, HostId = identity.IdentityId });
            }

            public Task<IReadOnlyList<RelayDeviceSummary>> ListDevicesAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
            {
                Calls.Add("ListDevices");
                ListDevicesConfig = config;
                if (ListDevicesException != null)
                {
                    throw ListDevicesException;
                }

                return Task.FromResult((IReadOnlyList<RelayDeviceSummary>)new RelayDeviceSummary[0]);
            }

            public Task<RelayPairingInvite> CreatePairingInviteAsync(RelayHostConfig config, CreatePairingInviteRequest request, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task CancelPairingInviteAsync(RelayHostConfig config, string inviteId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<IReadOnlyList<OpaquePairingClaim>> ListPairingClaimsAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task ApprovePairingClaimAsync(RelayHostConfig config, PairingApprovalRequest request, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task RejectPairingClaimAsync(RelayHostConfig config, string hostId, string claimId, string deviceId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<bool> RevokeDeviceAsync(RelayHostConfig config, string hostId, string deviceId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }
}
