namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionPlanModeAvailabilityContext
    {
        public bool HasSession { get; set; }

        public bool IsWorkspaceAvailable { get; set; }

        public bool HasThread { get; set; }

        public bool IsThreadBusy { get; set; }

        public bool CanInterruptThread { get; set; }


        public bool IsThreadCompacting { get; set; }
    }

    internal static class SessionPlanModePlanner
    {
        internal static bool CanChangePlanOrGoalState(SessionPlanModeAvailabilityContext context)
        {
            if (context == null ||
                !context.HasSession ||
                !context.IsWorkspaceAvailable)
            {
                return false;
            }

            if (!context.HasThread)
            {
                return true;
            }

            return !context.IsThreadBusy &&
                   !context.CanInterruptThread &&
                   !context.IsThreadCompacting;
        }
    }
}
