using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteMessageSender
    {
        Task<CodingRemoteMessageSendResult> SendMessageAsync(CodingRemoteMessageSendRequest request, CancellationToken cancellationToken);
    }
}
