using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteSessionStopper
    {
        Task<CodingRemoteSessionStopResult> StopSessionAsync(
            CodingRemoteSessionStopRequest request,
            CancellationToken cancellationToken);
    }
}
