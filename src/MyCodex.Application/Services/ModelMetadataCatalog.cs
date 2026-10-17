using System;
using System.Collections.Generic;
using System.Linq;

namespace MyCodex.Application.Services
{
    // models.dev providers and their model entries, indexed once for lookups.
    public sealed class ModelMetadataCatalog
    {
        private readonly Dictionary<string, ModelMetadataProvider> _providersById;
        private readonly Dictionary<string, List<KeyValuePair<string, ModelMetadataEntry>>> _entriesByNormalizedId;
        private readonly HashSet<string> _vendors;

        public ModelMetadataCatalog(IEnumerable<ModelMetadataProvider> providers)
        {
            Providers = (providers ?? Enumerable.Empty<ModelMetadataProvider>()).Where(provider => provider != null).ToList();
            _providersById = new Dictionary<string, ModelMetadataProvider>(StringComparer.Ordinal);
            _entriesByNormalizedId = new Dictionary<string, List<KeyValuePair<string, ModelMetadataEntry>>>(StringComparer.Ordinal);
            _vendors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var provider in Providers)
            {
                if (!_providersById.ContainsKey(provider.Id)) _providersById.Add(provider.Id, provider);
                foreach (var entry in provider.Models)
                {
                    var normalized = ModelMetadataResolver.NormalizeModelId(entry.Key);
                    if (normalized.Length > 0)
                    {
                        if (!_entriesByNormalizedId.TryGetValue(normalized, out var list))
                        {
                            list = new List<KeyValuePair<string, ModelMetadataEntry>>();
                            _entriesByNormalizedId.Add(normalized, list);
                        }
                        list.Add(new KeyValuePair<string, ModelMetadataEntry>(provider.Id, entry));
                    }
                    var vendor = VendorOf(entry.CanonicalModelId);
                    if (vendor != null) _vendors.Add(vendor);
                }
            }
        }

        public IReadOnlyList<ModelMetadataProvider> Providers { get; }

        internal ModelMetadataProvider FindProvider(string id)
        {
            return id != null && _providersById.TryGetValue(id, out var provider) ? provider : null;
        }

        internal IReadOnlyList<KeyValuePair<string, ModelMetadataEntry>> FindByNormalizedId(string normalized)
        {
            return _entriesByNormalizedId.TryGetValue(normalized, out var list)
                ? list : (IReadOnlyList<KeyValuePair<string, ModelMetadataEntry>>)new KeyValuePair<string, ModelMetadataEntry>[0];
        }

        internal bool IsVendor(string providerId)
        {
            return _vendors.Contains(providerId);
        }

        // Resellers point at the maker's entry, for example "openai/gpt-5.5".
        internal static string VendorOf(string canonicalModelId)
        {
            var separator = canonicalModelId?.IndexOf('/') ?? -1;
            return separator > 0 ? canonicalModelId.Substring(0, separator) : null;
        }
    }

    public sealed class ModelMetadataProvider
    {
        private readonly Dictionary<string, ModelMetadataEntry> _models;

        public ModelMetadataProvider(string id, string api, IEnumerable<ModelMetadataEntry> models)
        {
            Id = id ?? string.Empty;
            Api = api;
            Models = (models ?? Enumerable.Empty<ModelMetadataEntry>()).Where(entry => entry != null && !string.IsNullOrEmpty(entry.Key)).ToList();
            _models = new Dictionary<string, ModelMetadataEntry>(StringComparer.Ordinal);
            foreach (var entry in Models)
            {
                if (!_models.ContainsKey(entry.Key)) _models.Add(entry.Key, entry);
            }
        }

        public string Id { get; }
        // API base address that identifies this provider by the user's base URL.
        public string Api { get; }
        public IReadOnlyList<ModelMetadataEntry> Models { get; }

        internal ModelMetadataEntry FindExact(string key)
        {
            return _models.TryGetValue(key, out var entry) ? entry : null;
        }
    }

    public sealed class ModelMetadataEntry
    {
        // Key is the provider-local model ID; ModelId is the entry's own "id" field.
        public string Key { get; set; }
        public string ModelId { get; set; }
        public long? ContextWindow { get; set; }
        public IReadOnlyList<string> ReasoningEfforts { get; set; }
        public string CanonicalModelId { get; set; }
    }
}
