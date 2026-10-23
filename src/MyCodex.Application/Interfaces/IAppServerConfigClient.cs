using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerConfigClient
    {
        Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers);

        Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers, CancellationToken cancellationToken);

        Task WriteConfigBatchAsync(IReadOnlyCollection<AppServerConfigWriteEdit> edits, string filePath = null, string expectedVersion = null, bool reloadUserConfig = false);

        Task<AppServerConfigRequirementsSnapshot> ReadConfigRequirementsAsync();

        Task<List<AppServerModelOption>> ListModelsAsync();

        Task<List<AppServerModelOption>> ListModelsAsync(CancellationToken cancellationToken);

        Task<AppServerPermissionProfileListResult> ListPermissionProfilesAsync(
            string workspacePath = null,
            int? limit = null,
            string cursor = null,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
