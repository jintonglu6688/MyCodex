using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteThreadMessageReader
    {
        Task<CodingRemoteMessagePage> ListMessagesAsync(CodingRemoteMessagesListRequest request, CancellationToken cancellationToken);

        Task<CodingRemoteMessageContentPage> ReadMessageContentAsync(CodingRemoteMessageContentRequest request, CancellationToken cancellationToken);
    }
}
