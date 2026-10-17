using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteWorkspaceOperator
    {
        Task<CodingRemoteWorkspaceOperationResult> OperateWorkspaceAsync(CodingRemoteWorkspaceOperationRequest request, CancellationToken cancellationToken);
    }
}
