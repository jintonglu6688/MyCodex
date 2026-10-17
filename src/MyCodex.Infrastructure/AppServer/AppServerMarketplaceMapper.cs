using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerMarketplaceMapper
    {
        public static AppServerMarketplaceAddResult BuildAddResult(IDictionary<string, object> result)
        {
            return new AppServerMarketplaceAddResult
            {
                MarketplaceName = EmptyIfNull(GetFirstNonEmptyString(result, "marketplaceName", "name")),
                AlreadyAdded = GetNullableBool(result, "alreadyAdded") ?? false
            };
        }

        public static AppServerMarketplaceUpgradeResult BuildUpgradeResult(
            IDictionary<string, object> result,
            string requestedMarketplaceName)
        {
            var marketplace = AppServerJsonValue.GetDictionary(result, "marketplace");
            var firstSelectedMarketplace = GetFirstArrayString(result, "selectedMarketplaces");
            var firstError = GetFirstDictionary(result, "errors");
            var marketplaceName = GetFirstNonEmptyString(
                result,
                "marketplaceName",
                "name",
                "displayName") ??
                GetFirstNonEmptyString(marketplace, "marketplaceName", "name", "displayName") ??
                GetFirstNonEmptyString(firstError, "marketplaceName") ??
                firstSelectedMarketplace ??
                requestedMarketplaceName;
            var errorMessage = GetFirstNonEmptyString(firstError, "message", "details", "reason");

            return new AppServerMarketplaceUpgradeResult
            {
                MarketplaceName = EmptyIfNull(marketplaceName),
                Success = GetNullableBool(result, "success") ??
                          GetNullableBool(result, "updated") ??
                          GetNullableBool(marketplace, "success") ??
                          string.IsNullOrWhiteSpace(errorMessage),
                Message = EmptyIfNull(GetFirstNonEmptyString(result, "message", "details", "status") ??
                                      GetFirstNonEmptyString(marketplace, "message", "details", "status") ??
                                      errorMessage)
            };
        }

        private static IDictionary<string, object> GetFirstDictionary(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null || value is string)
            {
                return new Dictionary<string, object>();
            }

            var items = value as IEnumerable<object>;
            if (items == null)
            {
                return new Dictionary<string, object>();
            }

            foreach (var item in items)
            {
                var dictionary = item as IDictionary<string, object>;
                if (dictionary != null)
                {
                    return dictionary;
                }
            }

            return new Dictionary<string, object>();
        }

        private static string GetFirstArrayString(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null || value is string)
            {
                return null;
            }

            var items = value as IEnumerable<object>;
            if (items == null)
            {
                return null;
            }

            foreach (var item in items)
            {
                var text = item == null ? null : item.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }

            return null;
        }

        private static string GetFirstNonEmptyString(IDictionary<string, object> source, params string[] keys)
        {
            if (keys == null)
            {
                return null;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                var value = AppServerJsonValue.GetString(source, keys[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static bool? GetNullableBool(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            bool parsed;
            return bool.TryParse(System.Convert.ToString(value), out parsed) ? parsed : (bool?)null;
        }

        private static string EmptyIfNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
