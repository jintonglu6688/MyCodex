using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal sealed class MainFormSettingsMenuState
    {
        public bool AppServerRuntimeSettingsVisible { get; set; }

        public bool AppServerRuntimeSettingsEnabled { get; set; }

        public bool OptionsEnabled { get; set; }

        public bool McpManagementEnabled { get; set; }

        public bool SkillsManagementEnabled { get; set; }

        public bool PluginManagementEnabled { get; set; }
    }

    internal sealed class MainFormSessionSettingsContextMenuState
    {
        public bool SessionModelEnabled { get; set; }

        public bool SessionPermissionEnabled { get; set; }

        public bool SessionResetSettingsEnabled { get; set; }
    }

    internal sealed class MainFormSessionSettingsMenuText
    {
        public string DefaultModelText { get; set; }

        public string DefaultPermissionText { get; set; }

        public string SessionModelText { get; set; }

        public string SessionPermissionText { get; set; }
    }

    internal static class MainFormAuthenticationStatePlanner
    {
        internal static bool RequiresOfficialLogin(AppServerAuthenticationState authenticationState)
        {
            return authenticationState != null &&
                   !authenticationState.HasAccount &&
                   authenticationState.RequiresOpenAiAuth;
        }

        internal static MainFormSettingsMenuState BuildSettingsMenuState(
            bool isOfficeMode,
            bool isWorkspaceAvailable)
        {
            return new MainFormSettingsMenuState
            {
                AppServerRuntimeSettingsVisible = true,
                AppServerRuntimeSettingsEnabled = true,
                OptionsEnabled = true,
                McpManagementEnabled = true,
                SkillsManagementEnabled = isWorkspaceAvailable,
                PluginManagementEnabled = true
            };
        }

        internal static MainFormSessionSettingsContextMenuState BuildSessionSettingsContextMenuState(
            bool hasSession,
            bool hasModelOverride,
            bool hasApprovalOverride,
            bool hasPlanModeOverride)
        {
            return new MainFormSessionSettingsContextMenuState
            {
                SessionModelEnabled = hasSession,
                SessionPermissionEnabled = hasSession,
                SessionResetSettingsEnabled = hasSession && (hasModelOverride || hasApprovalOverride || hasPlanModeOverride)
            };
        }

        internal static MainFormSessionSettingsMenuText BuildSessionSettingsMenuText(
            string defaultModelTemplate,
            string defaultModelSummary,
            string defaultPermissionTemplate,
            string defaultPermissionSummary,
            bool hasSession,
            string sessionModelBaseText,
            string sessionModelTemplate,
            string sessionModelSummary,
            string sessionPermissionBaseText,
            string sessionPermissionTemplate,
            string sessionPermissionSummary)
        {
            return new MainFormSessionSettingsMenuText
            {
                DefaultModelText = string.Format(defaultModelTemplate, defaultModelSummary),
                DefaultPermissionText = string.Format(defaultPermissionTemplate, defaultPermissionSummary),
                SessionModelText = hasSession
                    ? string.Format(sessionModelTemplate, sessionModelSummary)
                    : sessionModelBaseText,
                SessionPermissionText = hasSession
                    ? string.Format(sessionPermissionTemplate, sessionPermissionSummary)
                    : sessionPermissionBaseText
            };
        }
    }
}
