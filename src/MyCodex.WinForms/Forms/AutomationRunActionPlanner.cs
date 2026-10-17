using System;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class AutomationRunActionPlanner
    {
        internal static bool CanStop(AutomationRunSnapshot run, Func<string, bool> canInterruptThread)
        {
            if (run == null ||
                string.IsNullOrWhiteSpace(run.ThreadId) ||
                !IsStoppableStatus(run.Status))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(run.TurnId) ||
                   (canInterruptThread != null && canInterruptThread(run.ThreadId));
        }

        private static bool IsStoppableStatus(AutomationRunStatus status)
        {
            return status == AutomationRunStatus.Running ||
                   status == AutomationRunStatus.WaitingForApproval ||
                   status == AutomationRunStatus.WaitingForInput;
        }
    }
}
