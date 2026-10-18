using System;

namespace MyCodex.Application.Services
{
    public static class ThreadGoalRuntimeStatePlanner
    {
        public static string ResolveEffectiveStatus(string persistedStatus, bool isThreadRunning)
        {
            if (!isThreadRunning || string.IsNullOrWhiteSpace(persistedStatus))
            {
                return persistedStatus;
            }

            return string.Equals(persistedStatus, "paused", StringComparison.OrdinalIgnoreCase)
                ? "active"
                : persistedStatus;
        }
    }
}
