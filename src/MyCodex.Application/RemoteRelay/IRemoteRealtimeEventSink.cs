using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.RemoteRelay
{
    public interface IRemoteRealtimeEventSink
    {
        Task PublishAsync(RemoteRelayEvent remoteEvent, CancellationToken cancellationToken);
    }
}
