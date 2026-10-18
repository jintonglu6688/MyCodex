using System;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class AutomationDisplayText
    {
        public static string Kind(AutomationKind kind)
        {
            switch (kind)
            {
                case AutomationKind.Thread:
                    return UiLocalization.Text("Automation.Kind.Thread", "Thread");
                case AutomationKind.Standalone:
                    return UiLocalization.Text("Automation.Kind.Standalone", "Standalone");
                case AutomationKind.Project:
                    return UiLocalization.Text("Automation.Kind.Project", "Project");
                case AutomationKind.Worktree:
                    return UiLocalization.Text("Automation.Kind.Worktree", "Worktree");
                case AutomationKind.Office:
                    return UiLocalization.Text("Automation.Kind.Office", "Office");
                default:
                    return kind.ToString();
            }
        }

        public static string ScheduleKind(AutomationScheduleKind kind)
        {
            switch (kind)
            {
                case AutomationScheduleKind.ManualOnly:
                    return UiLocalization.Text("Automation.Schedule.ManualOnly", "Manual only");
                case AutomationScheduleKind.EveryMinutes:
                    return UiLocalization.Text("Automation.Schedule.EveryMinutes", "Every few minutes");
                case AutomationScheduleKind.EveryHours:
                    return UiLocalization.Text("Automation.Schedule.EveryHours", "Every few hours");
                case AutomationScheduleKind.Daily:
                    return UiLocalization.Text("Automation.Schedule.Daily", "Daily");
                case AutomationScheduleKind.Weekly:
                    return UiLocalization.Text("Automation.Schedule.Weekly", "Weekly");
                default:
                    return kind.ToString();
            }
        }

        public static string InputItemKind(AutomationInputItemKind kind)
        {
            switch (kind)
            {
                case AutomationInputItemKind.Skill:
                    return UiLocalization.Text("Automation.InputItem.Skill", "Skill");
                case AutomationInputItemKind.Plugin:
                    return UiLocalization.Text("Automation.InputItem.Plugin", "Plugin");
                default:
                    return kind.ToString();
            }
        }

        public static string DayOfWeek(DayOfWeek dayOfWeek)
        {
            switch (dayOfWeek)
            {
                case System.DayOfWeek.Sunday:
                    return UiLocalization.Text("Automation.DayOfWeek.Sunday", "Sunday");
                case System.DayOfWeek.Monday:
                    return UiLocalization.Text("Automation.DayOfWeek.Monday", "Monday");
                case System.DayOfWeek.Tuesday:
                    return UiLocalization.Text("Automation.DayOfWeek.Tuesday", "Tuesday");
                case System.DayOfWeek.Wednesday:
                    return UiLocalization.Text("Automation.DayOfWeek.Wednesday", "Wednesday");
                case System.DayOfWeek.Thursday:
                    return UiLocalization.Text("Automation.DayOfWeek.Thursday", "Thursday");
                case System.DayOfWeek.Friday:
                    return UiLocalization.Text("Automation.DayOfWeek.Friday", "Friday");
                case System.DayOfWeek.Saturday:
                    return UiLocalization.Text("Automation.DayOfWeek.Saturday", "Saturday");
                default:
                    return dayOfWeek.ToString();
            }
        }

        public static string RunStatus(AutomationRunStatus status)
        {
            switch (status)
            {
                case AutomationRunStatus.Queued:
                    return UiLocalization.Text("Automation.RunStatus.Queued", "Queued");
                case AutomationRunStatus.Running:
                    return UiLocalization.Text("Automation.RunStatus.Running", "Running");
                case AutomationRunStatus.Completed:
                    return UiLocalization.Text("Automation.RunStatus.Completed", "Completed");
                case AutomationRunStatus.Failed:
                    return UiLocalization.Text("Automation.RunStatus.Failed", "Failed");
                case AutomationRunStatus.Canceled:
                    return UiLocalization.Text("Automation.RunStatus.Canceled", "Canceled");
                case AutomationRunStatus.WaitingForApproval:
                    return UiLocalization.Text("Automation.RunStatus.WaitingForApproval", "Waiting for approval");
                case AutomationRunStatus.WaitingForInput:
                    return UiLocalization.Text("Automation.RunStatus.WaitingForInput", "Waiting for input");
                case AutomationRunStatus.Skipped:
                    return UiLocalization.Text("Automation.RunStatus.Skipped", "Skipped");
                default:
                    return status.ToString();
            }
        }
    }
}
