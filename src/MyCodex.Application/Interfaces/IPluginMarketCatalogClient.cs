using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IPluginMarketCatalogClient
    {
        Task<PluginMarketCatalogSnapshot> LoadIndexAsync(CancellationToken cancellationToken);

        Task<PluginMarketSnapshot> LoadMarketAsync(string manifestUrl, CancellationToken cancellationToken);
    }
}
