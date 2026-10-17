using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteInteractionResponder
    {
        Task<CodingRemoteApprovalResponseResult> RespondApprovalAsync(
            CodingRemoteApprovalResponseRequest request,
            CancellationToken cancellationToken);

        Task<CodingRemoteToolInputResponseResult> RespondToolInputAsync(
            CodingRemoteToolInputRespondRequest request,
            CancellationToken cancellationToken);

        Task<CodingRemoteToolInputResponseResult> CancelToolInputAsync(
            CodingRemoteToolInputCancelRequest request,
            CancellationToken cancellationToken);
    }
}
