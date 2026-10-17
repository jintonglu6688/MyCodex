using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    // Public models.dev data (https://models.dev/api.json), cached per user and
    // shared by targets and channels. It contains no credentials or user data.
    public sealed class ModelsDevCatalogSource : IModelMetadataCatalogSource
    {
        internal const string CatalogUrl = "https://models.dev/api.json";
        internal const long MaxCatalogBytes = 32 * 1024 * 1024;
        internal static readonly TimeSpan RefreshAge = TimeSpan.FromHours(6);
        internal static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);
        private readonly string _cachePath;
        private readonly Func<CancellationToken, Task<string>> _download;
        private readonly Func<DateTime> _utcNow;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private volatile ModelMetadataCatalog _cached;
        private DateTime _cachedAtUtc;
        private DateTime _retryAfterUtc;

        public static ModelsDevCatalogSource Shared { get; } = new ModelsDevCatalogSource();

        public ModelsDevCatalogSource()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyCodex", "model-metadata", "models-dev.slim.v1.json"), DownloadAsync, () => DateTime.UtcNow)
        {
        }

        internal ModelsDevCatalogSource(string cachePath, Func<CancellationToken, Task<string>> download, Func<DateTime> utcNow)
        {
            _cachePath = cachePath;
            _download = download ?? throw new ArgumentNullException(nameof(download));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public ModelMetadataCatalog Cached => _cached;

        public async Task<ModelMetadataCatalog> LoadAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _utcNow();
                if (_cached == null)
                {
                    var disk = await Task.Run(() => ReadCache(), cancellationToken).ConfigureAwait(false);
                    if (disk != null)
                    {
                        _cached = disk.Item1;
                        _cachedAtUtc = disk.Item2;
                    }
                }
                if (_cached != null && now - _cachedAtUtc < RefreshAge) return _cached;
                if (now < _retryAfterUtc) return _cached;
                try
                {
                    var json = await _download(cancellationToken).ConfigureAwait(false);
                    _cached = await Task.Run(() => Refresh(json), cancellationToken).ConfigureAwait(false);
                    _cachedAtUtc = now;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    // Offline or invalid data: keep any older data and retry later.
                    _retryAfterUtc = now + RetryDelay;
                }
                return _cached;
            }
            finally
            {
                _gate.Release();
            }
        }

        private ModelMetadataCatalog Refresh(string json)
        {
            var root = Parse(json);
            var catalog = ToCatalog(root);
            if (catalog.Providers.Count == 0) throw new InvalidDataException("models.dev data contains no providers.");
            try { WriteCache(Slim(root)); }
            catch (Exception)
            {
                // The downloaded data remains usable for this process.
            }
            return catalog;
        }

        private Tuple<ModelMetadataCatalog, DateTime> ReadCache()
        {
            try
            {
                if (string.IsNullOrEmpty(_cachePath) || !File.Exists(_cachePath)) return null;
                var file = new FileInfo(_cachePath);
                if (file.Length > MaxCatalogBytes) return null;
                var catalog = ToCatalog(Parse(File.ReadAllText(_cachePath, Encoding.UTF8)));
                return catalog.Providers.Count == 0 ? null : Tuple.Create(catalog, file.LastWriteTimeUtc);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void WriteCache(IDictionary<string, object> slim)
        {
            if (string.IsNullOrEmpty(_cachePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath));
            var temporary = _cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, AppServerJsonValue.CreateSerializer().Serialize(slim), new UTF8Encoding(false));
                if (File.Exists(_cachePath)) File.Replace(temporary, _cachePath, null);
                else File.Move(temporary, _cachePath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        internal static IDictionary<string, object> Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxCatalogBytes) throw new InvalidDataException("models.dev data is empty or too large.");
            return AppServerJsonValue.CreateSerializer().DeserializeObject(json) as IDictionary<string, object>
                ?? throw new InvalidDataException("models.dev data is not an object.");
        }

        internal static ModelMetadataCatalog ToCatalog(IDictionary<string, object> root)
        {
            var providers = new List<ModelMetadataProvider>();
            foreach (var provider in root)
            {
                if (!(provider.Value is IDictionary<string, object> values)) continue;
                var entries = new List<ModelMetadataEntry>();
                foreach (var model in Dictionary(values, "models"))
                {
                    if (!(model.Value is IDictionary<string, object> fields)) continue;
                    entries.Add(new ModelMetadataEntry
                    {
                        Key = model.Key, ModelId = fields.TryGetValue("id", out var id) ? id as string : null,
                        ContextWindow = PositiveInteger(Value(Dictionary(fields, "limit"), "context")),
                        ReasoningEfforts = Efforts(fields),
                        CanonicalModelId = Value(fields, "canonical_model_id") as string
                    });
                }
                providers.Add(new ModelMetadataProvider(provider.Key, Value(values, "api") as string, entries));
            }
            return new ModelMetadataCatalog(providers);
        }

        // Keeps every entry (lookups count them) with only the fields used here.
        internal static IDictionary<string, object> Slim(IDictionary<string, object> root)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var provider in root)
            {
                if (!(provider.Value is IDictionary<string, object> values)) continue;
                var models = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var model in Dictionary(values, "models"))
                {
                    if (!(model.Value is IDictionary<string, object> fields)) continue;
                    var slim = new Dictionary<string, object>(StringComparer.Ordinal);
                    if (Value(fields, "id") is string id) slim["id"] = id;
                    var context = PositiveInteger(Value(Dictionary(fields, "limit"), "context"));
                    if (context.HasValue) slim["limit"] = new Dictionary<string, object> { { "context", context.Value } };
                    var efforts = Efforts(fields);
                    if (efforts != null) slim["reasoning_options"] = new object[] { new Dictionary<string, object> { { "type", "effort" }, { "values", efforts } } };
                    if (Value(fields, "canonical_model_id") is string canonical) slim["canonical_model_id"] = canonical;
                    models[model.Key] = slim;
                }
                var entry = new Dictionary<string, object> { { "models", models } };
                if (Value(values, "api") is string api) entry["api"] = api;
                result[provider.Key] = entry;
            }
            return result;
        }

        private static IReadOnlyList<string> Efforts(IDictionary<string, object> fields)
        {
            if (!(Value(fields, "reasoning_options") is IEnumerable options) || options is string) return null;
            var values = new List<string>();
            foreach (var option in options)
            {
                if (!(option is IDictionary<string, object> entry) || !Equals(Value(entry, "type"), "effort")) continue;
                if (!(Value(entry, "values") is IEnumerable items) || items is string) continue;
                values.AddRange(items.OfType<string>().Select(item => item.Trim().ToLowerInvariant()).Where(item => item.Length > 0));
            }
            var distinct = values.Distinct(StringComparer.Ordinal).ToList();
            return distinct.Count > 0 ? distinct : null;
        }

        private static long? PositiveInteger(object value)
        {
            double number;
            switch (value)
            {
                case int integer: number = integer; break;
                case long longValue: number = longValue; break;
                case decimal decimalValue: number = (double)decimalValue; break;
                case double doubleValue: number = doubleValue; break;
                default: return null;
            }
            return number >= 1 && number < long.MaxValue ? (long?)Math.Truncate(number) : null;
        }

        private static IDictionary<string, object> Dictionary(IDictionary<string, object> values, string key)
        {
            return Value(values, key) as IDictionary<string, object> ?? new Dictionary<string, object>();
        }

        private static object Value(IDictionary<string, object> values, string key)
        {
            return values != null && values.TryGetValue(key, out var value) ? value : null;
        }

        private static async Task<string> DownloadAsync(CancellationToken cancellationToken)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) })
            using (var response = await client.GetAsync(CatalogUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxCatalogBytes) throw new InvalidDataException("models.dev data is too large.");
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var buffer = new MemoryStream())
                {
                    var chunk = new byte[65536];
                    int count;
                    while ((count = await source.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        if (buffer.Length + count > MaxCatalogBytes) throw new InvalidDataException("models.dev data is too large.");
                        buffer.Write(chunk, 0, count);
                    }
                    return Encoding.UTF8.GetString(buffer.ToArray());
                }
            }
        }
    }
}
