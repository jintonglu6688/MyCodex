using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SecureRemoteProtocolTests
    {
        [Test]
        public void TranscriptVector_UsesExactBytes()
        {
            var vector = SecureRemoteVector.Load();
            var package = CreatePairingPackage(vector);
            var claim = CreatePairingClaim(vector);
            var header = CreatePairingClaimHeader(vector);
            var challenge = CreateChallenge(vector);
            var clientHello = CreateClientHello(vector);
            var serverHello = CreateServerHello(vector);
            var androidEnvelope = CreateEnvelope(vector, "androidToWindows");
            var windowsEnvelope = CreateEnvelope(vector, "windowsToAndroid");

            Assert.That(
                SecureRemoteTranscript.EncodePairingPackage(package),
                Is.EqualTo(vector.Bytes("transcripts", "pairingPackage")));
            Assert.That(
                SecureRemoteTranscript.EncodePairingClaim(claim),
                Is.EqualTo(vector.Bytes("transcripts", "pairingClaimWithoutProof")));
            Assert.That(
                SecureRemoteTranscript.EncodeVisiblePairingClaimHeader(header),
                Is.EqualTo(vector.Bytes("transcripts", "visibleClaimHeader")));
            Assert.That(
                SecureRemoteTranscript.EncodeRelayChallenge(challenge),
                Is.EqualTo(vector.Bytes("transcripts", "relayChallenge")));
            Assert.That(
                SecureRemoteTranscript.EncodeSessionHello(clientHello),
                Is.EqualTo(vector.Bytes("transcripts", "sessionClientHello")));
            Assert.That(
                SecureRemoteTranscript.EncodeSessionHello(serverHello),
                Is.EqualTo(vector.Bytes("transcripts", "sessionServerHelloUnsigned")));
            Assert.That(
                SecureRemoteTranscript.EncodeServerHelloSignature(clientHello, serverHello),
                Is.EqualTo(vector.Bytes("transcripts", "sessionServerHello")));
            Assert.That(
                SecureRemoteTranscript.EncodeCompleteSessionHello(clientHello, serverHello),
                Is.EqualTo(vector.Bytes("transcripts", "sessionComplete")));
            Assert.That(
                SecureRemoteTranscript.EncodeConfirmationTranscript(
                    vector.Bytes("transcripts", "sessionComplete"),
                    SecureRelayRoles.Android),
                Is.EqualTo(vector.Bytes("transcripts", "sessionConfirmAndroid")));
            Assert.That(
                SecureRemoteTranscript.EncodeConfirmationTranscript(
                    vector.Bytes("transcripts", "sessionComplete"),
                    SecureRelayRoles.Windows),
                Is.EqualTo(vector.Bytes("transcripts", "sessionConfirmWindows")));
            Assert.That(
                SecureRemoteTranscript.EncodeEnvelopeAad(androidEnvelope),
                Is.EqualTo(vector.Bytes("transcripts", "envelopeAadAndroidToWindows")));
            Assert.That(
                SecureRemoteTranscript.EncodeEnvelopeAad(windowsEnvelope),
                Is.EqualTo(vector.Bytes("transcripts", "envelopeAadWindowsToAndroid")));
        }

        [Test]
        public void ShortCodeVector_MatchesSixDigits()
        {
            var vector = SecureRemoteVector.Load();

            var actual = SecureRemotePairingRules.ComputeShortCode(
                vector.Bytes("pairing", "claimPlaintext"));

            Assert.That(actual, Is.EqualTo(vector.String("pairing", "pairingCode")));
            Assert.That(actual, Does.Match("^[0-9]{6}$"));
        }

        [Test]
        public void WireDtos_SerializeBinaryFieldsAsUnpaddedBase64UrlStrings()
        {
            var vector = SecureRemoteVector.Load();

            AssertBinaryJsonStrings(
                CreatePairingPackage(vector),
                "relay.signingPublicKey",
                "relay.signingKeyFingerprint",
                "relay.embeddedTlsCertificateSha256",
                "inviteSecret",
                "hostSigningPublicKey",
                "hostAgreementPublicKey",
                "hostSignature");
            AssertBinaryJsonStrings(
                CreatePairingClaimHeader(vector),
                "deviceAgreementPublicKey",
                "clientNonce");
            AssertBinaryJsonStrings(
                CreateChallenge(vector),
                "relayFingerprint",
                "challenge",
                "relaySignature");
            AssertBinaryJsonStrings(
                CreateClientHello(vector),
                "ephemeralPublicKey",
                "nonce",
                "signature");
            AssertBinaryJsonStrings(
                CreateEnvelope(vector, "androidToWindows"),
                "nonce",
                "ciphertext");
            AssertBinaryJsonStrings(
                CreateEnvelope(vector, "windowsToAndroid"),
                "nonce",
                "ciphertext");
        }

        [Test]
        public void Base64Url_StrictDecoderRoundTripsCanonicalValues()
        {
            var vector = SecureRemoteVector.Load();
            var encoded = vector.String("publicKeys", "hostSigning");

            Assert.That(
                SecureRemoteBase64Url.Encode(SecureRemoteBase64Url.Decode(encoded)),
                Is.EqualTo(encoded));
            Assert.That(SecureRemoteBase64Url.Decode(string.Empty), Is.Empty);
        }

        [TestCase("AA==")]
        [TestCase("A+")]
        [TestCase("A/")]
        [TestCase("A")]
        [TestCase("_x")]
        [TestCase("AA ")]
        public void Base64Url_StrictDecoderRejectsNonCanonicalValues(string value)
        {
            Assert.Throws<FormatException>(() => SecureRemoteBase64Url.Decode(value));
        }

        private static PairingPackage CreatePairingPackage(SecureRemoteVector vector)
        {
            return new PairingPackage
            {
                Version = vector.Long("protocolVersion"),
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
                InviteSecret = SecureRemoteBase64Url.Encode(
                    vector.HexBytes("inputs", "inviteSecretHex")),
                ExpiresAt = vector.Long("inputs", "unixMilliseconds", "inviteExpiresAt"),
                HostSigningPublicKey = vector.String("publicKeys", "hostSigning"),
                HostAgreementPublicKey = vector.String("publicKeys", "hostAgreement"),
                HostKeyVersion = 1,
                HostSignature = vector.String("signatures", "hostPairingPackage")
            };
        }

        private static PairingClaim CreatePairingClaim(SecureRemoteVector vector)
        {
            return new PairingClaim
            {
                ProtocolVersion = vector.Long("protocolVersion"),
                ClaimId = vector.String("inputs", "ids", "claimId"),
                InviteId = vector.String("inputs", "ids", "inviteId"),
                TenantId = vector.String("inputs", "ids", "tenantId"),
                HostId = vector.String("inputs", "ids", "hostId"),
                DeviceId = vector.String("inputs", "ids", "deviceId"),
                DeviceName = vector.String("inputs", "strings", "deviceName"),
                Platform = vector.String("inputs", "strings", "platform"),
                AppVersion = vector.String("inputs", "strings", "appVersion"),
                DeviceSigningPublicKey = vector.Bytes("publicKeys", "deviceSigning"),
                DeviceAgreementPublicKey = vector.Bytes("publicKeys", "deviceAgreement"),
                DeviceKeyVersion = 1,
                ClientNonce = vector.HexBytes("inputs", "clientNonceHex"),
                CreatedAt = vector.Long("inputs", "unixMilliseconds", "claimCreatedAt"),
                InviteProof = vector.Bytes("pairing", "inviteProof"),
                DeviceSignature = vector.Bytes("signatures", "devicePairingClaim")
            };
        }

        private static PairingClaimHeader CreatePairingClaimHeader(SecureRemoteVector vector)
        {
            return new PairingClaimHeader
            {
                ProtocolVersion = vector.Long("protocolVersion"),
                InviteId = vector.String("inputs", "ids", "inviteId"),
                TenantId = vector.String("inputs", "ids", "tenantId"),
                HostId = vector.String("inputs", "ids", "hostId"),
                ClaimId = vector.String("inputs", "ids", "claimId"),
                DeviceId = vector.String("inputs", "ids", "deviceId"),
                DeviceAgreementPublicKey = vector.String("publicKeys", "deviceAgreement"),
                DeviceKeyVersion = 1,
                ClientNonce = SecureRemoteBase64Url.Encode(
                    vector.HexBytes("inputs", "clientNonceHex")),
                CiphertextLength = vector.Bytes("pairing", "claimCiphertext").Length
            };
        }

        private static AuthChallenge CreateChallenge(SecureRemoteVector vector)
        {
            var deviceId = vector.String("inputs", "ids", "deviceId");
            return new AuthChallenge
            {
                ProtocolVersion = vector.Long("protocolVersion"),
                RelaySigningKeyFingerprint = vector.String("publicKeys", "relayFingerprintSha256"),
                ChallengeId = vector.String("inputs", "ids", "challengeId"),
                Challenge = SecureRemoteBase64Url.Encode(
                    vector.HexBytes("inputs", "challengeValueHex")),
                SubjectType = "device",
                SubjectId = deviceId,
                TenantId = vector.String("inputs", "ids", "tenantId"),
                HostId = vector.String("inputs", "ids", "hostId"),
                DeviceId = deviceId,
                Purpose = vector.String("inputs", "strings", "challengePurpose"),
                IssuedAt = vector.Long("inputs", "unixMilliseconds", "challengeIssuedAt"),
                ExpiresAt = vector.Long("inputs", "unixMilliseconds", "challengeExpiresAt"),
                RelaySignature = vector.String("signatures", "relayChallenge")
            };
        }

        private static SessionHello CreateClientHello(SecureRemoteVector vector)
        {
            return new SessionHello
            {
                ProtocolVersion = vector.Long("protocolVersion"),
                FrameType = SecureRelayFrameTypes.ClientHello,
                SessionId = vector.String("inputs", "ids", "sessionId"),
                TenantId = vector.String("inputs", "ids", "tenantId"),
                HostId = vector.String("inputs", "ids", "hostId"),
                DeviceId = vector.String("inputs", "ids", "deviceId"),
                Direction = RelayEnvelopeDirections.MobileToWindows,
                BindingVersion = 1,
                KeyVersion = 1,
                EphemeralPublicKey = vector.String("publicKeys", "sessionClientEphemeral"),
                Nonce = SecureRemoteBase64Url.Encode(
                    vector.HexBytes("inputs", "sessionClientNonceHex")),
                CreatedAt = vector.Long("inputs", "unixMilliseconds", "clientHelloCreatedAt"),
                Signature = vector.String("signatures", "deviceSessionHello")
            };
        }

        private static SessionHello CreateServerHello(SecureRemoteVector vector)
        {
            return new SessionHello
            {
                ProtocolVersion = vector.Long("protocolVersion"),
                FrameType = SecureRelayFrameTypes.ServerHello,
                SessionId = vector.String("inputs", "ids", "sessionId"),
                TenantId = vector.String("inputs", "ids", "tenantId"),
                HostId = vector.String("inputs", "ids", "hostId"),
                DeviceId = vector.String("inputs", "ids", "deviceId"),
                Direction = RelayEnvelopeDirections.WindowsToMobile,
                BindingVersion = 1,
                KeyVersion = 1,
                EphemeralPublicKey = vector.String("publicKeys", "sessionServerEphemeral"),
                Nonce = SecureRemoteBase64Url.Encode(
                    vector.HexBytes("inputs", "sessionServerNonceHex")),
                CreatedAt = vector.Long("inputs", "unixMilliseconds", "serverHelloCreatedAt"),
                Signature = vector.String("signatures", "hostSessionHello")
            };
        }

        private static SecureRelayEnvelope CreateEnvelope(SecureRemoteVector vector, string direction)
        {
            var source = "envelopes";
            return new SecureRelayEnvelope
            {
                ProtocolVersion = vector.Long("protocolVersion"),
                FrameType = vector.String(source, direction, "frameType"),
                SessionId = vector.String("inputs", "ids", "sessionId"),
                TenantId = vector.String("inputs", "ids", "tenantId"),
                HostId = vector.String("inputs", "ids", "hostId"),
                DeviceId = vector.String("inputs", "ids", "deviceId"),
                Direction = vector.String(source, direction, "direction"),
                Kind = vector.String(source, direction, "kind"),
                MessageId = vector.String(source, direction, "messageId"),
                Sequence = (ulong)vector.Long(source, direction, "sequence"),
                CreatedAt = vector.Long(source, direction, "createdAt"),
                PayloadEncoding = vector.String(source, direction, "payloadEncoding"),
                Nonce = vector.String(source, direction, "nonce"),
                Ciphertext = vector.String(source, direction, "ciphertext")
            };
        }

        private static void AssertBinaryJsonStrings(object value, params string[] paths)
        {
            var serializer = new DataContractJsonSerializer(value.GetType());
            string json;
            using (var output = new MemoryStream())
            {
                serializer.WriteObject(output, value);
                json = Encoding.UTF8.GetString(output.ToArray());
            }

            var root = new JavaScriptSerializer()
                .Deserialize<IDictionary<string, object>>(json);
            foreach (var path in paths)
            {
                object current = root;
                foreach (var segment in path.Split('.'))
                {
                    current = ((IDictionary<string, object>)current)[segment];
                }

                Assert.That(current, Is.TypeOf<string>(), path);
                Assert.That((string)current, Does.Not.Contain("="), path);
                Assert.That((string)current, Does.Not.Contain("+"), path);
                Assert.That((string)current, Does.Not.Contain("/"), path);
            }
        }
    }

    internal sealed class SecureRemoteVector
    {
        private readonly IDictionary<string, object> _root;

        private SecureRemoteVector(IDictionary<string, object> root)
        {
            _root = root;
        }

        public static SecureRemoteVector Load()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MyCodex.sln")))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new DirectoryNotFoundException("Could not locate the repository root.");
            }

            var path = Path.Combine(
                directory.FullName,
                "tests",
                "MyCodex.Application.Tests",
                "TestData",
                "secure-remote-v1.json");
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return new SecureRemoteVector(
                serializer.Deserialize<IDictionary<string, object>>(File.ReadAllText(path)));
        }

        public string String(params string[] path)
        {
            return Convert.ToString(Value(path));
        }

        public long Long(params string[] path)
        {
            return Convert.ToInt64(Value(path));
        }

        public byte[] Bytes(params string[] path)
        {
            var value = String(path).Replace('-', '+').Replace('_', '/');
            value = value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '=');
            return Convert.FromBase64String(value);
        }

        public byte[] HexBytes(params string[] path)
        {
            var value = String(path);
            var result = new byte[value.Length / 2];
            for (var index = 0; index < result.Length; index++)
            {
                result[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            }

            return result;
        }

        private object Value(IEnumerable<string> path)
        {
            object current = _root;
            foreach (var segment in path)
            {
                current = ((IDictionary<string, object>)current)[segment];
            }

            return current;
        }
    }
}
