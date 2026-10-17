using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.RemoteRelay
{
    public interface ISecureRemotePairingWorkflow
    {
        Task<ActivePairingInvite> CreatePairingInviteAsync(
            RelayHostConfig config,
            string hostId,
            string hostName,
            CancellationToken cancellationToken);

        VerifiedPairingClaim VerifyClaim(
            ActivePairingInvite invite,
            OpaquePairingClaim opaqueClaim);

        Task ApprovePairingClaimAsync(
            RelayHostConfig config,
            VerifiedPairingClaim verifiedClaim,
            CancellationToken cancellationToken);

        Task RetryPendingApprovalAsync(
            RelayHostConfig config,
            string claimId,
            CancellationToken cancellationToken);
    }
}
