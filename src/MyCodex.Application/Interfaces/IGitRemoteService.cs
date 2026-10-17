using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitRemoteService
    {
        Task<IReadOnlyList<GitRemoteEntry>> ReadRemotesAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> InitializeRepositoryAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> AddRemoteAsync(string workspacePath, string remoteName, string remoteUrl, CancellationToken cancellationToken = default);

        Task<GitOperationResult> SetRemoteUrlAsync(string workspacePath, string remoteName, string remoteUrl, CancellationToken cancellationToken = default);

        Task<GitOperationResult> SetRemotePushUrlAsync(string workspacePath, string remoteName, string remoteUrl, CancellationToken cancellationToken = default);

        Task<GitOperationResult> RemoveRemoteAsync(string workspacePath, string remoteName, CancellationToken cancellationToken = default);

        Task<GitOperationResult> SetUpstreamAsync(string workspacePath, string branchName, string upstream, CancellationToken cancellationToken = default);

        Task<GitOperationResult> UnsetUpstreamAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);
    }
}
