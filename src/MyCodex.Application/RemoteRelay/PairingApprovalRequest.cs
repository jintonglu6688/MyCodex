namespace MyCodex.Application.RemoteRelay
{
    public sealed class PairingApprovalRequest
    {
        public string HostId { get; set; }

        public string ClaimId { get; set; }

        public string DeviceId { get; set; }

        public string DeviceSigningPublicKey { get; set; }

        public string DeviceAgreementPublicKey { get; set; }

        public long DeviceKeyVersion { get; set; }

        public long BindingVersion { get; set; }

        public string ApprovalHeader { get; set; }

        public string ApprovalNonce { get; set; }

        public string ApprovalCiphertext { get; set; }
    }
}
