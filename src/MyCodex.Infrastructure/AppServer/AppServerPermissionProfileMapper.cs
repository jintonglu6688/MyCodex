using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerPermissionProfileMapper
    {
        internal static AppServerPermissionProfileListResult BuildListResult(IDictionary<string, object> result)
        {
            var listResult = new AppServerPermissionProfileListResult
            {
                NextCursor = AppServerJsonValue.GetString(result, "nextCursor")
            };

            foreach (var item in GetArray(result, "data"))
            {
                var profile = item as IDictionary<string, object>;
                if (profile == null)
                {
                    continue;
                }

                var id = AppServerJsonValue.GetString(profile, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                listResult.Profiles.Add(new AppServerPermissionProfileSummary
                {
                    Id = id,
                    Description = AppServerJsonValue.GetString(profile, "description")
                });
            }

            return listResult;
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                yield break;
            }

            var array = value as object[];
            if (array != null)
            {
                foreach (var item in array)
                {
                    yield return item;
                }

                yield break;
            }

            var enumerable = value as System.Collections.IEnumerable;
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
