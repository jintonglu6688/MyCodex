using System;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionWorkbenchContext
    {
        internal SessionWorkbenchContext(
            ChatSession session,
            WorkspaceInfo workspace,
            AppServerFileWindowRuntimeScope runtimeScope,
            string draftScopeKey)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            RuntimeScope = runtimeScope ?? new AppServerFileWindowRuntimeScope();
            DraftScopeKey = draftScopeKey ?? string.Empty;
        }

        internal ChatSession Session { get; private set; }

        internal WorkspaceInfo Workspace { get; }

        internal AppServerFileWindowRuntimeScope RuntimeScope { get; }

        internal string DraftScopeKey { get; }

        internal void RefreshSession(ChatSession session)
        {
            if (session != null && string.Equals(session.Id, Session.Id, StringComparison.OrdinalIgnoreCase))
            {
                Session = session;
            }
        }
    }
}
