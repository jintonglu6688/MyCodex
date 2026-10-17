using System;
using System.Collections;
using System.Collections.Generic;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerJsonValueReader
    {
        public static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValue.GetDictionary(source, key);
        }

        public static string GetString(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValue.GetString(source, key);
        }

        public static object GetValue(IDictionary<string, object> source, string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value))
            {
                return value;
            }

            return null;
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

        public static int? GetNullableInt(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            int result;
            if (int.TryParse(Convert.ToString(value), out result))
            {
                return result;
            }

            return null;
        }

        public static bool? GetNullableBool(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            bool result;
            if (bool.TryParse(Convert.ToString(value), out result))
            {
                return result;
            }

            return null;
        }

        public static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                yield break;
            }

            var array = value as object[];
            if (array != null)
            {
                for (var i = 0; i < array.Length; i++)
                {
                    yield return array[i];
                }

                yield break;
            }

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

        public static decimal? GetNullableDecimal(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            decimal result;
            if (decimal.TryParse(Convert.ToString(value), out result))
            {
                return result;
            }

            return null;
        }

        public static long? GetNullableLong(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            long result;
            if (long.TryParse(Convert.ToString(value), out result))
            {
                return result;
            }

            return null;
        }
    }
}
