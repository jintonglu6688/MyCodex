using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IEmbeddedRelayService
    {
        EmbeddedRelayStatus GetStatus(RemoteServiceConfigSnapshot snapshot);

        Task<EmbeddedRelayStartResult> StartAsync(RemoteServiceConfigSnapshot snapshot, CancellationToken cancellationToken);

        Task StopAsync(CancellationToken cancellationToken);

        EmbeddedRelayDiagnosticInfo GetDiagnostics(RemoteServiceConfigSnapshot snapshot);
    }
}
