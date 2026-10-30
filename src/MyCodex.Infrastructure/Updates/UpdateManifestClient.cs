using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Updates
{
    public sealed class UpdateManifestClient
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();
        private readonly string _manifestUrl;

        public UpdateManifestClient(UpdateSettingsSnapshot settings)
            : this(ResolveManifestUrl(settings))
        {
        }

        public UpdateManifestClient(string manifestUrl)
        {
            _manifestUrl = (manifestUrl ?? string.Empty).Trim();
        }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(_manifestUrl); }
        }

        public string ManifestUrl
        {
            get { return _manifestUrl; }
        }

        public async Task<UpdateManifest> LoadAsync(CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdateManifest.Error.ManifestUrlNotConfigured",
                    "Update manifest URL is not configured."));
            }

            var manifestUri = EnsureTrustedUpdateUri(new Uri(_manifestUrl));
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = new WebClient())
            using (cancellationToken.Register(client.CancelAsync))
            {
                try
                {
                    var data = await client.DownloadDataTaskAsync(manifestUri).ConfigureAwait(false);
                    var json = Encoding.UTF8.GetString(data);
                    return Parse(json);
                }
                catch (WebException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }

        public Uri ResolveAssetUri(UpdateAssetReference asset, UpdateManifest manifest)
        {
            if (asset == null || string.IsNullOrWhiteSpace(asset.Url))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdateManifest.Error.AssetUrlMissing",
                    "Update asset URL is missing."));
            }

            Uri absoluteUri;
            if (Uri.TryCreate(asset.Url.Trim(), UriKind.Absolute, out absoluteUri))
            {
                return EnsureTrustedUpdateUri(absoluteUri);
            }

            var baseUrl = manifest != null && !string.IsNullOrWhiteSpace(manifest.PackageBaseUrl)
                ? manifest.PackageBaseUrl.Trim()
                : _manifestUrl;
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdateManifest.Error.AssetBaseUrlMissing",
                    "Update asset base URL is missing."));
            }

            return EnsureTrustedUpdateUri(new Uri(new Uri(baseUrl), asset.Url.Trim()));
        }

        // The manifest carries the SHA-256 that authenticates every package, so it and the
        // packages must come over HTTPS. Local files (file://) cannot be intercepted on the network.
        internal static Uri EnsureTrustedUpdateUri(Uri uri)
        {
            if (uri != null &&
                uri.IsAbsoluteUri &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                 (uri.IsFile && !uri.IsUnc)))
            {
                return uri;
            }

            throw new InvalidOperationException(string.Format(
                CultureInfo.CurrentCulture,
                SharedLocalization.Text(
                    "UpdateManifest.Error.InsecureUrl",
                    "Update URL must use HTTPS: {0}"),
                uri == null ? string.Empty : uri.GetLeftPart(UriPartial.Path)));
        }

        public static string ResolveManifestUrl(UpdateSettingsSnapshot settings)
        {
            var environmentValue = Environment.GetEnvironmentVariable(AppConstants.UpdateManifestUrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue.Trim();
            }

            if (settings != null && !string.IsNullOrWhiteSpace(settings.ManifestUrl))
            {
                return settings.ManifestUrl.Trim();
            }

            return AppConstants.DefaultUpdateManifestUrl;
        }

        public UpdateManifest Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdateManifest.Error.Empty",
                    "Update manifest is empty."));
            }

            var root = _serializer.Deserialize<Dictionary<string, object>>(json);
            if (root == null)
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "UpdateManifest.Error.Invalid",
                    "Update manifest is invalid."));
            }

            return new UpdateManifest
            {
                SchemaVersion = GetOptionalString(root, "schemaVersion"),
                Channel = GetOptionalString(root, "channel"),
                GeneratedAt = GetOptionalString(root, "generatedAt"),
                PackageBaseUrl = GetOptionalString(root, "packageBaseUrl"),
                App = ParseApp(GetOptionalDictionary(root, "app"))
            };
        }

        private static UpdateManifestAppPackage ParseApp(Dictionary<string, object> values)
        {
            if (values == null)
            {
                return null;
            }

            return new UpdateManifestAppPackage
            {
                Version = GetOptionalString(values, "version"),
                MinimumSupportedVersion = GetOptionalString(values, "minimumSupportedVersion"),
                ReleaseDate = GetOptionalString(values, "releaseDate"),
                ReleaseNotes = GetOptionalString(values, "releaseNotes"),
                ReleaseNotesZhCn = GetLocalizedReleaseNotes(values, "zh-CN", "zhCn", "zh"),
                ReleaseNotesEnUs = GetLocalizedReleaseNotes(values, "en-US", "enUs", "en"),
                Asset = ParseAsset(GetOptionalDictionary(values, "asset"))
            };
        }

        private static UpdateAssetReference ParseAsset(Dictionary<string, object> values)
        {
            if (values == null)
            {
                return null;
            }

            return new UpdateAssetReference
            {
                Url = GetOptionalString(values, "url"),
                FileName = GetOptionalString(values, "fileName"),
                Sha256 = NormalizeSha256(GetOptionalString(values, "sha256")),
                Size = GetOptionalLong(values, "size")
            };
        }

        private static string NormalizeSha256(string value)
        {
            value = (value ?? string.Empty).Trim();
            const string prefix = "sha256:";
            return value.StartsWith(prefix, true, CultureInfo.InvariantCulture)
                ? value.Substring(prefix.Length).Trim()
                : value;
        }

        private static Dictionary<string, object> GetOptionalDictionary(
            IDictionary<string, object> values,
            string name)
        {
            object value;
            return values != null && values.TryGetValue(name, out value)
                ? value as Dictionary<string, object>
                : null;
        }

        private static string GetOptionalString(IDictionary<string, object> values, string name)
        {
            object value;
            if (values == null || !values.TryGetValue(name, out value) || value == null)
            {
                return null;
            }

            if (value is IDictionary || value is IEnumerable && !(value is string))
            {
                return null;
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string GetLocalizedReleaseNotes(
            IDictionary<string, object> values,
            params string[] languageKeys)
        {
            var releaseNotes = GetOptionalDictionary(values, "releaseNotes");
            if (releaseNotes != null)
            {
                for (var i = 0; i < languageKeys.Length; i++)
                {
                    var text = GetOptionalString(releaseNotes, languageKeys[i]);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            var releaseNotesLocalized = GetOptionalDictionary(values, "releaseNotesLocalized");
            if (releaseNotesLocalized != null)
            {
                for (var i = 0; i < languageKeys.Length; i++)
                {
                    var text = GetOptionalString(releaseNotesLocalized, languageKeys[i]);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            for (var i = 0; i < languageKeys.Length; i++)
            {
                var key = "releaseNotes" + languageKeys[i].Replace("-", string.Empty);
                var text = GetOptionalString(values, key);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return null;
        }

        private static long? GetOptionalLong(IDictionary<string, object> values, string name)
        {
            object value;
            if (values == null || !values.TryGetValue(name, out value) || value == null)
            {
                return null;
            }

            long result;
            return long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result)
                ? (long?)result
                : null;
        }
    }
}
