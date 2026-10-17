using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class UnixFileSystemBridgeParsers
    {
        public static Dictionary<string, string> ParseKeyValueOutput(string output)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(output))
            {
                return result;
            }

            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var separatorIndex = rawLine.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                result[rawLine.Substring(0, separatorIndex)] = rawLine.Substring(separatorIndex + 1);
            }

            return result;
        }

        public static IReadOnlyList<RuntimeFileSystemEntrySnapshot> ParseDirectoryListing(string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                return new List<RuntimeFileSystemEntrySnapshot>();
            }

            var entries = new List<RuntimeFileSystemEntrySnapshot>();
            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = rawLine.Split('\t');
                if (parts.Length < 3)
                {
                    continue;
                }

                var name = DecodeBase64Field(parts[1]);
                var fullPath = DecodeBase64Field(parts[2]);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(fullPath))
                {
                    continue;
                }

                entries.Add(new RuntimeFileSystemEntrySnapshot
                {
                    Name = name,
                    FullPath = fullPath,
                    IsDirectory = string.Equals(parts[0], "dir", StringComparison.OrdinalIgnoreCase),
                    IsHidden = parts.Length > 3 && string.Equals(parts[3], "1", StringComparison.Ordinal),
                    Size = parts.Length > 4 ? ParseNullableLong(parts[4]) : null,
                    ModifiedAt = parts.Length > 5 ? ParseUnixTime(parts[5]) : null
                });
            }

            return entries
                .OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string DecodeBase64Field(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        public static long? ParseNullableLong(string value)
        {
            long parsed;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : (long?)null;
        }

        public static DateTime? ParseUnixTime(string value)
        {
            var seconds = ParseNullableLong(value);
            if (!seconds.HasValue)
            {
                return null;
            }

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds.Value).LocalDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        public static string GetValue(IDictionary<string, string> values, string key)
        {
            string value;
            return values != null && values.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : null;
        }

        public static string[] SplitNullSeparated(string value)
        {
            return string.IsNullOrEmpty(value)
                ? new string[0]
                : value.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static GitDiffFileEntry ParseStatusEntry(string entryText)
        {
            if (string.IsNullOrWhiteSpace(entryText) || entryText.Length < 3)
            {
                return null;
            }

            var rawStatus = entryText.Substring(0, 2);
            var path = entryText.Length > 3 ? NormalizeStatusPath(entryText.Substring(3)) : string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return new GitDiffFileEntry
            {
                FilePath = path,
                ChangeKind = MapStatusToChangeKind(rawStatus)
            };
        }

        public static string NormalizeStatusPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalized = path.Trim().Trim('"').Replace('\\', '/');
            while (normalized.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                normalized = normalized.Replace("//", "/");
            }

            return normalized;
        }

        public static string MapStatusToChangeKind(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "modified";
            }

            if (status.IndexOf("??", StringComparison.Ordinal) >= 0)
            {
                return "untracked";
            }

            if (status.IndexOf('A') >= 0)
            {
                return "added";
            }

            if (status.IndexOf('D') >= 0)
            {
                return "deleted";
            }

            if (status.IndexOf('R') >= 0)
            {
                return "renamed";
            }

            if (status.IndexOf('C') >= 0)
            {
                return "copied";
            }

            return "modified";
        }

        public static string BuildDiffText(GitDiffFileEntry entry, string stagedDiff, string unstagedDiff)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
            {
                return string.Empty;
            }

            if (string.Equals(entry.ChangeKind, "untracked", StringComparison.OrdinalIgnoreCase))
            {
                return string.Format(
                    SharedLocalization.Text(
                        "GitDiffService.Untracked.Placeholder.Template",
                        "Untracked file: {0}\r\n\r\nThis file has not been added to Git yet. Open the file to inspect its current content."),
                    entry.FilePath);
            }

            if (string.IsNullOrWhiteSpace(stagedDiff))
            {
                return unstagedDiff == null ? string.Empty : unstagedDiff.Trim();
            }

            if (string.IsNullOrWhiteSpace(unstagedDiff))
            {
                return stagedDiff.Trim();
            }

            return stagedDiff.Trim() + Environment.NewLine + Environment.NewLine + unstagedDiff.Trim();
        }

        public static void PopulateDiffLineCounts(GitDiffFileEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            var counts = UnifiedDiffLineCounter.Count(entry.DiffText);
            entry.AddedLineCount = counts.Additions;
            entry.DeletedLineCount = counts.Deletions;
        }
    }
}
