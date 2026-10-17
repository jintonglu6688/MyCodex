using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionRuntimeIdentityResolver
    {
        internal static string ResolveRuntimeProfileId(ChatSession session, string fallbackRuntimeProfileId)
        {
            return session != null && !string.IsNullOrWhiteSpace(session.RuntimeProfileId)
                ? session.RuntimeProfileId
                : fallbackRuntimeProfileId;
        }

        internal static string ResolveDraftScopeId(
            ChatSession session,
            string fallbackRuntimeProfileId,
            string fallbackOfficeRuntimeScopeId)
        {
            if (session != null && session.Mode == AppInteractionMode.Office)
            {
                return !string.IsNullOrWhiteSpace(session.OfficeRuntimeScopeId)
                    ? session.OfficeRuntimeScopeId
                    : fallbackOfficeRuntimeScopeId;
            }

            return ResolveRuntimeProfileId(session, fallbackRuntimeProfileId);
        }
    }
}
