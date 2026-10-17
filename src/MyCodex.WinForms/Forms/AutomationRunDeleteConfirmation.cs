namespace MyCodex.WinForms.Forms
{
    internal sealed class AutomationRunDeleteConfirmation
    {
        public AutomationRunDeleteConfirmation(bool shouldDelete, bool deleteLocalHistory)
        {
            ShouldDelete = shouldDelete;
            DeleteLocalHistory = deleteLocalHistory;
        }

        public bool ShouldDelete { get; }

        public bool DeleteLocalHistory { get; }
    }
}
