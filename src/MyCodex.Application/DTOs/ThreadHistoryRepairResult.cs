namespace MyCodex.Application.DTOs
{
    public sealed class ThreadHistoryRepairResult
    {
        public bool Repaired { get; set; }

        public int RepairCount { get; set; }

        public string Message { get; set; }

        // Nothing is left to repair, so restarting the App Server and repairing again cannot help.
        public bool IsFinal { get; set; }

        public static ThreadHistoryRepairResult CreateRepaired(int repairCount)
        {
            return new ThreadHistoryRepairResult
            {
                Repaired = repairCount > 0,
                RepairCount = repairCount
            };
        }

        public static ThreadHistoryRepairResult NotRepaired(string message)
        {
            return new ThreadHistoryRepairResult
            {
                Repaired = false,
                RepairCount = 0,
                Message = message
            };
        }

        public static ThreadHistoryRepairResult Final(string message)
        {
            var result = NotRepaired(message);
            result.IsFinal = true;
            return result;
        }
    }
}
