using System;

namespace MyCodex.Shared.Constants
{
    public static class SessionListDisplayOrder
    {
        public const string AgentsTimeTitle = "agents-time-title";
        public const string TitleTimeAgents = "title-time-agents";
        public const string TitleAgentsTime = "title-agents-time";

        public static string Normalize(string order)
        {
            var value = order == null ? string.Empty : order.Trim();
            if (string.Equals(value, TitleTimeAgents, StringComparison.OrdinalIgnoreCase))
            {
                return TitleTimeAgents;
            }

            if (string.Equals(value, TitleAgentsTime, StringComparison.OrdinalIgnoreCase))
            {
                return TitleAgentsTime;
            }

            return AgentsTimeTitle;
        }
    }
}
