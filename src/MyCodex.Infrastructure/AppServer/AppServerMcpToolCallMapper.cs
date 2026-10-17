using System.Collections;
using System.Collections.Generic;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerMcpToolCallMapper
    {
        internal static AppServerMcpToolCallResult BuildResult(IDictionary<string, object> result)
        {
            var callResult = new AppServerMcpToolCallResult
            {
                IsError = GetBool(result, "isError")
            };

            foreach (var item in GetArray(result, "content"))
            {
                var text = BuildContentDisplayText(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    callResult.ContentItems.Add(text);
                }
            }

            object structuredContent;
            if (result != null && result.TryGetValue("structuredContent", out structuredContent) && structuredContent != null)
            {
                callResult.StructuredContentJson = Serialize(structuredContent);
            }

            object meta;
            if (result != null && result.TryGetValue("_meta", out meta) && meta != null)
            {
                callResult.MetaJson = Serialize(meta);
            }

            callResult.DisplayText = BuildDisplayText(callResult);
            return callResult;
        }

        private static string BuildContentDisplayText(object item)
        {
            var dictionary = item as IDictionary<string, object>;
            if (dictionary != null)
            {
                var text = AppServerJsonValue.GetString(dictionary, "text");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }

            return item == null ? null : Serialize(item);
        }

        private static string BuildDisplayText(AppServerMcpToolCallResult result)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < result.ContentItems.Count; i++)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(result.ContentItems[i]);
            }

            if (!string.IsNullOrWhiteSpace(result.StructuredContentJson))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine();
                }

                builder.AppendLine(SharedLocalization.Text(
                    "AppServerMcpToolCallMapper.Label.StructuredContent",
                    "Structured content:"));
                builder.Append(result.StructuredContentJson);
            }

            if (!string.IsNullOrWhiteSpace(result.MetaJson))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine();
                }

                builder.AppendLine(SharedLocalization.Text(
                    "AppServerMcpToolCallMapper.Label.Metadata",
                    "Metadata:"));
                builder.Append(result.MetaJson);
            }

            return builder.ToString();
        }

        private static bool GetBool(IDictionary<string, object> source, string key)
        {
            object value;
            bool result;
            return source != null &&
                   source.TryGetValue(key, out value) &&
                   value != null &&
                   bool.TryParse(System.Convert.ToString(value), out result) &&
                   result;
        }

        private static string Serialize(object value)
        {
            return AppServerJsonValue.CreateSerializer().Serialize(value);
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
