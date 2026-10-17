using System;

namespace MyCodex.Shared.Constants
{
    public static class ActiveTurnFollowUpMode
    {
        public const string Steer = "steer";
        public const string Queue = "queue";

        public static string Normalize(string mode)
        {
            var value = string.IsNullOrWhiteSpace(mode) ? Steer : mode.Trim();
            return string.Equals(value, Queue, StringComparison.OrdinalIgnoreCase)
                ? Queue
                : Steer;
        }
    }
}
