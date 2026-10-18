namespace MyCodex.WinForms.Forms
{
    internal sealed class MainFormInputStatePlan
    {
        public bool CanCompose { get; set; }

        public bool CanInteract { get; set; }

        public bool CanStop { get; set; }

        public bool ChatHistoryEnabled { get; set; }

        public bool InputEnabled { get; set; }

        public bool AttachmentButtonEnabled { get; set; }

        public bool SendButtonEnabled { get; set; }

        public bool PendingAttachmentListEnabled { get; set; }
    }

    internal static class MainFormInputStatePlanner
    {
        internal static MainFormInputStatePlan Build(
            bool workspaceAvailable,
            bool hasCurrentSession,
            bool isThreadCompacting,
            bool canInterruptThread,
            int historyItemCount,
            int pendingAttachmentCount)
        {
            var canCompose = workspaceAvailable && !isThreadCompacting;
            var canInteract = hasCurrentSession && workspaceAvailable;
            var canStop = canInteract && canInterruptThread;
            return new MainFormInputStatePlan
            {
                CanCompose = canCompose,
                CanInteract = canInteract,
                CanStop = canStop,
                ChatHistoryEnabled = canCompose || historyItemCount > 0,
                InputEnabled = canCompose,
                AttachmentButtonEnabled = canCompose,
                SendButtonEnabled = canCompose,
                PendingAttachmentListEnabled = canCompose && pendingAttachmentCount > 0
            };
        }
    }
}
