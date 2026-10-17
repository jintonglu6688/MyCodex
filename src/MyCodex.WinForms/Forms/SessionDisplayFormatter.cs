using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionDisplayFormatter
    {
        internal static string GetCreatedAgoText(DateTime createdAt)
        {
            return GetCreatedAgoText(createdAt, DateTime.Now);
        }

        internal static string GetCreatedAgoText(DateTime createdAt, DateTime now)
        {
            if (createdAt == default(DateTime))
            {
                return string.Empty;
            }

            var elapsed = now - createdAt;
            if (elapsed.TotalHours < 1)
            {
                return string.Empty;
            }

            if (elapsed.TotalDays >= 1)
            {
                var days = Math.Max(1, (int)Math.Floor(elapsed.TotalDays));
                return string.Format(UiLocalization.Text("MainForm.TimeAgo.Days.Template", "{0} day(s) ago"), days);
            }

            var hours = Math.Max(1, (int)Math.Floor(elapsed.TotalHours));
            return string.Format(UiLocalization.Text("MainForm.TimeAgo.Hours.Template", "{0} hour(s) ago"), hours);
        }

        internal static string BuildBranchLabel(ChatSession session)
        {
            if (session == null)
            {
                return UiLocalization.Text("MainForm.Branch.Mainline", "Mainline");
            }

            if (session.IsReview)
            {
                return UiLocalization.Text("MainForm.Branch.Review", "Review");
            }

            if (!session.IsFork)
            {
                return UiLocalization.Text("MainForm.Branch.Mainline", "Mainline");
            }

            return UiLocalization.Text("MainForm.Branch.Fork", "Fork");
        }

        internal static string BuildDisplayTitle(ChatSession session)
        {
            if (session == null)
            {
                return UiLocalization.Text("MainForm.Value.UnnamedSession", "Unnamed session");
            }

            var title = BuildNormalizedTitle(session);
            var branchLabel = BuildBranchLabel(session);
            var branchPrefix = branchLabel + UiLocalization.Text("MainForm.Branch.Separator", " | ");
            var displayTitle = branchPrefix + title;
            if (session.IsPlanModeEnabled)
            {
                displayTitle += UiLocalization.Text("MainForm.PlanMode.TitleSuffix", " [Plan]");
            }

            if (HasGoal(session))
            {
                displayTitle += UiLocalization.Text("MainForm.Goal.TitleSuffix", " [Goal]");
            }

            return displayTitle;
        }

        internal static string PrependWorkingStatus(string text, int workingSessionCount)
        {
            if (workingSessionCount <= 0)
            {
                return text ?? string.Empty;
            }

            var status = workingSessionCount == 1
                ? UiLocalization.Text("MainForm.WindowStatus.Working", "Working")
                : string.Format(
                    UiLocalization.Text("MainForm.WorkspaceList.WorkingSessionCount.Template", "{0} sessions working"),
                    workingSessionCount);
            return string.Format(
                UiLocalization.Text("MainForm.ListStatus.Prefix.Template", "{0}: {1}"),
                status,
                text ?? string.Empty);
        }

        internal static int? CalculateContextRemainingPercentage(ChatSession session)
        {
            if (session == null ||
                !session.ThreadTokenUsageModelContextWindow.HasValue ||
                session.ThreadTokenUsageModelContextWindow.Value <= 0 ||
                !session.ThreadTokenUsageLastTotalTokens.HasValue ||
                session.ThreadTokenUsageLastTotalTokens.Value < 0)
            {
                return null;
            }

            var window = session.ThreadTokenUsageModelContextWindow.Value;
            var used = Math.Min(window, session.ThreadTokenUsageLastTotalTokens.Value);
            return (int)Math.Round(
                (window - used) * 100m / window,
                MidpointRounding.AwayFromZero);
        }

        internal static string BuildWorkingStatusText(ChatSession session)
        {
            var remainingPercentage = CalculateContextRemainingPercentage(session);
            return remainingPercentage.HasValue
                ? string.Format(
                    UiLocalization.Text(
                        "MainForm.WindowStatus.WorkingWithContextRemaining.Template",
                        "Working ({0}% context remaining)"),
                    remainingPercentage.Value)
                : UiLocalization.Text("MainForm.WindowStatus.Working", "Working");
        }

        internal static bool HasGoal(ChatSession session)
        {
            return session != null && !string.IsNullOrWhiteSpace(session.ThreadGoalObjective);
        }

        internal static string BuildGoalSummaryText(ChatSession session)
        {
            if (!HasGoal(session))
            {
                return string.Empty;
            }

            var parts = new List<string>
            {
                string.Format(
                    UiLocalization.Text("MainForm.Goal.Summary.Objective.Template", "Goal: {0}"),
                    session.ThreadGoalObjective.Trim())
            };

            if (!string.IsNullOrWhiteSpace(session.ThreadGoalStatus))
            {
                parts.Add(string.Format(
                    UiLocalization.Text("MainForm.Goal.Summary.Status.Template", "status: {0}"),
                    MapGoalStatusText(session.ThreadGoalStatus)));
            }

            if (session.ThreadGoalTokensUsed.HasValue && session.ThreadGoalTokensUsed.Value > 0)
            {
                parts.Add(string.Format(
                    UiLocalization.Text("MainForm.Goal.Summary.TokensUsed.Template", "tokens used: {0}"),
                    session.ThreadGoalTokensUsed.Value));
            }

            if (session.ThreadGoalTokenBudget.HasValue && session.ThreadGoalTokenBudget.Value > 0)
            {
                parts.Add(string.Format(
                    UiLocalization.Text("MainForm.Goal.Summary.TokenBudget.Template", "token budget: {0}"),
                    session.ThreadGoalTokenBudget.Value));
            }

            return string.Join(UiLocalization.Text("MainForm.Goal.Summary.Separator", " | "), parts.ToArray());
        }

        internal static string MapGoalStatusText(string status)
        {
            switch ((status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "active":
                    return UiLocalization.Text("MainForm.Goal.Status.Active", "Active");
                case "paused":
                    return UiLocalization.Text("MainForm.Goal.Status.Paused", "Paused");
                case "budgetlimited":
                case "budget_limited":
                    return UiLocalization.Text("MainForm.Goal.Status.BudgetLimited", "Budget limited");
                case "complete":
                case "completed":
                    return UiLocalization.Text("MainForm.Goal.Status.Complete", "Complete");
                default:
                    return string.IsNullOrWhiteSpace(status)
                        ? UiLocalization.Text("MainForm.Goal.Status.Unknown", "Unknown")
                        : status.Trim();
            }
        }

        internal static string BuildNormalizedTitle(ChatSession session)
        {
            var title = session == null || string.IsNullOrWhiteSpace(session.Title)
                ? UiLocalization.Text("MainForm.Value.UnnamedSession", "Unnamed session")
                : session.Title;
            return SessionTitleStrategy.ExtractBaseTitle(title);
        }

        internal static string BuildForkSourceText(ChatSession session)
        {
            if (session == null || !session.IsFork)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(session.ForkSourceTitle))
            {
                return session.ForkSourceTitle;
            }

            return string.IsNullOrWhiteSpace(session.ForkSourceThreadId) ? string.Empty : session.ForkSourceThreadId;
        }

        internal static string BuildReviewSourceText(ChatSession session)
        {
            if (session == null || !session.IsReview)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(session.ReviewSourceTitle))
            {
                return session.ReviewSourceTitle;
            }

            return string.IsNullOrWhiteSpace(session.ReviewSourceThreadId) ? string.Empty : session.ReviewSourceThreadId;
        }

        internal static string BuildReviewSessionTitle(ChatSession session)
        {
            var sourceTitle = session == null || string.IsNullOrWhiteSpace(session.Title)
                ? UiLocalization.Text("MainForm.Value.UnnamedSession", "Unnamed session")
                : SessionTitleStrategy.ExtractBaseTitle(session.Title);

            return sourceTitle.IndexOf(UiLocalization.Text("MainForm.Branch.Review", "Review"), StringComparison.OrdinalIgnoreCase) >= 0
                ? sourceTitle
                : sourceTitle + UiLocalization.Text("MainForm.Branch.ReviewSuffix", " (Review)");
        }
    }
}
