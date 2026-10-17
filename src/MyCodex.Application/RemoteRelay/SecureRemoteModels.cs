using System.Runtime.Serialization;

namespace MyCodex.Application.RemoteRelay
{
    public enum RemoteBindingState
    {
        Pending,
        Active,
        Revoked
    }

    [DataContract]
    public sealed class RemoteDeviceBinding
    {
        [DataMember(Name = "claimId")]
        public string ClaimId { get; set; }

        [DataMember(Name = "tenantId")]
        public string TenantId { get; set; }

        [DataMember(Name = "hostId")]
        public string HostId { get; set; }

        [DataMember(Name = "deviceId")]
        public string DeviceId { get; set; }

        [DataMember(Name = "deviceName")]
        public string DeviceName { get; set; }

        [DataMember(Name = "platform")]
        public string Platform { get; set; }

        [DataMember(Name = "deviceSigningPublicKey")]
        public string DeviceSigningPublicKey { get; set; }

        [DataMember(Name = "deviceAgreementPublicKey")]
        public string DeviceAgreementPublicKey { get; set; }

        [DataMember(Name = "deviceKeyVersion")]
        public int DeviceKeyVersion { get; set; }

        [DataMember(Name = "bindingVersion")]
        public int BindingVersion { get; set; }

        [DataMember(Name = "state")]
        public RemoteBindingState State { get; set; }

        [DataMember(Name = "approvalHeader")]
        public string ApprovalHeader { get; set; }

        [DataMember(Name = "approvalNonce")]
        public string ApprovalNonce { get; set; }

        [DataMember(Name = "approvalCiphertext")]
        public string ApprovalCiphertext { get; set; }
    }

    public sealed class RemoteEndpointIdentity
    {
        public string IdentityId { get; set; }

        public int KeyVersion { get; set; }

        public string SigningPublicKey { get; set; }

        public string AgreementPublicKey { get; set; }
    }

    [DataContract]
    public sealed class RelayMetadata
    {
        [DataMember(Name = "protocolVersion")]
        public long ProtocolVersion { get; set; }

        [DataMember(Name = "signingPublicKey")]
        public string SigningPublicKey { get; set; }

        [DataMember(Name = "signingKeyFingerprint")]
        public string SigningKeyFingerprint { get; set; }

        [DataMember(Name = "serverTime")]
        public long ServerTime { get; set; }

        [DataMember(Name = "maxRequestBytes")]
        public long MaxRequestBytes { get; set; }

        [DataMember(Name = "maxMessageBytes")]
        public long MaxMessageBytes { get; set; }
    }

    public sealed class CreatePairingInviteRequest
    {
        public string TenantId { get; set; }

        public string HostId { get; set; }

        public string InviteId { get; set; }

        public long ExpiresAt { get; set; }
    }

    [DataContract]
    public sealed class PairingRelay
    {
        [DataMember(Name = "url")]
        public string Url { get; set; }

        [DataMember(Name = "signingPublicKey")]
        public string SigningPublicKey { get; set; }

        [DataMember(Name = "signingKeyFingerprint")]
        public string SigningKeyFingerprint { get; set; }

        [DataMember(Name = "embeddedTlsCertificateSha256")]
        public string EmbeddedTlsCertificateSha256 { get; set; }
    }

    [DataContract]
    public sealed class PairingPackage
    {
        [DataMember(Name = "version")]
        public long Version { get; set; }

        [DataMember(Name = "relay")]
        public PairingRelay Relay { get; set; }

        [DataMember(Name = "tenantId")]
        public string TenantId { get; set; }

        [DataMember(Name = "hostId")]
        public string HostId { get; set; }

        [DataMember(Name = "hostName")]
        public string HostName { get; set; }

        [DataMember(Name = "inviteId")]
        public string InviteId { get; set; }

        [DataMember(Name = "inviteSecret")]
        public string InviteSecret { get; set; }

        [DataMember(Name = "expiresAt")]
        public long ExpiresAt { get; set; }

        [DataMember(Name = "hostSigningPublicKey")]
        public string HostSigningPublicKey { get; set; }

        [DataMember(Name = "hostAgreementPublicKey")]
        public string HostAgreementPublicKey { get; set; }

        [DataMember(Name = "hostKeyVersion")]
        public long HostKeyVersion { get; set; }

        [DataMember(Name = "hostSignature")]
        public string HostSignature { get; set; }
    }

    public sealed class PairingClaim
    {
        public long ProtocolVersion { get; set; }

        public string ClaimId { get; set; }

        public string InviteId { get; set; }

        public string TenantId { get; set; }

        public string HostId { get; set; }

        public string DeviceId { get; set; }

        public string DeviceName { get; set; }

        public string Platform { get; set; }

        public string AppVersion { get; set; }

        public byte[] DeviceSigningPublicKey { get; set; }

        public byte[] DeviceAgreementPublicKey { get; set; }

        public long DeviceKeyVersion { get; set; }

        public byte[] ClientNonce { get; set; }

        public long CreatedAt { get; set; }

        public byte[] InviteProof { get; set; }

        public byte[] DeviceSignature { get; set; }
    }

    [DataContract]
    public sealed class PairingClaimHeader
    {
        [IgnoreDataMember]
        public long ProtocolVersion { get; set; }

        [DataMember(Name = "inviteId")]
        public string InviteId { get; set; }

        [DataMember(Name = "tenantId")]
        public string TenantId { get; set; }

        [DataMember(Name = "hostId")]
        public string HostId { get; set; }

        [DataMember(Name = "claimId")]
        public string ClaimId { get; set; }

        [DataMember(Name = "deviceId")]
        public string DeviceId { get; set; }

        [DataMember(Name = "deviceAgreementPublicKey")]
        public string DeviceAgreementPublicKey { get; set; }

        [DataMember(Name = "deviceKeyVersion")]
        public long DeviceKeyVersion { get; set; }

        [DataMember(Name = "clientNonce")]
        public string ClientNonce { get; set; }

        [DataMember(Name = "ciphertextLength")]
        public long CiphertextLength { get; set; }
    }

    public sealed class OpaquePairingClaim
    {
        public string ClaimId { get; set; }

        public string InviteId { get; set; }

        public string TenantId { get; set; }

        public string HostId { get; set; }

        public string Status { get; set; }

        public PairingClaimHeader Header { get; set; }

        public string Nonce { get; set; }

        public string Ciphertext { get; set; }

        public long CreatedAt { get; set; }

        public long ExpiresAt { get; set; }
    }

    public sealed class ActivePairingInvite
    {
        public RelayPairingInvite RelayInvite { get; set; }

        public PairingPackage Package { get; set; }

        public string PairingUri { get; set; }
    }

    public sealed class VerifiedPairingClaim
    {
        public PairingClaim Claim { get; set; }

        public string ShortCode { get; set; }

        public byte[] PairingBaseKey { get; set; }

        public byte[] VerifiedPlaintext { get; set; }

        public PairingPackage PairingPackage { get; set; }
    }

    public sealed class PairingApproval
    {
        public long ProtocolVersion { get; set; }

        public string BindingId { get; set; }

        public string TenantId { get; set; }

        public string HostId { get; set; }

        public string DeviceId { get; set; }

        public byte[] HostSigningPublicKey { get; set; }

        public byte[] HostAgreementPublicKey { get; set; }

        public long HostKeyVersion { get; set; }

        public byte[] DeviceSigningPublicKey { get; set; }

        public byte[] DeviceAgreementPublicKey { get; set; }

        public long DeviceKeyVersion { get; set; }

        public byte[] RelaySigningPublicKey { get; set; }

        public byte[] RelaySigningKeyFingerprint { get; set; }

        public long ApprovedAt { get; set; }

        public long BindingVersion { get; set; }
    }

    [DataContract]
    public sealed class PairingApprovalHeader
    {
        [IgnoreDataMember]
        public long ProtocolVersion { get; set; }

        [DataMember(Name = "claimId")]
        public string ClaimId { get; set; }

        [DataMember(Name = "inviteId")]
        public string InviteId { get; set; }

        [DataMember(Name = "tenantId")]
        public string TenantId { get; set; }

        [DataMember(Name = "hostId")]
        public string HostId { get; set; }

        [DataMember(Name = "deviceId")]
        public string DeviceId { get; set; }

        [DataMember(Name = "status")]
        public string Status { get; set; }

        [DataMember(Name = "bindingVersion")]
        public long BindingVersion { get; set; }

        [DataMember(Name = "approvalNonce")]
        public string ApprovalNonce { get; set; }

        [DataMember(Name = "ciphertextLength")]
        public long CiphertextLength { get; set; }
    }

    [DataContract]
    public sealed class AuthChallenge
    {
        [DataMember(Name = "protocolVersion")]
        public long ProtocolVersion { get; set; }

        [DataMember(Name = "relayFingerprint")]
        public string RelaySigningKeyFingerprint { get; set; }

        [DataMember(Name = "challengeId")]
        public string ChallengeId { get; set; }

        [DataMember(Name = "challenge")]
        public string Challenge { get; set; }

        [DataMember(Name = "subjectType")]
        public string SubjectType { get; set; }

        [DataMember(Name = "subjectId")]
        public string SubjectId { get; set; }

        [DataMember(Name = "tenantId")]
        public string TenantId { get; set; }

        [DataMember(Name = "hostId")]
        public string HostId { get; set; }

        [DataMember(Name = "deviceId")]
        public string DeviceId { get; set; }

        [DataMember(Name = "purpose")]
        public string Purpose { get; set; }

        [DataMember(Name = "issuedAt")]
        public long IssuedAt { get; set; }

        [DataMember(Name = "expiresAt")]
        public long ExpiresAt { get; set; }

        [DataMember(Name = "relaySignature")]
        public string RelaySignature { get; set; }
    }

    [DataContract]
    public sealed class SessionHello
    {
        [DataMember(Name = "protocolVersion")]
        public long ProtocolVersion { get; set; }

        [DataMember(Name = "frameType")]
        public string FrameType { get; set; }

        [DataMember(Name = "sessionId")]
        public string SessionId { get; set; }

        [DataMember(Name = "tenantId")]
        public string TenantId { get; set; }

        [DataMember(Name = "hostId")]
        public string HostId { get; set; }

        [DataMember(Name = "deviceId")]
        public string DeviceId { get; set; }

        [DataMember(Name = "direction")]
        public string Direction { get; set; }

        [DataMember(Name = "bindingVersion")]
        public long BindingVersion { get; set; }

        [DataMember(Name = "keyVersion")]
        public long KeyVersion { get; set; }

        [DataMember(Name = "ephemeralPublicKey")]
        public string EphemeralPublicKey { get; set; }

        [DataMember(Name = "nonce")]
        public string Nonce { get; set; }

        [DataMember(Name = "createdAt")]
        public long CreatedAt { get; set; }

        [DataMember(Name = "signature")]
        public string Signature { get; set; }
    }

    [DataContract]
    public sealed class SecureRelayEnvelope
    {
        [DataMember(Name = "protocolVersion")]
        public long ProtocolVersion { get; set; }

        [DataMember(Name = "frameType")]
        public string FrameType { get; set; }

        [DataMember(Name = "sessionId")]
        public string SessionId { get; set; }

        [DataMember(Name = "tenantId")]
        public string TenantId { get; set; }

        [DataMember(Name = "hostId")]
        public string HostId { get; set; }

        [DataMember(Name = "deviceId")]
        public string DeviceId { get; set; }

        [DataMember(Name = "direction")]
        public string Direction { get; set; }

        [DataMember(Name = "kind")]
        public string Kind { get; set; }

        [DataMember(Name = "messageId")]
        public string MessageId { get; set; }

        [DataMember(Name = "sequence")]
        public ulong Sequence { get; set; }

        [DataMember(Name = "createdAt")]
        public long CreatedAt { get; set; }

        [DataMember(Name = "payloadEncoding")]
        public string PayloadEncoding { get; set; }

        [DataMember(Name = "nonce")]
        public string Nonce { get; set; }

        [DataMember(Name = "ciphertext")]
        public string Ciphertext { get; set; }
    }

    [DataContract]
    public sealed class SessionConfirmationFrame
    {
        [DataMember(Name = "protocolVersion")]
        public long ProtocolVersion { get; set; }

        [DataMember(Name = "frameType")]
        public string FrameType { get; set; }

        [DataMember(Name = "sessionId")]
        public string SessionId { get; set; }

        [DataMember(Name = "tenantId")]
        public string TenantId { get; set; }

        [DataMember(Name = "hostId")]
        public string HostId { get; set; }

        [DataMember(Name = "deviceId")]
        public string DeviceId { get; set; }

        [DataMember(Name = "direction")]
        public string Direction { get; set; }

        [DataMember(Name = "senderRole")]
        public string SenderRole { get; set; }

        [DataMember(Name = "confirmation")]
        public string Confirmation { get; set; }
    }
}
