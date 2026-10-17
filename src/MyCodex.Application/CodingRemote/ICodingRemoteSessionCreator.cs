using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteSessionCreator
    {
        Task<CodingRemoteSessionSummary> CreateSessionAsync(CodingRemoteSessionCreateRequest request, CancellationToken cancellationToken);
    }
}
