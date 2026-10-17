using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class SecureRelayAuthenticatorTests
    {
        internal const long Now = 1784870400000L;
        internal const string HostId = "11111111-1111-4111-8111-111111111111";

        [Test]
        public async Task GetTicketAsync_VerifiesRelayAndSignsRawHostProof()
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = CreateFixture(relayKey);
                var challenge = CreateChallenge(relayKey, fixture.Config, RelayTicketPurpose.DeviceList, string.Empty, Now, Now + 60000);
                fixture.Transport.Enqueue(JsonResponse(challenge));
                fixture.Transport.Enqueue(JsonResponse(new Dictionary<string, object>
                {
                    { "ticket", SecureRemoteBase64Url.Encode(CreateBytes(32, 80)) },
                    { "purpose", "device_list" },
                    { "expiresAt", Now + 60000 }
                }));

                var ticket = await fixture.Authenticator.GetTicketAsync(
                    fixture.Config,
                    RelayTicketPurpose.DeviceList,
                    HostId,
                    string.Empty,
                    CancellationToken.None);

                Assert.That(ticket, Is.EqualTo(SecureRemoteBase64Url.Encode(CreateBytes(32, 80))));
                Assert.That(fixture.Transport.Requests, Has.Count.EqualTo(2));
                Assert.That(fixture.Transport.Requests[0].Url, Does.EndWith("/v1/auth/challenges"));
                Assert.That(fixture.Transport.Requests[0].Headers.ContainsKey("Authorization"), Is.False);
                Assert.That(fixture.Transport.Requests[0].Body, Does.Contain("\"subjectType\":\"host\""));
                Assert.That(fixture.Transport.Requests[0].Body, Does.Contain("\"subjectId\":\"" + HostId + "\""));
                Assert.That(fixture.Transport.Requests[0].Body, Does.Contain("\"purpose\":\"device_list\""));
                Assert.That(fixture.Transport.Requests[1].Url, Does.EndWith("/v1/auth/prove"));
                Assert.That(fixture.Transport.Requests[1].Headers.ContainsKey("Authorization"), Is.False);

                var proof = Deserialize(fixture.Transport.Requests[1].Body);
                Assert.That(proof["challenge"], Is.TypeOf<Dictionary<string, object>>());
                Assert.That(
                    Convert.ToString(proof["subjectSignature"]),
                    Is.EqualTo(SecureRemoteBase64Url.Encode(fixture.IdentityStore.Signature)));
                Assert.That(fixture.IdentityStore.Signature, Has.Length.EqualTo(64));
                Assert.That(
                    fixture.IdentityStore.LastTranscript,
                    Is.EqualTo(SecureRemoteTranscript.EncodeRelayProof(challenge)));
            }
        }

        [Test]
        public async Task GetTicketAsync_AcceptsSmallRelayClockLead()
        {
            const long relayClockLead = 2500;
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = CreateFixture(relayKey);
                var challenge = CreateChallenge(
                    relayKey,
                    fixture.Config,
                    RelayTicketPurpose.DeviceList,
                    string.Empty,
                    Now + relayClockLead,
                    Now + relayClockLead + 60000);
                fixture.Transport.Enqueue(JsonResponse(challenge));
                fixture.Transport.Enqueue(JsonResponse(new Dictionary<string, object>
                {
                    { "ticket", SecureRemoteBase64Url.Encode(CreateBytes(32, 80)) },
                    { "purpose", "device_list" },
                    { "expiresAt", Now + relayClockLead + 60000 }
                }));

                var ticket = await fixture.Authenticator.GetTicketAsync(
                    fixture.Config,
                    RelayTicketPurpose.DeviceList,
                    HostId,
                    string.Empty,
                    CancellationToken.None);

                Assert.That(ticket, Is.EqualTo(SecureRemoteBase64Url.Encode(CreateBytes(32, 80))));
            }
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void GetTicketAsync_RejectsWrongRelayIdentityOrSignature(bool wrongFingerprint, bool wrongSignature)
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = CreateFixture(relayKey);
                var challenge = CreateChallenge(relayKey, fixture.Config, RelayTicketPurpose.DeviceList, string.Empty, Now, Now + 60000);
                if (wrongFingerprint)
                {
                    challenge.RelaySigningKeyFingerprint = SecureRemoteBase64Url.Encode(new byte[32]);
                }

                if (wrongSignature)
                {
                    challenge.RelaySignature = SecureRemoteBase64Url.Encode(new byte[64]);
                }

                fixture.Transport.Enqueue(JsonResponse(challenge));

                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await fixture.Authenticator.GetTicketAsync(
                        fixture.Config,
                        RelayTicketPurpose.DeviceList,
                        HostId,
                        string.Empty,
                        CancellationToken.None));
                Assert.That(fixture.IdentityStore.LastTranscript, Is.Null);
            }
        }

        [Test]
        public void GetTicketAsync_RejectsConfiguredPublicKeyFingerprintMismatchBeforeHostProof()
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            using (var differentRelayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = CreateFixture(relayKey);
                fixture.Config.RelaySigningPublicKey = SecureRemoteBase64Url.Encode(
                    CngSecureRemoteCrypto.ExportSec1PublicKey(differentRelayKey));
                fixture.Transport.Enqueue(JsonResponse(CreateChallenge(
                    relayKey,
                    fixture.Config,
                    RelayTicketPurpose.DeviceList,
                    string.Empty,
                    Now,
                    Now + 60000)));

                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await fixture.Authenticator.GetTicketAsync(
                        fixture.Config,
                        RelayTicketPurpose.DeviceList,
                        HostId,
                        string.Empty,
                        CancellationToken.None));
                Assert.That(fixture.IdentityStore.LastTranscript, Is.Null);
            }
        }

        [TestCase("protocolVersion")]
        [TestCase("issuedAt")]
        [TestCase("expiresAt")]
        public void GetTicketAsync_RejectsCoercedProtocolIntegers(string field)
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = CreateFixture(relayKey);
                var challenge = CreateChallenge(
                    relayKey,
                    fixture.Config,
                    RelayTicketPurpose.DeviceList,
                    string.Empty,
                    Now,
                    Now + 60000);
                var json = JsonResponse(challenge).Body;
                if (string.Equals(field, "protocolVersion", StringComparison.Ordinal))
                {
                    json = json.Replace("\"protocolVersion\":1", "\"protocolVersion\":1.0");
                }
                else if (string.Equals(field, "issuedAt", StringComparison.Ordinal))
                {
                    json = json.Replace("\"issuedAt\":" + Now, "\"issuedAt\":\"" + Now + "\"");
                }
                else
                {
                    json = json.Replace(
                        "\"expiresAt\":" + (Now + 60000),
                        "\"expiresAt\":" + (Now + 60000) + ".0");
                }

                fixture.Transport.Enqueue(new RelayHttpResponse(200, json));

                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await fixture.Authenticator.GetTicketAsync(
                        fixture.Config,
                        RelayTicketPurpose.DeviceList,
                        HostId,
                        string.Empty,
                        CancellationToken.None));
                Assert.That(fixture.IdentityStore.LastTranscript, Is.Null);
            }
        }

        [TestCase("device_list", "22222222-2222-4222-8222-222222222222", "", "scope")]
        [TestCase("device_list", HostId, "device-a", "target")]
        [TestCase("device_revoke", HostId, "", "purpose")]
        public void GetTicketAsync_RejectsChangedSubjectTargetOrPurpose(
            string challengePurpose,
            string challengeSubjectId,
            string challengeDeviceId,
            string description)
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = CreateFixture(relayKey);
                var challenge = CreateChallenge(
                    relayKey,
                    fixture.Config,
                    challengePurpose,
                    challengeSubjectId,
                    challengeDeviceId,
                    Now,
                    Now + 60000);
                fixture.Transport.Enqueue(JsonResponse(challenge));

                var ex = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await fixture.Authenticator.GetTicketAsync(
                        fixture.Config,
                        RelayTicketPurpose.DeviceList,
                        HostId,
                        string.Empty,
                        CancellationToken.None),
                    description);
                Assert.That(ex.Message, Does.Not.Contain(fixture.Config.TenantSecret));
                Assert.That(fixture.IdentityStore.LastTranscript, Is.Null);
            }
        }

        [TestCase(1784870039999L, 1784870099999L, "expired")]
        [TestCase(1784870400000L, 1784870460001L, "overlong")]
        [TestCase(1784870700001L, 1784870760001L, "future")]
        public void GetTicketAsync_RejectsInvalidChallengeLifetime(long issuedAt, long expiresAt, string description)
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = CreateFixture(relayKey);
                fixture.Transport.Enqueue(JsonResponse(CreateChallenge(
                    relayKey,
                    fixture.Config,
                    RelayTicketPurpose.DeviceList,
                    string.Empty,
                    issuedAt,
                    expiresAt)));

                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await fixture.Authenticator.GetTicketAsync(
                        fixture.Config,
                        RelayTicketPurpose.DeviceList,
                        HostId,
                        string.Empty,
                        CancellationToken.None),
                    description);
                Assert.That(fixture.IdentityStore.LastTranscript, Is.Null);
            }
        }

        [TestCase("device_revoke", 1784870460000L, "purpose")]
        [TestCase("device_list", 1784870760001L, "lifetime")]
        [TestCase("device_list", 1784870099999L, "expired")]
        public void GetTicketAsync_RejectsInvalidTicketScopeOrLifetime(
            string ticketPurpose,
            long ticketExpiresAt,
            string description)
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = CreateFixture(relayKey);
                fixture.Transport.Enqueue(JsonResponse(CreateChallenge(
                    relayKey,
                    fixture.Config,
                    RelayTicketPurpose.DeviceList,
                    string.Empty,
                    Now,
                    Now + 60000)));
                fixture.Transport.Enqueue(JsonResponse(new Dictionary<string, object>
                {
                    { "ticket", SecureRemoteBase64Url.Encode(CreateBytes(32, 80)) },
                    { "purpose", ticketPurpose },
                    { "expiresAt", ticketExpiresAt }
                }));

                Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await fixture.Authenticator.GetTicketAsync(
                        fixture.Config,
                        RelayTicketPurpose.DeviceList,
                        HostId,
                        string.Empty,
                        CancellationToken.None),
                    description);
            }
        }

        [Test]
        public void GetTicketAsync_RedactsRelayErrorBodies()
        {
            using (var relayKey = CngKey.Create(CngAlgorithm.ECDsaP256))
            {
                var fixture = CreateFixture(relayKey);
                var secretBody = fixture.Config.TenantSecret + " " +
                    SecureRemoteBase64Url.Encode(CreateBytes(32, 30)) + " ticket-secret-marker";
                fixture.Transport.Enqueue(new RelayHttpResponse(401, "{\"code\":\"unauthorized\",\"details\":\"" + secretBody + "\"}"));

                var ex = Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await fixture.Authenticator.GetTicketAsync(
                        fixture.Config,
                        RelayTicketPurpose.DeviceList,
                        HostId,
                        string.Empty,
                        CancellationToken.None));

                Assert.That(ex.Message, Does.Contain("HTTP 401"));
                Assert.That(ex.Message, Does.Contain("unauthorized"));
                Assert.That(ex.Message, Does.Not.Contain(fixture.Config.TenantSecret));
                Assert.That(ex.Message, Does.Not.Contain("ticket-secret-marker"));
            }
        }

        internal static AuthChallenge CreateChallenge(
            CngKey relayKey,
            RelayHostConfig config,
            RelayTicketPurpose purpose,
            string deviceId,
            long issuedAt,
            long expiresAt)
        {
            return CreateChallenge(
                relayKey,
                config,
                SecureRelayAuthenticator.GetPurposeValue(purpose),
                HostId,
                deviceId,
                issuedAt,
                expiresAt);
        }

        private static AuthChallenge CreateChallenge(
            CngKey relayKey,
            RelayHostConfig config,
            string purpose,
            string subjectId,
            string deviceId,
            long issuedAt,
            long expiresAt)
        {
            var challenge = new AuthChallenge
            {
                ProtocolVersion = 1,
                RelaySigningKeyFingerprint = config.RelaySigningKeyFingerprint,
                ChallengeId = "challenge-a",
                Challenge = SecureRemoteBase64Url.Encode(CreateBytes(32, 10)),
                SubjectType = "host",
                SubjectId = subjectId,
                TenantId = config.TenantId,
                HostId = HostId,
                DeviceId = deviceId,
                Purpose = purpose,
                IssuedAt = issuedAt,
                ExpiresAt = expiresAt
            };
            using (var signer = new ECDsaCng(relayKey))
            {
                signer.HashAlgorithm = CngAlgorithm.Sha256;
                challenge.RelaySignature = SecureRemoteBase64Url.Encode(
                    signer.SignData(SecureRemoteTranscript.EncodeRelayChallenge(challenge)));
            }

            return challenge;
        }

        internal static TestFixture CreateFixture(CngKey relayKey)
        {
            var relayPublicKey = CngSecureRemoteCrypto.ExportSec1PublicKey(relayKey);
            byte[] relayFingerprint;
            using (var sha256 = SHA256.Create())
            {
                relayFingerprint = sha256.ComputeHash(relayPublicKey);
            }

            var transport = new FakeRelayHttpTransport();
            var identityStore = new FakeRemoteIdentityStore();
            var config = new RelayHostConfig
            {
                RelayHost = "relay.example.com",
                RelayPort = 443,
                TlsRequired = true,
                TenantId = "tenant_a",
                TenantSecret = "tenant-secret-value",
                RelaySigningPublicKey = SecureRemoteBase64Url.Encode(relayPublicKey),
                RelaySigningKeyFingerprint = SecureRemoteBase64Url.Encode(relayFingerprint)
            };
            return new TestFixture
            {
                Transport = transport,
                IdentityStore = identityStore,
                Config = config,
                Authenticator = new SecureRelayAuthenticator(transport, identityStore, () => Now)
            };
        }

        internal static RelayHttpResponse JsonResponse(object value)
        {
            var challenge = value as AuthChallenge;
            if (challenge != null)
            {
                value = CreateChallengePayload(challenge);
            }

            return new RelayHttpResponse(200, new JavaScriptSerializer().Serialize(value));
        }

        private static IDictionary<string, object> CreateChallengePayload(AuthChallenge challenge)
        {
            return new Dictionary<string, object>
            {
                { "protocolVersion", challenge.ProtocolVersion },
                { "relayFingerprint", challenge.RelaySigningKeyFingerprint },
                { "challengeId", challenge.ChallengeId },
                { "challenge", challenge.Challenge },
                { "subjectType", challenge.SubjectType },
                { "subjectId", challenge.SubjectId },
                { "tenantId", challenge.TenantId },
                { "hostId", challenge.HostId },
                { "deviceId", challenge.DeviceId },
                { "purpose", challenge.Purpose },
                { "issuedAt", challenge.IssuedAt },
                { "expiresAt", challenge.ExpiresAt },
                { "relaySignature", challenge.RelaySignature }
            };
        }

        private static IDictionary<string, object> Deserialize(string json)
        {
            return new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
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

        internal sealed class TestFixture
        {
            public SecureRelayAuthenticator Authenticator { get; set; }

            public RelayHostConfig Config { get; set; }

            public FakeRemoteIdentityStore IdentityStore { get; set; }

            public FakeRelayHttpTransport Transport { get; set; }
        }

        internal sealed class FakeRemoteIdentityStore : IRemoteIdentityStore
        {
            public FakeRemoteIdentityStore()
            {
                Signature = CreateBytes(64, 120);
            }

            public byte[] Signature { get; private set; }

            public byte[] LastTranscript { get; private set; }

            public RemoteEndpointIdentity LoadOrCreateHostIdentity(string hostId)
            {
                return new RemoteEndpointIdentity { IdentityId = hostId };
            }

            public byte[] Sign(byte[] transcript)
            {
                LastTranscript = (byte[])transcript.Clone();
                return (byte[])Signature.Clone();
            }

            public byte[] DeriveAgreement(byte[] peerPublicKey)
            {
                throw new NotSupportedException();
            }
        }

        internal sealed class FakeRelayHttpTransport : IRelayHttpTransport
        {
            private readonly Queue<RelayHttpResponse> _responses = new Queue<RelayHttpResponse>();

            public IList<RelayHttpRequest> Requests { get; } = new List<RelayHttpRequest>();

            public void Enqueue(RelayHttpResponse response)
            {
                _responses.Enqueue(response);
            }

            public Task<RelayHttpResponse> SendAsync(RelayHttpRequest request, CancellationToken cancellationToken)
            {
                Requests.Add(request);
                return Task.FromResult(_responses.Dequeue());
            }
        }
    }
}
