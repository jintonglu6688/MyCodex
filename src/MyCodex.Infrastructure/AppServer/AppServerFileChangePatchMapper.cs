using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerFileChangePatchMapper
    {
        internal static AppServerEventArgs BuildEvent(IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            var turnId = AppServerJsonValueReader.GetString(parameters, "turnId");
            var itemId = AppServerJsonValueReader.GetString(parameters, "itemId");
            var changedFiles = new List<string>();
            var detailLines = new List<string>();
            var diffBuilder = new StringBuilder();

            foreach (var changeObject in GetArray(parameters, "changes"))
            {
                var change = changeObject as IDictionary<string, object>;
                if (change == null)
                {
                    continue;
                }

                var path = AppServerTurnPayloadMapper.NormalizeChangedFilePath(AppServerJsonValueReader.GetFirstNonEmptyString(change, "path", "filePath", "file", "name"));
                var movePath = GetMovePath(change);
                var kind = GetPatchChangeKind(change);
                AppServerTurnPayloadMapper.AddUniqueChangedFile(changedFiles, path);
                AppServerTurnPayloadMapper.AddUniqueChangedFile(changedFiles, movePath);

                var detailLine = BuildDetailLine(kind, path, movePath);
                if (!string.IsNullOrWhiteSpace(detailLine))
                {
                    detailLines.Add(detailLine);
                }

                var diff = AppServerJsonValueReader.GetString(change, "diff");
                if (!string.IsNullOrWhiteSpace(diff))
                {
                    if (diffBuilder.Length > 0)
                    {
                        diffBuilder.AppendLine();
                        diffBuilder.AppendLine();
                    }

                    diffBuilder.Append(diff);
                }
            }

            if (string.IsNullOrWhiteSpace(threadId) &&
                string.IsNullOrWhiteSpace(turnId) &&
                string.IsNullOrWhiteSpace(itemId) &&
                changedFiles.Count == 0 &&
                detailLines.Count == 0 &&
                diffBuilder.Length == 0)
            {
                return null;
            }

            var details = detailLines.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, detailLines.ToArray());
            var content = BuildChangedFilesPreview(changedFiles, L("AppServerClient.FileChange.ProgressPrefix", "Processing"));
            if (string.IsNullOrWhiteSpace(content))
            {
                content = L("AppServerClient.FileChange.PatchUpdated", "File changes updated.");
            }

            return new AppServerEventArgs(
                "file_change_patch_updated",
                content,
                threadId: threadId,
                turnId: turnId,
                itemId: itemId,
                details: details,
                diffText: diffBuilder.Length == 0 ? null : diffBuilder.ToString(),
                changedFiles: changedFiles);
        }

        private static string BuildDetailLine(string kind, string path, string movePath)
        {
            var displayPath = string.IsNullOrWhiteSpace(path)
                ? L("AppServerClient.Value.UnknownFile", "(Unknown file)")
                : path;

            switch ((kind ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "add":
                    return L("AppServerClient.FileChange.LinePrefix.Added", "Added ") + displayPath;
                case "delete":
                    return L("AppServerClient.FileChange.LinePrefix.Deleted", "Deleted ") + displayPath;
                case "update":
                    return !string.IsNullOrWhiteSpace(movePath)
                        ? LF("AppServerClient.FileChange.Line.Moved.Template", "Moved {0} -> {1}", displayPath, movePath)
                        : L("AppServerClient.FileChange.LinePrefix.Modified", "Modified ") + displayPath;
                default:
                    return !string.IsNullOrWhiteSpace(path)
                        ? L("AppServerClient.FileChange.LinePrefix.Processed", "Processed ") + path
                        : string.Empty;
            }
        }

        private static string BuildChangedFilesPreview(IReadOnlyList<string> changedFiles, string prefix)
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

        private static string GetPatchChangeKind(IDictionary<string, object> change)
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
                return AppServerJsonValueReader.GetFirstNonEmptyString(change, "changeType", "type");
            }

            var kindType = AppServerJsonValueReader.GetFirstNonEmptyString(kindObject, "type", "changeType");
            if (!string.IsNullOrWhiteSpace(kindType))
            {
                return kindType;
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

        private static string GetMovePath(IDictionary<string, object> change)
        {
            var directMovePath = AppServerTurnPayloadMapper.NormalizeChangedFilePath(AppServerJsonValueReader.GetFirstNonEmptyString(change, "move_path", "movePath"));
            if (!string.IsNullOrWhiteSpace(directMovePath))
            {
                return directMovePath;
            }

            var kindObject = AppServerJsonValueReader.GetValue(change, "kind") as IDictionary<string, object>;
            if (kindObject == null || kindObject.Count == 0)
            {
                return null;
            }

            var kindMovePath = AppServerTurnPayloadMapper.NormalizeChangedFilePath(AppServerJsonValueReader.GetFirstNonEmptyString(kindObject, "move_path", "movePath"));
            if (!string.IsNullOrWhiteSpace(kindMovePath))
            {
                return kindMovePath;
            }

            var update = AppServerJsonValueReader.GetDictionary(kindObject, "update");
            return AppServerTurnPayloadMapper.NormalizeChangedFilePath(AppServerJsonValueReader.GetFirstNonEmptyString(update, "move_path", "movePath"));
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                yield break;
            }

            var array = value as object[];
            if (array != null)
            {
                foreach (var item in array)
                {
                    yield return item;
                }

                yield break;
            }

            var enumerable = value as IEnumerable<object>;
            if (enumerable != null)
            {
                foreach (var item in enumerable)
                {
                    yield return item;
                }
            }
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key, fallback), args ?? new object[0]);
        }
    }
}
