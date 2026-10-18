using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerFuzzyFileSearchMapper
    {
        internal static AppServerFuzzyFileSearchSessionEvent BuildSessionUpdated(IDictionary<string, object> parameters)
        {
            return new AppServerFuzzyFileSearchSessionEvent
            {
                SessionId = AppServerJsonValueReader.GetString(parameters, "sessionId"),
                Query = AppServerJsonValueReader.GetString(parameters, "query"),
                Files = GetArray(parameters, "files")
                    .Select(item => BuildResult(item as IDictionary<string, object>))
                    .Where(item => item != null)
                    .ToList(),
                IsCompleted = false
            };
        }

        internal static AppServerFuzzyFileSearchSessionEvent BuildSessionCompleted(IDictionary<string, object> parameters)
        {
            return new AppServerFuzzyFileSearchSessionEvent
            {
                SessionId = AppServerJsonValueReader.GetString(parameters, "sessionId"),
                IsCompleted = true
            };
        }

        internal static AppServerFuzzyFileSearchSessionEvent BuildSearchResponse(IDictionary<string, object> parameters)
        {
            return new AppServerFuzzyFileSearchSessionEvent
            {
                Files = GetArray(parameters, "files")
                    .Select(item => BuildResult(item as IDictionary<string, object>))
                    .Where(item => item != null)
                    .ToList(),
                IsCompleted = true
            };
        }

        private static AppServerFuzzyFileSearchResult BuildResult(IDictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            return new AppServerFuzzyFileSearchResult
            {
                FileName = AppServerJsonValueReader.GetString(source, "file_name"),
                MatchType = AppServerJsonValueReader.GetString(source, "match_type"),
                Path = AppServerJsonValueReader.GetString(source, "path"),
                Root = AppServerJsonValueReader.GetString(source, "root"),
                Score = GetUInt32(source, "score"),
                Indices = GetArray(source, "indices")
                    .Select(ToNullableUInt32)
                    .Where(item => item.HasValue)
                    .Select(item => item.Value)
                    .ToList()
            };
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null || value is string)
            {
                return Enumerable.Empty<object>();
            }

            var array = value as IEnumerable<object>;
            return array ?? Enumerable.Empty<object>();
        }

        private static uint GetUInt32(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value)
                ? ToNullableUInt32(value).GetValueOrDefault()
                : 0U;
        }

        private static uint? ToNullableUInt32(object value)
        {
            if (value == null)
            {
                return null;
            }

            uint result;
            return uint.TryParse(Convert.ToString(value), out result) ? (uint?)result : null;
        }
    }
}
