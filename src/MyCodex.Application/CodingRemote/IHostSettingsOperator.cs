using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public interface IHostSettingsOperator
    {
        Task<HostSettingsSnapshot> GetAsync(CancellationToken cancellationToken);

        Task<HostSettingsUpdateResult> UpdateAsync(
            HostSettingsUpdateRequest request,
            CancellationToken cancellationToken);
    }
}
