using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public static class OfficialCodexSettingsEditPlanner
    {
        private static readonly string[] ReasoningSummaries = { "auto", "concise", "detailed", "none" };
        private static readonly string[] Verbosities = { "low", "medium", "high" };
        private static readonly string[] ServiceTiers = { "priority" };
        private static readonly string[] WebSearchModes = { "disabled", "cached", "indexed", "live" };
        private static readonly string[] SearchContextSizes = { "low", "medium", "high" };
        private static readonly long[] ToolOutputLimits = { 8192L, 16384L, 32768L };
        private static readonly long[] ProjectDocumentLimits = { 65536L, 131072L, 262144L };

        public static OfficialCodexSettingsSelection ReadUserSettings(AppServerConfigSnapshot snapshot)
        {
            snapshot = snapshot ?? new AppServerConfigSnapshot();
            return new OfficialCodexSettingsSelection
            {
                ModelReasoningSummary = GetUserValue(snapshot, "model_reasoning_summary", snapshot.ModelReasoningSummary),
                ModelVerbosity = GetUserValue(snapshot, "model_verbosity", snapshot.ModelVerbosity),
                ServiceTier = NormalizeOptional(GetUserValue(snapshot, "service_tier", snapshot.ServiceTier)),
                WebSearchMode = GetUserValue(snapshot, "web_search", snapshot.WebSearchMode),
                WebSearchContextSize = GetUserValue(snapshot, "tools.web_search.context_size", snapshot.WebSearchContextSize),
                ViewImageToolEnabled = GetUserValue(snapshot, "tools.view_image", snapshot.ViewImageToolEnabled),
                ToolOutputTokenLimit = GetUserValue(snapshot, "tool_output_token_limit", snapshot.ToolOutputTokenLimit),
                ProjectDocMaxBytes = GetUserValue(snapshot, "project_doc_max_bytes", snapshot.ProjectDocMaxBytes)
            };
        }

        public static List<AppServerConfigWriteEdit> BuildEdits(
            AppServerConfigSnapshot current,
            OfficialCodexSettingsSelection requested)
        {
            var existing = ReadUserSettings(current);
            Validate(requested, existing);
            var edits = new List<AppServerConfigWriteEdit>();
            AddEdit(edits, "model_reasoning_summary", existing.ModelReasoningSummary, requested.ModelReasoningSummary);
            AddEdit(edits, "model_verbosity", existing.ModelVerbosity, requested.ModelVerbosity);
            AddEdit(edits, "service_tier", existing.ServiceTier, NormalizeOptional(requested.ServiceTier));
            AddEdit(edits, "web_search", existing.WebSearchMode, requested.WebSearchMode);
            AddEdit(edits, "tools.web_search.context_size", existing.WebSearchContextSize, requested.WebSearchContextSize);
            AddEdit(edits, "tools.view_image", existing.ViewImageToolEnabled, requested.ViewImageToolEnabled);
            AddEdit(edits, "tool_output_token_limit", existing.ToolOutputTokenLimit, requested.ToolOutputTokenLimit);
            AddEdit(edits, "project_doc_max_bytes", existing.ProjectDocMaxBytes, requested.ProjectDocMaxBytes);
            return edits;
        }

        public static List<AppServerConfigWriteEdit> BuildModelEdits(
            AppServerConfigSnapshot current, string modelId, string reasoningEffort)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }
            var edits = new List<AppServerConfigWriteEdit>();
            AddEdit(edits, "model", current.ModelId, string.IsNullOrWhiteSpace(modelId) ? null : modelId.Trim());
            AddEdit(edits, "model_reasoning_effort", current.ModelReasoningEffort,
                string.IsNullOrWhiteSpace(reasoningEffort) ? null : reasoningEffort.Trim());
            return edits;
        }

        private static void Validate(
            OfficialCodexSettingsSelection settings,
            OfficialCodexSettingsSelection existing)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            ValidateChoice(settings.ModelReasoningSummary, existing.ModelReasoningSummary, ReasoningSummaries, nameof(settings.ModelReasoningSummary));
            ValidateChoice(settings.ModelVerbosity, existing.ModelVerbosity, Verbosities, nameof(settings.ModelVerbosity));
            ValidateChoice(settings.ServiceTier, existing.ServiceTier, ServiceTiers, nameof(settings.ServiceTier));
            ValidateChoice(settings.WebSearchMode, existing.WebSearchMode, WebSearchModes, nameof(settings.WebSearchMode));
            ValidateChoice(settings.WebSearchContextSize, existing.WebSearchContextSize, SearchContextSizes, nameof(settings.WebSearchContextSize));
            ValidateLongChoice(settings.ToolOutputTokenLimit, existing.ToolOutputTokenLimit, ToolOutputLimits, nameof(settings.ToolOutputTokenLimit));
            ValidateLongChoice(settings.ProjectDocMaxBytes, existing.ProjectDocMaxBytes, ProjectDocumentLimits, nameof(settings.ProjectDocMaxBytes));
        }

        private static T GetUserValue<T>(AppServerConfigSnapshot snapshot, string keyPath, T value)
        {
            AppServerConfigOrigin origin;
            return snapshot.Origins != null &&
                snapshot.Origins.TryGetValue(keyPath, out origin) &&
                origin != null &&
                string.Equals(origin.SourceType, "user", StringComparison.OrdinalIgnoreCase)
                    ? value
                    : default(T);
        }

        private static void ValidateChoice(
            string value,
            string existingValue,
            IEnumerable<string> allowed,
            string name)
        {
            if (ValuesEqual(value, existingValue))
            {
                return;
            }

            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (normalized != null && !allowed.Contains(normalized, StringComparer.Ordinal))
            {
                throw new ArgumentException("Unsupported official Codex setting value.", name);
            }
        }

        private static void ValidateLongChoice(
            long? value,
            long? existingValue,
            IEnumerable<long> allowed,
            string name)
        {
            if (value == existingValue)
            {
                return;
            }

            if (value.HasValue && !allowed.Contains(value.Value))
            {
                throw new ArgumentException("Unsupported official Codex setting value.", name);
            }
        }

        private static void AddEdit(
            ICollection<AppServerConfigWriteEdit> edits,
            string keyPath,
            object currentValue,
            object newValue)
        {
            if (!ValuesEqual(currentValue, newValue))
            {
                edits.Add(AppServerConfigWriteEdit.CreateReplace(keyPath, newValue));
            }
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (left is string || right is string)
            {
                return string.Equals(
                    NormalizeOptional(left as string),
                    NormalizeOptional(right as string),
                    StringComparison.OrdinalIgnoreCase);
            }

            return object.Equals(left, right);
        }

        private static string NormalizeOptional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }
    }
}
