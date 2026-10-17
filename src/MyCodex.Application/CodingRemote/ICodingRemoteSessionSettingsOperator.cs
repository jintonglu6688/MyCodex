using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface ICodingRemoteSessionSettingsOperator
    {
        Task<CodingRemoteSessionSettings> GetSessionSettingsAsync(string sessionId, CancellationToken cancellationToken);

        Task<CodingRemoteSessionSettingsUpdateResult> UpdateSessionSettingsAsync(CodingRemoteSessionSettingsUpdateRequest request, CancellationToken cancellationToken);
    }
}
