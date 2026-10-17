using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Services;

namespace MyCodex.Application.Interfaces
{
    // Public model metadata used only to suggest blank model fields.
    public interface IModelMetadataCatalogSource
    {
        // Data already loaded in this process; never starts I/O.
        ModelMetadataCatalog Cached { get; }

        // Returns null when no data is available; only cancellation is thrown.
        Task<ModelMetadataCatalog> LoadAsync(CancellationToken cancellationToken);
    }
}
