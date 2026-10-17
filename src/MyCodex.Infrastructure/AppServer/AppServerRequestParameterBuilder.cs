using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerRequestParameterBuilder
    {
        public static Dictionary<string, object> BuildCommandInputWriteParameters(string processId, string input)
        {
            return new Dictionary<string, object>
            {
                { "processId", processId },
                { "input", input ?? string.Empty }
            };
        }

        public static Dictionary<string, object> BuildFileSystemWriteFileParameters(
            string path,
            byte[] data,
            Func<string, string> mapPath)
        {
            var parameters = BuildFileSystemPathParameters(path, mapPath);
            parameters["dataBase64"] = Convert.ToBase64String(data ?? Array.Empty<byte>());
            return parameters;
        }

        public static Dictionary<string, object> BuildFileSystemWatchParameters(
            string path,
            string watchId,
            Func<string, string> mapPath)
        {
            var parameters = BuildFileSystemPathParameters(path, mapPath);
            parameters["watchId"] = watchId;
            return parameters;
        }

        public static Dictionary<string, object> BuildFuzzyFileSearchParameters(
            AppServerFuzzyFileSearchRequest request,
            Func<string, string> mapRoot)
        {
            var roots = request.Roots == null
                ? new string[0]
                : request.Roots
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => mapRoot == null ? path : mapRoot(path))
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            var parameters = new Dictionary<string, object>
            {
                { "query", request.Query == null ? null : request.Query.Trim() },
                { "roots", roots }
            };

            if (!string.IsNullOrWhiteSpace(request.CancellationToken))
            {
                parameters["cancellationToken"] = request.CancellationToken.Trim();
            }

            return parameters;
        }

        private static Dictionary<string, object> BuildFileSystemPathParameters(
            string path,
            Func<string, string> mapPath)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("path");
            }

            return new Dictionary<string, object>
            {
                { "path", mapPath == null ? path : mapPath(path) }
            };
        }
    }
}
