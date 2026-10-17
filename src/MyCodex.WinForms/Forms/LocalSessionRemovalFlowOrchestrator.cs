namespace MyCodex.WinForms.Forms
{
    internal enum LocalSessionRemovalOperationKind
    {
        None,
        ArchiveSessions,
        DeleteSessionFiles,
        ArchiveSession,
        DeleteSessionFile
    }

    internal sealed class LocalSessionRemovalFlowPlan
    {
        public LocalSessionRemovalFlowPlan(LocalSessionRemovalOperationKind operationKind, int defaultProcessedCount)
        {
            OperationKind = operationKind;
            DefaultProcessedCount = defaultProcessedCount;
        }

        public LocalSessionRemovalOperationKind OperationKind { get; }

        public int DefaultProcessedCount { get; }

        public bool RequiresRemoteOperation
        {
            get { return OperationKind != LocalSessionRemovalOperationKind.None; }
        }
    }

    internal static class LocalSessionRemovalFlowOrchestrator
    {
        internal static LocalSessionRemovalFlowPlan BuildWorkspacePlan(LocalSessionRemovalMode removalMode, int sessionCount)
        {
            switch (removalMode)
            {
                case LocalSessionRemovalMode.Archive:
                    return new LocalSessionRemovalFlowPlan(LocalSessionRemovalOperationKind.ArchiveSessions, 0);
                case LocalSessionRemovalMode.PermanentDelete:
                    return new LocalSessionRemovalFlowPlan(LocalSessionRemovalOperationKind.DeleteSessionFiles, 0);
                default:
                    return new LocalSessionRemovalFlowPlan(LocalSessionRemovalOperationKind.None, sessionCount);
            }
        }

        internal static LocalSessionRemovalFlowPlan BuildSessionPlan(LocalSessionRemovalMode removalMode)
        {
            switch (removalMode)
            {
                case LocalSessionRemovalMode.Archive:
                    return new LocalSessionRemovalFlowPlan(LocalSessionRemovalOperationKind.ArchiveSession, 1);
                case LocalSessionRemovalMode.PermanentDelete:
                    return new LocalSessionRemovalFlowPlan(LocalSessionRemovalOperationKind.DeleteSessionFile, 1);
                default:
                    return new LocalSessionRemovalFlowPlan(LocalSessionRemovalOperationKind.None, 1);
            }
        }

        internal static int ResolveWorkspaceProcessedCount(LocalSessionRemovalFlowPlan plan, int processedCountFromOperation)
        {
            return plan != null && plan.RequiresRemoteOperation
                ? processedCountFromOperation
                : (plan != null ? plan.DefaultProcessedCount : 0);
        }
    }
}
