using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerLocalThreadCatalogClient
    {
        Task<bool> ConfirmThreadMissingAsync(string threadId, string sessionPath);

        Task<IReadOnlyList<string>> ListLocalCodexHomePathsForThreadScanAsync();

        Task<List<AppServerThreadSummary>> ListLocalThreadSummariesForThreadScanAsync(int maxSummaries);
    }
}
