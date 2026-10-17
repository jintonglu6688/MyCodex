using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerFuzzyFileSearchClient
    {
        Task<AppServerFuzzyFileSearchSessionEvent> StartFuzzyFileSearchAsync(AppServerFuzzyFileSearchRequest request, CancellationToken cancellationToken);
    }
}
