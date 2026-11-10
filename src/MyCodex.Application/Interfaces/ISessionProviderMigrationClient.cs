using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface ISessionProviderMigrationClient
    {
        Task RecoverSessionProvidersAsync(CancellationToken cancellationToken);
        Task<SessionProviderMigrationResult> PrepareSessionProviderAsync(string threadId,
            string modelProvider, bool apply, CancellationToken cancellationToken);
    }

}
