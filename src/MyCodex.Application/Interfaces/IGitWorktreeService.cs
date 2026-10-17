using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitWorktreeService
    {
        Task<IReadOnlyList<GitWorktreeEntry>> ReadWorktreesAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> CreateWorktreeAsync(string workspacePath, string targetPath, string branchName, string startPoint, CancellationToken cancellationToken = default);

        Task<GitOperationResult> RemoveWorktreeAsync(string workspacePath, string worktreePath, bool force, CancellationToken cancellationToken = default);

        Task<GitOperationResult> PruneWorktreesAsync(string workspacePath, CancellationToken cancellationToken = default);
    }
}
