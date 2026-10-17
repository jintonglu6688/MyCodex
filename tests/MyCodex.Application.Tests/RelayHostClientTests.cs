using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class RelayHostClientTests
    {
        [Test]
        public async Task GetMetadataAsync_ValidatesAndFixesRelayIdentityWithoutBearer()
        {
            var vector = SecureRemoteVector.Load();
            var transport = new QueueRelayHttpTransport(new RelayHttpResponse(
                200,
                "{\"protocolVersion\":1,\"signingPublicKey\":\"" +
                vector.String("publicKeys", "relaySigning") +
                "\",\"signingKeyFingerprint\":\"" +
                vector.String("publicKeys", "relayFingerprintSha256") +
                "\",\"serverTime\":1893456000000,\"maxRequestBytes\":1048576,\"maxMessageBytes\":1048576}"));
            var client = new RelayHostClient(transport);
            var config = CreateEnrollmentConfig();

            var metadata = await client.GetMetadataAsync(config, CancellationToken.None);

            Assert.That(metadata.ProtocolVersion, Is.EqualTo(1));
            Assert.That(config.RelaySigningPublicKey, Is.EqualTo(metadata.SigningPublicKey));
            Assert.That(config.RelaySigningKeyFingerprint, Is.EqualTo(metadata.SigningKeyFingerprint));
            Assert.That(transport.Requests[0].Url, Does.EndWith("/.well-known/mycodex-relay"));
            Assert.That(transport.Requests[0].Headers.ContainsKey("Authorization"), Is.False);
        }

        [Test]
        public void GetMetadataAsync_RejectsPartialOrChangedRelayPin()
        {
            var vector = SecureRemoteVector.Load();
            var json = "{\"protocolVersion\":1,\"signingPublicKey\":\"" +
                vector.String("publicKeys", "relaySigning") +
                "\",\"signingKeyFingerprint\":\"" +
                vector.String("publicKeys", "relayFingerprintSha256") +
                "\",\"serverTime\":1893456000000,\"maxRequestBytes\":1048576,\"maxMessageBytes\":1048576}";
            var partialConfig = CreateEnrollmentConfig();
            partialConfig.RelaySigningPublicKey = vector.String("publicKeys", "relaySigning");
            var changedConfig = CreateEnrollmentConfig();
            changedConfig.RelaySigningPublicKey = vector.String("publicKeys", "hostSigning");
            changedConfig.RelaySigningKeyFingerprint = vector.String("publicKeys", "relayFingerprintSha256");

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new RelayHostClient(
                    new QueueRelayHttpTransport(new RelayHttpResponse(200, json)))
                    .GetMetadataAsync(partialConfig, CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await new RelayHostClient(
                    new QueueRelayHttpTransport(new RelayHttpResponse(200, json)))
                    .GetMetadataAsync(changedConfig, CancellationToken.None));
        }

        [Test]
        public async Task EnrollHostAsync_UsesManagementBearerAndAllIdentityKeys()
        {
            var transport = new QueueRelayHttpTransport(
                new RelayHttpResponse(
                    200,
                    "{\"tenantId\":\"tenant_a\",\"hostId\":\"" + SecureRelayAuthenticatorTests.HostId + "\"}"));
            var client = new RelayHostClient(transport);
            var identity = CreateIdentity();
            var config = CreateEnrollmentConfig();
            config.HostDisplayName = "PC · WSL · Ubuntu development";

            var result = await client.EnrollHostAsync(config, identity, CancellationToken.None);

            Assert.That(result.TenantId, Is.EqualTo("tenant_a"));
            Assert.That(result.HostId, Is.EqualTo(SecureRelayAuthenticatorTests.HostId));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            Assert.That(transport.Requests[0].Method, Is.EqualTo("POST"));
            Assert.That(transport.Requests[0].Url, Is.EqualTo("https://relay.example.com:443/v1/hosts/enroll"));
            Assert.That(transport.Requests[0].Headers["Authorization"], Is.EqualTo("Bearer tenant-secret-value"));
            Assert.That(transport.Requests[0].Body, Does.Contain("\"signingPublicKey\":\"signing-public-key\""));
            Assert.That(transport.Requests[0].Body, Does.Contain("\"agreementPublicKey\":\"agreement-public-key\""));
            Assert.That(transport.Requests[0].Body, Does.Contain("\"keyVersion\":7"));
            Assert.That(transport.Requests[0].Body, Does.Contain("\"displayName\":\"PC · WSL · Ubuntu development\""));
            Assert.That(transport.Requests[0].Url, Does.Not.Contain("tenant-secret-value"));
        }

        [Test]
        public async Task EmbeddedConfig_EnrollmentUsesConnectionHost()
        {
            var transport = new QueueRelayHttpTransport(
                new RelayHttpResponse(
                    200,
                    "{\"tenantId\":\"tenant_a\",\"hostId\":\"" + SecureRelayAuthenticatorTests.HostId + "\"}"));
            var client = new RelayHostClient(transport);
            var config = CreateEnrollmentConfig();
            config.RelayHost = "192.168.1.50";
            config.ConnectionHost = "127.0.0.1";
            config.ConnectionPort = 39221;
            config.ConnectionTlsRequired = false;

            await client.EnrollHostAsync(config, CreateIdentity(), CancellationToken.None);

            Assert.That(transport.Requests[0].Url, Is.EqualTo("http://127.0.0.1:39221/v1/hosts/enroll"));
        }

        [Test]
        public async Task CreatePairingInviteAsync_UsesFreshPurposeTicketAndParsesInvite()
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = SecureRelayAuthenticatorTests.CreateFixture(relayKey);
                var ticket = QueueAuthentication(
                    fixture,
                    relayKey,
                    RelayTicketPurpose.CreatePairingInvite,
                    string.Empty,
                    10);
                fixture.Transport.Enqueue(new RelayHttpResponse(
                    200,
                    "{\"tenantId\":\"tenant_a\",\"hostId\":\"" + SecureRelayAuthenticatorTests.HostId +
                    "\",\"inviteId\":\"invite_a\",\"status\":\"pending\",\"expiresAt\":1893456600000}"));
                var client = new RelayHostClient(fixture.Transport, fixture.Authenticator);

                var result = await client.CreatePairingInviteAsync(
                    fixture.Config,
                    new CreatePairingInviteRequest
                    {
                        TenantId = "tenant_a",
                        HostId = SecureRelayAuthenticatorTests.HostId,
                        InviteId = "invite_a",
                        ExpiresAt = 1893456600000
                    },
                    CancellationToken.None);

                Assert.That(result.InviteId, Is.EqualTo("invite_a"));
                Assert.That(fixture.Transport.Requests[2].Headers["Authorization"], Is.EqualTo("Bearer " + ticket));
                Assert.That(fixture.Transport.Requests[2].Body, Does.Contain("\"expiresAt\":1893456600000"));
                Assert.That(fixture.Transport.Requests[2].Body, Does.Not.Contain("inviteSecret"));
                AssertRuntimeRequestDoesNotUseManagementBearer(fixture.Transport.Requests[2]);
            }
        }

        [Test]
        public async Task ApprovePairingClaimAsync_UsesDeviceBoundPurposeTicketAndExactBody()
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = SecureRelayAuthenticatorTests.CreateFixture(relayKey);
                var ticket = QueueAuthentication(
                    fixture,
                    relayKey,
                    RelayTicketPurpose.ApprovePairingClaim,
                    "device_a",
                    20);
                fixture.Transport.Enqueue(new RelayHttpResponse(
                    200,
                    "{\"claimId\":\"claim_a\",\"status\":\"approved\"}"));
                var client = new RelayHostClient(fixture.Transport, fixture.Authenticator);

                await client.ApprovePairingClaimAsync(
                    fixture.Config,
                    new PairingApprovalRequest
                    {
                        HostId = SecureRelayAuthenticatorTests.HostId,
                        ClaimId = "claim_a",
                        DeviceId = "device_a",
                        DeviceSigningPublicKey = "signing-key",
                        DeviceAgreementPublicKey = "agreement-key",
                        DeviceKeyVersion = 1,
                        BindingVersion = 1,
                        ApprovalHeader = "encoded-header",
                        ApprovalNonce = "nonce",
                        ApprovalCiphertext = "ciphertext"
                    },
                    CancellationToken.None);

                Assert.That(fixture.Transport.Requests[2].Headers["Authorization"], Is.EqualTo("Bearer " + ticket));
                Assert.That(fixture.Transport.Requests[0].Body, Does.Contain("\"purpose\":\"pairing_claim_approve\""));
                Assert.That(fixture.Transport.Requests[0].Body, Does.Contain("\"deviceId\":\"device_a\""));
                Assert.That(fixture.Transport.Requests[2].Url, Does.Contain("/pairing/claims/claim_a/approve?tenantId=tenant_a"));
                Assert.That(fixture.Transport.Requests[2].Body, Does.Contain("\"approvalHeader\":\"encoded-header\""));
                Assert.That(fixture.Transport.Requests[2].Body, Does.Not.Contain("\"approvalHeader\":{"));
                AssertRuntimeRequestDoesNotUseManagementBearer(fixture.Transport.Requests[2]);
            }
        }

        [Test]
        public async Task RejectPairingClaimAsync_UsesClaimDeviceBoundPurposeTicket()
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = SecureRelayAuthenticatorTests.CreateFixture(relayKey);
                var ticket = QueueAuthentication(
                    fixture,
                    relayKey,
                    RelayTicketPurpose.RejectPairingClaim,
                    "device_a",
                    25);
                fixture.Transport.Enqueue(new RelayHttpResponse(
                    200,
                    "{\"claimId\":\"claim_a\",\"status\":\"rejected\"}"));
                var client = new RelayHostClient(fixture.Transport, fixture.Authenticator);

                await client.RejectPairingClaimAsync(
                    fixture.Config,
                    SecureRelayAuthenticatorTests.HostId,
                    "claim_a",
                    "device_a",
                    CancellationToken.None);

                Assert.That(fixture.Transport.Requests[0].Body, Does.Contain("\"purpose\":\"pairing_claim_reject\""));
                Assert.That(fixture.Transport.Requests[0].Body, Does.Contain("\"deviceId\":\"device_a\""));
                Assert.That(fixture.Transport.Requests[2].Headers["Authorization"], Is.EqualTo("Bearer " + ticket));
                Assert.That(fixture.Transport.Requests[2].Body, Is.Null);
            }
        }

        [Test]
        public async Task ListPairingClaimsAsync_ParsesOpaqueWrappedClaims()
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var vector = SecureRemoteVector.Load();
                var fixture = SecureRelayAuthenticatorTests.CreateFixture(relayKey);
                QueueAuthentication(
                    fixture,
                    relayKey,
                    RelayTicketPurpose.ListPairingClaims,
                    string.Empty,
                    27);
                fixture.Transport.Enqueue(new RelayHttpResponse(
                    200,
                    "{\"claims\":[{\"header\":{\"inviteId\":\"" +
                    vector.String("inputs", "ids", "inviteId") +
                    "\",\"tenantId\":\"tenant_a\",\"hostId\":\"" +
                    SecureRelayAuthenticatorTests.HostId +
                    "\",\"claimId\":\"claim_a\",\"deviceId\":\"device_a\",\"deviceAgreementPublicKey\":\"" +
                    vector.String("publicKeys", "deviceAgreement") +
                    "\",\"deviceKeyVersion\":1,\"clientNonce\":\"" +
                    SecureRemoteBase64Url.Encode(vector.HexBytes("inputs", "clientNonceHex")) +
                    "\",\"ciphertextLength\":589},\"nonce\":\"nonce\",\"ciphertext\":\"ciphertext\",\"status\":\"pending\",\"createdAt\":1893456001000,\"expiresAt\":1893456600000}]}"));
                var client = new RelayHostClient(fixture.Transport, fixture.Authenticator);

                var claims = await client.ListPairingClaimsAsync(
                    fixture.Config,
                    SecureRelayAuthenticatorTests.HostId,
                    CancellationToken.None);

                Assert.That(claims, Has.Count.EqualTo(1));
                Assert.That(claims[0].ClaimId, Is.EqualTo("claim_a"));
                Assert.That(claims[0].Header.DeviceId, Is.EqualTo("device_a"));
                Assert.That(fixture.Transport.Requests[2].Body, Is.Null);
            }
        }

        [Test]
        public async Task ListDevicesAsync_RequestsAndConsumesOneFreshTicketPerOperation()
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = SecureRelayAuthenticatorTests.CreateFixture(relayKey);
                var firstTicket = QueueAuthentication(
                    fixture,
                    relayKey,
                    RelayTicketPurpose.DeviceList,
                    string.Empty,
                    30);
                fixture.Transport.Enqueue(new RelayHttpResponse(
                    200,
                    "{\"devices\":[{\"tenantId\":\"tenant_a\",\"hostId\":\"" +
                    SecureRelayAuthenticatorTests.HostId +
                    "\",\"deviceId\":\"device_a\",\"deviceSigningPublicKey\":\"signing\",\"deviceAgreementPublicKey\":\"agreement\",\"deviceKeyVersion\":1,\"bindingVersion\":1,\"revoked\":false,\"approvedAt\":1893456002000,\"lastSeenAt\":null,\"online\":false}]}"));
                var secondTicket = QueueAuthentication(
                    fixture,
                    relayKey,
                    RelayTicketPurpose.DeviceList,
                    string.Empty,
                    60);
                fixture.Transport.Enqueue(new RelayHttpResponse(200, "{\"devices\":[]}"));
                var client = new RelayHostClient(fixture.Transport, fixture.Authenticator);

                var first = await client.ListDevicesAsync(
                    fixture.Config,
                    SecureRelayAuthenticatorTests.HostId,
                    CancellationToken.None);
                var second = await client.ListDevicesAsync(
                    fixture.Config,
                    SecureRelayAuthenticatorTests.HostId,
                    CancellationToken.None);

                Assert.That(first, Has.Count.EqualTo(1));
                Assert.That(second, Is.Empty);
                Assert.That(firstTicket, Is.Not.EqualTo(secondTicket));
                Assert.That(fixture.Transport.Requests, Has.Count.EqualTo(6));
                Assert.That(fixture.Transport.Requests[0].Url, Does.EndWith("/v1/auth/challenges"));
                Assert.That(fixture.Transport.Requests[2].Headers["Authorization"], Is.EqualTo("Bearer " + firstTicket));
                Assert.That(fixture.Transport.Requests[3].Url, Does.EndWith("/v1/auth/challenges"));
                Assert.That(fixture.Transport.Requests[5].Headers["Authorization"], Is.EqualTo("Bearer " + secondTicket));
                Assert.That(fixture.Transport.Requests[2].Body, Is.Null);
                Assert.That(fixture.Transport.Requests[5].Body, Is.Null);
                AssertRuntimeRequestDoesNotUseManagementBearer(fixture.Transport.Requests[2]);
                AssertRuntimeRequestDoesNotUseManagementBearer(fixture.Transport.Requests[5]);
            }
        }

        [Test]
        public async Task RevokeDeviceAsync_UsesDeviceBoundPurposeTicket()
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = SecureRelayAuthenticatorTests.CreateFixture(relayKey);
                var ticket = QueueAuthentication(
                    fixture,
                    relayKey,
                    RelayTicketPurpose.DeviceRevoke,
                    "device_a",
                    90);
                fixture.Transport.Enqueue(new RelayHttpResponse(
                    200,
                    "{\"tenantId\":\"tenant_a\",\"hostId\":\"" + SecureRelayAuthenticatorTests.HostId +
                    "\",\"deviceId\":\"device_a\",\"revoked\":true}"));
                var client = new RelayHostClient(fixture.Transport, fixture.Authenticator);

                var result = await client.RevokeDeviceAsync(
                    fixture.Config,
                    SecureRelayAuthenticatorTests.HostId,
                    "device_a",
                    CancellationToken.None);

                Assert.That(result, Is.True);
                Assert.That(fixture.Transport.Requests[2].Headers["Authorization"], Is.EqualTo("Bearer " + ticket));
                Assert.That(fixture.Transport.Requests[0].Body, Does.Contain("\"purpose\":\"device_revoke\""));
                Assert.That(fixture.Transport.Requests[2].Url, Does.Contain("/devices/device_a/revoke?tenantId=tenant_a"));
                Assert.That(fixture.Transport.Requests[2].Body, Is.Null);
                AssertRuntimeRequestDoesNotUseManagementBearer(fixture.Transport.Requests[2]);
            }
        }

        [Test]
        public void EnrollmentErrors_AreRedacted()
        {
            var transport = new QueueRelayHttpTransport(new RelayHttpResponse(
                401,
                "{\"code\":\"unauthorized\",\"details\":\"tenant-secret-value ticket-secret-value\"}"));
            var client = new RelayHostClient(transport);

            var ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await client.EnrollHostAsync(
                    CreateEnrollmentConfig(),
                    CreateIdentity(),
                    CancellationToken.None));

            Assert.That(ex.Message, Does.Contain("HTTP 401"));
            Assert.That(ex.Message, Does.Contain("unauthorized"));
            Assert.That(ex.Message, Does.Not.Contain("tenant-secret-value"));
            Assert.That(ex.Message, Does.Not.Contain("ticket-secret-value"));
        }

        private static string QueueAuthentication(
            SecureRelayAuthenticatorTests.TestFixture fixture,
            CngKey relayKey,
            RelayTicketPurpose purpose,
            string deviceId,
            int ticketSeed)
        {
            fixture.Transport.Enqueue(SecureRelayAuthenticatorTests.JsonResponse(
                SecureRelayAuthenticatorTests.CreateChallenge(
                    relayKey,
                    fixture.Config,
                    purpose,
                    deviceId,
                    SecureRelayAuthenticatorTests.Now,
                    SecureRelayAuthenticatorTests.Now + 60000)));
            var ticket = SecureRemoteBase64Url.Encode(CreateBytes(32, ticketSeed));
            fixture.Transport.Enqueue(SecureRelayAuthenticatorTests.JsonResponse(new Dictionary<string, object>
            {
                { "ticket", ticket },
                { "purpose", SecureRelayAuthenticator.GetPurposeValue(purpose) },
                { "expiresAt", SecureRelayAuthenticatorTests.Now + 60000 }
            }));
            return ticket;
        }

        private static RelayHostConfig CreateEnrollmentConfig()
        {
            return new RelayHostConfig
            {
                RelayHost = "relay.example.com",
                RelayPort = 443,
                TlsRequired = true,
                TenantId = "tenant_a",
                TenantSecret = "tenant-secret-value"
            };
        }

        private static RemoteEndpointIdentity CreateIdentity()
        {
            return new RemoteEndpointIdentity
            {
                IdentityId = SecureRelayAuthenticatorTests.HostId,
                KeyVersion = 7,
                SigningPublicKey = "signing-public-key",
                AgreementPublicKey = "agreement-public-key"
            };
        }

        private static byte[] CreateBytes(int length, int seed)
        {
            var result = new byte[length];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = (byte)(seed + index);
            }

            return result;
        }

        private static void AssertRuntimeRequestDoesNotUseManagementBearer(RelayHttpRequest request)
        {
            Assert.That(request.Headers["Authorization"], Is.Not.EqualTo("Bearer tenant-secret-value"));
            Assert.That(request.Url, Does.Not.Contain("tenant-secret-value"));
            Assert.That(request.Body ?? string.Empty, Does.Not.Contain("tenant-secret-value"));
        }

        private sealed class QueueRelayHttpTransport : IRelayHttpTransport
        {
            private readonly Queue<RelayHttpResponse> _responses;

            public QueueRelayHttpTransport(params RelayHttpResponse[] responses)
            {
                _responses = new Queue<RelayHttpResponse>(responses);
            }

            public IList<RelayHttpRequest> Requests { get; } = new List<RelayHttpRequest>();

            public Task<RelayHttpResponse> SendAsync(RelayHttpRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(_responses.Dequeue());
            }
        }
    }
}
