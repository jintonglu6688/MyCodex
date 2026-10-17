using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerConversationSummaryMapper
    {
        internal static AppServerConversationSummary BuildSummary(
            IDictionary<string, object> summary,
            Func<string, string> mapServerPathToClient)
        {
            if (summary == null)
            {
                return null;
            }

            var gitInfo = GetDictionary(summary, "gitInfo");
            return new AppServerConversationSummary
            {
                ThreadId = GetString(summary, "conversationId"),
                Path = MapServerPathToClient(mapServerPathToClient, GetString(summary, "path")),
                Preview = GetString(summary, "preview"),
                Timestamp = ParseDateTime(GetString(summary, "timestamp")),
                UpdatedAt = ParseDateTime(GetString(summary, "updatedAt")),
                ModelProviderId = GetString(summary, "modelProvider"),
                WorkingDirectory = LocalSessionCatalog.NormalizeLocalThreadWorkingDirectory(
                    MapServerPathToClient(mapServerPathToClient, GetString(summary, "cwd"))),
                CliVersion = GetString(summary, "cliVersion"),
                Source = GetSessionSourceText(GetValue(summary, "source")),
                GitInfo = gitInfo.Count == 0
                    ? null
                    : new WorkspaceGitInfoSnapshot
                    {
                        IsGitRepository = true,
                        Sha = GetString(gitInfo, "sha"),
                        Branch = GetString(gitInfo, "branch"),
                        OriginUrl = GetString(gitInfo, "origin_url")
                    }
            };
        }

        internal static AppServerThreadSummary ToThreadSummary(
            AppServerConversationSummary summary,
            AppServerThreadSummary existing)
        {
            if (summary == null && existing == null)
            {
                return null;
            }

            if (existing == null)
            {
                existing = new AppServerThreadSummary();
            }

            if (string.IsNullOrWhiteSpace(existing.ThreadId))
            {
                existing.ThreadId = summary != null ? summary.ThreadId : null;
            }

            if (summary == null)
            {
                return existing;
            }

            if (string.IsNullOrWhiteSpace(existing.Preview) && !string.IsNullOrWhiteSpace(summary.Preview))
            {
                existing.Preview = summary.Preview;
            }

            if (string.IsNullOrWhiteSpace(existing.ModelProviderId) && !string.IsNullOrWhiteSpace(summary.ModelProviderId))
            {
                existing.ModelProviderId = summary.ModelProviderId;
            }

            if (string.IsNullOrWhiteSpace(existing.WorkingDirectory) && !string.IsNullOrWhiteSpace(summary.WorkingDirectory))
            {
                existing.WorkingDirectory = summary.WorkingDirectory;
            }

            if ((string.IsNullOrWhiteSpace(existing.Source) ||
                 string.Equals(existing.Source, "unknown", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(summary.Source))
            {
                existing.Source = summary.Source;
            }

            if (existing.CreatedAt == default(DateTime) && summary.Timestamp.HasValue)
            {
                existing.CreatedAt = summary.Timestamp.Value;
            }

            if (summary.UpdatedAt.HasValue && summary.UpdatedAt.Value > existing.UpdatedAt)
            {
                existing.UpdatedAt = summary.UpdatedAt.Value;
            }
            else if (existing.UpdatedAt == default(DateTime) && summary.Timestamp.HasValue)
            {
                existing.UpdatedAt = summary.Timestamp.Value;
            }

            if (string.IsNullOrWhiteSpace(existing.SessionPath) && !string.IsNullOrWhiteSpace(summary.Path))
            {
                existing.SessionPath = summary.Path;
            }

            return existing;
        }

        private static string MapServerPathToClient(Func<string, string> mapper, string path)
        {
            return mapper == null ? path : mapper(path);
        }

        private static DateTime? ParseDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            DateTimeOffset timestamp;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp)
                ? (DateTime?)timestamp.LocalDateTime
                : null;
        }

        private static string GetSessionSourceText(object value)
        {
            if (value == null)
            {
                return "unknown";
            }

            var text = value as string;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var dictionary = value as IDictionary<string, object>;
            if (dictionary == null || dictionary.Count == 0)
            {
                return "unknown";
            }

            foreach (var pair in dictionary)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    return pair.Key + ":" + Convert.ToString(pair.Value, CultureInfo.CurrentCulture);
                }
            }

            return "unknown";
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValue.GetDictionary(source, key);
        }

        private static object GetValue(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValue.GetString(source, key);
        }
    }
}
