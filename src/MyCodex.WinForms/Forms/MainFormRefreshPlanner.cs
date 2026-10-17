namespace MyCodex.WinForms.Forms
{
    internal enum MainFormRefreshPreset
    {
        WorkspaceSessionHeaderInputPersist,
        WorkspaceSessionHeaderInput,
        SessionHeaderInputPersist,
        SessionHeaderPersist
    }

    internal sealed class MainFormRefreshPlan
    {
        public MainFormRefreshPlan(
            bool refreshWorkspaceView,
            bool refreshSessionList,
            bool refreshHeader,
            bool refreshInputState,
            bool persistState)
        {
            RefreshWorkspaceView = refreshWorkspaceView;
            RefreshSessionList = refreshSessionList;
            RefreshHeader = refreshHeader;
            RefreshInputState = refreshInputState;
            PersistState = persistState;
        }

        public bool RefreshWorkspaceView { get; }

        public bool RefreshSessionList { get; }

        public bool RefreshHeader { get; }

        public bool RefreshInputState { get; }

        public bool PersistState { get; }
    }

    internal static class MainFormRefreshPlanner
    {
        internal static MainFormRefreshPlan Build(MainFormRefreshPreset preset)
        {
            switch (preset)
            {
                case MainFormRefreshPreset.WorkspaceSessionHeaderInputPersist:
                    return new MainFormRefreshPlan(true, true, true, true, true);
                case MainFormRefreshPreset.WorkspaceSessionHeaderInput:
                    return new MainFormRefreshPlan(true, true, true, true, false);
                case MainFormRefreshPreset.SessionHeaderInputPersist:
                    return new MainFormRefreshPlan(false, true, true, true, true);
                default:
                    return new MainFormRefreshPlan(false, true, true, false, true);
            }
        }
    }
}
