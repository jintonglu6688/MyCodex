using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitEnvironmentService
    {
        Task<GitEnvironmentSnapshot> ReadStatusAsync(CancellationToken cancellationToken);

        Task SetGlobalUserNameAsync(string value, CancellationToken cancellationToken);

        Task SetGlobalUserEmailAsync(string value, CancellationToken cancellationToken);
    }
}
