using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class SecureRemotePairingWorkflow : ISecureRemotePairingWorkflow
    {
        private static readonly DateTime UnixEpoch =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly IRelayHostClient _relayClient;
        private readonly IRemoteIdentityStore _identityStore;
        private readonly IRemoteBindingStore _bindingStore;
        private readonly CngSecureRemoteCrypto _crypto;
        private readonly Func<long> _clock;
        private readonly Action<byte[]> _fillRandom;
        private readonly object _sync = new object();
        private readonly HashSet<string> _verifiedClaimIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _verifiedClientNonces =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, PairingApprovalRequest> _preparedApprovals =
            new Dictionary<string, PairingApprovalRequest>(StringComparer.Ordinal);
        private ActivePairingInvite _activeInvite;

        public SecureRemotePairingWorkflow(
            IRelayHostClient relayClient,
            IRemoteIdentityStore identityStore,
            IRemoteBindingStore bindingStore)
            : this(
                relayClient,
                identityStore,
                bindingStore,
                new CngSecureRemoteCrypto(),
                GetCurrentUnixMilliseconds,
                FillRandom)
        {
        }

        internal SecureRemotePairingWorkflow(
            IRelayHostClient relayClient,
            IRemoteIdentityStore identityStore,
            IRemoteBindingStore bindingStore,
            CngSecureRemoteCrypto crypto,
            Func<long> clock,
            Action<byte[]> fillRandom)
        {
            _relayClient = relayClient ?? throw new ArgumentNullException(nameof(relayClient));
            _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
            _bindingStore = bindingStore ?? throw new ArgumentNullException(nameof(bindingStore));
            _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _fillRandom = fillRandom ?? throw new ArgumentNullException(nameof(fillRandom));
        }

        public async Task<ActivePairingInvite> CreatePairingInviteAsync(
            RelayHostConfig config,
            string hostId,
            string hostName,
            CancellationToken cancellationToken)
        {
            Trace.TraceInformation("[RemoteRelay] Pairing invite creation started. Host={0}", RemoteRelayDiagnostics.ShortId(hostId));
            if (string.IsNullOrWhiteSpace(hostId))
            {
                throw new ArgumentException("A host ID is required.", nameof(hostId));
            }

            ActivePairingInvite previous;
            lock (_sync)
            {
                previous = _activeInvite;
            }

            if (previous != null)
            {
                await _relayClient.CancelPairingInviteAsync(
                    config,
                    previous.Package.InviteId,
                    cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    _activeInvite = null;
                }
            }

            var metadata = await _relayClient.GetMetadataAsync(config, cancellationToken).ConfigureAwait(false);
            var identity = _identityStore.LoadOrCreateHostIdentity(hostId);
            var inviteSecret = new byte[32];
            _fillRandom(inviteSecret);
            try
            {
                var now = _clock();
                if (metadata.ServerTime <= 0 ||
                    GetAbsoluteDifference(metadata.ServerTime, now) > 86400000)
                {
                    throw new InvalidOperationException("The Relay clock is invalid or too far from the local clock.");
                }

                var request = new CreatePairingInviteRequest
                {
                    TenantId = config.TenantId,
                    HostId = hostId,
                    InviteId = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                    ExpiresAt = checked(metadata.ServerTime + 540000)
                };
                var relayInvite = await _relayClient.CreatePairingInviteAsync(
                    config,
                    request,
                    cancellationToken).ConfigureAwait(false);
                RequireInviteResponse(request, relayInvite);

                var package = new PairingPackage
                {
                    Version = 1,
                    Relay = new PairingRelay
                    {
                        Url = BuildRelayUrl(config),
                        SigningPublicKey = metadata.SigningPublicKey,
                        SigningKeyFingerprint = metadata.SigningKeyFingerprint,
                        EmbeddedTlsCertificateSha256 = EncodeCertificateFingerprint(config.CertificateSha256)
                    },
                    TenantId = request.TenantId,
                    HostId = request.HostId,
                    HostName = (string.IsNullOrWhiteSpace(config.HostDisplayName) ? hostName ?? string.Empty : config.HostDisplayName).Trim(),
                    InviteId = request.InviteId,
                    InviteSecret = SecureRemoteBase64Url.Encode(inviteSecret),
                    ExpiresAt = request.ExpiresAt,
                    HostSigningPublicKey = identity.SigningPublicKey,
                    HostAgreementPublicKey = identity.AgreementPublicKey,
                    HostKeyVersion = identity.KeyVersion
                };
                var transcript = SecureRemoteTranscript.EncodePairingPackage(package);
                try
                {
                    package.HostSignature = SecureRemoteBase64Url.Encode(_identityStore.Sign(transcript));
                }
                finally
                {
                    Array.Clear(transcript, 0, transcript.Length);
                }

                var active = new ActivePairingInvite
                {
                    RelayInvite = relayInvite,
                    Package = package,
                    PairingUri = RemoteServicePairingInfoFormatter.FormatCopyText(package)
                };
                lock (_sync)
                {
                    _activeInvite = active;
                    _verifiedClaimIds.Clear();
                    _verifiedClientNonces.Clear();
                    _preparedApprovals.Clear();
                }

                Trace.TraceInformation(
                    "[RemoteRelay] Pairing invite created. Host={0}; Invite={1}",
                    RemoteRelayDiagnostics.ShortId(hostId),
                    RemoteRelayDiagnostics.ShortId(request.InviteId));

                return active;
            }
            catch (OperationCanceledException)
            {
                Trace.TraceInformation(
                    "[RemoteRelay] Pairing invite creation cancelled. Host={0}",
                    RemoteRelayDiagnostics.ShortId(hostId));
                throw;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "[RemoteRelay] Pairing invite creation failed. Host={0}; Error={1}",
                    RemoteRelayDiagnostics.ShortId(hostId),
                    RemoteRelayDiagnostics.ExceptionSummary(ex, config != null ? config.TenantSecret : null));
                throw;
            }
            finally
            {
                Array.Clear(inviteSecret, 0, inviteSecret.Length);
            }
        }

        public VerifiedPairingClaim VerifyClaim(
            ActivePairingInvite invite,
            OpaquePairingClaim opaqueClaim)
        {
            ValidateLocalInvite(invite, opaqueClaim);
            var header = opaqueClaim.Header;
            var package = invite.Package;
            var nonce = SecureRemoteBase64Url.Decode(opaqueClaim.Nonce);
            var encrypted = SecureRemoteBase64Url.Decode(opaqueClaim.Ciphertext);
            var inviteSecret = SecureRemoteBase64Url.Decode(package.InviteSecret);
            var deviceAgreementKey = SecureRemoteBase64Url.Decode(header.DeviceAgreementPublicKey);
            byte[] sharedSecret = null;
            byte[] pairingBaseKey = null;
            byte[] claimKey = null;
            byte[] aad = null;
            byte[] plaintext = null;
            try
            {
                if (nonce.Length != 12 ||
                    encrypted.Length < 16 ||
                    header.CiphertextLength != encrypted.Length)
                {
                    throw InvalidClaim();
                }

                sharedSecret = _identityStore.DeriveAgreement(deviceAgreementKey);
                pairingBaseKey = DerivePairingBaseKey(
                    package,
                    header,
                    inviteSecret,
                    sharedSecret);
                claimKey = _crypto.HkdfSha256(
                    pairingBaseKey,
                    new byte[0],
                    Encoding.UTF8.GetBytes("claim encryption"),
                    32);
                aad = SecureRemoteTranscript.EncodeVisiblePairingClaimHeader(header);
                var ciphertext = new byte[encrypted.Length - 16];
                var tag = new byte[16];
                Buffer.BlockCopy(encrypted, 0, ciphertext, 0, ciphertext.Length);
                Buffer.BlockCopy(encrypted, ciphertext.Length, tag, 0, tag.Length);
                try
                {
                    plaintext = _crypto.DecryptAesGcm(
                        claimKey,
                        nonce,
                        ciphertext,
                        tag,
                        aad);
                }
                finally
                {
                    Array.Clear(ciphertext, 0, ciphertext.Length);
                    Array.Clear(tag, 0, tag.Length);
                }

                var claim = ParseClaimPlaintext(plaintext);
                ValidateOuterAndInner(opaqueClaim, claim);
                VerifyInviteProof(inviteSecret, claim);
                VerifyDeviceSignature(claim);
                RememberReplayValues(claim);
                Trace.TraceInformation(
                    "[RemoteRelay] Pairing claim verified. Claim={0}; Device={1}",
                    RemoteRelayDiagnostics.ShortId(claim.ClaimId),
                    RemoteRelayDiagnostics.ShortId(claim.DeviceId));
                return new VerifiedPairingClaim
                {
                    Claim = claim,
                    ShortCode = SecureRemotePairingRules.ComputeShortCode(plaintext),
                    PairingBaseKey = (byte[])pairingBaseKey.Clone(),
                    VerifiedPlaintext = (byte[])plaintext.Clone(),
                    PairingPackage = package
                };
            }
            catch (Exception ex) when (
                ex is CryptographicException ||
                ex is FormatException ||
                ex is EndOfStreamException)
            {
                Trace.TraceWarning(
                    "[RemoteRelay] Pairing claim verification failed. Claim={0}; ErrorType={1}",
                    RemoteRelayDiagnostics.ShortId(header != null ? header.ClaimId : null),
                    ex.GetType().Name);
                throw InvalidClaim();
            }
            finally
            {
                Clear(nonce);
                Clear(encrypted);
                Clear(inviteSecret);
                Clear(deviceAgreementKey);
                Clear(sharedSecret);
                Clear(pairingBaseKey);
                Clear(claimKey);
                Clear(aad);
                Clear(plaintext);
            }
        }

        public async Task ApprovePairingClaimAsync(
            RelayHostConfig config,
            VerifiedPairingClaim verifiedClaim,
            CancellationToken cancellationToken)
        {
            if (verifiedClaim == null || verifiedClaim.Claim == null)
            {
                throw new ArgumentNullException(nameof(verifiedClaim));
            }

            RequireCurrentInvite(verifiedClaim);
            PairingApprovalRequest request;
            lock (_sync)
            {
                if (!_preparedApprovals.TryGetValue(verifiedClaim.Claim.ClaimId, out request))
                {
                    var pendingBinding = ToPendingBinding(verifiedClaim.Claim);
                    _bindingStore.SavePending(pendingBinding);
                    request = BuildApprovalRequest(verifiedClaim, pendingBinding.BindingVersion);
                    pendingBinding.ApprovalHeader = request.ApprovalHeader;
                    pendingBinding.ApprovalNonce = request.ApprovalNonce;
                    pendingBinding.ApprovalCiphertext = request.ApprovalCiphertext;
                    _bindingStore.SavePending(pendingBinding);
                    _preparedApprovals.Add(verifiedClaim.Claim.ClaimId, request);
                }
            }

            try
            {
                await _relayClient.ApprovePairingClaimAsync(
                    config,
                    request,
                    cancellationToken).ConfigureAwait(false);
                _bindingStore.Activate(request.ClaimId, checked((int)request.BindingVersion));
                Trace.TraceInformation(
                    "[RemoteRelay] Pairing claim approved. Claim={0}; Device={1}",
                    RemoteRelayDiagnostics.ShortId(request.ClaimId),
                    RemoteRelayDiagnostics.ShortId(request.DeviceId));
            }
            catch (OperationCanceledException)
            {
                Trace.TraceInformation(
                    "[RemoteRelay] Pairing claim approval cancelled. Claim={0}",
                    RemoteRelayDiagnostics.ShortId(request.ClaimId));
                throw;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "[RemoteRelay] Pairing claim approval failed. Claim={0}; Error={1}",
                    RemoteRelayDiagnostics.ShortId(request.ClaimId),
                    RemoteRelayDiagnostics.ExceptionSummary(ex, config != null ? config.TenantSecret : null));
                throw;
            }
            lock (_sync)
            {
                _activeInvite = null;
            }

            Clear(verifiedClaim.PairingBaseKey);
            Clear(verifiedClaim.VerifiedPlaintext);
        }

        private PairingApprovalRequest BuildApprovalRequest(
            VerifiedPairingClaim verifiedClaim,
            int bindingVersion)
        {
            var claim = verifiedClaim.Claim;
            var package = verifiedClaim.PairingPackage;
            var hostIdentity = _identityStore.LoadOrCreateHostIdentity(claim.HostId);
            var approval = new PairingApproval
            {
                ProtocolVersion = 1,
                BindingId = Guid.NewGuid().ToString("D").ToLowerInvariant(),
                TenantId = claim.TenantId,
                HostId = claim.HostId,
                DeviceId = claim.DeviceId,
                HostSigningPublicKey = SecureRemoteBase64Url.Decode(hostIdentity.SigningPublicKey),
                HostAgreementPublicKey = SecureRemoteBase64Url.Decode(hostIdentity.AgreementPublicKey),
                HostKeyVersion = hostIdentity.KeyVersion,
                DeviceSigningPublicKey = (byte[])claim.DeviceSigningPublicKey.Clone(),
                DeviceAgreementPublicKey = (byte[])claim.DeviceAgreementPublicKey.Clone(),
                DeviceKeyVersion = claim.DeviceKeyVersion,
                RelaySigningPublicKey = SecureRemoteBase64Url.Decode(package.Relay.SigningPublicKey),
                RelaySigningKeyFingerprint = SecureRemoteBase64Url.Decode(package.Relay.SigningKeyFingerprint),
                ApprovedAt = _clock(),
                BindingVersion = bindingVersion
            };
            var transcript = SecureRemoteTranscript.EncodePairingApproval(approval);
            var signature = _identityStore.Sign(transcript);
            var plaintext = SecureRemoteTranscript.EncodePairingApprovalPlaintext(approval, signature);
            var approvalKey = _crypto.HkdfSha256(
                verifiedClaim.PairingBaseKey,
                new byte[0],
                Encoding.UTF8.GetBytes("approval encryption"),
                32);
            var nonce = new byte[12];
            _fillRandom(nonce);
            var header = new PairingApprovalHeader
            {
                ProtocolVersion = 1,
                ClaimId = claim.ClaimId,
                InviteId = claim.InviteId,
                TenantId = claim.TenantId,
                HostId = claim.HostId,
                DeviceId = claim.DeviceId,
                Status = "approved",
                BindingVersion = approval.BindingVersion,
                ApprovalNonce = SecureRemoteBase64Url.Encode(nonce),
                CiphertextLength = checked(plaintext.Length + 16)
            };
            var aad = SecureRemoteTranscript.EncodePairingApprovalHeader(header);
            byte[] tag = null;
            byte[] ciphertext = null;
            try
            {
                ciphertext = _crypto.EncryptAesGcm(
                    approvalKey,
                    nonce,
                    plaintext,
                    aad,
                    out tag);
                var encrypted = new byte[ciphertext.Length + tag.Length];
                Buffer.BlockCopy(ciphertext, 0, encrypted, 0, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, encrypted, ciphertext.Length, tag.Length);
                try
                {
                    return new PairingApprovalRequest
                    {
                        HostId = claim.HostId,
                        ClaimId = claim.ClaimId,
                        DeviceId = claim.DeviceId,
                        DeviceSigningPublicKey = SecureRemoteBase64Url.Encode(claim.DeviceSigningPublicKey),
                        DeviceAgreementPublicKey = SecureRemoteBase64Url.Encode(claim.DeviceAgreementPublicKey),
                        DeviceKeyVersion = claim.DeviceKeyVersion,
                        BindingVersion = approval.BindingVersion,
                        ApprovalHeader = SecureRemoteBase64Url.Encode(aad),
                        ApprovalNonce = header.ApprovalNonce,
                        ApprovalCiphertext = SecureRemoteBase64Url.Encode(encrypted)
                    };
                }
                finally
                {
                    Array.Clear(encrypted, 0, encrypted.Length);
                }
            }
            finally
            {
                Clear(approval.HostSigningPublicKey);
                Clear(approval.HostAgreementPublicKey);
                Clear(approval.DeviceSigningPublicKey);
                Clear(approval.DeviceAgreementPublicKey);
                Clear(approval.RelaySigningPublicKey);
                Clear(approval.RelaySigningKeyFingerprint);
                Clear(transcript);
                Clear(signature);
                Clear(plaintext);
                Clear(approvalKey);
                Clear(nonce);
                Clear(aad);
                Clear(tag);
                Clear(ciphertext);
            }
        }

        private void ValidateLocalInvite(
            ActivePairingInvite invite,
            OpaquePairingClaim opaqueClaim)
        {
            if (invite == null || invite.Package == null || opaqueClaim == null || opaqueClaim.Header == null)
            {
                throw InvalidClaim();
            }

            lock (_sync)
            {
                if (!ReferenceEquals(invite, _activeInvite))
                {
                    throw InvalidClaim();
                }
            }

            if (_clock() > invite.Package.ExpiresAt ||
                !string.Equals(invite.Package.InviteId, opaqueClaim.Header.InviteId, StringComparison.Ordinal) ||
                !string.Equals(invite.Package.TenantId, opaqueClaim.Header.TenantId, StringComparison.Ordinal) ||
                !string.Equals(invite.Package.HostId, opaqueClaim.Header.HostId, StringComparison.Ordinal) ||
                !string.Equals(opaqueClaim.Status, "pending", StringComparison.Ordinal))
            {
                throw InvalidClaim();
            }
        }

        private void RequireCurrentInvite(VerifiedPairingClaim verifiedClaim)
        {
            lock (_sync)
            {
                if (_activeInvite == null ||
                    verifiedClaim.PairingPackage == null ||
                    !ReferenceEquals(_activeInvite.Package, verifiedClaim.PairingPackage) ||
                    !string.Equals(
                        _activeInvite.Package.InviteId,
                        verifiedClaim.Claim.InviteId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The verified pairing invitation is no longer active.");
                }
            }
        }

        private static void ValidateOuterAndInner(
            OpaquePairingClaim opaqueClaim,
            PairingClaim claim)
        {
            var header = opaqueClaim.Header;
            if (claim.ProtocolVersion != 1 ||
                !string.Equals(claim.ClaimId, header.ClaimId, StringComparison.Ordinal) ||
                !string.Equals(claim.InviteId, header.InviteId, StringComparison.Ordinal) ||
                !string.Equals(claim.TenantId, header.TenantId, StringComparison.Ordinal) ||
                !string.Equals(claim.HostId, header.HostId, StringComparison.Ordinal) ||
                !string.Equals(claim.DeviceId, header.DeviceId, StringComparison.Ordinal) ||
                claim.DeviceKeyVersion != header.DeviceKeyVersion ||
                !SecureRemotePairingRules.FixedTimeEquals(
                    claim.DeviceAgreementPublicKey,
                    SecureRemoteBase64Url.Decode(header.DeviceAgreementPublicKey)) ||
                !SecureRemotePairingRules.FixedTimeEquals(
                    claim.ClientNonce,
                    SecureRemoteBase64Url.Decode(header.ClientNonce)) ||
                !string.Equals(opaqueClaim.ClaimId, header.ClaimId, StringComparison.Ordinal) ||
                !string.Equals(opaqueClaim.InviteId, header.InviteId, StringComparison.Ordinal) ||
                !string.Equals(opaqueClaim.TenantId, header.TenantId, StringComparison.Ordinal) ||
                !string.Equals(opaqueClaim.HostId, header.HostId, StringComparison.Ordinal))
            {
                throw InvalidClaim();
            }
        }

        private static void VerifyInviteProof(byte[] inviteSecret, PairingClaim claim)
        {
            var withoutProof = SecureRemoteTranscript.EncodePairingClaim(claim);
            byte[] expected;
            try
            {
                using (var hmac = new HMACSHA256(inviteSecret))
                {
                    expected = hmac.ComputeHash(withoutProof);
                }
            }
            finally
            {
                Array.Clear(withoutProof, 0, withoutProof.Length);
            }

            try
            {
                if (!SecureRemotePairingRules.FixedTimeEquals(expected, claim.InviteProof))
                {
                    throw InvalidClaim();
                }
            }
            finally
            {
                Array.Clear(expected, 0, expected.Length);
            }
        }

        private void VerifyDeviceSignature(PairingClaim claim)
        {
            var transcript = SecureRemoteTranscript.EncodePairingClaimForSignature(claim);
            try
            {
                if (!_crypto.VerifyRawEcdsa(
                    claim.DeviceSigningPublicKey,
                    transcript,
                    claim.DeviceSignature))
                {
                    throw InvalidClaim();
                }
            }
            finally
            {
                Array.Clear(transcript, 0, transcript.Length);
            }
        }

        private void RememberReplayValues(PairingClaim claim)
        {
            var nonce = SecureRemoteBase64Url.Encode(claim.ClientNonce);
            lock (_sync)
            {
                if (!_verifiedClaimIds.Add(claim.ClaimId) ||
                    !_verifiedClientNonces.Add(nonce))
                {
                    throw InvalidClaim();
                }
            }
        }

        private byte[] DerivePairingBaseKey(
            PairingPackage package,
            PairingClaimHeader header,
            byte[] inviteSecret,
            byte[] sharedSecret)
        {
            var packageTranscript = SecureRemoteTranscript.EncodePairingPackage(package);
            var signature = SecureRemoteBase64Url.Decode(package.HostSignature);
            var visibleHeader = SecureRemoteTranscript.EncodeVisiblePairingClaimHeader(header);
            byte[] salt;
            byte[] info;
            try
            {
                using (var sha256 = SHA256.Create())
                {
                    salt = sha256.ComputeHash(inviteSecret);
                    using (var stream = new MemoryStream())
                    {
                        stream.Write(packageTranscript, 0, packageTranscript.Length);
                        WriteCanonicalBytes(stream, signature);
                        stream.Write(visibleHeader, 0, visibleHeader.Length);
                        info = sha256.ComputeHash(stream.ToArray());
                    }
                }

                try
                {
                    return _crypto.HkdfSha256(
                        sharedSecret,
                        salt,
                        info,
                        32);
                }
                finally
                {
                    Array.Clear(salt, 0, salt.Length);
                    Array.Clear(info, 0, info.Length);
                }
            }
            finally
            {
                Array.Clear(packageTranscript, 0, packageTranscript.Length);
                Array.Clear(signature, 0, signature.Length);
                Array.Clear(visibleHeader, 0, visibleHeader.Length);
            }
        }

        private static PairingClaim ParseClaimPlaintext(byte[] plaintext)
        {
            var reader = new CanonicalReader(plaintext);
            if (!string.Equals(
                reader.ReadString(),
                "MYCODEX-PAIRING-CLAIM-V1",
                StringComparison.Ordinal))
            {
                throw InvalidClaim();
            }

            var claim = new PairingClaim
            {
                ProtocolVersion = reader.ReadInt64(),
                ClaimId = reader.ReadString(),
                InviteId = reader.ReadString(),
                TenantId = reader.ReadString(),
                HostId = reader.ReadString(),
                DeviceId = reader.ReadString(),
                DeviceName = reader.ReadString(),
                Platform = reader.ReadString(),
                AppVersion = reader.ReadString(),
                DeviceSigningPublicKey = reader.ReadBytes(),
                DeviceAgreementPublicKey = reader.ReadBytes(),
                DeviceKeyVersion = reader.ReadInt64(),
                ClientNonce = reader.ReadBytes(),
                CreatedAt = reader.ReadInt64(),
                InviteProof = reader.ReadBytes(),
                DeviceSignature = reader.ReadBytes()
            };
            reader.RequireEnd();
            var canonical = SecureRemoteTranscript.EncodePairingClaimPlaintext(claim);
            try
            {
                if (!SecureRemotePairingRules.FixedTimeEquals(canonical, plaintext))
                {
                    throw InvalidClaim();
                }
            }
            finally
            {
                Array.Clear(canonical, 0, canonical.Length);
            }

            return claim;
        }

        private static RemoteDeviceBinding ToPendingBinding(PairingClaim claim)
        {
            return new RemoteDeviceBinding
            {
                ClaimId = claim.ClaimId,
                TenantId = claim.TenantId,
                HostId = claim.HostId,
                DeviceId = claim.DeviceId,
                DeviceName = claim.DeviceName,
                Platform = claim.Platform,
                DeviceSigningPublicKey = SecureRemoteBase64Url.Encode(claim.DeviceSigningPublicKey),
                DeviceAgreementPublicKey = SecureRemoteBase64Url.Encode(claim.DeviceAgreementPublicKey),
                DeviceKeyVersion = checked((int)claim.DeviceKeyVersion),
                BindingVersion = 0,
                State = RemoteBindingState.Pending
            };
        }

        private static void RequireInviteResponse(
            CreatePairingInviteRequest request,
            RelayPairingInvite response)
        {
            if (response == null ||
                !string.Equals(response.TenantId, request.TenantId, StringComparison.Ordinal) ||
                !string.Equals(response.HostId, request.HostId, StringComparison.Ordinal) ||
                !string.Equals(response.InviteId, request.InviteId, StringComparison.Ordinal) ||
                !string.Equals(response.Status, "pending", StringComparison.Ordinal) ||
                response.ExpiresAt != request.ExpiresAt)
            {
                throw new InvalidOperationException("The Relay returned a mismatched pairing invitation.");
            }
        }

        private static string BuildRelayUrl(RelayHostConfig config)
        {
            if (!config.TlsRequired)
            {
                throw new InvalidOperationException("Pairing packages require an HTTPS Relay.");
            }

            var defaultPort = config.RelayPort == 443 ? string.Empty : ":" + config.RelayPort;
            return "https://" + config.RelayHost + defaultPort;
        }

        private static string EncodeCertificateFingerprint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Trim();
            if (normalized.Length != 64)
            {
                throw new InvalidOperationException("The embedded Relay certificate fingerprint is invalid.");
            }

            var bytes = new byte[32];
            try
            {
                for (var index = 0; index < bytes.Length; index++)
                {
                    var high = ParseHexNibble(normalized[index * 2]);
                    var low = ParseHexNibble(normalized[(index * 2) + 1]);
                    if (high < 0 || low < 0)
                    {
                        throw new InvalidOperationException("The embedded Relay certificate fingerprint is invalid.");
                    }

                    bytes[index] = (byte)((high << 4) | low);
                }

                return SecureRemoteBase64Url.Encode(bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static int ParseHexNibble(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return value - 'a' + 10;
            }

            if (value >= 'A' && value <= 'F')
            {
                return value - 'A' + 10;
            }

            return -1;
        }

        private static void WriteCanonicalBytes(Stream output, byte[] value)
        {
            var length = checked((uint)value.Length);
            output.WriteByte((byte)(length >> 24));
            output.WriteByte((byte)(length >> 16));
            output.WriteByte((byte)(length >> 8));
            output.WriteByte((byte)length);
            output.Write(value, 0, value.Length);
        }

        private static long GetCurrentUnixMilliseconds()
        {
            return checked((long)(DateTime.UtcNow - UnixEpoch).TotalMilliseconds);
        }

        private static long GetAbsoluteDifference(long left, long right)
        {
            return left >= right
                ? checked(left - right)
                : checked(right - left);
        }

        private static void FillRandom(byte[] value)
        {
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(value);
            }
        }

        private static InvalidOperationException InvalidClaim()
        {
            return new InvalidOperationException("The pairing claim is invalid.");
        }

        private static void Clear(byte[] value)
        {
            if (value != null)
            {
                Array.Clear(value, 0, value.Length);
            }
        }

        private sealed class CanonicalReader
        {
            private readonly byte[] _value;
            private int _offset;

            public CanonicalReader(byte[] value)
            {
                _value = value ?? throw new ArgumentNullException(nameof(value));
            }

            public string ReadString()
            {
                return Encoding.UTF8.GetString(ReadBytes());
            }

            public byte[] ReadBytes()
            {
                var length = ReadUInt32();
                if (length > int.MaxValue || length > _value.Length - _offset)
                {
                    throw new EndOfStreamException();
                }

                var result = new byte[(int)length];
                Buffer.BlockCopy(_value, _offset, result, 0, result.Length);
                _offset += result.Length;
                return result;
            }

            public long ReadInt64()
            {
                if (_value.Length - _offset < 8)
                {
                    throw new EndOfStreamException();
                }

                ulong value = 0;
                for (var index = 0; index < 8; index++)
                {
                    value = (value << 8) | _value[_offset++];
                }

                return unchecked((long)value);
            }

            public void RequireEnd()
            {
                if (_offset != _value.Length)
                {
                    throw InvalidClaim();
                }
            }

            private uint ReadUInt32()
            {
                if (_value.Length - _offset < 4)
                {
                    throw new EndOfStreamException();
                }

                uint value = 0;
                for (var index = 0; index < 4; index++)
                {
                    value = (value << 8) | _value[_offset++];
                }

                return value;
            }
        }
    }
}
