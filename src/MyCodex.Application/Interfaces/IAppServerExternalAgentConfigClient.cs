using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerExternalAgentConfigClient
    {
        Task<ExternalAgentConfigDetectResult> DetectExternalAgentConfigAsync(
            IReadOnlyCollection<string> workingDirectories,
            bool includeHome,
            CancellationToken cancellationToken);

        Task ImportExternalAgentConfigAsync(
            IReadOnlyCollection<ExternalAgentConfigMigrationItem> migrationItems,
            CancellationToken cancellationToken);
    }
}
