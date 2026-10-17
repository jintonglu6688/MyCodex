using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.RemoteRelay
{
    public interface IRelayHostRealtimeClient : IRemoteRealtimeEventSink, IDisposable
    {
        event EventHandler<RelayRealtimeConnectionStatusEventArgs> StatusChanged;

        Task StartAsync(RelayHostConfig config, string hostId, CancellationToken cancellationToken);

        Task StopAsync();
    }
}
