using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class AppServerFileWindowRuntimeScopeComposer
    {
        public static AppServerFileWindowRuntimeScope FromSnapshot(
            AppConfigSnapshot snapshot,
            string runtimeProfileId,
            AppInteractionMode interactionMode)
        {
            return new AppServerFileWindowRuntimeScope
            {
                RuntimeSource = snapshot != null ? snapshot.CodexRuntimeSource : null,
                RuntimeProfileId = runtimeProfileId,
                RemoteProfileId = snapshot != null ? snapshot.SelectedRemoteUnixSshProfileId : null,
                InteractionMode = interactionMode
            };
        }
    }
}
