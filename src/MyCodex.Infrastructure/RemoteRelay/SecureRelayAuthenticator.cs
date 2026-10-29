using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class SecureRelayAuthenticator
    {
        private const long MaximumLifetimeMilliseconds = 60000;
        private const long AllowedClockSkewMilliseconds = 300000;
        private readonly IRelayHttpTransport _transport;
        private readonly IRemoteIdentityStore _identityStore;
        private readonly Func<long> _utcNowMilliseconds;
        private readonly CngSecureRemoteCrypto _crypto;
        private readonly JavaScriptSerializer _serializer;

        internal IRemoteIdentityStore IdentityStore
        {
            get { return _identityStore; }
        }

        public SecureRelayAuthenticator(
            IRelayHttpTransport transport,
            IRemoteIdentityStore identityStore)
            : this(
                transport,
                identityStore,
                () => new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds())
        {
        }

        internal SecureRelayAuthenticator(
            IRelayHttpTransport transport,
            IRemoteIdentityStore identityStore,
            Func<long> utcNowMilliseconds)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
            _utcNowMilliseconds = utcNowMilliseconds ?? throw new ArgumentNullException(nameof(utcNowMilliseconds));
            _crypto = new CngSecureRemoteCrypto();
            _serializer = new JavaScriptSerializer();
        }

        public async Task<string> GetTicketAsync(
            RelayHostConfig config,
            RelayTicketPurpose purpose,
            string hostId,
            string deviceId,
            CancellationToken cancellationToken)
        {
            var currentConfig = RequireConfig(config);
            if (string.IsNullOrWhiteSpace(hostId))
            {
                throw new ArgumentException("The host ID is required.", nameof(hostId));
            }

            var purposeValue = GetPurposeValue(purpose);
            var targetDeviceId = deviceId ?? string.Empty;
            var challengeRequest = new Dictionary<string, object>
            {
                { "subjectType", "host" },
                { "subjectId", hostId },
                { "tenantId", currentConfig.TenantId },
                { "hostId", hostId },
                { "deviceId", targetDeviceId },
                { "purpose", purposeValue }
            };
            var challengeResponse = await SendJsonAsync(
                currentConfig,
                "/v1/auth/challenges",
                challengeRequest,
                cancellationToken).ConfigureAwait(false);
            var challengeObject = ParseObject(challengeResponse.Body);
            var challenge = ParseChallenge(challengeObject);
            ValidateChallenge(
                currentConfig,
                challenge,
                hostId,
                targetDeviceId,
                purposeValue,
                _utcNowMilliseconds());

            _identityStore.LoadOrCreateHostIdentity(hostId);
            var proofTranscript = SecureRemoteTranscript.EncodeRelayProof(challenge);
            byte[] subjectSignature = null;
            try
            {
                subjectSignature = _identityStore.Sign(proofTranscript);
                if (subjectSignature == null || subjectSignature.Length != 64)
                {
                    throw new InvalidOperationException("The host proof signature is invalid.");
                }

                var proofRequest = new Dictionary<string, object>
                {
                    { "challenge", challengeObject },
                    { "subjectSignature", SecureRemoteBase64Url.Encode(subjectSignature) }
                };
                var ticketResponse = await SendJsonAsync(
                    currentConfig,
                    "/v1/auth/prove",
                    proofRequest,
                    cancellationToken).ConfigureAwait(false);
                return ParseAndValidateTicket(ticketResponse.Body, purposeValue, _utcNowMilliseconds());
            }
            finally
            {
                Array.Clear(proofTranscript, 0, proofTranscript.Length);
                if (subjectSignature != null)
                {
                    Array.Clear(subjectSignature, 0, subjectSignature.Length);
                }
            }
        }

        internal static string GetPurposeValue(RelayTicketPurpose purpose)
        {
            switch (purpose)
            {
                case RelayTicketPurpose.CreatePairingInvite:
                    return "pairing_invite_create";
                case RelayTicketPurpose.CancelPairingInvite:
                    return "pairing_invite_cancel";
                case RelayTicketPurpose.ListPairingClaims:
                    return "pairing_claim_list";
                case RelayTicketPurpose.ApprovePairingClaim:
                    return "pairing_claim_approve";
                case RelayTicketPurpose.RejectPairingClaim:
                    return "pairing_claim_reject";
                case RelayTicketPurpose.DeviceList:
                    return "device_list";
                case RelayTicketPurpose.DeviceRevoke:
                    return "device_revoke";
                case RelayTicketPurpose.WebSocketHost:
                    return "websocket_host";
                case RelayTicketPurpose.WebSocketDevice:
                    return "websocket_device";
                default:
                    throw new ArgumentOutOfRangeException(nameof(purpose));
            }
        }

        private async Task<RelayHttpResponse> SendJsonAsync(
            RelayHostConfig config,
            string path,
            IDictionary<string, object> body,
            CancellationToken cancellationToken)
        {
            var request = new RelayHttpRequest(
                "POST",
                HttpRelayTransport.BuildUrl(config, path),
                _serializer.Serialize(body));
            request.Headers["Content-Type"] = "application/json";
            var response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                ThrowRelayError(response, config);
            }

            return response;
        }

        private void ValidateChallenge(
            RelayHostConfig config,
            AuthChallenge challenge,
            string hostId,
            string deviceId,
            string purpose,
            long now)
        {
            if (challenge.ProtocolVersion != 1 ||
                !string.Equals(challenge.SubjectType, "host", StringComparison.Ordinal) ||
                !string.Equals(challenge.SubjectId, hostId, StringComparison.Ordinal) ||
                !string.Equals(challenge.TenantId, config.TenantId, StringComparison.Ordinal) ||
                !string.Equals(challenge.HostId, hostId, StringComparison.Ordinal) ||
                !string.Equals(challenge.DeviceId, deviceId, StringComparison.Ordinal) ||
                !string.Equals(challenge.Purpose, purpose, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(challenge.ChallengeId) ||
                string.IsNullOrWhiteSpace(challenge.Challenge) ||
                string.IsNullOrWhiteSpace(challenge.RelaySigningKeyFingerprint) ||
                string.IsNullOrWhiteSpace(challenge.RelaySignature) ||
                challenge.IssuedAt > now + AllowedClockSkewMilliseconds ||
                challenge.ExpiresAt <= now - AllowedClockSkewMilliseconds ||
                challenge.ExpiresAt <= challenge.IssuedAt ||
                challenge.ExpiresAt > challenge.IssuedAt + MaximumLifetimeMilliseconds)
            {
                throw new InvalidOperationException("The Relay returned an invalid authentication challenge.");
            }

            var expectedFingerprint = DecodeExact(
                config.RelaySigningKeyFingerprint,
                32,
                "The configured Relay identity is invalid.");
            var relayPublicKey = DecodeExact(
                config.RelaySigningPublicKey,
                65,
                "The configured Relay identity is invalid.");
            var relaySignature = DecodeExact(
                challenge.RelaySignature,
                64,
                "The Relay returned an invalid authentication challenge.");
            var challengeValue = DecodeExact(
                challenge.Challenge,
                32,
                "The Relay returned an invalid authentication challenge.");
            byte[] challengeTranscript = null;
            try
            {
                challengeTranscript = SecureRemoteTranscript.EncodeRelayChallenge(challenge);
                if (!string.Equals(
                        challenge.RelaySigningKeyFingerprint,
                        config.RelaySigningKeyFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The Relay returned an invalid authentication challenge.");
                }

                byte[] actualFingerprint;
                using (var sha256 = SHA256.Create())
                {
                    actualFingerprint = sha256.ComputeHash(relayPublicKey);
                }

                try
                {
                    if (!SecureRemotePairingRules.FixedTimeEquals(actualFingerprint, expectedFingerprint) ||
                        !_crypto.VerifyRawEcdsa(relayPublicKey, challengeTranscript, relaySignature))
                    {
                        throw new InvalidOperationException("The Relay returned an invalid authentication challenge.");
                    }
                }
                finally
                {
                    Array.Clear(actualFingerprint, 0, actualFingerprint.Length);
                }
            }
            catch (Exception ex) when (
                ex is ArgumentException ||
                ex is FormatException ||
                ex is OverflowException)
            {
                throw new InvalidOperationException("The Relay returned an invalid authentication challenge.");
            }
            finally
            {
                Array.Clear(expectedFingerprint, 0, expectedFingerprint.Length);
                Array.Clear(relayPublicKey, 0, relayPublicKey.Length);
                Array.Clear(relaySignature, 0, relaySignature.Length);
                Array.Clear(challengeValue, 0, challengeValue.Length);
                if (challengeTranscript != null)
                {
                    Array.Clear(challengeTranscript, 0, challengeTranscript.Length);
                }
            }
        }

        private string ParseAndValidateTicket(string json, string purpose, long now)
        {
            var root = ParseObject(json);
            var ticket = GetString(root, "ticket");
            var returnedPurpose = GetString(root, "purpose");
            long expiresAt;
            try
            {
                expiresAt = GetInt64(root, "expiresAt");
            }
            catch (FormatException)
            {
                throw new InvalidOperationException("The Relay returned an invalid authentication ticket.");
            }

            var ticketValue = DecodeExact(
                ticket,
                32,
                "The Relay returned an invalid authentication ticket.");

            try
            {
                if (!string.Equals(returnedPurpose, purpose, StringComparison.Ordinal) ||
                    expiresAt <= now - AllowedClockSkewMilliseconds ||
                    expiresAt > now + MaximumLifetimeMilliseconds + AllowedClockSkewMilliseconds)
                {
                    throw new InvalidOperationException("The Relay returned an invalid authentication ticket.");
                }

                return ticket;
            }
            finally
            {
                Array.Clear(ticketValue, 0, ticketValue.Length);
            }
        }

        private AuthChallenge ParseChallenge(IDictionary<string, object> root)
        {
            try
            {
                return new AuthChallenge
                {
                    ProtocolVersion = GetInt64(root, "protocolVersion"),
                    RelaySigningKeyFingerprint = GetString(root, "relayFingerprint"),
                    ChallengeId = GetString(root, "challengeId"),
                    Challenge = GetString(root, "challenge"),
                    SubjectType = GetString(root, "subjectType"),
                    SubjectId = GetString(root, "subjectId"),
                    TenantId = GetString(root, "tenantId"),
                    HostId = GetString(root, "hostId"),
                    DeviceId = GetString(root, "deviceId"),
                    Purpose = GetString(root, "purpose"),
                    IssuedAt = GetInt64(root, "issuedAt"),
                    ExpiresAt = GetInt64(root, "expiresAt"),
                    RelaySignature = GetString(root, "relaySignature")
                };
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException)
            {
                throw new InvalidOperationException("The Relay returned an invalid authentication challenge.");
            }
        }

        private IDictionary<string, object> ParseObject(string json)
        {
            try
            {
                return _serializer.DeserializeObject(json ?? "{}") as IDictionary<string, object> ??
                    new Dictionary<string, object>();
            }
            catch (ArgumentException)
            {
                return new Dictionary<string, object>();
            }
        }

        private void ThrowRelayError(RelayHttpResponse response, RelayHostConfig config)
        {
            var code = GetString(ParseObject(response.Body), "code");
            var message = "Relay authentication failed with HTTP " +
                response.StatusCode.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(code))
            {
                message += " (" + code + ")";
            }

            message += ".";
            throw new InvalidOperationException(RelaySecretRedactor.Redact(
                message,
                new[] { config.TenantSecret }));
        }

        private static RelayHostConfig RequireConfig(RelayHostConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (string.IsNullOrWhiteSpace(config.RelayHost) ||
                config.RelayPort <= 0 ||
                string.IsNullOrWhiteSpace(config.TenantId))
            {
                throw new InvalidOperationException("Relay connection settings are incomplete.");
            }

            if (string.IsNullOrWhiteSpace(config.RelaySigningPublicKey) ||
                string.IsNullOrWhiteSpace(config.RelaySigningKeyFingerprint))
            {
                throw new InvalidOperationException("Relay identity settings are required.");
            }

            return config;
        }

        private static string GetString(IDictionary<string, object> value, string key)
        {
            object item;
            return value != null && value.TryGetValue(key, out item)
                ? Convert.ToString(item, CultureInfo.InvariantCulture)
                : null;
        }

        private static long GetInt64(IDictionary<string, object> value, string key)
        {
            object item;
            if (value == null || !value.TryGetValue(key, out item))
            {
                throw new FormatException("A required protocol integer is missing.");
            }

            if (item is int)
            {
                return (int)item;
            }

            if (item is long)
            {
                return (long)item;
            }

            throw new FormatException("A protocol integer has an invalid JSON type.");
        }

        private static byte[] DecodeExact(string value, int length, string errorMessage)
        {
            try
            {
                var decoded = SecureRemoteBase64Url.Decode(value);
                if (decoded.Length == length)
                {
                    return decoded;
                }

                Array.Clear(decoded, 0, decoded.Length);
            }
            catch (Exception ex) when (ex is ArgumentNullException || ex is FormatException)
            {
            }

            throw new InvalidOperationException(errorMessage);
        }
    }
}
