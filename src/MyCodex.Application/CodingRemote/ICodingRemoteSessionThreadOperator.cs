using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteSessionThreadOperator
    {
        Task<CodingRemoteSessionThreadOperationResult> OperateSessionThreadAsync(
            CodingRemoteSessionThreadOperationRequest request,
            CancellationToken cancellationToken);
    }
}
