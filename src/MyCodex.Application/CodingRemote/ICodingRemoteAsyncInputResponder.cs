using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteAsyncInputResponder
    {
        Task<CodingRemoteToolInputResponseResult> RespondAsyncInputAsync(
            CodingRemoteToolInputRespondRequest request,
            CancellationToken cancellationToken);

        Task<CodingRemoteToolInputResponseResult> IgnoreAsyncInputAsync(
            CodingRemoteToolInputCancelRequest request,
            CancellationToken cancellationToken);
    }
}
