using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SecureRemotePairingTests
    {
        [Test]
        public void PairingUri_UsesMyCodexSchemeAndSingleDataParameter()
        {
            var package = VectorPackage();

            var uri = RemoteServicePairingInfoFormatter.FormatCopyText(package);

            Assert.That(uri, Does.StartWith("mycodex://pair?data="));
            Assert.That(uri.IndexOf('&'), Is.EqualTo(-1));
            var encoded = Uri.UnescapeDataString(uri.Substring("mycodex://pair?data=".Length));
            var json = Encoding.UTF8.GetString(SecureRemoteBase64Url.Decode(encoded));
            var root = new JavaScriptSerializer().Deserialize<IDictionary<string, object>>(json);
            Assert.That(root["inviteSecret"], Is.EqualTo(package.InviteSecret));
            Assert.That(root["hostSignature"], Is.EqualTo(package.HostSignature));
        }

        [Test]
        public void PairingPackage_SignatureCoversRelayInviteAndBothHostKeys()
        {
            var vector = SecureRemoteVector.Load();
            var package = VectorPackage();
            var crypto = new CngSecureRemoteCrypto();

            Assert.That(
                crypto.VerifyRawEcdsa(
                    vector.Bytes("publicKeys", "hostSigning"),
                    SecureRemoteTranscript.EncodePairingPackage(package),
                    vector.Bytes("signatures", "hostPairingPackage")),
                Is.True);

            package.HostAgreementPublicKey = vector.String("publicKeys", "deviceAgreement");
            Assert.That(
                crypto.VerifyRawEcdsa(
                    vector.Bytes("publicKeys", "hostSigning"),
                    SecureRemoteTranscript.EncodePairingPackage(package),
                    vector.Bytes("signatures", "hostPairingPackage")),
                Is.False);
        }

        [Test]
        public void PairingShortCode_MatchesVector()
        {
            var vector = SecureRemoteVector.Load();
            Assert.That(
                SecureRemotePairingRules.ComputeShortCode(vector.Bytes("pairing", "claimPlaintext")),
                Is.EqualTo("952372"));
        }

        [Test]
        public void PairingApproval_TranscriptsMatchCanonicalVector()
        {
            var vector = SecureRemoteVector.Load();
            var approval = new PairingApproval
            {
                ProtocolVersion = 1,
                BindingId = vector.String("inputs", "ids", "bindingId"),
                TenantId = vector.String("inputs", "ids", "tenantId"),
                HostId = vector.String("inputs", "ids", "hostId"),
                DeviceId = vector.String("inputs", "ids", "deviceId"),
                HostSigningPublicKey = vector.Bytes("publicKeys", "hostSigning"),
                HostAgreementPublicKey = vector.Bytes("publicKeys", "hostAgreement"),
                HostKeyVersion = 1,
                DeviceSigningPublicKey = vector.Bytes("publicKeys", "deviceSigning"),
                DeviceAgreementPublicKey = vector.Bytes("publicKeys", "deviceAgreement"),
                DeviceKeyVersion = 1,
                RelaySigningPublicKey = vector.Bytes("publicKeys", "relaySigning"),
                RelaySigningKeyFingerprint = vector.Bytes("publicKeys", "relayFingerprintSha256"),
                ApprovedAt = vector.Long("inputs", "unixMilliseconds", "approvedAt"),
                BindingVersion = 1
            };
            var header = new PairingApprovalHeader
            {
                ProtocolVersion = 1,
                ClaimId = vector.String("inputs", "ids", "claimId"),
                InviteId = vector.String("inputs", "ids", "inviteId"),
                TenantId = vector.String("inputs", "ids", "tenantId"),
                HostId = vector.String("inputs", "ids", "hostId"),
                DeviceId = vector.String("inputs", "ids", "deviceId"),
                Status = vector.String("inputs", "strings", "approvalStatus"),
                BindingVersion = 1,
                ApprovalNonce = SecureRemoteBase64Url.Encode(
                    vector.HexBytes("inputs", "approvalNonceHex")),
                CiphertextLength = vector.Bytes("pairing", "approvalCiphertext").Length
            };

            Assert.That(
                SecureRemoteTranscript.EncodePairingApproval(approval),
                Is.EqualTo(vector.Bytes("transcripts", "pairingApproval")));
            Assert.That(
                SecureRemoteTranscript.EncodePairingApprovalHeader(header),
                Is.EqualTo(vector.Bytes("transcripts", "approvalHeader")));
            Assert.That(
                SecureRemoteTranscript.EncodePairingApprovalPlaintext(
                    approval,
                    vector.Bytes("signatures", "hostPairingApproval")),
                Is.EqualTo(vector.Bytes("pairing", "approvalPlaintext")));
        }

        [Test]
        public async Task InviteSecret_IsGeneratedLocallyAndNeverSentToRelay()
        {
            var vector = SecureRemoteVector.Load();
            var relay = new FakeRelayClient(vector);
            var workflow = CreateWorkflow(relay, new FakeBindingStore(), vector);
            var config = CreateConfig(vector);
            config.CertificateSha256 = new string('f', 64);
            config.HostDisplayName = "PC · WSL · Ubuntu development";

            var active = await workflow.CreatePairingInviteAsync(
                config,
                vector.String("inputs", "ids", "hostId"),
                "Windows",
                CancellationToken.None);

            Assert.That(relay.LastInviteRequest, Is.Not.Null);
            Assert.That(
                typeof(CreatePairingInviteRequest).GetProperties().Select(item => item.Name),
                Does.Not.Contain("InviteSecret"));
            Assert.That(
                SecureRemoteBase64Url.Decode(active.Package.InviteSecret),
                Has.Length.EqualTo(32));
            Assert.That(
                active.Package.ExpiresAt,
                Is.EqualTo(vector.Long("inputs", "unixMilliseconds", "approvedAt") + 540000));
            Assert.That(
                active.Package.Relay.EmbeddedTlsCertificateSha256,
                Is.EqualTo(SecureRemoteBase64Url.Encode(Enumerable.Repeat((byte)0xff, 32).ToArray())));
            Assert.That(active.PairingUri, Does.StartWith("mycodex://pair?data="));
            Assert.That(active.Package.HostName, Is.EqualTo(config.HostDisplayName));
            Assert.That(active.Package.HostId, Is.EqualTo(vector.String("inputs", "ids", "hostId")));
        }

        [Test]
        public void CreatePairingInvite_RejectsInvalidOrImplausiblySkewedRelayTime()
        {
            var vector = SecureRemoteVector.Load();
            var invalidRelay = new FakeRelayClient(vector)
            {
                MetadataServerTime = 0
            };
            var skewedRelay = new FakeRelayClient(vector)
            {
                MetadataServerTime =
                    vector.Long("inputs", "unixMilliseconds", "approvedAt") + 86400001
            };

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await CreateWorkflow(
                    invalidRelay,
                    new FakeBindingStore(),
                    vector).CreatePairingInviteAsync(
                        CreateConfig(vector),
                        vector.String("inputs", "ids", "hostId"),
                        "Windows",
                        CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await CreateWorkflow(
                    skewedRelay,
                    new FakeBindingStore(),
                    vector).CreatePairingInviteAsync(
                        CreateConfig(vector),
                        vector.String("inputs", "ids", "hostId"),
                        "Windows",
                        CancellationToken.None));
        }

        [Test]
        public void ClaimVerification_RejectsChangedHeaderCiphertextProofAndSignature()
        {
            var vector = SecureRemoteVector.Load();
            var original = VectorOpaqueClaim();

            var changedHeader = CloneOpaqueClaim(original);
            changedHeader.Header.DeviceId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
            AssertInvalidClaim(vector, changedHeader);

            var changedCiphertext = CloneOpaqueClaim(original);
            var ciphertext = SecureRemoteBase64Url.Decode(changedCiphertext.Ciphertext);
            ciphertext[0] ^= 0x01;
            changedCiphertext.Ciphertext = SecureRemoteBase64Url.Encode(ciphertext);
            AssertInvalidClaim(vector, changedCiphertext);

            var proofPlaintext = vector.Bytes("pairing", "claimPlaintext");
            proofPlaintext[proofPlaintext.Length - 100] ^= 0x01;
            AssertInvalidClaim(vector, WithEncryptedPlaintext(original, proofPlaintext, vector));

            var signaturePlaintext = vector.Bytes("pairing", "claimPlaintext");
            signaturePlaintext[signaturePlaintext.Length - 1] ^= 0x01;
            AssertInvalidClaim(vector, WithEncryptedPlaintext(original, signaturePlaintext, vector));
        }

        [Test]
        public async Task Approval_IsSignedEncryptedAndIdempotent()
        {
            var vector = SecureRemoteVector.Load();
            var relay = new FakeRelayClient(vector)
            {
                FailFirstApproval = true
            };
            var bindings = new FakeBindingStore();
            var workflow = CreateVectorWorkflow(relay, bindings, vector);
            var verified = workflow.VerifyClaim(GetActiveInvite(workflow), VectorOpaqueClaim());
            var config = CreateConfig(vector);

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await workflow.ApprovePairingClaimAsync(
                    config,
                    verified,
                    CancellationToken.None));
            Assert.That(bindings.Active, Is.False);

            await workflow.ApprovePairingClaimAsync(
                config,
                verified,
                CancellationToken.None);

            Assert.That(relay.ApprovalRequests, Has.Count.EqualTo(2));
            Assert.That(
                relay.ApprovalRequests[1],
                Is.SameAs(relay.ApprovalRequests[0]));
            Assert.That(bindings.Active, Is.True);
            Assert.That(bindings.Pending.BindingVersion, Is.EqualTo(1));

            var request = relay.ApprovalRequests[0];
            var encrypted = SecureRemoteBase64Url.Decode(request.ApprovalCiphertext);
            var ciphertext = encrypted.Take(encrypted.Length - 16).ToArray();
            var tag = encrypted.Skip(encrypted.Length - 16).ToArray();
            var plaintext = new CngSecureRemoteCrypto().DecryptAesGcm(
                vector.Bytes("pairing", "approvalKey"),
                SecureRemoteBase64Url.Decode(request.ApprovalNonce),
                ciphertext,
                tag,
                SecureRemoteBase64Url.Decode(request.ApprovalHeader));
            Assert.That(plaintext.Skip(plaintext.Length - 64), Is.All.EqualTo(0x5a));
            Assert.That(request.ApprovalHeader, Is.Not.Empty);
            Assert.That(request.BindingVersion, Is.EqualTo(1));
        }

        private static PairingPackage VectorPackage()
        {
            var vector = SecureRemoteVector.Load();
            return new PairingPackage
            {
                Version = 1,
                Relay = new PairingRelay
                {
                    Url = vector.String("inputs", "strings", "relayUrl"),
                    SigningPublicKey = vector.String("publicKeys", "relaySigning"),
                    SigningKeyFingerprint = vector.String("publicKeys", "relayFingerprintSha256"),
                    EmbeddedTlsCertificateSha256 = string.Empty
                },
                TenantId = vector.String("inputs", "ids", "tenantId"),
                HostId = vector.String("inputs", "ids", "hostId"),
                HostName = vector.String("inputs", "strings", "hostName"),
                InviteId = vector.String("inputs", "ids", "inviteId"),
                InviteSecret = SecureRemoteBase64Url.Encode(vector.HexBytes("inputs", "inviteSecretHex")),
                ExpiresAt = vector.Long("inputs", "unixMilliseconds", "inviteExpiresAt"),
                HostSigningPublicKey = vector.String("publicKeys", "hostSigning"),
                HostAgreementPublicKey = vector.String("publicKeys", "hostAgreement"),
                HostKeyVersion = 1,
                HostSignature = vector.String("signatures", "hostPairingPackage")
            };
        }

        private static SecureRemotePairingWorkflow CreateWorkflow(
            FakeRelayClient relay,
            IRemoteBindingStore bindings,
            SecureRemoteVector vector)
        {
            return new SecureRemotePairingWorkflow(
                relay,
                new FakeIdentityStore(vector),
                bindings,
                new CngSecureRemoteCrypto(),
                () => vector.Long("inputs", "unixMilliseconds", "approvedAt"),
                value =>
                {
                    var source = vector.HexBytes("inputs", "approvalNonceHex");
                    for (var index = 0; index < value.Length; index++)
                    {
                        value[index] = source[index % source.Length];
                    }
                });
        }

        private static SecureRemotePairingWorkflow CreateVectorWorkflow(
            FakeRelayClient relay,
            IRemoteBindingStore bindings,
            SecureRemoteVector vector)
        {
            var workflow = CreateWorkflow(relay, bindings, vector);
            var active = new ActivePairingInvite
            {
                Package = VectorPackage(),
                RelayInvite = new RelayPairingInvite
                {
                    TenantId = vector.String("inputs", "ids", "tenantId"),
                    HostId = vector.String("inputs", "ids", "hostId"),
                    InviteId = vector.String("inputs", "ids", "inviteId"),
                    Status = "pending",
                    ExpiresAt = vector.Long("inputs", "unixMilliseconds", "inviteExpiresAt")
                }
            };
            typeof(SecureRemotePairingWorkflow)
                .GetField("_activeInvite", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(workflow, active);
            return workflow;
        }

        private static ActivePairingInvite GetActiveInvite(SecureRemotePairingWorkflow workflow)
        {
            return (ActivePairingInvite)typeof(SecureRemotePairingWorkflow)
                .GetField("_activeInvite", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(workflow);
        }

        private static OpaquePairingClaim VectorOpaqueClaim()
        {
            var vector = SecureRemoteVector.Load();
            var header = new PairingClaimHeader
            {
                ProtocolVersion = 1,
                InviteId = vector.String("inputs", "ids", "inviteId"),
                TenantId = vector.String("inputs", "ids", "tenantId"),
                HostId = vector.String("inputs", "ids", "hostId"),
                ClaimId = vector.String("inputs", "ids", "claimId"),
                DeviceId = vector.String("inputs", "ids", "deviceId"),
                DeviceAgreementPublicKey = vector.String("publicKeys", "deviceAgreement"),
                DeviceKeyVersion = 1,
                ClientNonce = SecureRemoteBase64Url.Encode(vector.HexBytes("inputs", "clientNonceHex")),
                CiphertextLength = vector.Bytes("pairing", "claimCiphertext").Length
            };
            return new OpaquePairingClaim
            {
                ClaimId = header.ClaimId,
                InviteId = header.InviteId,
                TenantId = header.TenantId,
                HostId = header.HostId,
                Status = "pending",
                Header = header,
                Nonce = SecureRemoteBase64Url.Encode(vector.HexBytes("inputs", "claimNonceHex")),
                Ciphertext = vector.String("pairing", "claimCiphertext"),
                CreatedAt = vector.Long("inputs", "unixMilliseconds", "claimCreatedAt"),
                ExpiresAt = vector.Long("inputs", "unixMilliseconds", "inviteExpiresAt")
            };
        }

        private static OpaquePairingClaim CloneOpaqueClaim(OpaquePairingClaim value)
        {
            return new OpaquePairingClaim
            {
                ClaimId = value.ClaimId,
                InviteId = value.InviteId,
                TenantId = value.TenantId,
                HostId = value.HostId,
                Status = value.Status,
                Nonce = value.Nonce,
                Ciphertext = value.Ciphertext,
                CreatedAt = value.CreatedAt,
                ExpiresAt = value.ExpiresAt,
                Header = new PairingClaimHeader
                {
                    ProtocolVersion = value.Header.ProtocolVersion,
                    InviteId = value.Header.InviteId,
                    TenantId = value.Header.TenantId,
                    HostId = value.Header.HostId,
                    ClaimId = value.Header.ClaimId,
                    DeviceId = value.Header.DeviceId,
                    DeviceAgreementPublicKey = value.Header.DeviceAgreementPublicKey,
                    DeviceKeyVersion = value.Header.DeviceKeyVersion,
                    ClientNonce = value.Header.ClientNonce,
                    CiphertextLength = value.Header.CiphertextLength
                }
            };
        }

        private static OpaquePairingClaim WithEncryptedPlaintext(
            OpaquePairingClaim original,
            byte[] plaintext,
            SecureRemoteVector vector)
        {
            var result = CloneOpaqueClaim(original);
            byte[] tag;
            var ciphertext = new CngSecureRemoteCrypto().EncryptAesGcm(
                vector.Bytes("pairing", "claimKey"),
                vector.HexBytes("inputs", "claimNonceHex"),
                plaintext,
                SecureRemoteTranscript.EncodeVisiblePairingClaimHeader(result.Header),
                out tag);
            result.Ciphertext = SecureRemoteBase64Url.Encode(ciphertext.Concat(tag).ToArray());
            return result;
        }

        private static void AssertInvalidClaim(
            SecureRemoteVector vector,
            OpaquePairingClaim claim)
        {
            var workflow = CreateVectorWorkflow(
                new FakeRelayClient(vector),
                new FakeBindingStore(),
                vector);
            Assert.Throws<InvalidOperationException>(
                () => workflow.VerifyClaim(GetActiveInvite(workflow), claim));
        }

        private static RelayHostConfig CreateConfig(SecureRemoteVector vector)
        {
            return new RelayHostConfig
            {
                RelayHost = "relay.example.com",
                RelayPort = 443,
                TlsRequired = true,
                TenantId = vector.String("inputs", "ids", "tenantId"),
                RelaySigningPublicKey = vector.String("publicKeys", "relaySigning"),
                RelaySigningKeyFingerprint = vector.String("publicKeys", "relayFingerprintSha256")
            };
        }

        private sealed class FakeIdentityStore : IRemoteIdentityStore
        {
            private readonly SecureRemoteVector _vector;

            public FakeIdentityStore(SecureRemoteVector vector)
            {
                _vector = vector;
            }

            public RemoteEndpointIdentity LoadOrCreateHostIdentity(string hostId)
            {
                return new RemoteEndpointIdentity
                {
                    IdentityId = hostId,
                    KeyVersion = 1,
                    SigningPublicKey = _vector.String("publicKeys", "hostSigning"),
                    AgreementPublicKey = _vector.String("publicKeys", "hostAgreement")
                };
            }

            public byte[] Sign(byte[] transcript)
            {
                return Enumerable.Repeat((byte)0x5a, 64).ToArray();
            }

            public byte[] DeriveAgreement(byte[] peerPublicKey)
            {
                return _vector.Bytes("pairing", "ecdhSharedSecret");
            }
        }

        private sealed class FakeBindingStore : IRemoteBindingStore
        {
            public RemoteDeviceBinding Pending { get; private set; }

            public bool Active { get; private set; }

            public IReadOnlyList<RemoteDeviceBinding> ListActive(string tenantId, string hostId)
            {
                return Active && Pending != null
                    ? new[] { Pending }
                    : new RemoteDeviceBinding[0];
            }

            public RemoteDeviceBinding FindActive(string tenantId, string hostId, string deviceId)
            {
                return Active ? Pending : null;
            }

            public void SavePending(RemoteDeviceBinding binding)
            {
                binding.BindingVersion = 1;
                Pending = binding;
            }

            public void Activate(string claimId, int bindingVersion)
            {
                Assert.That(claimId, Is.EqualTo(Pending.ClaimId));
                Assert.That(bindingVersion, Is.EqualTo(Pending.BindingVersion));
                Active = true;
            }

            public void Revoke(string tenantId, string hostId, string deviceId)
            {
                Active = false;
            }
        }

        private sealed class FakeRelayClient : IRelayHostClient
        {
            private readonly SecureRemoteVector _vector;

            public FakeRelayClient(SecureRemoteVector vector)
            {
                _vector = vector;
                MetadataServerTime = vector.Long("inputs", "unixMilliseconds", "approvedAt");
            }

            public CreatePairingInviteRequest LastInviteRequest { get; private set; }

            public List<PairingApprovalRequest> ApprovalRequests { get; } =
                new List<PairingApprovalRequest>();

            public bool FailFirstApproval { get; set; }

            public long MetadataServerTime { get; set; }

            public Task<RelayMetadata> GetMetadataAsync(RelayHostConfig config, CancellationToken cancellationToken)
            {
                config.RelaySigningPublicKey = _vector.String("publicKeys", "relaySigning");
                config.RelaySigningKeyFingerprint = _vector.String("publicKeys", "relayFingerprintSha256");
                return Task.FromResult(new RelayMetadata
                {
                    ProtocolVersion = 1,
                    SigningPublicKey = config.RelaySigningPublicKey,
                    SigningKeyFingerprint = config.RelaySigningKeyFingerprint,
                    ServerTime = MetadataServerTime
                });
            }

            public Task<RelayHostRegistrationResult> EnrollHostAsync(RelayHostConfig config, RemoteEndpointIdentity identity, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public Task<RelayPairingInvite> CreatePairingInviteAsync(RelayHostConfig config, CreatePairingInviteRequest request, CancellationToken cancellationToken)
            {
                LastInviteRequest = request;
                return Task.FromResult(new RelayPairingInvite
                {
                    TenantId = request.TenantId,
                    HostId = request.HostId,
                    InviteId = request.InviteId,
                    Status = "pending",
                    ExpiresAt = request.ExpiresAt
                });
            }

            public Task CancelPairingInviteAsync(RelayHostConfig config, string inviteId, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<OpaquePairingClaim>> ListPairingClaimsAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
            {
                return Task.FromResult((IReadOnlyList<OpaquePairingClaim>)new OpaquePairingClaim[0]);
            }

            public Task ApprovePairingClaimAsync(RelayHostConfig config, PairingApprovalRequest request, CancellationToken cancellationToken)
            {
                ApprovalRequests.Add(request);
                if (FailFirstApproval && ApprovalRequests.Count == 1)
                {
                    throw new InvalidOperationException("response lost");
                }

                return Task.CompletedTask;
            }

            public Task RejectPairingClaimAsync(RelayHostConfig config, string hostId, string claimId, string deviceId, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public Task<IReadOnlyList<RelayDeviceSummary>> ListDevicesAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken)
            {
                return Task.FromResult((IReadOnlyList<RelayDeviceSummary>)new RelayDeviceSummary[0]);
            }

            public Task<bool> RevokeDeviceAsync(RelayHostConfig config, string hostId, string deviceId, CancellationToken cancellationToken)
            {
                return Task.FromResult(true);
            }
        }
    }
}
