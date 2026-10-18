namespace MyCodex.WinForms.Forms
{
    internal static class TurnInputStateRefreshPlanner
    {
        internal static bool ShouldRefresh(AppServerEventRoute route)
        {
            return route == AppServerEventRoute.TurnStarted ||
                   route == AppServerEventRoute.Error ||
                   AppServerEventRouting.IsTurnTerminal(route);
        }
    }
}
