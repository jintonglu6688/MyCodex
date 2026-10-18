using System;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public sealed class SessionActivationPlan
    {
        public SessionActivationPlan(ChatSession session, bool includeSystemHeader)
        {
            Session = session;
            IncludeSystemHeader = includeSystemHeader;
        }

        public ChatSession Session { get; }

        public bool IncludeSystemHeader { get; }

        public bool ShouldActivate
        {
            get { return Session != null; }
        }

        public void ApplyActivityTimestamp(DateTime now)
        {
            if (Session != null)
            {
                Session.LastActiveAt = now;
            }
        }
    }

    public static class SessionActivationWorkflowPlanner
    {
        public static SessionActivationPlan BuildActivation(ChatSession session, bool includeSystemHeader)
        {
            return new SessionActivationPlan(session, includeSystemHeader);
        }

        public static bool ShouldSkipExplicitActivation(ChatSession currentSession, ChatSession requestedSession)
        {
            return currentSession != null &&
                   requestedSession != null &&
                   string.Equals(currentSession.Id, requestedSession.Id, StringComparison.OrdinalIgnoreCase);
        }
    }
}
