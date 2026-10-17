using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitCommitHistoryService
    {
        Task<IReadOnlyList<GitCommitHistoryEntry>> ReadCommitHistoryAsync(string workspacePath, int maxCount, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GitCommitHistoryEntry>> ReadCommitHistoryAsync(string workspacePath, string revision, int maxCount, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GitCommitFileEntry>> ReadCommitFilesAsync(string workspacePath, string commitRef, CancellationToken cancellationToken = default);

        Task<GitOperationResult> ReadCommitDiffAsync(string workspacePath, string commitRef, CancellationToken cancellationToken = default);

        Task<GitCommitMessageEntry> ReadCommitMessageAsync(string workspacePath, string commitRef, CancellationToken cancellationToken = default);

        Task<GitOperationResult> RevertCommitAsync(string workspacePath, string commitRef, CancellationToken cancellationToken = default);

        Task<GitOperationResult> RevertCommitsAsync(string workspacePath, IReadOnlyList<string> commitRefs, CancellationToken cancellationToken = default);

        Task<GitOperationResult> CherryPickAsync(string workspacePath, string commitRef, CancellationToken cancellationToken = default);

        Task<GitOperationResult> ResetCommitAsync(string workspacePath, string commitRef, GitResetMode mode, CancellationToken cancellationToken = default);
    }
}
