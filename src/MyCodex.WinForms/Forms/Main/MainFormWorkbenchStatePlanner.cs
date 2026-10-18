using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class MainFormWorkbenchStatePlanner
    {
        internal static AutomationRunStatus ResolveAutomationTerminalStatus(AppServerEventRoute route)
        {
            if (route == AppServerEventRoute.TurnCompleted)
            {
                return AutomationRunStatus.Completed;
            }

            if (route == AppServerEventRoute.TurnInterrupted)
            {
                return AutomationRunStatus.Canceled;
            }

            return AutomationRunStatus.Failed;
        }

        internal static bool ShouldEnsureTurnSummaryState(string threadId)
        {
            return !string.IsNullOrWhiteSpace(threadId);
        }

        internal static bool ShouldRefreshAutomationForm(bool automationUpdated, bool automationsFormAvailable)
        {
            return automationUpdated && automationsFormAvailable;
        }
    }
}
