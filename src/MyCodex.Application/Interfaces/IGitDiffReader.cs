using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitDiffReader
    {
        Task<GitOperationResult> ReadDiffAsync(string workspacePath, GitChangedFileEntry entry, CancellationToken cancellationToken = default);

        Task<GitOperationResult> ReadDiffAsync(string workspacePath, GitChangedFileEntry entry, GitDiffKind diffKind, CancellationToken cancellationToken = default);

        Task<GitOperationResult> ReadConflictDetailsAsync(string workspacePath, GitChangedFileEntry entry, CancellationToken cancellationToken = default);
    }
}
