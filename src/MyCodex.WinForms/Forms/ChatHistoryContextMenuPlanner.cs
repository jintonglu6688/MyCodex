namespace MyCodex.WinForms.Forms
{
    internal sealed class ChatHistoryBasicContextMenuState
    {
        public bool CopySelectedEnabled { get; set; }

        public bool FindEnabled { get; set; }
    }

    internal sealed class ChatHistoryPlanModeMenuState
    {
        public bool Enabled { get; set; }

        public bool Checked { get; set; }
    }

    internal sealed class ChatHistoryGoalMenuState
    {
        public bool SetEnabled { get; set; }

        public bool ViewEnabled { get; set; }

        public bool ClearEnabled { get; set; }

        public bool MenuEnabled { get; set; }
    }

    internal static class ChatHistoryContextMenuPlanner
    {
        internal static ChatHistoryBasicContextMenuState BuildBasicState(bool hasSelectedChatItem, int visibleItemCount)
        {
            var hasVisibleItems = visibleItemCount > 0;
            return new ChatHistoryBasicContextMenuState
            {
                CopySelectedEnabled = hasSelectedChatItem,
                FindEnabled = hasVisibleItems
            };
        }

        internal static ChatHistoryPlanModeMenuState BuildPlanModeState(bool canTogglePlanMode, bool isPlanModeEnabled)
        {
            return new ChatHistoryPlanModeMenuState
            {
                Enabled = canTogglePlanMode,
                Checked = isPlanModeEnabled
            };
        }

        internal static ChatHistoryGoalMenuState BuildGoalState(bool canManageGoal, bool hasThread)
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
