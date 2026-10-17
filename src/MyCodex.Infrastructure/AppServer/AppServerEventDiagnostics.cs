using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerEventDiagnostics
    {
        public const string EnvironmentVariableName = "MYCODEX_APP_SERVER_EVENT_DIAGNOSTICS";

        public static void TraceNotification(string method, IDictionary<string, object> parameters)
        {
            if (!IsEnabled() || !ShouldTraceNotification(method))
            {
                return;
            }

            Trace.TraceInformation(
                "App Server event diagnostic. Method={0}; {1}",
                method ?? string.Empty,
                BuildSummary(parameters));
        }

        public static void TraceMappedEvent(AppServerEventArgs args)
        {
            if (!IsEnabled() || args == null || !ShouldTraceMappedEvent(args.EventType))
            {
                return;
            }

            Trace.TraceInformation(
                "Mapped App Server event diagnostic. {0}",
                BuildMappedEventSummary(args));
        }

        public static void TraceConfigWrite(IReadOnlyCollection<AppServerConfigWriteEdit> edits, bool reloadUserConfig, string filePath)
        {
            if (!IsEnabled() || edits == null)
            {
                return;
            }

            var keyPaths = edits
                .Where(edit => edit != null && !string.IsNullOrWhiteSpace(edit.KeyPath))
                .Select(edit => edit.KeyPath.Trim())
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            Trace.TraceInformation(
                "App Server config write diagnostic. KeyPaths={0}; ReloadUserConfig={1}; FilePathSet={2}",
                string.Join(",", keyPaths),
                reloadUserConfig,
                !string.IsNullOrWhiteSpace(filePath));
        }

        public static bool IsEnabled()
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldTraceNotification(string method)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                return false;
            }

            return method.StartsWith("turn/", StringComparison.OrdinalIgnoreCase) ||
                   method.StartsWith("item/", StringComparison.OrdinalIgnoreCase) ||
                   method.IndexOf("reasoning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   method.IndexOf("agentMessage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(method, "error", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldTraceMappedEvent(string eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return false;
            }

            return eventType.StartsWith("turn_", StringComparison.OrdinalIgnoreCase) ||
                   eventType.IndexOf("reasoning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   eventType.IndexOf("assistant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   string.Equals(eventType, "error", StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildMappedEventSummary(AppServerEventArgs args)
        {
            if (args == null)
            {
                return "EventType=; ThreadId=; TurnId=; ItemId=; ContentLength=0; DetailsLength=0";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "EventType={0}; ThreadId={1}; TurnId={2}; ItemId={3}; ContentLength={4}; DetailsLength={5}",
                args.EventType ?? string.Empty,
                args.ThreadId ?? string.Empty,
                args.TurnId ?? string.Empty,
                args.ItemId ?? string.Empty,
                GetLength(args.Content),
                GetLength(args.Details));
        }

        public static string BuildSummary(IDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return "params=(empty)";
            }

            var parts = new List<string>();
            parts.Add("keys=" + string.Join(",", parameters.Keys.OrderBy(key => key).ToArray()));
            AddStringPart(parts, "threadId", AppServerJsonValueReader.GetString(parameters, "threadId"));
            AddStringPart(parts, "turnId", AppServerJsonValueReader.GetString(parameters, "turnId"));
            AddStringPart(parts, "itemId", AppServerJsonValueReader.GetString(parameters, "itemId"));
            AddStringPart(parts, "type", AppServerJsonValueReader.GetString(parameters, "type"));
            AddStringPart(parts, "status", AppServerJsonValueReader.GetString(parameters, "status"));
            AddIntPart(parts, "summaryIndex", AppServerJsonValueReader.GetNullableInt(parameters, "summaryIndex"));
            AddIntPart(parts, "contentIndex", AppServerJsonValueReader.GetNullableInt(parameters, "contentIndex"));
            AddLengthPart(parts, "deltaLength", AppServerJsonValueReader.GetString(parameters, "delta"));
            AddLengthPart(parts, "textLength", AppServerJsonValueReader.GetString(parameters, "text"));
            AddLengthPart(parts, "contentLength", AppServerJsonValueReader.GetString(parameters, "content"));
            AddLengthPart(parts, "detailsLength", AppServerJsonValueReader.GetString(parameters, "details"));
            AddLengthPart(parts, "messageLength", AppServerJsonValueReader.GetString(parameters, "message"));

            var item = AppServerJsonValueReader.GetDictionary(parameters, "item");
            if (item != null && item.Count > 0)
            {
                AddStringPart(parts, "item.id", AppServerJsonValueReader.GetString(item, "id"));
                AddStringPart(parts, "item.type", AppServerJsonValueReader.GetString(item, "type"));
                AddStringPart(parts, "item.status", AppServerJsonValueReader.GetString(item, "status"));
                AddLengthPart(parts, "item.summaryLength", AppServerJsonValueReader.GetString(item, "summary"));
                var summaryValue = AppServerJsonValueReader.GetValue(item, "summary");
                if (summaryValue != null && !(summaryValue is string))
                {
                    parts.Add("item.summaryType=" + summaryValue.GetType().Name);
                }
            }

            return string.Join("; ", parts.ToArray());
        }

        private static void AddStringPart(List<string> parts, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(name + "=" + value);
            }
        }

        private static void AddIntPart(List<string> parts, string name, int? value)
        {
            if (value.HasValue)
            {
                parts.Add(name + "=" + value.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AddLengthPart(List<string> parts, string name, string value)
        {
            if (value != null)
            {
                parts.Add(name + "=" + value.Length.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static int GetLength(string value)
        {
            return value == null ? 0 : value.Length;
        }
    }
}
