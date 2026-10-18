using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerTurnPayloadMapper
    {
        internal static IReadOnlyList<AppServerTurnPlanStep> BuildTurnPlanSteps(IDictionary<string, object> parameters)
        {
            var steps = new List<AppServerTurnPlanStep>();
            foreach (var planObject in AppServerJsonValueReader.GetArray(parameters, "plan"))
            {
                var plan = planObject as IDictionary<string, object>;
                if (plan == null)
                {
                    continue;
                }

                var stepText = AppServerJsonValueReader.GetFirstNonEmptyString(plan, "step", "title", "description", "text", "content");
                if (string.IsNullOrWhiteSpace(stepText))
                {
                    continue;
                }

                steps.Add(new AppServerTurnPlanStep
                {
                    Step = stepText,
                    Status = AppServerJsonValue.GetString(plan, "status")
                });
            }

            return steps;
        }

        internal static string ExtractPlanDeltaText(IDictionary<string, object> parameters)
        {
            var deltaText = AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "delta", "text", "content");
            if (!string.IsNullOrWhiteSpace(deltaText))
            {
                return deltaText;
            }

            var item = AppServerJsonValue.GetDictionary(parameters, "item");
            return AppServerJsonValueReader.GetFirstNonEmptyString(item, "delta", "text", "content");
        }

        internal static string ExtractDiffText(IDictionary<string, object> parameters)
        {
            object rawDiff;
            if (parameters != null && parameters.TryGetValue("diff", out rawDiff) && rawDiff != null)
            {
                var diff = rawDiff as IDictionary<string, object>;
                if (diff != null)
                {
                    var nestedDiffText = AppServerJsonValueReader.GetFirstNonEmptyString(diff, "text", "diff", "unifiedDiff", "outputDiff");
                    if (!string.IsNullOrWhiteSpace(nestedDiffText))
                    {
                        return nestedDiffText;
                    }
                }
                else
                {
                    var directDiffText = Convert.ToString(rawDiff);
                    if (!string.IsNullOrWhiteSpace(directDiffText))
                    {
                        return directDiffText;
                    }
                }
            }

            return AppServerJsonValueReader.GetFirstNonEmptyString(parameters, "unifiedDiff", "outputDiff", "aggregatedDiff");
        }

        internal static List<string> ExtractChangedFiles(IDictionary<string, object> parameters, string diffText)
        {
            var files = new List<string>();
            AddChangedFilesFromArray(files, AppServerJsonValueReader.GetArray(parameters, "changedFiles"));
            AddChangedFilesFromArray(files, AppServerJsonValueReader.GetArray(parameters, "files"));

            if (files.Count == 0 && !string.IsNullOrWhiteSpace(diffText))
            {
                AddChangedFilesFromDiff(files, diffText);
            }

            return files;
        }

        internal static void AddChangedFilesFromArray(List<string> files, IEnumerable<object> items)
        {
            if (files == null || items == null)
            {
                return;
            }

            foreach (var item in items)
            {
                var text = item as string;
                if (string.IsNullOrWhiteSpace(text))
                {
                    var dictionary = item as IDictionary<string, object>;
                    if (dictionary != null)
                    {
                        text = AppServerJsonValueReader.GetFirstNonEmptyString(dictionary, "filePath", "path", "file", "name");
                    }
                }

                AddUniqueChangedFile(files, text);
            }
        }

        internal static void AddChangedFilesFromDiff(List<string> files, string diffText)
        {
            if (files == null || string.IsNullOrWhiteSpace(diffText))
            {
                return;
            }

            var lines = diffText.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    var markerIndex = line.LastIndexOf(" b/", StringComparison.Ordinal);
                    if (markerIndex >= 0 && markerIndex + 3 < line.Length)
                    {
                        AddUniqueChangedFile(files, line.Substring(markerIndex + 3));
                    }

                    continue;
                }

                if (line.StartsWith("+++ ", StringComparison.Ordinal) || line.StartsWith("--- ", StringComparison.Ordinal))
                {
                    AddUniqueChangedFile(files, line.Substring(4));
                }
            }
        }

        internal static void AddUniqueChangedFile(List<string> files, string filePath)
        {
            if (files == null)
            {
                return;
            }

            var normalizedPath = NormalizeChangedFilePath(filePath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return;
            }

            for (var i = 0; i < files.Count; i++)
            {
                if (string.Equals(files[i], normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            files.Add(normalizedPath);
        }

        internal static string NormalizeChangedFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var normalized = filePath.Trim();
            if (string.Equals(normalized, "/dev/null", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (normalized.StartsWith("a/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("b/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(2);
            }

            return normalized;
        }
    }
}
