using System;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class PluginUninstallStatusMessageComposer
    {
        public static string BuildStillInstalledMessage(AppServerPluginSummary plugin)
        {
            var template = string.Equals(
                plugin == null ? null : plugin.InstallPolicySource,
                "IMPLICIT_CANONICAL_APP",
                StringComparison.OrdinalIgnoreCase)
                ? UiLocalization.Text(
                    "PluginManagementForm.Message.DefaultManagedPluginStillInstalled.Template",
                    "The runtime still reports {0} as installed after the uninstall request. It is supplied by the default app policy and was not removed.")
                : UiLocalization.Text(
                    "PluginManagementForm.Message.PluginStillInstalled.Template",
                    "The runtime still reports {0} as installed after the uninstall request. The plugin was not removed.");
            return string.Format(template, GetDisplayName(plugin));
        }

        private static string GetDisplayName(AppServerPluginSummary plugin)
        {
            if (plugin == null)
            {
                return UiLocalization.Text("PluginManagementForm.Value.UnknownPlugin", "(Unknown Plugin)");
            }

            if (!string.IsNullOrWhiteSpace(plugin.DisplayName))
            {
                return plugin.DisplayName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(plugin.Name))
            {
                return plugin.Name.Trim();
            }

            return UiLocalization.Text("PluginManagementForm.Value.UnnamedPlugin", "(Unnamed Plugin)");
        }
    }
}
