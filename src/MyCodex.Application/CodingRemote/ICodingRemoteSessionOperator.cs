using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteSessionOperator
    {
        Task<CodingRemoteSessionOperationResult> OperateSessionAsync(CodingRemoteSessionOperationRequest request, CancellationToken cancellationToken);
    }
}
