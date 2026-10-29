using System.Collections.Generic;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerJsonValueReader
    {
        public static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return JsonValueReader.GetDictionary(source, key);
        }

        public static string GetString(IDictionary<string, object> source, string key)
        {
            return JsonValueReader.GetString(source, key);
        }

        public static object GetValue(IDictionary<string, object> source, string key)
        {
            return JsonValueReader.GetValue(source, key);
        }

        public static string GetFirstNonEmptyString(IDictionary<string, object> source, params string[] keys)
        {
            return JsonValueReader.GetFirstNonEmptyString(source, keys);
        }

        public static int? GetNullableInt(IDictionary<string, object> source, string key)
        {
            return JsonValueReader.GetNullableInt(source, key);
        }

        public static bool? GetNullableBool(IDictionary<string, object> source, string key)
        {
            return JsonValueReader.GetNullableBool(source, key);
        }

        public static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            return JsonValueReader.GetArray(source, key);
        }

        public static decimal? GetNullableDecimal(IDictionary<string, object> source, string key)
        {
            return JsonValueReader.GetNullableDecimal(source, key);
        }

        public static long? GetNullableLong(IDictionary<string, object> source, string key)
        {
            return JsonValueReader.GetNullableLong(source, key);
        }
    }
}
