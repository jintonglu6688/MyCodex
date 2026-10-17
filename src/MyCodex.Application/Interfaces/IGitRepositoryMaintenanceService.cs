using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IGitRepositoryMaintenanceService
    {
        Task<IReadOnlyList<GitRepositoryDiagnosticEntry>> RunRepositoryDiagnosticsAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> RunRepositoryIntegrityCheckAsync(string workspacePath, CancellationToken cancellationToken = default);

        Task<GitOperationResult> RunRepositoryMaintenanceAsync(string workspacePath, CancellationToken cancellationToken = default);
    }
}
