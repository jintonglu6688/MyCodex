using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionStatisticsDetailBuilder
    {
        internal static List<string> BuildDetailLines(ChatSession session)
        {
            return BuildDetailLines(session, null);
        }

        internal static List<string> BuildDetailLines(ChatSession session, SessionStatisticsDisplayContext context)
        {
            var lines = new List<string>();
            if (session == null)
            {
                lines.Add(UiLocalization.Text("SessionStatisticsForm.Line.SessionUnavailable", "Session: not available."));
                return lines;
            }

            AddLine(lines, "SessionStatisticsForm.Line.Title.Template", "Title: {0}", BuildStatisticsTitle(session));
            AddLine(lines, "SessionStatisticsForm.Line.Thread.Template", "Thread: {0}", EmptyAsUnknown(session.ThreadId));
            AddLine(lines, "SessionStatisticsForm.Line.Model.Template", "Model: {0}", EmptyAsUnknown(ResolveDisplayValue(session.ModelId, context != null ? context.EffectiveModelId : null)));
            AddLine(lines, "SessionStatisticsForm.Line.ReasoningEffort.Template", "Reasoning effort: {0}", EmptyAsUnknown(ResolveDisplayValue(session.ReasoningEffort, context != null ? context.EffectiveReasoningEffort : null)));
            AddLine(lines, "SessionStatisticsForm.Line.CreatedAt.Template", "Created: {0}", FormatDateTime(session.CreatedAt));
            AddLine(lines, "SessionStatisticsForm.Line.LastActiveAt.Template", "Last active: {0}", FormatDateTime(session.LastActiveAt));
            lines.Add(string.Empty);

            if (HasTokenUsage(session))
            {
                AddTokenUsageLines(lines, session);
            }
            else
            {
                lines.Add(UiLocalization.Text(
                    "SessionStatisticsForm.Line.TokenUsageUnavailable",
                    "Token usage: not available for this conversation yet."));
            }

            lines.Add(string.Empty);
            AddGoalLines(lines, session);
            return lines;
        }

        private static void AddTokenUsageLines(List<string> lines, ChatSession session)
        {
            if (session.ThreadTokenUsageModelContextWindow.HasValue)
            {
                AddLine(
                    lines,
                    "SessionStatisticsForm.Line.ContextWindow.Template",
                    "Context window: {0}",
                    FormatTokenCount(session.ThreadTokenUsageModelContextWindow.Value));
            }

            if (session.ThreadTokenUsageTotalTokens.HasValue)
            {
                AddLine(lines, "SessionStatisticsForm.Line.TotalTokens.Template", "Total tokens: {0}", FormatTokenCount(session.ThreadTokenUsageTotalTokens.Value));
            }

            AddOptionalNumber(lines, "SessionStatisticsForm.Line.InputTokens.Template", "Input tokens: {0}", session.ThreadTokenUsageInputTokens);
            AddOptionalNumber(lines, "SessionStatisticsForm.Line.CachedInputTokens.Template", "Cached input tokens: {0}", session.ThreadTokenUsageCachedInputTokens);
            AddOptionalNumber(lines, "SessionStatisticsForm.Line.OutputTokens.Template", "Output tokens: {0}", session.ThreadTokenUsageOutputTokens);
            AddOptionalNumber(lines, "SessionStatisticsForm.Line.ReasoningOutputTokens.Template", "Reasoning output tokens: {0}", session.ThreadTokenUsageReasoningOutputTokens);

            if (HasLastTurnTokenUsage(session))
            {
                lines.Add(string.Empty);
                AddOptionalNumber(lines, "SessionStatisticsForm.Line.LastTurnTotalTokens.Template", "Last turn total tokens: {0}", session.ThreadTokenUsageLastTotalTokens);
                AddOptionalNumber(lines, "SessionStatisticsForm.Line.LastTurnInputTokens.Template", "Last turn input tokens: {0}", session.ThreadTokenUsageLastInputTokens);
                AddOptionalNumber(lines, "SessionStatisticsForm.Line.LastTurnCachedInputTokens.Template", "Last turn cached input tokens: {0}", session.ThreadTokenUsageLastCachedInputTokens);
                AddOptionalNumber(lines, "SessionStatisticsForm.Line.LastTurnOutputTokens.Template", "Last turn output tokens: {0}", session.ThreadTokenUsageLastOutputTokens);
                AddOptionalNumber(lines, "SessionStatisticsForm.Line.LastTurnReasoningOutputTokens.Template", "Last turn reasoning output tokens: {0}", session.ThreadTokenUsageLastReasoningOutputTokens);
            }
        }

        private static void AddGoalLines(List<string> lines, ChatSession session)
        {
            if (!SessionDisplayFormatter.HasGoal(session))
            {
                lines.Add(UiLocalization.Text("SessionStatisticsForm.Line.GoalNotSet", "Goal: not set."));
                return;
            }

            AddLine(lines, "SessionStatisticsForm.Line.Goal.Template", "Goal: {0}", session.ThreadGoalObjective.Trim());
            if (!string.IsNullOrWhiteSpace(session.ThreadGoalStatus))
            {
                AddLine(
                    lines,
                    "SessionStatisticsForm.Line.GoalStatus.Template",
                    "Goal status: {0}",
                    SessionDisplayFormatter.MapGoalStatusText(session.ThreadGoalStatus));
            }

            AddOptionalNumber(lines, "SessionStatisticsForm.Line.GoalTokensUsed.Template", "Goal tokens used: {0}", session.ThreadGoalTokensUsed);
            AddOptionalNumber(lines, "SessionStatisticsForm.Line.GoalTokenBudget.Template", "Goal token budget: {0}", session.ThreadGoalTokenBudget);
            if (session.ThreadGoalTimeUsedSeconds.HasValue)
            {
                AddLine(
                    lines,
                    "SessionStatisticsForm.Line.GoalTimeUsed.Template",
                    "Goal time used: {0}",
                    FormatDuration(session.ThreadGoalTimeUsedSeconds.Value));
            }
        }

        private static bool HasTokenUsage(ChatSession session)
        {
            return session.ThreadTokenUsageModelContextWindow.HasValue ||
                   session.ThreadTokenUsageTotalTokens.HasValue ||
                   session.ThreadTokenUsageInputTokens.HasValue ||
                   session.ThreadTokenUsageCachedInputTokens.HasValue ||
                   session.ThreadTokenUsageOutputTokens.HasValue ||
                   session.ThreadTokenUsageReasoningOutputTokens.HasValue ||
                   HasLastTurnTokenUsage(session);
        }

        private static bool HasLastTurnTokenUsage(ChatSession session)
        {
            return session.ThreadTokenUsageLastTotalTokens.HasValue ||
                   session.ThreadTokenUsageLastInputTokens.HasValue ||
                   session.ThreadTokenUsageLastCachedInputTokens.HasValue ||
                   session.ThreadTokenUsageLastOutputTokens.HasValue ||
                   session.ThreadTokenUsageLastReasoningOutputTokens.HasValue;
        }

        private static string ResolveDisplayValue(string sessionValue, string effectiveValue)
        {
            return string.IsNullOrWhiteSpace(sessionValue) ? effectiveValue : sessionValue;
        }

        private static string BuildStatisticsTitle(ChatSession session)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                UiLocalization.Text("SessionStatisticsForm.Value.BranchTitle.Template", "{0} | {1}"),
                SessionDisplayFormatter.BuildBranchLabel(session),
                SessionDisplayFormatter.BuildNormalizedTitle(session));
        }

        private static void AddOptionalNumber(List<string> lines, string key, string fallback, long? value)
        {
            if (!value.HasValue)
            {
                return;
            }

            AddLine(lines, key, fallback, FormatTokenCount(value.Value));
        }

        private static void AddLine(List<string> lines, string key, string fallback, object value)
        {
            lines.Add(string.Format(CultureInfo.CurrentCulture, UiLocalization.Text(key, fallback), value));
        }

        private static string FormatTokenCount(long value)
        {
            return TokenCountFormatter.Format(value);
        }

        private static string FormatDateTime(DateTime value)
        {
            return value == default(DateTime)
                ? UiLocalization.Text("SessionStatisticsForm.Value.Unknown", "Unknown")
                : value.ToString("g", CultureInfo.CurrentCulture);
        }

        private static string EmptyAsUnknown(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? UiLocalization.Text("SessionStatisticsForm.Value.Unknown", "Unknown")
                : value.Trim();
        }

        private static string FormatDuration(long seconds)
        {
            if (seconds <= 0)
            {
                return UiLocalization.Text("SessionStatisticsForm.Duration.None", "0s");
            }

            var parts = new List<string>();
            var hours = seconds / 3600;
            var minutes = seconds % 3600 / 60;
            var remainingSeconds = seconds % 60;
            if (hours > 0)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, UiLocalization.Text("SessionStatisticsForm.Duration.Hours.Template", "{0}h"), hours));
            }

            if (minutes > 0)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, UiLocalization.Text("SessionStatisticsForm.Duration.Minutes.Template", "{0}m"), minutes));
            }

            if (remainingSeconds > 0 || parts.Count == 0)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, UiLocalization.Text("SessionStatisticsForm.Duration.Seconds.Template", "{0}s"), remainingSeconds));
            }

            return string.Join(" ", parts.ToArray());
        }
    }
}
