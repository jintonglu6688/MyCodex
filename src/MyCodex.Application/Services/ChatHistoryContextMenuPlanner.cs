namespace MyCodex.Application.Services
{
    public sealed class ChatHistoryBasicContextMenuState
    {
        public bool CopySelectedEnabled { get; set; }

        public bool FindEnabled { get; set; }
    }

    public sealed class ChatHistoryPlanModeMenuState
    {
        public bool Enabled { get; set; }

        public bool Checked { get; set; }
    }

    public sealed class ChatHistoryGoalMenuState
    {
        public bool SetEnabled { get; set; }

        public bool ViewEnabled { get; set; }

        public bool ClearEnabled { get; set; }

        public bool MenuEnabled { get; set; }
    }

    public static class ChatHistoryContextMenuPlanner
    {
        public static ChatHistoryBasicContextMenuState BuildBasicState(bool hasSelectedChatItem, int visibleItemCount)
        {
            var hasVisibleItems = visibleItemCount > 0;
            return new ChatHistoryBasicContextMenuState
            {
                CopySelectedEnabled = hasSelectedChatItem,
                FindEnabled = hasVisibleItems
            };
        }

        public static ChatHistoryPlanModeMenuState BuildPlanModeState(bool canTogglePlanMode, bool isPlanModeEnabled)
        {
            return new ChatHistoryPlanModeMenuState
            {
                Enabled = canTogglePlanMode,
                Checked = isPlanModeEnabled
            };
        }

        public static ChatHistoryGoalMenuState BuildGoalState(bool canManageGoal, bool hasThread)
        {
            var state = new ChatHistoryGoalMenuState
            {
                SetEnabled = canManageGoal,
                ViewEnabled = hasThread,
                ClearEnabled = canManageGoal && hasThread
            };
            state.MenuEnabled = state.SetEnabled || state.ViewEnabled || state.ClearEnabled;
            return state;
        }
    }
}
