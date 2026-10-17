using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Codex;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class LocalSessionCatalog
    {
        internal static bool TryResolveLocalCodexHomePathForThreadScan(
            AppServerHostPlatform hostPlatform,
            AppConfigSnapshot appConfig,
            out string codexHomePath)
        {
            codexHomePath = null;
            if (hostPlatform == AppServerHostPlatform.Wsl)
            {
                return false;
            }

            if (hostPlatform == AppServerHostPlatform.RemoteMac)
            {
                return false;
            }

            codexHomePath = GetDefaultLocalCodexHomePath();
            return !string.IsNullOrWhiteSpace(codexHomePath);
        }

        internal static IReadOnlyList<string> ResolveLocalCodexHomePathsForThreadScan(
            AppServerHostPlatform hostPlatform,
            AppConfigSnapshot appConfig)
        {
            var paths = new List<string>();

            string sharedCodexHomePath;
            if (TryResolveLocalCodexHomePathForThreadScan(hostPlatform, appConfig, out sharedCodexHomePath))
            {
                AddDistinctPath(paths, sharedCodexHomePath);
            }

            return paths;
        }

        internal static IEnumerable<AppServerThreadSummary> EnumerateLocalThreadSummaries(
            bool archived,
            string workspacePath,
            string codexHomePath,
            int maxSummaries = 512,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var sessionsRoot = GetLocalSessionRootPath(archived, codexHomePath);
            if (!Directory.Exists(sessionsRoot))
            {
                yield break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            string normalizedWorkspacePath = null;
            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                try
                {
                    normalizedWorkspacePath = AppServerPathUtilities.NormalizeLocalPath(workspacePath);
                }
                catch
                {
                    yield break;
                }
            }

            var sessionIndexEntries = LoadLocalSessionIndexEntries(codexHomePath);
            var emittedCount = 0;
            var sessionPaths = new List<string>(Directory.EnumerateFiles(sessionsRoot, "*.jsonl", SearchOption.AllDirectories));
            sessionPaths.Sort(CompareSessionFileNewestFirst);
            foreach (var sessionPath in sessionPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var summary = TryBuildLocalThreadSummary(sessionPath, sessionIndexEntries);
                if (summary == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(normalizedWorkspacePath))
                {
                    if (string.IsNullOrWhiteSpace(summary.WorkingDirectory))
                    {
                        continue;
                    }

                    string normalizedThreadWorkspacePath;
                    try
                    {
                        normalizedThreadWorkspacePath = AppServerPathUtilities.NormalizeLocalPath(summary.WorkingDirectory);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!string.Equals(normalizedWorkspacePath, normalizedThreadWorkspacePath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                yield return summary;
                emittedCount++;
                if (maxSummaries > 0 && emittedCount >= maxSummaries)
                {
                    yield break;
                }
            }
        }

        private static int CompareSessionFileNewestFirst(string leftPath, string rightPath)
        {
            var updatedComparison = File.GetLastWriteTimeUtc(rightPath).CompareTo(File.GetLastWriteTimeUtc(leftPath));
            if (updatedComparison != 0)
            {
                return updatedComparison;
            }

            return string.Compare(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizeLocalThreadWorkingDirectory(string path)
        {
            var displayPath = NormalizeDriveRelativeWindowsPath(AppServerPathUtilities.NormalizeDisplayPath(path));
            var embeddedWindowsPath = ExtractEmbeddedWindowsAbsolutePath(displayPath);
            if (!string.IsNullOrWhiteSpace(embeddedWindowsPath))
            {
                return AppServerPathUtilities.NormalizeDisplayPath(embeddedWindowsPath);
            }

            if (!AppServerPathUtilities.LooksLikeUnixAbsolutePath(displayPath))
            {
                return displayPath;
            }

            var mapper = new WindowsMountedDriveWslPathMapper();
            string clientPath;
            return mapper.TryMapToClientPath(displayPath, out clientPath)
                ? clientPath
                : displayPath;
        }

        internal static void MergeThreadSummary(
            ICollection<AppServerThreadSummary> orderedThreads,
            IDictionary<string, AppServerThreadSummary> mergedThreadMap,
            AppServerThreadSummary candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.ThreadId))
            {
                return;
            }

            AppServerThreadSummary existing;
            if (!mergedThreadMap.TryGetValue(candidate.ThreadId, out existing))
            {
                mergedThreadMap[candidate.ThreadId] = candidate;
                orderedThreads.Add(candidate);
                return;
            }

            if (string.IsNullOrWhiteSpace(existing.Title) && !string.IsNullOrWhiteSpace(candidate.Title))
            {
                existing.Title = candidate.Title;
            }

            if (string.IsNullOrWhiteSpace(existing.Preview) && !string.IsNullOrWhiteSpace(candidate.Preview))
            {
                existing.Preview = candidate.Preview;
            }

            if (ShouldReplaceThreadWorkingDirectory(existing.WorkingDirectory, candidate.WorkingDirectory))
            {
                existing.WorkingDirectory = candidate.WorkingDirectory;
            }

            if ((string.IsNullOrWhiteSpace(existing.Source) ||
                 string.Equals(existing.Source, "unknown", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(candidate.Source))
            {
                existing.Source = candidate.Source;
            }

            if (existing.CreatedAt == default(DateTime) && candidate.CreatedAt != default(DateTime))
            {
                existing.CreatedAt = candidate.CreatedAt;
            }

            if (candidate.UpdatedAt > existing.UpdatedAt)
            {
                existing.UpdatedAt = candidate.UpdatedAt;
            }

            if (string.IsNullOrWhiteSpace(existing.SessionPath) && !string.IsNullOrWhiteSpace(candidate.SessionPath))
            {
                existing.SessionPath = candidate.SessionPath;
            }

            if (string.IsNullOrWhiteSpace(existing.ThreadSource) && !string.IsNullOrWhiteSpace(candidate.ThreadSource))
            {
                existing.ThreadSource = candidate.ThreadSource;
            }

            if (string.IsNullOrWhiteSpace(existing.ParentThreadId) && !string.IsNullOrWhiteSpace(candidate.ParentThreadId))
            {
                existing.ParentThreadId = candidate.ParentThreadId;
            }

            if (!existing.SubagentDepth.HasValue && candidate.SubagentDepth.HasValue)
            {
                existing.SubagentDepth = candidate.SubagentDepth;
            }

            if (string.IsNullOrWhiteSpace(existing.AgentNickname) && !string.IsNullOrWhiteSpace(candidate.AgentNickname))
            {
                existing.AgentNickname = candidate.AgentNickname;
            }

            if (string.IsNullOrWhiteSpace(existing.AgentRole) && !string.IsNullOrWhiteSpace(candidate.AgentRole))
            {
                existing.AgentRole = candidate.AgentRole;
            }
        }

        internal static string GetDefaultLocalCodexHomePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex");
        }

        internal static AppServerThreadSummary TryReadLocalThreadSummary(string sessionPath)
        {
            return TryBuildLocalThreadSummary(
                sessionPath,
                new Dictionary<string, LocalSessionIndexEntry>(StringComparer.OrdinalIgnoreCase));
        }

        internal static AppServerThreadSummary TryBuildThreadSummaryFromSessionMetaLine(
            string firstLine,
            string sessionPath,
            DateTime updatedAt,
            string title = null)
        {
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return null;
            }

            try
            {
                var envelope = AppServerJsonValue.DeserializeDictionary(firstLine);
                if (!string.Equals(AppServerJsonValue.GetString(envelope, "type"), "session_meta", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var payload = AppServerJsonValue.GetDictionary(envelope, "payload");
                var threadId = AppServerJsonValue.GetString(payload, "id");
                if (string.IsNullOrWhiteSpace(threadId))
                {
                    return null;
                }

                var createdAt = ParseIsoDateTime(AppServerJsonValue.GetString(payload, "timestamp"));
                var threadSource = AppServerJsonValue.GetString(payload, "thread_source");
                var source = threadSource;
                if (string.IsNullOrWhiteSpace(source))
                {
                    source = GetScalarString(payload, "source");
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        source = AppServerJsonValue.GetString(payload, "originator");
                    }
                }

                var threadSpawn = AppServerJsonValue.GetDictionary(
                    AppServerJsonValue.GetDictionary(
                        AppServerJsonValue.GetDictionary(payload, "source"),
                        "subagent"),
                    "thread_spawn");
                var parentThreadId = AppServerJsonValue.GetString(threadSpawn, "parent_thread_id");
                var subagentDepth = GetNullableInt(threadSpawn, "depth");
                if (string.IsNullOrWhiteSpace(threadSource) && !string.IsNullOrWhiteSpace(parentThreadId))
                {
                    threadSource = "subagent";
                }

                return new AppServerThreadSummary
                {
                    ThreadId = threadId,
                    Title = title,
                    Preview = string.Empty,
                    WorkingDirectory = NormalizeLocalThreadWorkingDirectory(AppServerJsonValue.GetString(payload, "cwd")),
                    Source = source,
                    ThreadSource = threadSource,
                    ParentThreadId = parentThreadId,
                    SubagentDepth = subagentDepth,
                    AgentNickname = AppServerJsonValue.GetString(payload, "agent_nickname"),
                    AgentRole = AppServerJsonValue.GetString(payload, "agent_role"),
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    SessionPath = sessionPath
                };
            }
            catch
            {
                return null;
            }
        }

        private static AppServerThreadSummary TryBuildLocalThreadSummary(
            string sessionPath,
            IDictionary<string, LocalSessionIndexEntry> sessionIndexEntries)
        {
            if (string.IsNullOrWhiteSpace(sessionPath) || !File.Exists(sessionPath))
            {
                return null;
            }

            try
            {
                string firstLine;
                using (var stream = new FileStream(
                    sessionPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    firstLine = reader.ReadLine();
                }

                if (string.IsNullOrWhiteSpace(firstLine))
                {
                    return null;
                }

                var envelope = AppServerJsonValue.DeserializeDictionary(firstLine);
                if (!string.Equals(AppServerJsonValue.GetString(envelope, "type"), "session_meta", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var payload = AppServerJsonValue.GetDictionary(envelope, "payload");
                var threadId = AppServerJsonValue.GetString(payload, "id");
                if (string.IsNullOrWhiteSpace(threadId))
                {
                    return null;
                }

                LocalSessionIndexEntry sessionIndexEntry;
                sessionIndexEntries.TryGetValue(threadId, out sessionIndexEntry);

                var fileInfo = new FileInfo(sessionPath);
                var createdAt = ParseIsoDateTime(AppServerJsonValue.GetString(payload, "timestamp"));
                var updatedAt = sessionIndexEntry != null && sessionIndexEntry.UpdatedAt != default(DateTime)
                    ? sessionIndexEntry.UpdatedAt
                    : fileInfo.LastWriteTime;
                var title = sessionIndexEntry != null ? sessionIndexEntry.Title : null;
                var threadSource = AppServerJsonValue.GetString(payload, "thread_source");
                var source = threadSource;
                if (string.IsNullOrWhiteSpace(source))
                {
                    source = GetScalarString(payload, "source");
                    if (string.IsNullOrWhiteSpace(source))
                    {
                        source = AppServerJsonValue.GetString(payload, "originator");
                    }
                }
                var threadSpawn = AppServerJsonValue.GetDictionary(
                    AppServerJsonValue.GetDictionary(
                        AppServerJsonValue.GetDictionary(payload, "source"),
                        "subagent"),
                    "thread_spawn");
                var parentThreadId = AppServerJsonValue.GetString(threadSpawn, "parent_thread_id");
                var subagentDepth = GetNullableInt(threadSpawn, "depth");
                if (string.IsNullOrWhiteSpace(threadSource) && !string.IsNullOrWhiteSpace(parentThreadId))
                {
                    threadSource = "subagent";
                }

                return new AppServerThreadSummary
                {
                    ThreadId = threadId,
                    Title = title,
                    Preview = string.Empty,
                    WorkingDirectory = NormalizeLocalThreadWorkingDirectory(AppServerJsonValue.GetString(payload, "cwd")),
                    Source = source,
                    ThreadSource = threadSource,
                    ParentThreadId = parentThreadId,
                    SubagentDepth = subagentDepth,
                    AgentNickname = AppServerJsonValue.GetString(payload, "agent_nickname"),
                    AgentRole = AppServerJsonValue.GetString(payload, "agent_role"),
                    CreatedAt = createdAt == default(DateTime) ? fileInfo.CreationTime : createdAt,
                    UpdatedAt = updatedAt,
                    SessionPath = sessionPath
                };
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, LocalSessionIndexEntry> LoadLocalSessionIndexEntries(string codexHomePath)
        {
            var result = new Dictionary<string, LocalSessionIndexEntry>(StringComparer.OrdinalIgnoreCase);
            var sessionIndexPath = GetLocalSessionIndexPath(codexHomePath);
            if (!File.Exists(sessionIndexPath))
            {
                return result;
            }

            foreach (var line in File.ReadLines(sessionIndexPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                IDictionary<string, object> entry;
                try
                {
                    entry = AppServerJsonValue.DeserializeDictionary(line);
                }
                catch
                {
                    continue;
                }

                var threadId = AppServerJsonValue.GetString(entry, "id");
                if (string.IsNullOrWhiteSpace(threadId))
                {
                    continue;
                }

                var updatedAt = ParseIsoDateTime(AppServerJsonValue.GetString(entry, "updated_at"));
                var title = AppServerJsonValue.GetString(entry, "thread_name");

                LocalSessionIndexEntry existingEntry;
                if (!result.TryGetValue(threadId, out existingEntry))
                {
                    result[threadId] = new LocalSessionIndexEntry
                    {
                        Title = title,
                        UpdatedAt = updatedAt
                    };
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(title))
                {
                    existingEntry.Title = title;
                }

                if (updatedAt > existingEntry.UpdatedAt)
                {
                    existingEntry.UpdatedAt = updatedAt;
                }
            }

            return result;
        }

        private static string GetLocalSessionRootPath(bool archived, string codexHomePath)
        {
            return Path.Combine(codexHomePath, archived ? "archived_sessions" : "sessions");
        }

        private static string GetLocalSessionIndexPath(string codexHomePath)
        {
            return Path.Combine(codexHomePath, "session_index.jsonl");
        }

        private static void AddDistinctPath(ICollection<string> paths, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalizedPath = NormalizePathForComparison(path);
            foreach (var existingPath in paths)
            {
                if (string.Equals(NormalizePathForComparison(existingPath), normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            paths.Add(path);
        }

        private static string NormalizePathForComparison(string path)
        {
            try
            {
                return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static DateTime ParseIsoDateTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return default(DateTime);
            }

            DateTimeOffset timestamp;
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp)
                ? timestamp.LocalDateTime
                : default(DateTime);
        }

        private static string GetScalarString(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return value is IDictionary<string, object>
                ? null
                : Convert.ToString(value, CultureInfo.CurrentCulture);
        }

        private static int? GetNullableInt(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            int result;
            return int.TryParse(Convert.ToString(value, CultureInfo.CurrentCulture), out result)
                ? (int?)result
                : null;
        }

        private static string NormalizeDriveRelativeWindowsPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var trimmedPath = path.Trim();
            if (trimmedPath.Length < 2 || !char.IsLetter(trimmedPath[0]) || trimmedPath[1] != ':')
            {
                return trimmedPath;
            }

            if (trimmedPath.Length == 2)
            {
                return trimmedPath + "\\";
            }

            var separator = trimmedPath[2];
            return separator == '\\' || separator == '/'
                ? trimmedPath
                : trimmedPath.Insert(2, "\\");
        }

        private static bool IsWindowsDriveAbsolutePath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.Length >= 3 &&
                   char.IsLetter(path[0]) &&
                   path[1] == ':' &&
                   (path[2] == '\\' || path[2] == '/');
        }

        private static string ExtractEmbeddedWindowsAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var trimmedPath = path.Trim();
            var lastWindowsPathIndex = -1;
            for (var i = 0; i + 2 < trimmedPath.Length; i++)
            {
                if (!char.IsLetter(trimmedPath[i]) || trimmedPath[i + 1] != ':')
                {
                    continue;
                }

                var separator = trimmedPath[i + 2];
                if (separator != '\\' && separator != '/')
                {
                    continue;
                }

                lastWindowsPathIndex = i;
            }

            return lastWindowsPathIndex >= 0
                ? trimmedPath.Substring(lastWindowsPathIndex)
                : null;
        }

        private static bool ShouldReplaceThreadWorkingDirectory(string existingPath, string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(existingPath))
            {
                return true;
            }

            return GetThreadWorkingDirectoryQuality(candidatePath) > GetThreadWorkingDirectoryQuality(existingPath);
        }

        private static int GetThreadWorkingDirectoryQuality(string path)
        {
            var normalizedPath = AppServerPathUtilities.NormalizeDisplayPath(path);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return 0;
            }

            if (IsWindowsDriveAbsolutePath(normalizedPath) ||
                normalizedPath.StartsWith(@"\\", StringComparison.Ordinal) ||
                AppServerPathUtilities.LooksLikeUnixAbsolutePath(normalizedPath))
            {
                return 2;
            }

            return 1;
        }

        private sealed class LocalSessionIndexEntry
        {
            public string Title { get; set; }

            public DateTime UpdatedAt { get; set; }
        }
    }
}
