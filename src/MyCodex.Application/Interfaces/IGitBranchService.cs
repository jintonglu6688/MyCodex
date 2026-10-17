using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitBranchService
    {
        Task<IReadOnlyList<GitBranchEntry>> ReadBranchesAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitBranchComparison> CompareBranchesAsync(string workspacePath, string currentRef, string targetRef, CancellationToken cancellationToken = default);

        Task<GitOperationResult> MergeBranchIntoCurrentAsync(string workspacePath, string branchName, bool squash, CancellationToken cancellationToken = default);

        Task<GitOperationResult> RebaseCurrentBranchOntoAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> CheckoutBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> CheckoutRemoteBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> CreateBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> RenameCurrentBranchAsync(string workspacePath, string newBranchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> RenameBranchAsync(string workspacePath, string oldBranchName, string newBranchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> DeleteBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> ForceDeleteBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> DeleteRemoteBranchAsync(string workspacePath, string remoteName, string branchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> CheckoutPullRequestAsync(string workspacePath, string remoteName, int pullRequestNumber, string localBranchName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> PushForceWithLeaseAsync(string workspacePath, CancellationToken cancellationToken = default);
    }
}
