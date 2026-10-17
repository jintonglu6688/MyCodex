namespace MyCodex.Domain.Entities
{
    using System;
    using MyCodex.Domain.Enums;

    public class CommandTask
    {
        public string Id { get; set; }

        public string ProcessId { get; set; }

        public string SessionId { get; set; }

        public string CommandText { get; set; }

        public string WorkingDirectory { get; set; }

        public string Status { get; set; }

        public int? ExitCode { get; set; }

        public string StdOut { get; set; }

        public string StdErr { get; set; }

        public bool HasStreamedStdOut { get; set; }

        public bool HasStreamedStdErr { get; set; }

        public bool IsWaitingForInput { get; set; }

        public string LastInteractionText { get; set; }

        public DateTime? LastInteractionAt { get; set; }

        public DateTime? LastInputSentAt { get; set; }

        public DateTime StartedAt { get; set; }

        public TimeSpan? Duration { get; set; }

        public ApprovalMode ApprovalMode { get; set; }

        public bool TerminationRequested { get; set; }
    }
}
