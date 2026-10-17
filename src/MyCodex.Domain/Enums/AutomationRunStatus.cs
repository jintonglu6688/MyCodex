namespace MyCodex.Domain.Enums
{
    public enum AutomationRunStatus
    {
        Queued = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Canceled = 4,
        WaitingForApproval = 5,
        WaitingForInput = 6,
        Skipped = 7
    }
}
