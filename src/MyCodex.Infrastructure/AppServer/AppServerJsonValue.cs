using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerJsonValue
    {
        internal static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue
            };
        }

        internal static IDictionary<string, object> DeserializeDictionary(string json)
        {
            var serializer = CreateSerializer();
            var result = serializer.DeserializeObject(json) as IDictionary<string, object>;
            return result ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }

        internal static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return JsonValueReader.GetDictionary(source, key);
        }

        internal static string GetString(IDictionary<string, object> source, string key)
        {
            return JsonValueReader.GetString(source, key);
        }
    }
}
