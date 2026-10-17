using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerProtocolTextComposer
    {
        internal static string BuildFileChangeSummaryText(IDictionary<string, object> item, string bufferedOutput, out List<string> changedFiles)
        {
            changedFiles = new List<string>();
            var lines = new List<string>();

            foreach (var changeObject in GetArray(item, "changes"))
            {
                var change = changeObject as IDictionary<string, object>;
                if (change == null)
                {
                    continue;
                }

                var path = AppServerTurnPayloadMapper.NormalizeChangedFilePath(GetFirstNonEmptyString(change, "path", "filePath", "file", "name"));
                var movePath = GetMovePath(change);
                var kind = GetPatchChangeKind(change);
                AppServerTurnPayloadMapper.AddUniqueChangedFile(changedFiles, path);
                AppServerTurnPayloadMapper.AddUniqueChangedFile(changedFiles, movePath);

                string line;
                switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "add":
                        line = L("AppServerClient.FileChange.LinePrefix.Added", "Added ") + (path ?? L("AppServerClient.Value.UnknownFile", "(Unknown file)"));
                        break;
                    case "delete":
                        line = L("AppServerClient.FileChange.LinePrefix.Deleted", "Deleted ") + (path ?? L("AppServerClient.Value.UnknownFile", "(Unknown file)"));
                        break;
                    case "update":
                        line = !string.IsNullOrWhiteSpace(movePath)
                            ? LF("AppServerClient.FileChange.Line.Moved.Template", "Moved {0} -> {1}", path ?? L("AppServerClient.Value.UnknownFile", "(Unknown file)"), movePath)
                            : L("AppServerClient.FileChange.LinePrefix.Modified", "Modified ") + (path ?? L("AppServerClient.Value.UnknownFile", "(Unknown file)"));
                        break;
                    default:
                        line = !string.IsNullOrWhiteSpace(path)
                            ? L("AppServerClient.FileChange.LinePrefix.Processed", "Processed ") + path
                            : string.Empty;
                        break;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            if (changedFiles.Count == 0)
            {
                AppServerTurnPayloadMapper.AddChangedFilesFromArray(changedFiles, GetArray(item, "changedFiles"));
            }

            if (changedFiles.Count == 0)
            {
                var diffText = AppServerTurnPayloadMapper.ExtractDiffText(item);
                if (!string.IsNullOrWhiteSpace(diffText))
                {
                    AppServerTurnPayloadMapper.AddChangedFilesFromDiff(changedFiles, diffText);
                    var preview = BuildFileChangeProgressPreview(diffText);
                    if (!string.IsNullOrWhiteSpace(preview))
                    {
                        return preview;
                    }
                }
            }

            if (lines.Count > 0)
            {
                if (lines.Count > 4)
                {
                    lines = lines.GetRange(0, 4);
                    lines.Add("...");
                }

                return string.Join(Environment.NewLine, lines.ToArray());
            }

            if (!string.IsNullOrWhiteSpace(bufferedOutput))
            {
                return BuildFileChangeProgressPreview(bufferedOutput);
            }

            if (changedFiles.Count > 0)
            {
                return BuildChangedFilesPreview(changedFiles, L("AppServerClient.FileChange.ProcessedPrefix", "Processed"));
            }

            return string.Empty;
        }

        internal static string BuildChangedFilesPreview(IReadOnlyList<string> changedFiles, string prefix)
        {
            if (changedFiles == null || changedFiles.Count == 0)
            {
                return string.Empty;
            }

            if (changedFiles.Count == 1)
            {
                return prefix + " " + changedFiles[0];
            }

            var builder = new StringBuilder();
            builder.Append(prefix);
            builder.Append(" ");
            builder.Append(changedFiles[0]);

            var displayCount = Math.Min(3, changedFiles.Count);
            for (var i = 1; i < displayCount; i++)
            {
                builder.Append(Environment.NewLine);
                builder.Append(changedFiles[i]);
            }

            if (changedFiles.Count > displayCount)
            {
                builder.Append(Environment.NewLine);
                builder.Append(L("AppServerClient.FileChange.MoreFiles.Prefix", "And "));
                builder.Append(changedFiles.Count - displayCount);
                builder.Append(L("AppServerClient.FileChange.MoreFiles.Suffix", " more files"));
            }

            return builder.ToString();
        }

        internal static string BuildRecentTextPreview(string text, int maxLines, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var lines = new List<string>();
            for (var i = 0; i < rawLines.Length; i++)
            {
                var line = rawLines[i].Trim();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            if (lines.Count == 0)
            {
                return string.Empty;
            }

            var startIndex = Math.Max(0, lines.Count - Math.Max(1, maxLines));
            var selectedLines = lines.GetRange(startIndex, lines.Count - startIndex);
            var result = string.Join(Environment.NewLine, selectedLines.ToArray());
            if (result.Length > maxCharacters)
            {
                result = result.Substring(0, maxCharacters).TrimEnd() + "...";
            }

            return result;
        }

        internal static string GetPatchChangeKind(IDictionary<string, object> change)
        {
            var kindValue = AppServerJsonValueReader.GetValue(change, "kind");
            var text = kindValue as string;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var kindObject = kindValue as IDictionary<string, object>;
            if (kindObject == null || kindObject.Count == 0)
            {
                return GetFirstNonEmptyString(change, "changeType", "type");
            }

            foreach (var pair in kindObject)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    return pair.Key;
                }
            }

            return string.Empty;
        }

        internal static string GetMovePath(IDictionary<string, object> change)
        {
            var directMovePath = AppServerTurnPayloadMapper.NormalizeChangedFilePath(GetFirstNonEmptyString(change, "move_path", "movePath"));
            if (!string.IsNullOrWhiteSpace(directMovePath))
            {
                return directMovePath;
            }

            var kindObject = AppServerJsonValueReader.GetValue(change, "kind") as IDictionary<string, object>;
            if (kindObject == null || kindObject.Count == 0)
            {
                return null;
            }

            var update = GetDictionary(kindObject, "update");
            return AppServerTurnPayloadMapper.NormalizeChangedFilePath(GetFirstNonEmptyString(update, "move_path", "movePath"));
        }

        internal static string StripAnsiEscapeSequences(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var current = text[i];
                if (current == '\u001b')
                {
                    i++;
                    if (i >= text.Length)
                    {
                        break;
                    }

                    if (text[i] == '[')
                    {
                        while (i + 1 < text.Length)
                        {
                            var next = text[i + 1];
                            i++;
                            if (next >= '@' && next <= '~')
                            {
                                break;
                            }
                        }
                    }

                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        internal static string BuildMcpToolStatusText(string message, bool isCompleted)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message.Trim();
            }

            return isCompleted
                ? L("AppServerClient.Status.Completed", "Completed")
                : L("AppServerClient.Status.Running", "Running");
        }

        internal static string BuildPlanMessageText(string planText)
        {
            return L("AppServerClient.Plan.MessagePrefix", "Plan:") +
                Environment.NewLine +
                (planText ?? string.Empty).Trim();
        }

        internal static string BuildThreadGoalUpdatedMessage(AppServerThreadGoal goal)
        {
            if (goal == null || string.IsNullOrWhiteSpace(goal.Objective))
            {
                return L("AppServerClient.ThreadGoal.Updated", "Thread goal updated.");
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                L("AppServerClient.ThreadGoal.Updated.Template", "Thread goal updated: {0}"),
                goal.Objective.Trim());
        }

        internal static string BuildFileChangeProgressPreview(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var changedFiles = new List<string>();
            AppServerTurnPayloadMapper.AddChangedFilesFromDiff(changedFiles, text);
            if (changedFiles.Count > 0)
            {
                return BuildChangedFilesPreview(changedFiles, L("AppServerClient.FileChange.ProgressPrefix", "Processing"));
            }

            return BuildRecentTextPreview(text, 3, 220);
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetDictionary(source, key);
        }

        private static string GetFirstNonEmptyString(IDictionary<string, object> source, params string[] keys)
        {
            return AppServerJsonValueReader.GetFirstNonEmptyString(source, keys);
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetArray(source, key);
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key, fallback), args ?? Array.Empty<object>());
        }
    }
}
