namespace MyCodex.Application.DTOs
{
    public sealed class AppServerThreadGoal
    {
        public string ThreadId { get; set; }

        public string Objective { get; set; }

        public string Status { get; set; }

        public long? TokenBudget { get; set; }

        public long TokensUsed { get; set; }

        public long TimeUsedSeconds { get; set; }

        public long CreatedAt { get; set; }

        public long UpdatedAt { get; set; }
    }
}
