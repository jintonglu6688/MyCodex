using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.RemoteRelay
{
    public enum RelayTicketPurpose
    {
        CreatePairingInvite,
        CancelPairingInvite,
        ListPairingClaims,
        ApprovePairingClaim,
        RejectPairingClaim,
        DeviceList,
        DeviceRevoke,
        WebSocketHost,
        WebSocketDevice
    }

    public interface IRelayHostClient
    {
        Task<RelayMetadata> GetMetadataAsync(
            RelayHostConfig config,
            CancellationToken cancellationToken);

        Task<RelayHostRegistrationResult> EnrollHostAsync(
            RelayHostConfig config,
            RemoteEndpointIdentity identity,
            CancellationToken cancellationToken);

        Task<RelayPairingInvite> CreatePairingInviteAsync(
            RelayHostConfig config,
            CreatePairingInviteRequest request,
            CancellationToken cancellationToken);

        Task CancelPairingInviteAsync(
            RelayHostConfig config,
            string inviteId,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<OpaquePairingClaim>> ListPairingClaimsAsync(
            RelayHostConfig config,
            string hostId,
            CancellationToken cancellationToken);

        Task ApprovePairingClaimAsync(
            RelayHostConfig config,
            PairingApprovalRequest request,
            CancellationToken cancellationToken);

        Task RejectPairingClaimAsync(
            RelayHostConfig config,
            string hostId,
            string claimId,
            string deviceId,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<RelayDeviceSummary>> ListDevicesAsync(
            RelayHostConfig config,
            string hostId,
            CancellationToken cancellationToken);

        Task<bool> RevokeDeviceAsync(
            RelayHostConfig config,
            string hostId,
            string deviceId,
            CancellationToken cancellationToken);
    }
}

