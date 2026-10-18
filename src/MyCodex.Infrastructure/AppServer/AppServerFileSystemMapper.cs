using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerFileSystemMapper
    {
        internal static List<AppServerFileSystemEntry> BuildDirectoryEntries(IDictionary<string, object> result)
        {
            return GetArray(result, "entries")
                .Select(item => BuildEntry(item as IDictionary<string, object>))
                .Where(item => item != null)
                .ToList();
        }

        internal static AppServerFileSystemReadResult BuildReadResult(IDictionary<string, object> result)
        {
            return new AppServerFileSystemReadResult
            {
                DataBase64 = AppServerJsonValueReader.GetString(result, "dataBase64") ?? string.Empty
            };
        }

        internal static AppServerFileSystemMetadata BuildMetadata(IDictionary<string, object> result)
        {
            return new AppServerFileSystemMetadata
            {
                IsDirectory = GetBool(result, "isDirectory"),
                IsFile = GetBool(result, "isFile"),
                IsSymlink = GetBool(result, "isSymlink"),
                CreatedAtMs = GetLong(result, "createdAtMs"),
                ModifiedAtMs = GetLong(result, "modifiedAtMs")
            };
        }

        internal static AppServerFileSystemWatchResult BuildWatchResult(IDictionary<string, object> result)
        {
            return new AppServerFileSystemWatchResult
            {
                Path = AppServerJsonValueReader.GetString(result, "path")
            };
        }

        internal static AppServerFileSystemChangedEvent BuildChangedEvent(IDictionary<string, object> parameters)
        {
            return new AppServerFileSystemChangedEvent
            {
                WatchId = AppServerJsonValueReader.GetString(parameters, "watchId"),
                ChangedPaths = GetArray(parameters, "changedPaths")
                    .Select(item => item == null ? null : Convert.ToString(item))
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToList()
            };
        }

        private static AppServerFileSystemEntry BuildEntry(IDictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            return new AppServerFileSystemEntry
            {
                FileName = AppServerJsonValueReader.GetString(source, "fileName"),
                IsDirectory = GetBool(source, "isDirectory"),
                IsFile = GetBool(source, "isFile")
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

        private static bool GetBool(IDictionary<string, object> source, string key)
        {
            object value;
            bool result;
            return source != null &&
                source.TryGetValue(key, out value) &&
                value != null &&
                bool.TryParse(Convert.ToString(value), out result) &&
                result;
        }

        private static long GetLong(IDictionary<string, object> source, string key)
        {
            object value;
            long result;
            return source != null &&
                source.TryGetValue(key, out value) &&
                value != null &&
                long.TryParse(Convert.ToString(value), out result)
                    ? result
                    : 0L;
        }
    }
}
