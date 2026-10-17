using System;
using System.IO;
using System.Text;

namespace MyCodex.Application.RemoteRelay
{
    public static class SecureRemoteTranscript
    {
        private const string PairingPackageDomain = "MYCODEX-PAIRING-PACKAGE-V1";
        private const string PairingClaimDomain = "MYCODEX-PAIRING-CLAIM-V1";
        private const string PairingApprovalDomain = "MYCODEX-PAIRING-APPROVAL-V1";
        private const string RelayChallengeDomain = "MYCODEX-RELAY-CHALLENGE-V1";
        private const string RelayProofDomain = "MYCODEX-RELAY-PROOF-V1";
        private const string SessionHelloDomain = "MYCODEX-SESSION-HELLO-V1";
        private const string SessionConfirmationDomain = "MYCODEX-SESSION-CONFIRM-V1";
        private const string EnvelopeAadDomain = "MYCODEX-ENVELOPE-AAD-V1";

        public static byte[] EncodePairingPackage(PairingPackage value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.Relay == null)
            {
                throw new ArgumentException("The Relay descriptor is required.", nameof(value));
            }

            using (var output = new MemoryStream())
            {
                AppendString(output, PairingPackageDomain);
                AppendInt64(output, value.Version);
                AppendString(output, value.Relay.Url);
                AppendBase64Url(output, value.Relay.SigningPublicKey);
                AppendBase64Url(output, value.Relay.SigningKeyFingerprint);
                AppendBase64Url(output, value.Relay.EmbeddedTlsCertificateSha256 ?? string.Empty);
                AppendString(output, value.TenantId);
                AppendString(output, value.HostId);
                AppendString(output, value.HostName);
                AppendString(output, value.InviteId);
                AppendBase64Url(output, value.InviteSecret);
                AppendInt64(output, value.ExpiresAt);
                AppendBase64Url(output, value.HostSigningPublicKey);
                AppendBase64Url(output, value.HostAgreementPublicKey);
                AppendInt64(output, value.HostKeyVersion);
                return output.ToArray();
            }
        }

        public static byte[] EncodePairingClaim(PairingClaim value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var output = new MemoryStream())
            {
                AppendString(output, PairingClaimDomain);
                AppendInt64(output, value.ProtocolVersion);
                AppendString(output, value.ClaimId);
                AppendString(output, value.InviteId);
                AppendString(output, value.TenantId);
                AppendString(output, value.HostId);
                AppendString(output, value.DeviceId);
                AppendString(output, value.DeviceName);
                AppendString(output, value.Platform);
                AppendString(output, value.AppVersion);
                AppendBytes(output, value.DeviceSigningPublicKey);
                AppendBytes(output, value.DeviceAgreementPublicKey);
                AppendInt64(output, value.DeviceKeyVersion);
                AppendBytes(output, value.ClientNonce);
                AppendInt64(output, value.CreatedAt);
                return output.ToArray();
            }
        }

        public static byte[] EncodeVisiblePairingClaimHeader(PairingClaimHeader value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var output = new MemoryStream())
            {
                AppendString(output, PairingClaimDomain);
                AppendInt64(output, value.ProtocolVersion);
                AppendString(output, value.InviteId);
                AppendString(output, value.TenantId);
                AppendString(output, value.HostId);
                AppendString(output, value.ClaimId);
                AppendString(output, value.DeviceId);
                AppendBase64Url(output, value.DeviceAgreementPublicKey);
                AppendInt64(output, value.DeviceKeyVersion);
                AppendBase64Url(output, value.ClientNonce);
                AppendInt64(output, value.CiphertextLength);
                return output.ToArray();
            }
        }

        public static byte[] EncodePairingClaimForSignature(PairingClaim value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var transcript = EncodePairingClaim(value);
            try
            {
                return Join(transcript, EncodeField(value.InviteProof));
            }
            finally
            {
                Array.Clear(transcript, 0, transcript.Length);
            }
        }

        public static byte[] EncodePairingClaimPlaintext(PairingClaim value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var transcript = EncodePairingClaimForSignature(value);
            try
            {
                return Join(transcript, EncodeField(value.DeviceSignature));
            }
            finally
            {
                Array.Clear(transcript, 0, transcript.Length);
            }
        }

        public static byte[] EncodePairingApproval(PairingApproval value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var output = new MemoryStream())
            {
                AppendString(output, PairingApprovalDomain);
                AppendInt64(output, value.ProtocolVersion);
                AppendString(output, value.BindingId);
                AppendString(output, value.TenantId);
                AppendString(output, value.HostId);
                AppendString(output, value.DeviceId);
                AppendBytes(output, value.HostSigningPublicKey);
                AppendBytes(output, value.HostAgreementPublicKey);
                AppendInt64(output, value.HostKeyVersion);
                AppendBytes(output, value.DeviceSigningPublicKey);
                AppendBytes(output, value.DeviceAgreementPublicKey);
                AppendInt64(output, value.DeviceKeyVersion);
                AppendBytes(output, value.RelaySigningPublicKey);
                AppendBytes(output, value.RelaySigningKeyFingerprint);
                AppendInt64(output, value.ApprovedAt);
                AppendInt64(output, value.BindingVersion);
                return output.ToArray();
            }
        }

        public static byte[] EncodePairingApprovalPlaintext(PairingApproval value, byte[] hostSignature)
        {
            if (hostSignature == null)
            {
                throw new ArgumentNullException(nameof(hostSignature));
            }

            var transcript = EncodePairingApproval(value);
            try
            {
                return Join(transcript, EncodeField(hostSignature));
            }
            finally
            {
                Array.Clear(transcript, 0, transcript.Length);
            }
        }

        public static byte[] EncodePairingApprovalHeader(PairingApprovalHeader value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var output = new MemoryStream())
            {
                AppendString(output, PairingApprovalDomain);
                AppendInt64(output, value.ProtocolVersion);
                AppendString(output, value.ClaimId);
                AppendString(output, value.InviteId);
                AppendString(output, value.TenantId);
                AppendString(output, value.HostId);
                AppendString(output, value.DeviceId);
                AppendString(output, value.Status);
                AppendInt64(output, value.BindingVersion);
                AppendBase64Url(output, value.ApprovalNonce);
                AppendInt64(output, value.CiphertextLength);
                return output.ToArray();
            }
        }

        public static byte[] EncodeRelayChallenge(AuthChallenge value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var output = new MemoryStream())
            {
                AppendString(output, RelayChallengeDomain);
                AppendInt64(output, value.ProtocolVersion);
                AppendBase64Url(output, value.RelaySigningKeyFingerprint);
                AppendString(output, value.ChallengeId);
                AppendBase64Url(output, value.Challenge);
                AppendString(output, value.SubjectType);
                AppendString(output, value.SubjectId);
                AppendString(output, value.TenantId);
                AppendString(output, value.HostId);
                AppendString(output, value.DeviceId);
                AppendString(output, value.Purpose);
                AppendInt64(output, value.IssuedAt);
                AppendInt64(output, value.ExpiresAt);
                return output.ToArray();
            }
        }

        public static byte[] EncodeRelayProof(AuthChallenge value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var challengeTranscript = EncodeRelayChallenge(value);
            var relaySignature = SecureRemoteBase64Url.Decode(value.RelaySignature);
            try
            {
                if (relaySignature.Length != 64)
                {
                    throw new ArgumentException("Relay signatures must be 64 bytes.", nameof(value));
                }

                using (var output = new MemoryStream())
                {
                    AppendString(output, RelayProofDomain);
                    AppendInt64(output, 1);
                    AppendBytes(output, challengeTranscript);
                    AppendBytes(output, relaySignature);
                    AppendString(output, value.SubjectType);
                    AppendString(output, value.SubjectId);
                    return output.ToArray();
                }
            }
            finally
            {
                Array.Clear(challengeTranscript, 0, challengeTranscript.Length);
                Array.Clear(relaySignature, 0, relaySignature.Length);
            }
        }

        public static byte[] EncodeSessionHello(SessionHello value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var output = new MemoryStream())
            {
                AppendString(output, SessionHelloDomain);
                AppendInt64(output, value.ProtocolVersion);
                AppendString(output, value.FrameType);
                AppendString(output, value.SessionId);
                AppendString(output, value.TenantId);
                AppendString(output, value.HostId);
                AppendString(output, value.DeviceId);
                AppendString(output, value.Direction);
                AppendInt64(output, value.BindingVersion);
                AppendInt64(output, value.KeyVersion);
                AppendBase64Url(output, value.EphemeralPublicKey);
                AppendBase64Url(output, value.Nonce);
                AppendInt64(output, value.CreatedAt);
                return output.ToArray();
            }
        }

        public static byte[] EncodeServerHelloSignature(SessionHello client, SessionHello server)
        {
            return Join(
                EncodeSessionHello(client),
                EncodeSignatureField(client, nameof(client)),
                EncodeSessionHello(server));
        }

        public static byte[] EncodeCompleteSessionHello(SessionHello client, SessionHello server)
        {
            return Join(
                EncodeServerHelloSignature(client, server),
                EncodeSignatureField(server, nameof(server)));
        }

        public static byte[] EncodeEnvelopeAad(SecureRelayEnvelope value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            using (var output = new MemoryStream())
            {
                AppendString(output, EnvelopeAadDomain);
                AppendInt64(output, value.ProtocolVersion);
                AppendString(output, value.FrameType);
                AppendString(output, value.SessionId);
                AppendString(output, value.TenantId);
                AppendString(output, value.HostId);
                AppendString(output, value.DeviceId);
                AppendString(output, value.Direction);
                AppendString(output, value.Kind);
                AppendString(output, value.MessageId);
                AppendUInt64(output, value.Sequence);
                AppendInt64(output, value.CreatedAt);
                AppendString(output, value.PayloadEncoding);
                AppendBase64Url(output, value.Nonce);
                return output.ToArray();
            }
        }

        public static byte[] EncodeConfirmationTranscript(byte[] completeTranscript, string senderRole)
        {
            if (completeTranscript == null)
            {
                throw new ArgumentNullException(nameof(completeTranscript));
            }

            using (var output = new MemoryStream())
            {
                AppendString(output, SessionConfirmationDomain);
                AppendBytes(output, completeTranscript);
                AppendString(output, senderRole);
                return output.ToArray();
            }
        }

        internal static void AppendString(Stream output, string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            AppendBytes(output, Encoding.UTF8.GetBytes(value));
        }

        internal static void AppendBytes(Stream output, byte[] value)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            AppendUInt32(output, (uint)value.Length);
            output.Write(value, 0, value.Length);
        }

        private static byte[] EncodeSignatureField(SessionHello value, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var signature = SecureRemoteBase64Url.Decode(value.Signature);
            try
            {
                if (signature.Length != 64)
                {
                    throw new ArgumentException(
                        "Session Hello signatures must be 64 bytes.",
                        parameterName);
                }

                return EncodeField(signature);
            }
            finally
            {
                Array.Clear(signature, 0, signature.Length);
            }
        }

        private static void AppendBase64Url(Stream output, string value)
        {
            var decoded = SecureRemoteBase64Url.Decode(value);
            try
            {
                AppendBytes(output, decoded);
            }
            finally
            {
                Array.Clear(decoded, 0, decoded.Length);
            }
        }

        private static byte[] EncodeField(byte[] value)
        {
            using (var output = new MemoryStream())
            {
                AppendBytes(output, value);
                return output.ToArray();
            }
        }

        private static byte[] Join(params byte[][] values)
        {
            using (var output = new MemoryStream())
            {
                foreach (var value in values)
                {
                    output.Write(value, 0, value.Length);
                }

                return output.ToArray();
            }
        }

        private static void AppendInt64(Stream output, long value)
        {
            AppendUInt64(output, unchecked((ulong)value));
        }

        private static void AppendUInt64(Stream output, ulong value)
        {
            for (var shift = 56; shift >= 0; shift -= 8)
            {
                output.WriteByte((byte)(value >> shift));
            }
        }

        private static void AppendUInt32(Stream output, uint value)
        {
            output.WriteByte((byte)(value >> 24));
            output.WriteByte((byte)(value >> 16));
            output.WriteByte((byte)(value >> 8));
            output.WriteByte((byte)value);
        }
    }
}
