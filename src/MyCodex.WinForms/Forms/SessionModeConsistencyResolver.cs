using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class SessionModeConsistencyResolver
    {
        internal static ChatSession Resolve(ChatSession currentSession, AppInteractionMode currentMode)
        {
            return currentSession != null && currentSession.Mode == currentMode
                ? currentSession
                : null;
        }
    }
}
