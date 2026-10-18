using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class MainFormModelOptionComposer
    {
        private const string AggregatedModelPrefix = "ccs-";

        internal static List<AppServerModelOption> BuildOrderedModelOptions(IEnumerable<AppServerModelOption> models)
        {
            var result = new List<AppServerModelOption>();
            if (models != null)
            {
                foreach (var model in models)
                {
                    if (model != null && !string.IsNullOrWhiteSpace(model.ModelId))
                    {
                        result.Add(model);
                    }
                }
            }

            result.Sort(CompareModelOptions);
            return result;
        }

        internal static int CompareModelOptions(AppServerModelOption left, AppServerModelOption right)
        {
            var leftKey = BuildModelSortKey(left != null ? left.ModelId : null);
            var rightKey = BuildModelSortKey(right != null ? right.ModelId : null);

            var compareResult = rightKey.Major.CompareTo(leftKey.Major);
            if (compareResult != 0)
            {
                return compareResult;
            }

            compareResult = rightKey.Minor.CompareTo(leftKey.Minor);
            if (compareResult != 0)
            {
                return compareResult;
            }

            compareResult = leftKey.VariantRank.CompareTo(rightKey.VariantRank);
            if (compareResult != 0)
            {
                return compareResult;
            }

            return string.Compare(BuildModelMenuLabel(left), BuildModelMenuLabel(right), StringComparison.OrdinalIgnoreCase);
        }

        internal static List<AppServerModelEffortOption> GetEffortsForModel(AppServerModelOption model)
        {
            var efforts = new List<AppServerModelEffortOption>();
            if (model != null && model.ReasoningEfforts != null)
            {
                foreach (var effort in model.ReasoningEfforts)
                {
                    if (effort != null && !string.IsNullOrWhiteSpace(effort.EffortId))
                    {
                        efforts.Add(effort);
                    }
                }
            }

            efforts.Sort((left, right) => GetReasoningEffortRank(right.EffortId).CompareTo(GetReasoningEffortRank(left.EffortId)));
            return efforts;
        }

        internal static string BuildModelDescriptionText(AppServerModelOption model)
        {
            if (model == null)
            {
                return string.Empty;
            }

            var name = string.IsNullOrWhiteSpace(model.DisplayName) ? model.ModelId : model.DisplayName;
            if (string.IsNullOrWhiteSpace(model.Description))
            {
                return name;
            }

            return name + " - " + model.Description;
        }

        internal static string BuildModelMenuLabel(AppServerModelOption model)
        {
            if (model == null || string.IsNullOrWhiteSpace(model.ModelId))
            {
                return UiLocalization.Text("Common.Default", "Default");
            }

            if (IsAggregatedModelId(model.ModelId) && !string.IsNullOrWhiteSpace(model.DisplayName))
            {
                return model.DisplayName.Trim();
            }

            return GetModelVersionLabel(model.ModelId);
        }

        // Aggregated models ("ccs-<key>/<model>") carry an opaque provider key; their
        // display name names the model and its provider instead.
        internal static bool IsAggregatedModelId(string modelId)
        {
            return !string.IsNullOrWhiteSpace(modelId) &&
                modelId.StartsWith(AggregatedModelPrefix, StringComparison.OrdinalIgnoreCase) &&
                modelId.IndexOf('/') > AggregatedModelPrefix.Length;
        }

        internal static string GetModelVersionLabel(string modelId, IEnumerable<AppServerModelOption> models)
        {
            if (IsAggregatedModelId(modelId) && models != null)
            {
                foreach (var model in models)
                {
                    if (model != null && string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
                    {
                        return BuildModelMenuLabel(model);
                    }
                }
            }

            return GetModelVersionLabel(modelId);
        }

        internal static string GetModelVersionLabel(AppServerModelOption model)
        {
            return GetModelVersionLabel(model != null ? model.ModelId : null);
        }

        internal static string GetModelVersionLabel(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return UiLocalization.Text("Common.Default", "Default");
            }

            var marker = "gpt-";
            var index = modelId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var suffix = modelId.Substring(index + marker.Length);
                var endIndex = suffix.IndexOf('-');
                return endIndex >= 0 ? suffix.Substring(0, endIndex).Replace("-", " ") + " " + suffix.Substring(endIndex + 1).Replace("-", " ") : suffix.Replace("-", " ");
            }

            return modelId.Replace("-", " ");
        }

        internal static string GetReasoningEffortText(string effortId)
        {
            switch ((effortId ?? string.Empty).ToLowerInvariant())
            {
                case "minimal":
                    return UiLocalization.Text("ReasoningEffort.Minimal", "Minimal");
                case "low":
                    return UiLocalization.Text("ReasoningEffort.Low", "Low");
                case "medium":
                    return UiLocalization.Text("ReasoningEffort.Medium", "Medium");
                case "high":
                    return UiLocalization.Text("ReasoningEffort.High", "High");
                case "xhigh":
                    return UiLocalization.Text("ReasoningEffort.XHigh", "Extra High");
                case "max":
                    return UiLocalization.Text("ReasoningEffort.Max", "Max");
                case "ultra":
                    return UiLocalization.Text("ReasoningEffort.Ultra", "Ultra");
                case "none":
                    return UiLocalization.Text("ReasoningEffort.None", "None");
                default:
                    return string.IsNullOrWhiteSpace(effortId)
                        ? UiLocalization.Text("Common.Default", "Default")
                        : effortId;
            }
        }

        internal static int GetReasoningEffortRank(string effortId)
        {
            switch ((effortId ?? string.Empty).ToLowerInvariant())
            {
                case "ultra":
                    return 7;
                case "max":
                    return 6;
                case "xhigh":
                    return 5;
                case "high":
                    return 4;
                case "medium":
                    return 3;
                case "low":
                    return 2;
                case "minimal":
                    return 1;
                case "none":
                    return 0;
                default:
                    return -1;
            }
        }

        private static ModelSortKey BuildModelSortKey(string modelId)
        {
            var normalized = (modelId ?? string.Empty).Trim();
            if (normalized.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(4);
            }

            var parts = normalized.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            var versionText = parts.Length > 0 ? parts[0] : string.Empty;
            var suffixText = parts.Length > 1 ? string.Join("-", parts, 1, parts.Length - 1).ToLowerInvariant() : string.Empty;

            var major = 0;
            var minor = 0;
            var versionParts = versionText.Split('.');
            if (versionParts.Length > 0)
            {
                int.TryParse(versionParts[0], out major);
            }

            if (versionParts.Length > 1)
            {
                int.TryParse(versionParts[1], out minor);
            }

            var variantRank = 2;
            if (string.IsNullOrWhiteSpace(suffixText))
            {
                variantRank = 0;
            }
            else if (string.Equals(suffixText, "mini", StringComparison.OrdinalIgnoreCase))
            {
                variantRank = 1;
            }

            return new ModelSortKey(major, minor, variantRank);
        }

        private sealed class ModelSortKey
        {
            public ModelSortKey(int major, int minor, int variantRank)
            {
                Major = major;
                Minor = minor;
                VariantRank = variantRank;
            }

            public int Major { get; }

            public int Minor { get; }

            public int VariantRank { get; }
        }
    }
}
