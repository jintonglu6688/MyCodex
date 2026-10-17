using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class RelayHostClient : IRelayHostClient
    {
        private readonly IRelayHttpTransport _transport;
        private readonly SecureRelayAuthenticator _authenticator;
        private readonly JavaScriptSerializer _serializer;
        private readonly object _inviteSync = new object();
        private readonly Dictionary<string, string> _inviteHosts =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public RelayHostClient()
            : this(new HttpRelayTransport(), new CngRemoteIdentityStore())
        {
        }

        public RelayHostClient(IRelayHttpTransport transport)
            : this(transport, new CngRemoteIdentityStore())
        {
        }

        internal RelayHostClient(
            IRelayHttpTransport transport,
            IRemoteIdentityStore identityStore)
            : this(transport, new SecureRelayAuthenticator(transport, identityStore))
        {
        }

        internal RelayHostClient(
            IRelayHttpTransport transport,
            SecureRelayAuthenticator authenticator)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
            _serializer = new JavaScriptSerializer();
        }

        public async Task<RelayMetadata> GetMetadataAsync(
            RelayHostConfig config,
            CancellationToken cancellationToken)
        {
            var currentConfig = RequireEndpointConfig(config);
            var request = new RelayHttpRequest(
                "GET",
                HttpRelayTransport.BuildUrl(currentConfig, "/.well-known/mycodex-relay"),
                null);
            var response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                ThrowRelayError(response, currentConfig, null);
            }

            var root = ParseObject(response.Body);
            var metadata = new RelayMetadata
            {
                ProtocolVersion = GetInt64(root, "protocolVersion"),
                SigningPublicKey = GetString(root, "signingPublicKey"),
                SigningKeyFingerprint = GetString(root, "signingKeyFingerprint"),
                ServerTime = GetInt64(root, "serverTime"),
                MaxRequestBytes = GetInt64(root, "maxRequestBytes"),
                MaxMessageBytes = GetInt64(root, "maxMessageBytes")
            };
            if (metadata.ProtocolVersion != 1)
            {
                throw new InvalidOperationException("The Relay protocol version is not supported.");
            }

            var publicKey = SecureRemoteBase64Url.Decode(metadata.SigningPublicKey);
            var fingerprint = SecureRemoteBase64Url.Decode(metadata.SigningKeyFingerprint);
            try
            {
                if (publicKey.Length != 65 ||
                    fingerprint.Length != 32 ||
                    !new CngSecureRemoteCrypto().IsValidP256PublicKey(publicKey))
                {
                    throw new InvalidOperationException("The Relay signing identity is invalid.");
                }

                byte[] expected;
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    expected = sha256.ComputeHash(publicKey);
                }

                try
                {
                    if (!SecureRemotePairingRules.FixedTimeEquals(expected, fingerprint))
                    {
                        throw new InvalidOperationException("The Relay signing key fingerprint is invalid.");
                    }
                }
                finally
                {
                    Array.Clear(expected, 0, expected.Length);
                }
            }
            finally
            {
                Array.Clear(publicKey, 0, publicKey.Length);
                Array.Clear(fingerprint, 0, fingerprint.Length);
            }

            var hasConfiguredKey = !string.IsNullOrWhiteSpace(config.RelaySigningPublicKey);
            var hasConfiguredFingerprint = !string.IsNullOrWhiteSpace(config.RelaySigningKeyFingerprint);
            if (hasConfiguredKey != hasConfiguredFingerprint ||
                (hasConfiguredKey &&
                 (!string.Equals(config.RelaySigningPublicKey, metadata.SigningPublicKey, StringComparison.Ordinal) ||
                  !string.Equals(config.RelaySigningKeyFingerprint, metadata.SigningKeyFingerprint, StringComparison.Ordinal))))
            {
                throw new InvalidOperationException("The Relay identity does not match the configured identity.");
            }

            config.RelaySigningPublicKey = metadata.SigningPublicKey;
            config.RelaySigningKeyFingerprint = metadata.SigningKeyFingerprint;
            return metadata;
        }

        public async Task<RelayHostRegistrationResult> EnrollHostAsync(
            RelayHostConfig config,
            RemoteEndpointIdentity identity,
            CancellationToken cancellationToken)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            var body = new Dictionary<string, object>
            {
                { "tenantId", RequireConfig(config, true).TenantId },
                { "hostId", identity.IdentityId },
                { "displayName", string.IsNullOrWhiteSpace(config.HostDisplayName) ? Environment.MachineName : config.HostDisplayName.Trim() },
                { "signingPublicKey", identity.SigningPublicKey },
                { "agreementPublicKey", identity.AgreementPublicKey },
                { "keyVersion", identity.KeyVersion }
            };
            var response = await SendJsonAsync(
                config,
                "POST",
                "/v1/hosts/enroll",
                body,
                RequireConfig(config, true).TenantSecret,
                cancellationToken).ConfigureAwait(false);
            var root = ParseObject(response.Body);
            return new RelayHostRegistrationResult
            {
                TenantId = GetString(root, "tenantId"),
                HostId = GetString(root, "hostId")
            };
        }

        public async Task<RelayPairingInvite> CreatePairingInviteAsync(
            RelayHostConfig config,
            CreatePairingInviteRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var ticket = await _authenticator.GetTicketAsync(
                config,
                RelayTicketPurpose.CreatePairingInvite,
                request.HostId,
                string.Empty,
                cancellationToken).ConfigureAwait(false);
            var body = new Dictionary<string, object>
            {
                { "tenantId", request.TenantId },
                { "hostId", request.HostId },
                { "inviteId", request.InviteId },
                { "expiresAt", request.ExpiresAt }
            };
            var response = await SendJsonAsync(
                config,
                "POST",
                "/v1/pairing/invites",
                body,
                ticket,
                cancellationToken).ConfigureAwait(false);
            var root = ParseObject(response.Body);
            var invite = new RelayPairingInvite
            {
                TenantId = GetString(root, "tenantId"),
                HostId = GetString(root, "hostId"),
                InviteId = GetString(root, "inviteId"),
                Status = GetString(root, "status"),
                ExpiresAt = GetInt64(root, "expiresAt")
            };
            lock (_inviteSync)
            {
                _inviteHosts[invite.InviteId] = invite.HostId;
            }

            return invite;
        }

        public async Task CancelPairingInviteAsync(
            RelayHostConfig config,
            string inviteId,
            CancellationToken cancellationToken)
        {
            var currentConfig = RequireConfig(config, false);
            string hostId;
            lock (_inviteSync)
            {
                if (!_inviteHosts.TryGetValue(inviteId, out hostId))
                {
                    throw new InvalidOperationException("The local pairing invitation was not found.");
                }
            }

            var ticket = await _authenticator.GetTicketAsync(
                config,
                RelayTicketPurpose.CancelPairingInvite,
                hostId,
                string.Empty,
                cancellationToken).ConfigureAwait(false);
            var path = "/v1/pairing/invites/" + EscapePath(inviteId) + "/cancel?tenantId=" +
                EscapeQuery(currentConfig.TenantId) + "&hostId=" + EscapeQuery(hostId);
            await SendJsonAsync(
                config,
                "POST",
                path,
                null,
                ticket,
                cancellationToken).ConfigureAwait(false);
            lock (_inviteSync)
            {
                _inviteHosts.Remove(inviteId);
            }
        }

        public async Task<IReadOnlyList<OpaquePairingClaim>> ListPairingClaimsAsync(
            RelayHostConfig config,
            string hostId,
            CancellationToken cancellationToken)
        {
            var currentConfig = RequireConfig(config, false);
            var ticket = await _authenticator.GetTicketAsync(
                config,
                RelayTicketPurpose.ListPairingClaims,
                hostId,
                string.Empty,
                cancellationToken).ConfigureAwait(false);
            var path = "/v1/hosts/" + EscapePath(hostId) + "/pairing/claims?tenantId=" +
                EscapeQuery(currentConfig.TenantId);
            var response = await SendJsonAsync(
                config,
                "GET",
                path,
                null,
                ticket,
                cancellationToken).ConfigureAwait(false);
            var root = ParseObject(response.Body);
            var values = GetEnumerable(root, "claims");
            var result = new List<OpaquePairingClaim>();
            foreach (var item in values)
            {
                var claim = item as IDictionary<string, object>;
                if (claim == null)
                {
                    continue;
                }

                var header = GetObject(claim, "header");
                result.Add(new OpaquePairingClaim
                {
                    ClaimId = GetString(header, "claimId"),
                    InviteId = GetString(header, "inviteId"),
                    TenantId = GetString(header, "tenantId"),
                    HostId = GetString(header, "hostId"),
                    Status = GetString(claim, "status"),
                    Header = ParseClaimHeader(header),
                    Nonce = GetString(claim, "nonce"),
                    Ciphertext = GetString(claim, "ciphertext"),
                    CreatedAt = GetInt64(claim, "createdAt"),
                    ExpiresAt = GetInt64(claim, "expiresAt")
                });
            }

            return result;
        }

        public async Task ApprovePairingClaimAsync(
            RelayHostConfig config,
            PairingApprovalRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var currentConfig = RequireConfig(config, false);
            var ticket = await _authenticator.GetTicketAsync(
                config,
                RelayTicketPurpose.ApprovePairingClaim,
                request.HostId,
                request.DeviceId,
                cancellationToken).ConfigureAwait(false);
            var body = new Dictionary<string, object>
            {
                { "deviceSigningPublicKey", request.DeviceSigningPublicKey },
                { "deviceAgreementPublicKey", request.DeviceAgreementPublicKey },
                { "deviceKeyVersion", request.DeviceKeyVersion },
                { "bindingVersion", request.BindingVersion },
                { "approvalHeader", request.ApprovalHeader },
                { "approvalNonce", request.ApprovalNonce },
                { "approvalCiphertext", request.ApprovalCiphertext }
            };
            var path = "/v1/hosts/" + EscapePath(request.HostId) + "/pairing/claims/" +
                EscapePath(request.ClaimId) + "/approve?tenantId=" + EscapeQuery(currentConfig.TenantId);
            await SendJsonAsync(
                config,
                "POST",
                path,
                body,
                ticket,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task RejectPairingClaimAsync(
            RelayHostConfig config,
            string hostId,
            string claimId,
            string deviceId,
            CancellationToken cancellationToken)
        {
            var currentConfig = RequireConfig(config, false);
            var ticket = await _authenticator.GetTicketAsync(
                config,
                RelayTicketPurpose.RejectPairingClaim,
                hostId,
                deviceId,
                cancellationToken).ConfigureAwait(false);
            var path = "/v1/hosts/" + EscapePath(hostId) + "/pairing/claims/" +
                EscapePath(claimId) + "/reject?tenantId=" + EscapeQuery(currentConfig.TenantId);
            await SendJsonAsync(
                config,
                "POST",
                path,
                null,
                ticket,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<RelayDeviceSummary>> ListDevicesAsync(
            RelayHostConfig config,
            string hostId,
            CancellationToken cancellationToken)
        {
            var currentConfig = RequireConfig(config, false);
            var ticket = await _authenticator.GetTicketAsync(
                config,
                RelayTicketPurpose.DeviceList,
                hostId,
                string.Empty,
                cancellationToken).ConfigureAwait(false);
            var path = "/v1/hosts/" + EscapePath(hostId) + "/devices?tenantId=" +
                EscapeQuery(currentConfig.TenantId);
            var response = await SendJsonAsync(
                config,
                "GET",
                path,
                null,
                ticket,
                cancellationToken).ConfigureAwait(false);
            var root = ParseObject(response.Body);
            var result = new List<RelayDeviceSummary>();
            foreach (var item in GetEnumerable(root, "devices"))
            {
                var device = item as IDictionary<string, object>;
                if (device == null)
                {
                    continue;
                }

                result.Add(new RelayDeviceSummary
                {
                    TenantId = GetString(device, "tenantId"),
                    HostId = GetString(device, "hostId"),
                    DeviceId = GetString(device, "deviceId"),
                    DeviceSigningPublicKey = GetString(device, "deviceSigningPublicKey"),
                    DeviceAgreementPublicKey = GetString(device, "deviceAgreementPublicKey"),
                    DeviceKeyVersion = checked((int)GetInt64(device, "deviceKeyVersion")),
                    BindingVersion = checked((int)GetInt64(device, "bindingVersion")),
                    Revoked = GetBoolean(device, "revoked"),
                    ApprovedAt = GetInt64(device, "approvedAt"),
                    LastSeenAt = GetNullableInt64(device, "lastSeenAt"),
                    Online = GetBoolean(device, "online")
                });
            }

            return result;
        }

        public async Task<bool> RevokeDeviceAsync(
            RelayHostConfig config,
            string hostId,
            string deviceId,
            CancellationToken cancellationToken)
        {
            var currentConfig = RequireConfig(config, false);
            var ticket = await _authenticator.GetTicketAsync(
                config,
                RelayTicketPurpose.DeviceRevoke,
                hostId,
                deviceId,
                cancellationToken).ConfigureAwait(false);
            var path = "/v1/hosts/" + EscapePath(hostId) + "/devices/" +
                EscapePath(deviceId) + "/revoke?tenantId=" + EscapeQuery(currentConfig.TenantId);
            var response = await SendJsonAsync(
                config,
                "POST",
                path,
                null,
                ticket,
                cancellationToken).ConfigureAwait(false);
            var root = ParseObject(response.Body);
            return GetBoolean(root, "revoked");
        }

        private async Task<RelayHttpResponse> SendJsonAsync(
            RelayHostConfig config,
            string method,
            string path,
            IDictionary<string, object> body,
            string bearer,
            CancellationToken cancellationToken)
        {
            var currentConfig = RequireConfig(config, false);
            var request = new RelayHttpRequest(
                method,
                HttpRelayTransport.BuildUrl(currentConfig, path),
                body == null ? null : _serializer.Serialize(body));
            request.Headers["Authorization"] = "Bearer " + bearer;
            if (body != null)
            {
                request.Headers["Content-Type"] = "application/json";
            }

            var response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                ThrowRelayError(response, currentConfig, bearer);
            }

            return response;
        }

        private static RelayHostConfig RequireConfig(RelayHostConfig config, bool requireTenantSecret)
        {
            RequireEndpointConfig(config);

            if (string.IsNullOrWhiteSpace(config.TenantId))
            {
                throw new InvalidOperationException("Relay tenant ID is required.");
            }

            if (requireTenantSecret && string.IsNullOrWhiteSpace(config.TenantSecret))
            {
                throw new InvalidOperationException("Relay tenant secret is required.");
            }

            return config;
        }

        private static RelayHostConfig RequireEndpointConfig(RelayHostConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (string.IsNullOrWhiteSpace(config.RelayHost))
            {
                throw new InvalidOperationException("Relay host is required.");
            }

            if (config.RelayPort <= 0)
            {
                throw new InvalidOperationException("Relay port is required.");
            }

            return config;
        }

        private static string EscapeQuery(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
        }

        private static string EscapePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A route identifier is required.", nameof(value));
            }

            return Uri.EscapeDataString(value);
        }

        private static PairingClaimHeader ParseClaimHeader(IDictionary<string, object> header)
        {
            return new PairingClaimHeader
            {
                ProtocolVersion = 1,
                InviteId = GetString(header, "inviteId"),
                TenantId = GetString(header, "tenantId"),
                HostId = GetString(header, "hostId"),
                ClaimId = GetString(header, "claimId"),
                DeviceId = GetString(header, "deviceId"),
                DeviceAgreementPublicKey = GetString(header, "deviceAgreementPublicKey"),
                DeviceKeyVersion = GetInt64(header, "deviceKeyVersion"),
                ClientNonce = GetString(header, "clientNonce"),
                CiphertextLength = GetInt64(header, "ciphertextLength")
            };
        }

        private void ThrowRelayError(
            RelayHttpResponse response,
            RelayHostConfig config,
            string bearer)
        {
            var code = ExtractErrorCode(response.Body);
            var message = "Relay request failed with HTTP " + response.StatusCode.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(code))
            {
                message += " (" + code + ")";
            }

            message += ".";
            throw new InvalidOperationException(RelaySecretRedactor.Redact(
                message,
                new[] { config.TenantSecret, bearer }));
        }

        private string ExtractErrorCode(string body)
        {
            var root = ParseObject(body);
            return GetString(root, "code");
        }

        private IDictionary<string, object> ParseObject(string json)
        {
            return _serializer.DeserializeObject(json ?? "{}") as IDictionary<string, object> ?? new Dictionary<string, object>();
        }

        private static IEnumerable GetEnumerable(IDictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) ? value as IEnumerable ?? new object[0] : new object[0];
        }

        private static string GetString(IDictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        private static IDictionary<string, object> GetObject(IDictionary<string, object> obj, string key)
        {
            object value;
            return obj != null && obj.TryGetValue(key, out value)
                ? value as IDictionary<string, object>
                : null;
        }

        private static long GetInt64(IDictionary<string, object> obj, string key)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value))
            {
                throw new InvalidOperationException("The Relay response is missing " + key + ".");
            }

            if (value is int)
            {
                return (int)value;
            }

            if (value is long)
            {
                return (long)value;
            }

            throw new InvalidOperationException("The Relay response contains an invalid " + key + ".");
        }

        private static long? GetNullableInt64(IDictionary<string, object> obj, string key)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return GetInt64(obj, key);
        }

        private static bool GetBoolean(IDictionary<string, object> obj, string key)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value))
            {
                return false;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            bool parsed;
            return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed) && parsed;
        }

    }
}
