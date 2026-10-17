using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemotePlanExecutionResponder
    {
        Task<CodingRemotePlanExecutionResponseResult> RespondPlanExecutionAsync(
            CodingRemotePlanExecutionResponseRequest request,
            CancellationToken cancellationToken);
    }
}
