using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    [Flags]
    public enum ModelMetadataSources
    {
        None = 0,
        Preset = 1,
        ModelsDev = 2
    }

    public sealed class ModelMetadataMatch
    {
        public long? ContextWindow { get; internal set; }
        public IReadOnlyList<string> ReasoningEfforts { get; internal set; }
        // Sources that actually contributed a field, in priority order.
        public ModelMetadataSources Sources { get; internal set; }
    }

    // Suggests blank model fields from presets and models.dev, field by field:
    // 1. a preset for the same endpoint (hand-checked values for that deployment);
    // 2. the models.dev provider recognized by the same endpoint;
    // 3. the maker's models.dev entry, which relays normally share.
    // Mirrors CC Switch 4 resolveModelMetadata/fillCodexCatalogModel.
    public sealed class ModelMetadataResolver
    {
        // Levels Codex understands, in ascending depth; other values are dropped.
        public static readonly IReadOnlyList<string> CodexReasoningLevels = new[]
        {
            "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra"
        };

        private static readonly Regex SchemePrefix = new Regex("^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex VersionSegment = new Regex(@"^v\d+(?:(?:alpha|beta)\d*)?$", RegexOptions.CultureInvariant);
        private readonly List<AuthenticationProviderPreset> _presets;
        private readonly List<ModelMetadataProvider> _providers;
        private readonly ModelMetadataCatalog _catalog;

        public ModelMetadataResolver(string baseUrl, IEnumerable<AuthenticationProviderPreset> presets, ModelMetadataCatalog catalog)
        {
            var endpoint = EndpointKey(baseUrl);
            _catalog = catalog;
            _presets = endpoint == null ? new List<AuthenticationProviderPreset>() : (presets ?? Enumerable.Empty<AuthenticationProviderPreset>())
                .Where(preset => preset?.Models != null && preset.Models.Count > 0 && EndpointKey(preset.BaseUrl) == endpoint).ToList();
            _providers = endpoint == null || catalog == null ? new List<ModelMetadataProvider>() : catalog.Providers
                .Where(provider => EndpointKey(provider.Api) == endpoint).ToList();
        }

        public ModelMetadataMatch Resolve(string modelId)
        {
            var id = modelId?.Trim();
            if (string.IsNullOrEmpty(id)) return null;
            var match = new ModelMetadataMatch();
            foreach (var preset in _presets)
            {
                var model = Find(preset.Models, item => item?.Model, id);
                if (model == null) continue;
                Merge(match, ModelMetadataSources.Preset, Positive(model.ContextWindow), Efforts(model.ReasoningLevels));
                break;
            }
            if (_catalog != null)
            {
                foreach (var provider in _providers)
                {
                    var entry = provider.FindExact(id) ?? Find(provider.Models, item => item.Key, id);
                    if (entry == null) continue;
                    Merge(match, ModelMetadataSources.ModelsDev, Positive(entry.ContextWindow), Efforts(entry.ReasoningEfforts));
                    break;
                }
                var vendor = FindByVendor(id);
                if (vendor != null)
                {
                    Merge(match, ModelMetadataSources.ModelsDev, Positive(vendor.ContextWindow), Efforts(vendor.ReasoningEfforts));
                }
            }
            return match.Sources == ModelMetadataSources.None ? null : match;
        }

        // Fills blank fields only and returns the sources used, or None when unchanged.
        public ModelMetadataSources FillMissing(AuthenticationCatalogModel row)
        {
            var match = row == null ? null : Resolve(row.Model);
            return match != null && FillMissing(row, match) ? match.Sources : ModelMetadataSources.None;
        }

        public static bool FillMissing(AuthenticationCatalogModel row, ModelMetadataMatch match)
        {
            if (row == null || match == null) return false;
            var changed = false;
            if ((!row.ContextWindow.HasValue || row.ContextWindow.Value <= 0) && match.ContextWindow.HasValue)
            {
                row.ContextWindow = match.ContextWindow;
                changed = true;
            }
            if ((row.ReasoningLevels == null || row.ReasoningLevels.Count == 0) && match.ReasoningEfforts != null)
            {
                var levels = CodexReasoningLevels.Where(level => match.ReasoningEfforts.Contains(level)).ToList();
                // A saved default must remain one of the levels.
                if (levels.Count > 0 && (string.IsNullOrEmpty(row.DefaultReasoningLevel) || levels.Contains(row.DefaultReasoningLevel)))
                {
                    row.ReasoningLevels = levels;
                    changed = true;
                }
            }
            return changed;
        }

        // Host plus path without trailing version (v1, v4, v1beta) or request
        // segments; any other path difference denotes a different plan.
        internal static string EndpointKey(string url)
        {
            var text = (url ?? string.Empty).Trim();
            if (text.Length == 0) return null;
            if (!SchemePrefix.IsMatch(text)) text = "https://" + text;
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return null;
            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.ToLowerInvariant()).ToList();
            while (segments.Count > 0)
            {
                var last = segments[segments.Count - 1];
                if (last == "completions" && segments.Count > 1 && segments[segments.Count - 2] == "chat") segments.RemoveRange(segments.Count - 2, 2);
                else if (last == "responses" || last == "messages" || VersionSegment.IsMatch(last)) segments.RemoveAt(segments.Count - 1);
                else break;
            }
            return string.Join("/", new[] { uri.Host.ToLowerInvariant() }.Concat(segments));
        }

        // Drops a vendor/ namespace, :variant suffix and [1m] marker so one model
        // written differently by several providers maps to the same ID.
        internal static string NormalizeModelId(string modelId)
        {
            var value = modelId ?? string.Empty;
            value = value.Substring(value.LastIndexOf('/') + 1);
            var colon = value.IndexOf(':');
            if (colon >= 0) value = value.Substring(0, colon);
            value = value.Trim().Replace('@', '-').ToLowerInvariant();
            if (value.EndsWith("[1m]", StringComparison.Ordinal)) value = value.Substring(0, value.Length - 4).Trim();
            return value;
        }

        // Prefer entries other providers point at; use a maker's own entry only
        // when nothing points. Ambiguous makers yield no match.
        private ModelMetadataEntry FindByVendor(string id)
        {
            var normalized = NormalizeModelId(id);
            var pointed = new Dictionary<string, ModelMetadataEntry>(StringComparer.Ordinal);
            var owned = new Dictionary<string, ModelMetadataEntry>(StringComparer.Ordinal);
            foreach (var pair in _catalog.FindByNormalizedId(normalized))
            {
                var canonical = pair.Value.CanonicalModelId;
                var vendorId = ModelMetadataCatalog.VendorOf(canonical);
                if (vendorId != null)
                {
                    var vendorModel = _catalog.FindProvider(vendorId)?.FindExact(canonical.Substring(vendorId.Length + 1));
                    if (vendorModel != null)
                    {
                        pointed[canonical] = vendorModel;
                        continue;
                    }
                }
                if (_catalog.IsVendor(pair.Key)) owned[pair.Key + "/" + (pair.Value.ModelId ?? normalized)] = pair.Value;
            }
            var found = pointed.Count > 0 ? pointed : owned;
            return found.Count == 1 ? found.Values.First() : null;
        }

        private static T Find<T>(IEnumerable<T> items, Func<T, string> key, string id) where T : class
        {
            var list = items as IList<T> ?? items.ToList();
            var exact = list.FirstOrDefault(item => string.Equals(key(item), id, StringComparison.Ordinal));
            if (exact != null) return exact;
            var normalized = NormalizeModelId(id);
            T normalizedMatch = null;
            foreach (var item in list)
            {
                var candidate = key(item);
                if (candidate == null) continue;
                if (string.Equals(candidate, id, StringComparison.OrdinalIgnoreCase)) return item;
                if (normalizedMatch == null && NormalizeModelId(candidate) == normalized) normalizedMatch = item;
            }
            return normalizedMatch;
        }

        private static void Merge(ModelMetadataMatch match, ModelMetadataSources source, long? contextWindow, IReadOnlyList<string> efforts)
        {
            var added = false;
            if (!match.ContextWindow.HasValue && contextWindow.HasValue)
            {
                match.ContextWindow = contextWindow;
                added = true;
            }
            if (match.ReasoningEfforts == null && efforts != null)
            {
                match.ReasoningEfforts = efforts;
                added = true;
            }
            if (added) match.Sources |= source;
        }

        private static long? Positive(long? value)
        {
            return value.HasValue && value.Value > 0 ? value : null;
        }

        private static IReadOnlyList<string> Efforts(IEnumerable<string> values)
        {
            var list = (values ?? Enumerable.Empty<string>()).Where(value => value != null)
                .Select(value => value.Trim().ToLowerInvariant()).Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal).ToList();
            return list.Count > 0 ? list : null;
        }
    }
}
