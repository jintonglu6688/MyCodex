using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public sealed class PersistedSessionService
    {
        private readonly RuntimeProfileService _runtimeProfileService;

        public PersistedSessionService()
            : this(new RuntimeProfileService())
        {
        }

        public PersistedSessionService(RuntimeProfileService runtimeProfileService)
        {
            _runtimeProfileService = runtimeProfileService ?? throw new ArgumentNullException(nameof(runtimeProfileService));
        }

        public List<ChatSession> BuildPersistedSessions(
            IReadOnlyCollection<ChatSession> activeSessions,
            IReadOnlyCollection<ChatSession> inactiveRuntimeSessions,
            string currentRuntimeProfileId,
            string preferredSessionId)
        {
            currentRuntimeProfileId = _runtimeProfileService.NormalizeRuntimeProfileId(currentRuntimeProfileId);

            var result = new List<ChatSession>();
            var sessionIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            AppendSessions(result, sessionIndexes, activeSessions, currentRuntimeProfileId, preferredSessionId, true);
            AppendSessions(result, sessionIndexes, inactiveRuntimeSessions, currentRuntimeProfileId, preferredSessionId, false);

            return result;
        }

        public void AddOrReplacePersistedSession(
            IList<ChatSession> sessions,
            IDictionary<string, int> sessionIndexes,
            ChatSession session,
            string preferredSessionId)
        {
            if (sessions == null || sessionIndexes == null || session == null || string.IsNullOrWhiteSpace(session.Id))
            {
                return;
            }

            var sessionKey = BuildPersistedSessionKey(session);
            if (string.IsNullOrWhiteSpace(sessionKey))
            {
                sessions.Add(session);
                return;
            }

            int existingIndex;
            if (!sessionIndexes.TryGetValue(sessionKey, out existingIndex))
            {
                sessionIndexes[sessionKey] = sessions.Count;
                sessions.Add(session);
                return;
            }

            if (ShouldReplacePersistedSession(sessions[existingIndex], session, preferredSessionId))
            {
                sessions[existingIndex] = session;
            }
        }

        public string BuildPersistedSessionKey(ChatSession session)
        {
            if (session == null)
            {
                return null;
            }

            var runtimeProfileId = _runtimeProfileService.NormalizeRuntimeProfileId(session.RuntimeProfileId);
            var officeRuntimeScopeId = session.Mode == AppInteractionMode.Office
                ? NormalizeOfficeRuntimeScopeId(session.OfficeRuntimeScopeId)
                : null;
            var workspaceId = string.IsNullOrWhiteSpace(session.WorkspaceId)
                ? "-"
                : session.WorkspaceId.Trim();

            if (!string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return BuildPersistedSessionKey(
                    "thread",
                    runtimeProfileId,
                    officeRuntimeScopeId,
                    workspaceId,
                    session.ThreadId.Trim());
            }

            if (!string.IsNullOrWhiteSpace(session.SessionPath))
            {
                return BuildPersistedSessionKey(
                    "path",
                    runtimeProfileId,
                    officeRuntimeScopeId,
                    workspaceId,
                    session.SessionPath.Trim());
            }

            if (!string.IsNullOrWhiteSpace(session.Id))
            {
                return BuildPersistedSessionKey(
                    "id",
                    runtimeProfileId,
                    officeRuntimeScopeId,
                    workspaceId,
                    session.Id.Trim());
            }

            return null;
        }

        private static string BuildPersistedSessionKey(
            string kind,
            string runtimeProfileId,
            string officeRuntimeScopeId,
            string workspaceId,
            string identity)
        {
            return officeRuntimeScopeId == null
                ? string.Join("|", kind, runtimeProfileId, workspaceId, identity)
                : string.Join("|", kind, officeRuntimeScopeId, workspaceId, identity);
        }

        private static string NormalizeOfficeRuntimeScopeId(string runtimeScopeId)
        {
            return string.IsNullOrWhiteSpace(runtimeScopeId)
                ? "-"
                : runtimeScopeId.Trim().ToLowerInvariant();
        }

        public bool ShouldReplacePersistedSession(ChatSession existing, ChatSession candidate, string preferredSessionId)
        {
            if (candidate == null)
            {
                return false;
            }

            if (existing == null)
            {
                return true;
            }

            var normalizedPreferredSessionId = string.IsNullOrWhiteSpace(preferredSessionId)
                ? null
                : preferredSessionId.Trim();
            var existingIsPreferred = !string.IsNullOrWhiteSpace(normalizedPreferredSessionId) &&
                                      string.Equals(existing.Id, normalizedPreferredSessionId, StringComparison.OrdinalIgnoreCase);
            var candidateIsPreferred = !string.IsNullOrWhiteSpace(normalizedPreferredSessionId) &&
                                       string.Equals(candidate.Id, normalizedPreferredSessionId, StringComparison.OrdinalIgnoreCase);
            if (existingIsPreferred != candidateIsPreferred)
            {
                return candidateIsPreferred;
            }

            if (candidate.LastActiveAt != existing.LastActiveAt)
            {
                return candidate.LastActiveAt > existing.LastActiveAt;
            }

            if (candidate.CreatedAt != existing.CreatedAt)
            {
                return candidate.CreatedAt > existing.CreatedAt;
            }

            return GetPersistedSessionCompletenessScore(candidate) > GetPersistedSessionCompletenessScore(existing);
        }

        public void NormalizeRestoredSessionTimestamps(ChatSession session)
        {
            if (session == null)
            {
                return;
            }

            session.CreatedAt = NormalizeLocalDateTime(session.CreatedAt);
            session.LastActiveAt = NormalizeLocalDateTime(session.LastActiveAt);
        }

        private void AppendSessions(
            IList<ChatSession> result,
            IDictionary<string, int> sessionIndexes,
            IReadOnlyCollection<ChatSession> sourceSessions,
            string currentRuntimeProfileId,
            string preferredSessionId,
            bool assignCurrentRuntimeProfile)
        {
            if (result == null || sessionIndexes == null || sourceSessions == null)
            {
                return;
            }

            foreach (var session in sourceSessions)
            {
                if (session == null || string.IsNullOrWhiteSpace(session.Id))
                {
                    continue;
                }

                session.RuntimeProfileId = assignCurrentRuntimeProfile
                    ? currentRuntimeProfileId
                    : _runtimeProfileService.NormalizeRuntimeProfileId(session.RuntimeProfileId);
                AddOrReplacePersistedSession(result, sessionIndexes, session, preferredSessionId);
            }
        }

        private static int GetPersistedSessionCompletenessScore(ChatSession session)
        {
            if (session == null)
            {
                return 0;
            }

            var score = 0;
            if (!string.IsNullOrWhiteSpace(session.ThreadId))
            {
                score++;
            }

            if (!string.IsNullOrWhiteSpace(session.Title))
            {
                score++;
            }

            if (!string.IsNullOrWhiteSpace(session.Preview))
            {
                score++;
            }

            if (!string.IsNullOrWhiteSpace(session.SessionPath))
            {
                score++;
            }

            if (!string.IsNullOrWhiteSpace(session.WorkspaceId))
            {
                score++;
            }

            if (!string.IsNullOrWhiteSpace(session.ModelId))
            {
                score++;
            }

            return score;
        }

        private static DateTime NormalizeLocalDateTime(DateTime value)
        {
            if (value == default(DateTime))
            {
                return value;
            }

            return value.Kind == DateTimeKind.Utc
                ? value.ToLocalTime()
                : value;
        }
    }
}
