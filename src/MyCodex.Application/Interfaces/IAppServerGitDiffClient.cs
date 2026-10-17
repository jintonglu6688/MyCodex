using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerGitDiffClient
    {
        Task<AppServerGitDiffToRemoteResult> GetGitDiffToRemoteAsync(
            string workspacePath,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
