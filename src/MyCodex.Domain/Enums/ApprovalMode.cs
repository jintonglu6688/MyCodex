namespace MyCodex.Domain.Enums
{
    public enum ApprovalMode
    {
        ReadOnly = 0,
        Auto = 1,
        FullAccess = 2,
        Custom = 3,
        // Compatibility with the unreleased auto-approval preview. New settings use FullAccess (2).
        FullAccessAutoApprove = 4
    }
}
