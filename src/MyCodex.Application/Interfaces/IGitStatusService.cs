using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitStatusService
    {
        Task<GitRepositoryStatusSnapshot> ReadStatusAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> StageAsync(string workspacePath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

        Task<GitOperationResult> MarkConflictsResolvedAsync(string workspacePath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

        Task<GitOperationResult> StageAllAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> UnstageAsync(string workspacePath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

        Task<GitOperationResult> UnstageAllAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> CommitAsync(string workspacePath, string message, CancellationToken cancellationToken = default);

        Task<GitOperationResult> CommitAsync(string workspacePath, string subject, string body, CancellationToken cancellationToken = default);

        Task<GitOperationResult> CommitAmendAsync(string workspacePath, string message, CancellationToken cancellationToken = default);

        Task<GitOperationResult> CommitAmendAsync(string workspacePath, string subject, string body, CancellationToken cancellationToken = default);

        Task<GitOperationResult> ContinueSequencerAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> AbortSequencerAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> SkipSequencerAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> FetchAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> PullAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> PushAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> PushSetUpstreamAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> DiscardUnstagedAsync(string workspacePath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

        Task<GitOperationResult> DeleteUntrackedAsync(string workspacePath, IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
    }
}
