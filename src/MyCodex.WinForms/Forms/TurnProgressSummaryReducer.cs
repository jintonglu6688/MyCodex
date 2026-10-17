using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class TurnProgressSummaryReducer
    {
        private const int StreamingPlanStepMaxLines = 2;
        private const int StreamingPlanStepMaxCharacters = 240;
        private const int StreamingPlanSummaryMaxLines = 8;
        private const int StreamingPlanSummaryMaxCharacters = 1200;
        private const int MaxCompletedReasoningSummaries = 12;

        internal static void PrepareForSend(TurnProgressSummaryState state, IReadOnlyList<string> skillNames)
        {
            ResetForPreparingCore(state, skillNames, true);
        }

        internal static void ResetForPreparing(TurnProgressSummaryState state)
        {
            ResetForPreparingCore(state, null, false);
        }

        private static void ResetForPreparingCore(TurnProgressSummaryState state, IReadOnlyList<string> skillNames, bool storePendingSkillNames)
        {
            if (state == null)
            {
                return;
            }

            state.TurnId = string.Empty;
            state.Explanation = string.Empty;
            state.PlanSteps.Clear();
            state.PlanDeltaText = string.Empty;
            state.ReasoningSummaryText = string.Empty;
            state.ReasoningText = string.Empty;
            state.IsReasoningActive = false;
            state.ActiveReasoningItemId = string.Empty;
            state.CompletedReasoningSummaries.Clear();
            state.FileChangeDetailText = string.Empty;
            state.IsFileChangeActive = false;
            state.McpToolProgressItems.Clear();
            state.DiffText = string.Empty;
            state.ChangedFiles.Clear();
            ReplaceSkillNames(state.SkillNames, skillNames);
            if (storePendingSkillNames)
            {
                ReplaceSkillNames(state.PendingSkillNames, skillNames);
            }
            else
            {
                state.PendingSkillNames.Clear();
            }

            state.StatusText = UiLocalization.Text("MainForm.TurnSummary.Status.Preparing", "Preparing turn summary...");
            state.IsFinalized = false;
        }

        internal static void FinalizeInterrupted(TurnProgressSummaryState state, string message)
        {
            if (state == null || state.IsFinalized)
            {
                return;
            }

            state.IsReasoningActive = false;
            state.IsFileChangeActive = false;
            state.PendingSkillNames.Clear();
            state.StatusText = string.IsNullOrWhiteSpace(message)
                ? UiLocalization.Text("MainForm.TurnSummary.Status.Interrupted", "Turn interrupted")
                : string.Format(
                    UiLocalization.Text("MainForm.TurnSummary.Status.InterruptedWithReason.Template", "Turn interrupted: {0}"),
                    message);
            state.IsFinalized = true;
        }

        internal static bool ApplyEvent(TurnProgressSummaryState state, AppServerEventArgs e)
        {
            if (state == null || e == null)
            {
                return false;
            }

            var eventType = (e.EventType ?? string.Empty).Trim();
            if (eventType.Length == 0)
            {
                return false;
            }

            switch (eventType.ToLowerInvariant())
            {
                case "turn_started":
                    var pendingSkillNames = state.PendingSkillNames.Count > 0
                        ? state.PendingSkillNames.ToArray()
                        : null;
                    ResetForPreparingCore(state, pendingSkillNames, false);
                    state.TurnId = e.TurnId ?? string.Empty;
                    state.StatusText = UiLocalization.Text("MainForm.TurnSummary.Status.Started", "Starting a new conversation turn");
                    return true;
                case "turn_plan_updated":
                    SetTurnIdIfPresent(state, e);
                    state.Explanation = e.Explanation ?? string.Empty;
                    state.PlanSteps.Clear();
                    CloneTurnPlanSteps(state.PlanSteps, e.PlanSteps);
                    if (state.PlanSteps.Count > 0)
                    {
                        state.PlanDeltaText = string.Empty;
                    }

                    state.IsFinalized = false;
                    return true;
                case "plan_delta":
                    SetTurnIdIfPresent(state, e);
                    state.PlanDeltaText = !string.IsNullOrWhiteSpace(e.PlanDeltaText)
                        ? e.PlanDeltaText
                        : e.Content ?? string.Empty;
                    state.IsFinalized = false;
                    return true;
                case "turn_diff_updated":
                    SetTurnIdIfPresent(state, e);
                    state.DiffText = !string.IsNullOrWhiteSpace(e.DiffText)
                        ? e.DiffText
                        : e.Content ?? string.Empty;
                    state.ChangedFiles.Clear();
                    AddChangedFiles(state.ChangedFiles, e.ChangedFiles);
                    state.IsFinalized = false;
                    return true;
                case "reasoning_started":
                    SetTurnIdIfPresent(state, e);
                    state.ReasoningSummaryText = string.Empty;
                    state.ReasoningText = string.Empty;
                    state.ActiveReasoningItemId = e.ItemId ?? string.Empty;
                    state.IsReasoningActive = true;
                    state.IsFinalized = false;
                    return true;
                case "reasoning_summary_delta":
                    SetTurnIdIfPresent(state, e);
                    state.ReasoningSummaryText = e.Content ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(e.ItemId))
                    {
                        state.ActiveReasoningItemId = e.ItemId;
                    }

                    state.IsReasoningActive = true;
                    state.IsFinalized = false;
                    return true;
                case "reasoning_text_delta":
                    SetTurnIdIfPresent(state, e);
                    state.ReasoningText = e.Content ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(e.ItemId))
                    {
                        state.ActiveReasoningItemId = e.ItemId;
                    }

                    state.IsReasoningActive = true;
                    state.IsFinalized = false;
                    return true;
                case "reasoning_completed":
                    SetTurnIdIfPresent(state, e);
                    if (!string.IsNullOrWhiteSpace(e.Content))
                    {
                        state.ReasoningSummaryText = e.Content;
                    }

                    if (!string.IsNullOrWhiteSpace(e.Details))
                    {
                        state.ReasoningText = e.Details;
                    }

                    AddCompletedReasoningSummary(state, e.ItemId, state.ReasoningSummaryText);
                    state.ActiveReasoningItemId = string.Empty;
                    state.ReasoningSummaryText = string.Empty;
                    state.ReasoningText = string.Empty;
                    state.IsReasoningActive = false;
                    state.IsFinalized = false;
                    return true;
                case "file_change_progress":
                    SetTurnIdIfPresent(state, e);
                    if (!string.IsNullOrWhiteSpace(e.Content))
                    {
                        state.FileChangeDetailText = e.Content;
                    }

                    state.IsFileChangeActive = true;
                    state.IsFinalized = false;
                    return true;
                case "file_change_patch_updated":
                    SetTurnIdIfPresent(state, e);
                    if (!string.IsNullOrWhiteSpace(e.Details))
                    {
                        state.FileChangeDetailText = e.Details;
                    }
                    else if (!string.IsNullOrWhiteSpace(e.Content))
                    {
                        state.FileChangeDetailText = e.Content;
                    }

                    if (e.ChangedFiles != null)
                    {
                        state.ChangedFiles.Clear();
                        AddChangedFiles(state.ChangedFiles, e.ChangedFiles);
                    }

                    if (!string.IsNullOrWhiteSpace(e.DiffText))
                    {
                        state.DiffText = e.DiffText;
                    }

                    state.IsFileChangeActive = true;
                    state.IsFinalized = false;
                    return true;
                case "file_change_completed":
                    SetTurnIdIfPresent(state, e);
                    if (!string.IsNullOrWhiteSpace(e.Details))
                    {
                        state.FileChangeDetailText = e.Details;
                    }
                    else if (!string.IsNullOrWhiteSpace(e.Content))
                    {
                        state.FileChangeDetailText = e.Content;
                    }

                    if (e.ChangedFiles != null)
                    {
                        state.ChangedFiles.Clear();
                        AddChangedFiles(state.ChangedFiles, e.ChangedFiles);
                    }

                    state.IsFileChangeActive = false;
                    state.IsFinalized = false;
                    return true;
                case "mcp_tool_progress":
                    SetTurnIdIfPresent(state, e);
                    UpdateMcpToolProgressState(state, e, false);
                    state.IsFinalized = false;
                    return true;
                case "mcp_tool_completed":
                    SetTurnIdIfPresent(state, e);
                    UpdateMcpToolProgressState(state, e, true);
                    state.IsFinalized = false;
                    return true;
                case "token_usage":
                    return TryApplyTokenUsage(state, e);
                case "turn_completed":
                    SetTurnIdIfPresent(state, e);
                    state.IsReasoningActive = false;
                    state.IsFileChangeActive = false;
                    state.PendingSkillNames.Clear();
                    state.StatusText = UiLocalization.Text("MainForm.TurnSummary.Status.Completed", "Current turn completed");
                    state.IsFinalized = true;
                    return true;
                case "turn_interrupted":
                    SetTurnIdIfPresent(state, e);
                    state.IsReasoningActive = false;
                    state.IsFileChangeActive = false;
                    state.PendingSkillNames.Clear();
                    state.StatusText = UiLocalization.Text("MainForm.TurnSummary.Status.Interrupted", "Turn interrupted");
                    state.IsFinalized = true;
                    return true;
                case "turn_failed":
                    SetTurnIdIfPresent(state, e);
                    state.IsReasoningActive = false;
                    state.IsFileChangeActive = false;
                    state.PendingSkillNames.Clear();
                    state.StatusText = string.IsNullOrWhiteSpace(e.Content)
                        ? UiLocalization.Text("MainForm.TurnSummary.Status.Failed", "Turn failed")
                        : string.Format(UiLocalization.Text("MainForm.TurnSummary.Status.FailedWithReason.Template", "Turn failed: {0}"), e.Content);
                    state.IsFinalized = true;
                    return true;
                default:
                    return false;
            }
        }

        internal static string BuildSummaryText(TurnProgressSummaryState state)
        {
            if (state == null)
            {
                return UiLocalization.Text("MainForm.TurnSummary.Empty", "No summary currently.");
            }

            var builder = new StringBuilder();
            builder.Append(UiLocalization.Text("MainForm.TurnSummary.CurrentStepPrefix", "Current step: "));
            builder.Append(GetCurrentTurnSummaryStepText(state));

            if (state.SkillNames.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.Section.Skills", "Skills:"));

                for (var i = 0; i < state.SkillNames.Count; i++)
                {
                    var skillName = state.SkillNames[i];
                    if (string.IsNullOrWhiteSpace(skillName))
                    {
                        continue;
                    }

                    builder.Append("- ");
                    builder.AppendLine(skillName.Trim());
                }
            }

            var reasoningBlock = BuildReasoningSummaryBlock(state);
            if (!string.IsNullOrWhiteSpace(reasoningBlock))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.Section.Reasoning", "Reasoning:"));
                builder.Append(reasoningBlock);
            }

            if (!string.IsNullOrWhiteSpace(state.Explanation))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.Section.Explanation", "Explanation:"));
                builder.Append(state.Explanation.Trim());
            }

            if (state.PlanSteps.Count > 0 || !string.IsNullOrWhiteSpace(state.PlanDeltaText))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.Section.Plan", "Plan:"));

                if (state.PlanSteps.Count > 0)
                {
                    for (var i = 0; i < state.PlanSteps.Count; i++)
                    {
                        var step = state.PlanSteps[i];
                        if (step == null || string.IsNullOrWhiteSpace(step.Step))
                        {
                            continue;
                        }

                        builder.Append(MapPlanStatusText(step.Status));
                        builder.Append(UiLocalization.Text("MainForm.TurnSummary.Plan.Separator", ":"));
                        builder.AppendLine(step.Step.Trim());
                    }
                }
                else
                {
                    builder.Append(TruncateSummaryText(
                        state.PlanDeltaText,
                        StreamingPlanSummaryMaxLines,
                        StreamingPlanSummaryMaxCharacters));
                }
            }

            if (state.McpToolProgressItems.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.Section.Mcp", "MCP:"));

                for (var i = 0; i < state.McpToolProgressItems.Count; i++)
                {
                    var entry = state.McpToolProgressItems[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    builder.AppendLine(BuildMcpToolProgressLine(entry));
                }
            }

            var fileChangeDetailBlock = BuildFileChangeDetailBlock(state);
            if (!string.IsNullOrWhiteSpace(fileChangeDetailBlock))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.Section.ChangeDetails", "Change details:"));
                builder.Append(fileChangeDetailBlock);
            }

            if (state.ChangedFiles.Count > 0 || !string.IsNullOrWhiteSpace(state.DiffText))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.Section.Changes", "Changes:"));

                if (state.ChangedFiles.Count > 0)
                {
                    builder.Append(UiLocalization.Text("MainForm.TurnSummary.ChangedFiles.Prefix", "Modified "));
                    builder.Append(state.ChangedFiles.Count);
                    builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.ChangedFiles.Suffix", " file(s)"));

                    var displayCount = Math.Min(3, state.ChangedFiles.Count);
                    for (var i = 0; i < displayCount; i++)
                    {
                        builder.Append("- ");
                        builder.AppendLine(state.ChangedFiles[i]);
                    }

                    if (state.ChangedFiles.Count > displayCount)
                    {
                        builder.Append(UiLocalization.Text("MainForm.TurnSummary.ChangedFiles.MorePrefix", "- Plus "));
                        builder.Append(state.ChangedFiles.Count - displayCount);
                        builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.ChangedFiles.MoreSuffix", " more file(s)"));
                    }
                }
                else
                {
                    builder.Append(UiLocalization.Text("MainForm.TurnSummary.ChangesReceived", "A summary of this turn's changes has been received."));
                }
            }

            var result = builder.ToString().Trim();
            return result.Length == 0 ? UiLocalization.Text("MainForm.TurnSummary.Empty", "No summary currently.") : result;
        }

        internal static string BuildCurrentStepText(TurnProgressSummaryState state)
        {
            return GetCurrentTurnSummaryStepText(state);
        }

        private static void SetTurnIdIfPresent(TurnProgressSummaryState state, AppServerEventArgs e)
        {
            if (state != null && e != null && !string.IsNullOrWhiteSpace(e.TurnId))
            {
                state.TurnId = e.TurnId;
            }
        }

        private static void AddChangedFiles(List<string> target, IReadOnlyList<string> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var changedFile = source[i];
                if (!string.IsNullOrWhiteSpace(changedFile))
                {
                    target.Add(changedFile);
                }
            }
        }

        private static void ReplaceSkillNames(List<string> target, IReadOnlyList<string> source)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            if (source == null)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < source.Count; i++)
            {
                var skillName = source[i];
                if (string.IsNullOrWhiteSpace(skillName))
                {
                    continue;
                }

                var trimmedSkillName = skillName.Trim();
                if (trimmedSkillName.Length == 0 || !seen.Add(trimmedSkillName))
                {
                    continue;
                }

                target.Add(trimmedSkillName);
            }
        }

        private static void UpdateMcpToolProgressState(TurnProgressSummaryState state, AppServerEventArgs e, bool isFinalized)
        {
            if (state == null || e == null || string.IsNullOrWhiteSpace(e.ItemId))
            {
                return;
            }

            McpToolProgressEntry existingEntry = null;
            for (var i = 0; i < state.McpToolProgressItems.Count; i++)
            {
                var candidate = state.McpToolProgressItems[i];
                if (candidate != null && string.Equals(candidate.ItemId, e.ItemId, StringComparison.Ordinal))
                {
                    existingEntry = candidate;
                    state.McpToolProgressItems.RemoveAt(i);
                    break;
                }
            }

            var entry = existingEntry ?? new McpToolProgressEntry { ItemId = e.ItemId };
            if (!string.IsNullOrWhiteSpace(e.McpServerName))
            {
                entry.ServerName = e.McpServerName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(e.McpToolName))
            {
                entry.ToolName = e.McpToolName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(e.Content))
            {
                entry.Message = e.Content.Trim();
            }

            if (!string.IsNullOrWhiteSpace(e.ItemStatus))
            {
                entry.Status = e.ItemStatus.Trim();
            }

            entry.IsFinalized = isFinalized;
            entry.UpdatedAt = DateTime.Now;
            state.McpToolProgressItems.Add(entry);

            while (state.McpToolProgressItems.Count > 3)
            {
                state.McpToolProgressItems.RemoveAt(0);
            }
        }

        private static string GetCurrentTurnSummaryStepText(TurnProgressSummaryState state)
        {
            if (state == null)
            {
                return UiLocalization.Text("MainForm.TurnSummary.Empty", "No summary currently.");
            }

            if (state.IsFinalized && !string.IsNullOrWhiteSpace(state.StatusText))
            {
                return state.StatusText + BuildTokenUsageSuffix(state);
            }

            if (state.IsFileChangeActive &&
                (!string.IsNullOrWhiteSpace(state.FileChangeDetailText) || state.ChangedFiles.Count > 0))
            {
                return UiLocalization.Text("MainForm.TurnSummary.Step.CollectingFileChanges", "Collecting file changes");
            }

            for (var i = state.McpToolProgressItems.Count - 1; i >= 0; i--)
            {
                var mcpEntry = state.McpToolProgressItems[i];
                if (mcpEntry == null)
                {
                    continue;
                }

                if (!mcpEntry.IsFinalized)
                {
                    return UiLocalization.Text("MainForm.TurnSummary.Step.CallingMcpToolPrefix", "Calling ") + BuildMcpToolDisplayName(mcpEntry);
                }
            }

            if (state.IsReasoningActive &&
                (!string.IsNullOrWhiteSpace(state.ReasoningSummaryText) || !string.IsNullOrWhiteSpace(state.ReasoningText)))
            {
                return UiLocalization.Text("MainForm.TurnSummary.Step.Analyzing", "Analyzing");
            }

            for (var i = 0; i < state.PlanSteps.Count; i++)
            {
                var step = state.PlanSteps[i];
                var status = step != null ? (step.Status ?? string.Empty).Trim() : string.Empty;
                if (step != null &&
                    (string.Equals(status, "in_progress", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(status, "inprogress", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(status, "in progress", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(status, "running", StringComparison.OrdinalIgnoreCase)))
                {
                    return step.Step;
                }
            }

            if (!string.IsNullOrWhiteSpace(state.PlanDeltaText))
            {
                return TruncateSummaryText(
                    state.PlanDeltaText,
                    StreamingPlanStepMaxLines,
                    StreamingPlanStepMaxCharacters);
            }

            if (!string.IsNullOrWhiteSpace(state.StatusText))
            {
                return state.StatusText;
            }

            if (state.PlanSteps.Count > 0)
            {
                var lastStep = state.PlanSteps[state.PlanSteps.Count - 1];
                if (lastStep != null && !string.IsNullOrWhiteSpace(lastStep.Step))
                {
                    return lastStep.Step.Trim();
                }
            }

            if (state.ChangedFiles.Count > 0)
            {
                return UiLocalization.Text("MainForm.TurnSummary.Step.CollectingTurnChanges", "Collecting this turn's changes");
            }

            return UiLocalization.Text("MainForm.TurnSummary.Empty", "No summary currently.");
        }

        private static bool TryApplyTokenUsage(TurnProgressSummaryState state, AppServerEventArgs e)
        {
            if (state == null || e == null || e.ThreadTokenUsage == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(e.TurnId))
            {
                if (!string.IsNullOrWhiteSpace(state.TurnId) &&
                    !string.Equals(state.TurnId, e.TurnId, StringComparison.Ordinal))
                {
                    return false;
                }

                state.TurnId = e.TurnId;
            }

            var usage = e.ThreadTokenUsage;
            var changed = false;
            if (usage.Last != null && usage.Last.TotalTokens > 0)
            {
                changed = state.LastTurnTotalTokens != usage.Last.TotalTokens || changed;
                state.LastTurnTotalTokens = usage.Last.TotalTokens;
            }

            if (usage.Total != null && usage.Total.TotalTokens > 0)
            {
                changed = state.TotalTokens != usage.Total.TotalTokens || changed;
                state.TotalTokens = usage.Total.TotalTokens;
            }

            return changed;
        }

        private static string BuildTokenUsageSuffix(TurnProgressSummaryState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            var hasLast = state.LastTurnTotalTokens.HasValue && state.LastTurnTotalTokens.Value > 0;
            var hasTotal = state.TotalTokens.HasValue && state.TotalTokens.Value > 0;
            if (!hasLast && !hasTotal)
            {
                return string.Empty;
            }

            if (hasLast && hasTotal)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("MainForm.TurnSummary.TokenUsage.LastAndTotal.Template", " | This turn {0} | Total {1}"),
                    TokenCountFormatter.Format(state.LastTurnTotalTokens.Value),
                    TokenCountFormatter.Format(state.TotalTokens.Value));
            }

            if (hasLast)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("MainForm.TurnSummary.TokenUsage.Last.Template", " | This turn {0}"),
                    TokenCountFormatter.Format(state.LastTurnTotalTokens.Value));
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("MainForm.TurnSummary.TokenUsage.Total.Template", " | Total {0}"),
                TokenCountFormatter.Format(state.TotalTokens.Value));
        }

        private static string BuildReasoningSummaryBlock(TurnProgressSummaryState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var summaryText = TruncateSummaryText(NormalizeReasoningSummaryForDisplay(state.ReasoningSummaryText), 4, 320);
            var reasoningPreview = TruncateSummaryText(state.ReasoningText, 3, 220);
            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.Reasoning.CurrentPrefix", "Current: ") + summaryText);
            }
            else if (!string.IsNullOrWhiteSpace(reasoningPreview))
            {
                builder.AppendLine(UiLocalization.Text("MainForm.TurnSummary.Reasoning.CurrentPrefix", "Current: ") + reasoningPreview);
            }

            var summaries = state.CompletedReasoningSummaries;
            if (summaries.Count > 0)
            {
                for (var i = 0; i < summaries.Count; i++)
                {
                    var text = CollapseSummaryTextToSingleLine(NormalizeReasoningSummaryForDisplay(summaries[i].Text));
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    builder.Append(UiLocalization.Text("MainForm.TurnSummary.Reasoning.CompletedHeader", "Completed: "));
                    builder.AppendLine(text);
                }
            }

            return builder.ToString().Trim();
        }

        private static string CollapseSummaryTextToSingleLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var builder = new StringBuilder();
            for (var i = 0; i < rawLines.Length; i++)
            {
                var line = rawLines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(line);
            }

            return builder.ToString();
        }

        private static void AddCompletedReasoningSummary(TurnProgressSummaryState state, string itemId, string text)
        {
            if (state == null)
            {
                return;
            }

            var normalized = NormalizeReasoningSummaryForDisplay(text);
            if (IsPlaceholderReasoningSummary(normalized))
            {
                return;
            }

            for (var i = state.CompletedReasoningSummaries.Count - 1; i >= 0; i--)
            {
                var entry = state.CompletedReasoningSummaries[i];
                if (entry != null &&
                    !string.IsNullOrWhiteSpace(itemId) &&
                    string.Equals(entry.ItemId, itemId, StringComparison.Ordinal))
                {
                    state.CompletedReasoningSummaries.RemoveAt(i);
                    break;
                }
            }

            state.CompletedReasoningSummaries.Add(new ReasoningSummaryEntry
            {
                ItemId = itemId ?? string.Empty,
                Text = normalized
            });

            while (state.CompletedReasoningSummaries.Count > MaxCompletedReasoningSummaries)
            {
                state.CompletedReasoningSummaries.RemoveAt(0);
            }
        }

        private static bool IsPlaceholderReasoningSummary(string text)
        {
            var normalized = (text ?? string.Empty).Trim();
            return normalized.Length == 0 ||
                   string.Equals(normalized, "Analysis completed", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "\u5206\u6790\u5b8c\u6210", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeReasoningSummaryForDisplay(string text)
        {
            var normalized = (text ?? string.Empty).Trim();
            if (normalized.Length == 0)
            {
                return string.Empty;
            }

            normalized = normalized.Replace("\r\n", "\n").Replace('\r', '\n');
            var lines = normalized.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                lines[i] = NormalizeReasoningSummaryLine(lines[i]);
            }

            return string.Join(Environment.NewLine, lines).Trim();
        }

        private static string NormalizeReasoningSummaryLine(string line)
        {
            var text = (line ?? string.Empty).Trim();
            if (text.StartsWith("**", StringComparison.Ordinal))
            {
                var end = text.IndexOf("**", 2, StringComparison.Ordinal);
                if (end > 2)
                {
                    var title = text.Substring(2, end - 2).Trim();
                    var rest = text.Substring(end + 2).Trim();
                    if (rest.Length > 0)
                    {
                        return title + ": " + rest;
                    }

                    return title;
                }
            }

            return text.Replace("**", string.Empty).Trim();
        }

        private static string BuildFileChangeDetailBlock(TurnProgressSummaryState state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            return TruncateSummaryText(state.FileChangeDetailText, 4, 260);
        }

        private static string TruncateSummaryText(string text, int maxLines, int maxCharacters)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var lines = new List<string>();
            var hadMoreLines = false;
            for (var i = 0; i < rawLines.Length; i++)
            {
                var line = rawLines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (lines.Count < Math.Max(1, maxLines))
                {
                    lines.Add(line);
                }
                else
                {
                    hadMoreLines = true;
                    break;
                }
            }

            if (lines.Count == 0)
            {
                return string.Empty;
            }

            var result = string.Join(Environment.NewLine, lines.ToArray());
            var truncated = false;
            if (result.Length > maxCharacters)
            {
                result = result.Substring(0, maxCharacters).TrimEnd();
                truncated = true;
            }

            if (truncated || hadMoreLines)
            {
                result += "...";
            }

            return result;
        }

        private static string BuildMcpToolProgressLine(McpToolProgressEntry entry)
        {
            var target = BuildMcpToolDisplayName(entry);
            var message = (entry.Message ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                return entry.IsFinalized
                    ? target + UiLocalization.Text("MainForm.TurnSummary.Mcp.StatusCompletedSuffix", ": Completed")
                    : target;
            }

            if (string.Equals(message, UiLocalization.Text("MainForm.TurnSummary.Mcp.Status.Completed", "Completed"), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message, UiLocalization.Text("MainForm.TurnSummary.Mcp.Status.Done", "Done"), StringComparison.OrdinalIgnoreCase))
            {
                return target + UiLocalization.Text("MainForm.TurnSummary.Mcp.StatusCompletedSuffix", ": Completed");
            }

            if (string.Equals(message, UiLocalization.Text("MainForm.TurnSummary.Mcp.Status.Running", "Running"), StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }

            return target + UiLocalization.Text("MainForm.TurnSummary.Mcp.MessageSeparator", ": ") + message;
        }

        private static string BuildMcpToolDisplayName(McpToolProgressEntry entry)
        {
            if (entry == null)
            {
                return UiLocalization.Text("MainForm.TurnSummary.Mcp.DefaultToolName", "MCP tool");
            }

            if (!string.IsNullOrWhiteSpace(entry.ServerName) && !string.IsNullOrWhiteSpace(entry.ToolName))
            {
                return entry.ServerName + " / " + entry.ToolName;
            }

            if (!string.IsNullOrWhiteSpace(entry.ToolName))
            {
                return entry.ToolName;
            }

            if (!string.IsNullOrWhiteSpace(entry.ServerName))
            {
                return entry.ServerName;
            }

            return UiLocalization.Text("MainForm.TurnSummary.Mcp.DefaultToolName", "MCP tool");
        }

        private static string MapPlanStatusText(string status)
        {
            switch ((status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "completed":
                    return UiLocalization.Text("MainForm.TurnSummary.Plan.Status.Completed", "Completed");
                case "in_progress":
                case "inprogress":
                case "in progress":
                case "running":
                    return UiLocalization.Text("MainForm.TurnSummary.Plan.Status.InProgress", "In Progress");
                case "pending":
                    return UiLocalization.Text("MainForm.TurnSummary.Plan.Status.Pending", "Pending");
                default:
                    return UiLocalization.Text("MainForm.TurnSummary.Plan.Status.Step", "Step");
            }
        }

        private static void CloneTurnPlanSteps(List<AppServerTurnPlanStep> target, IReadOnlyList<AppServerTurnPlanStep> source)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            if (source == null)
            {
                return;
            }

            for (var i = 0; i < source.Count; i++)
            {
                var step = source[i];
                if (step == null || string.IsNullOrWhiteSpace(step.Step))
                {
                    continue;
                }

                target.Add(new AppServerTurnPlanStep
                {
                    Step = step.Step,
                    Status = step.Status
                });
            }
        }
    }
}
