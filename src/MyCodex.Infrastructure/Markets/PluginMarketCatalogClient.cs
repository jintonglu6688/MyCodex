using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Markets
{
    public sealed class PluginMarketCatalogClient : IPluginMarketCatalogClient
    {
        private readonly JavaScriptSerializer _serializer;
        private readonly Func<Uri, CancellationToken, Task<string>> _downloadJsonAsync;
        private readonly string _indexUrl;

        public PluginMarketCatalogClient()
            : this(ResolveDefaultIndexUrl())
        {
        }

        public PluginMarketCatalogClient(string indexUrl)
            : this(indexUrl, DownloadJsonAsync)
        {
        }

        internal PluginMarketCatalogClient(string indexUrl, Func<Uri, CancellationToken, Task<string>> downloadJsonAsync)
        {
            _indexUrl = (indexUrl ?? string.Empty).Trim();
            _downloadJsonAsync = downloadJsonAsync ?? DownloadJsonAsync;
            _serializer = AppServerJsonValue.CreateSerializer();
        }

        internal static string ResolveDefaultIndexUrl(string environmentOverride = null)
        {
            var configuredValue = environmentOverride;
            if (configuredValue == null)
            {
                configuredValue = Environment.GetEnvironmentVariable(AppConstants.PluginMarketIndexUrlEnvironmentVariable);
            }

            return string.IsNullOrWhiteSpace(configuredValue)
                ? AppConstants.DefaultPluginMarketIndexUrl
                : configuredValue.Trim();
        }

        public async Task<PluginMarketCatalogSnapshot> LoadIndexAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_indexUrl))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "PluginMarketCatalogClient.Error.IndexUrlRequired",
                    "Plugin market index URL is not configured."));
            }

            var json = await _downloadJsonAsync(new Uri(_indexUrl), cancellationToken).ConfigureAwait(false);
            var root = DeserializeRoot(json, "PluginMarketCatalogClient.Error.IndexInvalid", "Plugin market index is invalid.");
            var snapshot = new PluginMarketCatalogSnapshot
            {
                IndexUrl = _indexUrl
            };

            foreach (var item in GetArray(root, "markets"))
            {
                var values = item as IDictionary<string, object>;
                if (values == null)
                {
                    continue;
                }

                snapshot.Markets.Add(ParseMarketSummary(values, _indexUrl));
            }

            return snapshot;
        }

        public async Task<PluginMarketSnapshot> LoadMarketAsync(string manifestUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                throw new ArgumentException(
                    SharedLocalization.Text(
                        "PluginMarketCatalogClient.Error.ManifestUrlRequired",
                        "Plugin market manifest URL cannot be empty."),
                    nameof(manifestUrl));
            }

            var resolvedManifestUrl = ResolveUrl(_indexUrl, manifestUrl) ?? manifestUrl.Trim();
            var json = await _downloadJsonAsync(new Uri(resolvedManifestUrl), cancellationToken).ConfigureAwait(false);
            var root = DeserializeRoot(json, "PluginMarketCatalogClient.Error.ManifestInvalid", "Plugin market manifest is invalid.");
            var snapshot = ParseMarketSummary(root, resolvedManifestUrl);
            snapshot.ManifestUrl = resolvedManifestUrl;

            foreach (var item in GetArray(root, "plugins"))
            {
                var values = item as IDictionary<string, object>;
                if (values == null)
                {
                    continue;
                }

                snapshot.Plugins.Add(ParsePluginEntry(values, snapshot.MarketId, resolvedManifestUrl));
            }

            return snapshot;
        }

        private IDictionary<string, object> DeserializeRoot(string json, string resourceKey, string fallbackText)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(SharedLocalization.Text(resourceKey, fallbackText));
            }

            var root = _serializer.DeserializeObject(json) as IDictionary<string, object>;
            if (root == null)
            {
                throw new InvalidOperationException(SharedLocalization.Text(resourceKey, fallbackText));
            }

            return root;
        }

        private static PluginMarketSnapshot ParseMarketSummary(IDictionary<string, object> values, string baseUrl)
        {
            var interfaceValues = JsonValueReader.GetDictionary(values, "interface");
            return new PluginMarketSnapshot
            {
                MarketId = FirstNonEmptyString(values, "marketId", "id", "name"),
                DisplayName = FirstNonEmptyString(interfaceValues, "displayName") ?? FirstNonEmptyString(values, "displayName", "title", "name"),
                ManifestUrl = ResolveUrl(baseUrl, FirstNonEmptyString(values, "manifestUrl")),
                AppServerSource = ResolveUrl(baseUrl, FirstNonEmptyString(values, "appServerSource")),
                DetailsUrl = ResolveUrl(baseUrl, FirstNonEmptyString(values, "detailsUrl", "homepage"))
            };
        }

        private static PluginMarketEntrySnapshot ParsePluginEntry(
            IDictionary<string, object> values,
            string marketId,
            string manifestUrl)
        {
            var interfaceValues = JsonValueReader.GetDictionary(values, "interface");
            var policyValues = JsonValueReader.GetDictionary(values, "policy");
            var sourceValues = JsonValueReader.GetDictionary(values, "source");
            return new PluginMarketEntrySnapshot
            {
                MarketId = marketId,
                PluginId = FirstNonEmptyString(values, "pluginId", "id", "name"),
                DisplayName = FirstNonEmptyString(interfaceValues, "displayName") ?? FirstNonEmptyString(values, "displayName", "name"),
                Version = FirstNonEmptyString(values, "version"),
                Category = FirstNonEmptyString(values, "category") ?? FirstNonEmptyString(interfaceValues, "category"),
                Summary = FirstNonEmptyString(values, "summary", "description") ??
                    FirstNonEmptyString(interfaceValues, "shortDescription", "longDescription"),
                PackageUrl = ResolveUrl(manifestUrl, FirstNonEmptyString(values, "packageUrl") ?? FirstNonEmptyString(sourceValues, "packageUrl", "url")),
                ManifestUrl = ResolveUrl(manifestUrl, FirstNonEmptyString(values, "manifestUrl") ?? FirstNonEmptyString(sourceValues, "manifestUrl")),
                DetailsUrl = ResolveUrl(manifestUrl, FirstNonEmptyString(values, "detailsUrl", "homepage") ?? FirstNonEmptyString(interfaceValues, "websiteURL")),
                AuthPolicy = FirstNonEmptyString(values, "authPolicy") ?? FirstNonEmptyString(policyValues, "authentication"),
                Sha256 = FirstNonEmptyString(values, "sha256", "sha256Hash"),
                SizeBytes = GetLong(values, "size", "sizeBytes")
            };
        }

        private static async Task<string> DownloadJsonAsync(Uri url, CancellationToken cancellationToken)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new TimeoutWebClient(TimeSpan.FromSeconds(30)))
            using (cancellationToken.Register(client.CancelAsync))
            {
                try
                {
                    var data = await client.DownloadDataTaskAsync(url).ConfigureAwait(false);
                    return Encoding.UTF8.GetString(data);
                }
                catch (WebException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }

        private static IEnumerable GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value) && value is IEnumerable && !(value is string))
            {
                return (IEnumerable)value;
            }

            return Array.Empty<object>();
        }

        private static string FirstNonEmptyString(IDictionary<string, object> source, params string[] keys)
        {
            if (source == null || keys == null)
            {
                return null;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                var value = GetString(source, keys[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || string.IsNullOrWhiteSpace(key) || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            if (value is IDictionary || value is IEnumerable && !(value is string))
            {
                return null;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static long GetLong(IDictionary<string, object> source, params string[] keys)
        {
            if (source == null || keys == null)
            {
                return 0L;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                object value;
                if (string.IsNullOrWhiteSpace(keys[i]) || !source.TryGetValue(keys[i], out value) || value == null)
                {
                    continue;
                }

                long result;
                if (long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
                {
                    return result < 0L ? 0L : result;
                }
            }

            return 0L;
        }

        private static string ResolveUrl(string baseUrl, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmedValue = value.Trim();
            Uri absoluteUri;
            if (Uri.TryCreate(trimmedValue, UriKind.Absolute, out absoluteUri))
            {
                return absoluteUri.ToString();
            }

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return trimmedValue;
            }

            return new Uri(new Uri(baseUrl.Trim()), trimmedValue).ToString();
        }
    }
}
