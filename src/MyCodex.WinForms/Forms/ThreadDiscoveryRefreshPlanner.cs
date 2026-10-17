using System;

namespace MyCodex.WinForms.Forms
{
    internal static class ThreadDiscoveryRefreshPlanner
    {
        internal static bool ShouldRequestThreadListRefresh(
            AppServerEventRoute route,
            string threadId,
            Func<string, bool> isKnownThread)
        {
            return AppServerEventRouting.ShouldRequestThreadListRefresh(route) ||
                   IsUnknownThreadEvent(threadId, isKnownThread);
        }

        internal static bool ShouldScheduleFollowUp(
            AppServerEventRoute route,
            string threadId,
            Func<string, bool> isKnownThread)
        {
            return AppServerEventRouting.ShouldScheduleThreadListRefreshFollowUp(route) ||
                   IsUnknownThreadEvent(threadId, isKnownThread);
        }

        private static bool IsUnknownThreadEvent(string threadId, Func<string, bool> isKnownThread)
        {
            return !string.IsNullOrWhiteSpace(threadId) &&
                   (isKnownThread == null || !isKnownThread(threadId));
        }
    }
}
