using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerModerationMetadataMapper
    {
        private const int MaxValueLength = 160;

        internal static AppServerModerationMetadataEvent BuildEvent(IDictionary<string, object> parameters)
        {
            var metadata = AppServerJsonValueReader.GetValue(parameters, "metadata");
            var summary = BuildSummary(metadata);
            return new AppServerModerationMetadataEvent
            {
                ThreadId = AppServerJsonValue.GetString(parameters, "threadId"),
                TurnId = AppServerJsonValue.GetString(parameters, "turnId"),
                Summary = string.IsNullOrWhiteSpace(summary) ? "moderation metadata received" : summary,
                Details = BuildDetails(metadata)
            };
        }

        private static string BuildSummary(object metadata)
        {
            var dictionary = metadata as IDictionary<string, object>;
            if (dictionary == null)
            {
                return metadata == null ? null : Convert.ToString(metadata, CultureInfo.InvariantCulture);
            }

            var parts = new List<string>();
            AddSafePair(parts, dictionary, "decision");
            AddSafePair(parts, dictionary, "status");
            AddSafePair(parts, dictionary, "category");
            AddSafePair(parts, dictionary, "reason");
            if (parts.Count == 0)
            {
                foreach (var pair in dictionary)
                {
                    if (parts.Count >= 4)
                    {
                        break;
                    }

                    if (IsSensitiveKey(pair.Key))
                    {
                        continue;
                    }

                    parts.Add(pair.Key);
                }
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string BuildDetails(object metadata)
        {
            var dictionary = metadata as IDictionary<string, object>;
            if (dictionary == null)
            {
                return "turn/moderationMetadata";
            }

            var builder = new StringBuilder();
            builder.AppendLine("turn/moderationMetadata");
            foreach (var pair in dictionary)
            {
                if (IsSensitiveKey(pair.Key))
                {
                    builder.AppendLine(pair.Key + "=<redacted>");
                    continue;
                }

                builder.AppendLine(pair.Key + "=" + SafeValue(pair.Value));
            }

            return builder.ToString().TrimEnd();
        }

        private static void AddSafePair(List<string> parts, IDictionary<string, object> dictionary, string key)
        {
            object value;
            if (dictionary != null &&
                dictionary.TryGetValue(key, out value) &&
                value != null &&
                !IsSensitiveKey(key))
            {
                parts.Add(key + "=" + SafeValue(value));
            }
        }

        private static string SafeValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (!(value is string) && value is IEnumerable)
            {
                return "<structured>";
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return text.Length <= MaxValueLength ? text : text.Substring(0, MaxValueLength) + "...";
        }

        private static bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            return key.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("apiKey", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("api_key", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   key.IndexOf("authorization", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
