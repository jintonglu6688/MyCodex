namespace MyCodex.Application.CodingRemote
{
    public static class CodingRemoteKinds
    {
        public const string Coding = "coding";
        public const string Office = "office";
    }

    public static class CodingRemoteSessionStates
    {
        public const string Idle = "idle";
        public const string Running = "running";
        public const string WaitingApproval = "waitingApproval";
        public const string Failed = "failed";
    }

    public static class CodingRemoteMessageRoles
    {
        public const string User = "user";
        public const string Assistant = "assistant";
        public const string System = "system";
        public const string Tool = "tool";
        public const string Approval = "approval";
    }

    public static class CodingRemoteMessageStatuses
    {
        public const string Complete = "complete";
        public const string Streaming = "streaming";
        public const string Failed = "failed";
    }
}
