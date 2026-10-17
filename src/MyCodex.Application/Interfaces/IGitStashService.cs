using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitStashService
    {
        Task<IReadOnlyList<GitStashEntry>> ReadStashesAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GitCommitFileEntry>> ReadStashFilesAsync(string workspacePath, string stashRef, CancellationToken cancellationToken = default);

        Task<GitOperationResult> ReadStashDiffAsync(string workspacePath, string stashRef, CancellationToken cancellationToken = default);

        Task<GitOperationResult> ReadStashFileDiffAsync(string workspacePath, string stashRef, string filePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> StashPushAsync(string workspacePath, string message, bool includeUntracked, CancellationToken cancellationToken = default);

        Task<GitOperationResult> StashPushPathsAsync(string workspacePath, string message, bool includeUntracked, IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

        Task<GitOperationResult> StashApplyAsync(string workspacePath, string stashRef, CancellationToken cancellationToken = default);

        Task<GitOperationResult> StashPopAsync(string workspacePath, string stashRef, CancellationToken cancellationToken = default);

        Task<GitOperationResult> StashDropAsync(string workspacePath, string stashRef, CancellationToken cancellationToken = default);

        Task<GitOperationResult> StashBranchAsync(string workspacePath, string stashRef, string branchName, CancellationToken cancellationToken = default);
    }
}
