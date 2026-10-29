using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Helpers;

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
                IsDirectory = JsonValueReader.GetBool(result, "isDirectory"),
                IsFile = JsonValueReader.GetBool(result, "isFile"),
                IsSymlink = JsonValueReader.GetBool(result, "isSymlink"),
                CreatedAtMs = JsonValueReader.GetLong(result, "createdAtMs"),
                ModifiedAtMs = JsonValueReader.GetLong(result, "modifiedAtMs")
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
                IsDirectory = JsonValueReader.GetBool(source, "isDirectory"),
                IsFile = JsonValueReader.GetBool(source, "isFile")
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
    }
}
