using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    // Supplies presets and public catalog data for suggesting blank model fields.
    // Every failure degrades to fewer suggestions; model retrieval never depends on it.
    public sealed class AuthenticationModelMetadataService
    {
        private readonly IModelMetadataCatalogSource _catalog;
        private readonly Func<CancellationToken, Task<IReadOnlyList<AuthenticationProviderPreset>>> _readPresets;
        private IReadOnlyList<AuthenticationProviderPreset> _presets;

        public AuthenticationModelMetadataService(IModelMetadataCatalogSource catalog,
            Func<CancellationToken, Task<IReadOnlyList<AuthenticationProviderPreset>>> readPresets)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _readPresets = readPresets;
        }

        public ModelMetadataCatalog CachedCatalog => _catalog.Cached;

        public async Task<IReadOnlyList<AuthenticationProviderPreset>> ReadPresetsAsync(CancellationToken cancellationToken)
        {
            if (_presets != null || _readPresets == null) return _presets ?? new AuthenticationProviderPreset[0];
            try
            {
                _presets = await _readPresets(cancellationToken) ?? new AuthenticationProviderPreset[0];
                return _presets;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                // Retry on the next retrieval; suggestions continue without presets.
                return new AuthenticationProviderPreset[0];
            }
        }

        public async Task<ModelMetadataCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
        {
            try { return await _catalog.LoadAsync(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
        }
    }
}
