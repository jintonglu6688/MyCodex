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

            foreach (var item in AppServerJsonValueReader.GetArray(result, "data"))
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
    }
}
