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
    }
}
