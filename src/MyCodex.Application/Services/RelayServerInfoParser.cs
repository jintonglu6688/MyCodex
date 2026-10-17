using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace MyCodex.Application.Services
{
    public sealed class RelayServerImportInfo
    {
        public string RelayHost { get; set; }

        public int RelayPort { get; set; }

        public bool TlsRequired { get; set; }

        public string TenantId { get; set; }

        public string AccessSecret { get; set; }
    }

    public static class RelayServerInfoParser
    {
        private const int MaxJsonCharacters = 65536;

        public static RelayServerImportInfo Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonCharacters)
            {
                throw new InvalidOperationException("invalid_server_info");
            }

            IDictionary<string, object> root;
            try
            {
                root = new JavaScriptSerializer().DeserializeObject(json) as IDictionary<string, object>;
            }
            catch (ArgumentException)
            {
                throw new InvalidOperationException("invalid_server_info");
            }

            if (root == null)
            {
                throw new InvalidOperationException("invalid_server_info");
            }

            var relayHost = GetString(root, "relayHost");
            var relayPort = GetInt(root, "relayPort");
            var tlsRequired = GetBoolean(root, "tlsRequired");
            if (string.IsNullOrWhiteSpace(relayHost) ||
                Uri.CheckHostName(relayHost) == UriHostNameType.Unknown ||
                relayPort <= 0 ||
                relayPort > 65535)
            {
                throw new InvalidOperationException("invalid_server_endpoint");
            }

            var tenants = GetDictionaries(root, "tenants")
                .Where(item => GetBoolean(item, "enabled"))
                .Select(item => GetString(item, "tenantId"))
                .Where(IsCanonicalGuid)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var secret = GetDictionary(root, "secret");
            var secretTenantId = GetString(secret, "tenantId");
            var secretValue = GetString(secret, "value");
            var tenantId = IsCanonicalGuid(secretTenantId)
                ? secretTenantId
                : tenants.Count == 1 ? tenants[0] : null;
            if (tenantId == null)
            {
                throw new InvalidOperationException("tenant_selection_required");
            }

            if (secret != null &&
                (!tenants.Contains(secretTenantId, StringComparer.Ordinal) ||
                 !string.Equals(secretTenantId, tenantId, StringComparison.Ordinal) ||
                 string.IsNullOrWhiteSpace(secretValue)))
            {
                throw new InvalidOperationException("invalid_server_secret");
            }

            return new RelayServerImportInfo
            {
                RelayHost = relayHost,
                RelayPort = relayPort,
                TlsRequired = tlsRequired,
                TenantId = tenantId,
                AccessSecret = secretValue
            };
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            if (source == null ||
                !source.TryGetValue(key, out var value) ||
                value == null)
            {
                return null;
            }

            var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static int GetInt(IDictionary<string, object> source, string key)
        {
            if (source == null ||
                !source.TryGetValue(key, out var value) ||
                value == null)
            {
                return 0;
            }

            try
            {
                return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            {
                return 0;
            }
        }

        private static bool GetBoolean(IDictionary<string, object> source, string key)
        {
            return source != null &&
                source.TryGetValue(key, out var value) &&
                value is bool result &&
                result;
        }

        private static IDictionary<string, object> GetDictionary(
            IDictionary<string, object> source,
            string key)
        {
            return source != null &&
                source.TryGetValue(key, out var value)
                ? value as IDictionary<string, object>
                : null;
        }

        private static IEnumerable<IDictionary<string, object>> GetDictionaries(
            IDictionary<string, object> source,
            string key)
        {
            if (source == null ||
                !source.TryGetValue(key, out var value) ||
                !(value is IEnumerable items))
            {
                return Enumerable.Empty<IDictionary<string, object>>();
            }

            return items.Cast<object>()
                .Select(item => item as IDictionary<string, object>)
                .Where(item => item != null);
        }

        private static bool IsCanonicalGuid(string value)
        {
            return Guid.TryParseExact(value, "D", out var parsed) &&
                string.Equals(parsed.ToString("D"), value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
