using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitSshKeyService
    {
        Task<GitSshKeySnapshot> ReadKeysAsync(CancellationToken cancellationToken);

        Task<GitSshTestResult> TestGitHubSshAsync(CancellationToken cancellationToken);
    }
}
