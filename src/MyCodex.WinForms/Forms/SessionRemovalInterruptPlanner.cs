using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionRemovalInterruptTarget
    {
        public SessionRemovalInterruptTarget(string threadId, string turnId)
        {
            ThreadId = threadId;
            TurnId = turnId;
        }

        public string ThreadId { get; }

        public string TurnId { get; }
    }

    internal static class SessionRemovalInterruptPlanner
    {
        internal static List<SessionRemovalInterruptTarget> BuildTargets(
            IEnumerable<ChatSession> sessions,
            Func<string, bool> canInterruptThread,
            Func<string, string> getInterruptibleTurnId)
        {
            var targets = new List<SessionRemovalInterruptTarget>();
            if (sessions == null || canInterruptThread == null)
            {
                return targets;
            }

            var seenThreadIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var session in sessions)
            {
                var threadId = session != null ? session.ThreadId : null;
                if (string.IsNullOrWhiteSpace(threadId) || seenThreadIds.Contains(threadId))
                {
                    continue;
                }

                seenThreadIds.Add(threadId);
                if (!canInterruptThread(threadId))
                {
                    continue;
                }

                var turnId = getInterruptibleTurnId != null ? getInterruptibleTurnId(threadId) : null;
                targets.Add(new SessionRemovalInterruptTarget(threadId, turnId));
            }

            return targets;
        }
    }
}
