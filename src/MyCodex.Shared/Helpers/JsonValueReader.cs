using System;
using System.Collections;
using System.Collections.Generic;

namespace MyCodex.Shared.Helpers
{
    /// <summary>Reads values from the dictionaries that <c>JavaScriptSerializer</c> produces for JSON objects.</summary>
    public static class JsonValueReader
    {
        public static object GetValue(IDictionary<string, object> source, string key)
        {
            object value;
            if (source != null && key != null && source.TryGetValue(key, out value))
            {
                return value;
            }

            return null;
        }

        /// <summary>Returns the nested object, or an empty dictionary when it is missing or not an object.</summary>
        public static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return GetValue(source, key) as IDictionary<string, object> ??
                new Dictionary<string, object>(StringComparer.Ordinal);
        }

        public static string GetString(IDictionary<string, object> source, string key)
        {
            var value = GetValue(source, key);
            return value == null ? null : Convert.ToString(value);
        }

        public static string GetFirstNonEmptyString(IDictionary<string, object> source, params string[] keys)
        {
            if (keys == null)
            {
                return null;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                var value = GetString(source, keys[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        public static bool GetBool(IDictionary<string, object> source, string key)
        {
            return GetNullableBool(source, key) == true;
        }

        public static int GetInt(IDictionary<string, object> source, string key)
        {
            return GetNullableInt(source, key) ?? 0;
        }

        public static long GetLong(IDictionary<string, object> source, string key)
        {
            return GetNullableLong(source, key) ?? 0L;
        }

        public static bool? GetNullableBool(IDictionary<string, object> source, string key)
        {
            bool result;
            return bool.TryParse(GetString(source, key), out result) ? result : (bool?)null;
        }

        public static int? GetNullableInt(IDictionary<string, object> source, string key)
        {
            int result;
            return int.TryParse(GetString(source, key), out result) ? result : (int?)null;
        }

        public static long? GetNullableLong(IDictionary<string, object> source, string key)
        {
            long result;
            return long.TryParse(GetString(source, key), out result) ? result : (long?)null;
        }

        public static decimal? GetNullableDecimal(IDictionary<string, object> source, string key)
        {
            decimal result;
            return decimal.TryParse(GetString(source, key), out result) ? result : (decimal?)null;
        }

        public static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            var value = GetValue(source, key);
            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                yield return item;
            }
        }
    }
}
