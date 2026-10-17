using System;
using System.Collections;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerMcpResourceMapper
    {
        internal static AppServerMcpResourceReadResult BuildReadResult(IDictionary<string, object> result)
        {
            var readResult = new AppServerMcpResourceReadResult();
            foreach (var item in GetArray(result, "contents"))
            {
                var content = item as IDictionary<string, object>;
                if (content == null)
                {
                    continue;
                }

                var uri = AppServerJsonValue.GetString(content, "uri");
                if (string.IsNullOrWhiteSpace(uri))
                {
                    continue;
                }

                readResult.Contents.Add(new AppServerMcpResourceContent
                {
                    Uri = uri,
                    MimeType = AppServerJsonValue.GetString(content, "mimeType"),
                    Text = AppServerJsonValue.GetString(content, "text"),
                    Blob = AppServerJsonValue.GetString(content, "blob"),
                    MetaSummary = BuildMetaSummary(content)
                });
            }

            return readResult;
        }

        private static string BuildMetaSummary(IDictionary<string, object> content)
        {
            object meta;
            if (content == null || !content.TryGetValue("_meta", out meta) || meta == null)
            {
                return null;
            }

            return AppServerJsonValue.CreateSerializer().Serialize(meta);
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null || value is string)
            {
                yield break;
            }

            var enumerable = value as IEnumerable;
            if (enumerable == null)
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
