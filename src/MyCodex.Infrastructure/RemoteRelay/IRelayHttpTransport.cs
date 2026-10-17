using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public interface IRelayHttpTransport
    {
        Task<RelayHttpResponse> SendAsync(RelayHttpRequest request, CancellationToken cancellationToken);
    }
}

