using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal static class PluginAttachmentSelectionBuilder
    {
        public static List<AppServerPluginSummary> BuildInsertablePlugins(
            IEnumerable<AppServerPluginSummary> plugins)
        {
            return (plugins ?? new List<AppServerPluginSummary>())
                .Where(plugin => plugin != null && plugin.Installed && plugin.Enabled && !string.IsNullOrWhiteSpace(plugin.SourcePath))
                .OrderByDescending(plugin => plugin.Enabled)
                .ThenBy(plugin => ComposeAttachmentState.GetPluginInsertionDisplayName(plugin), StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

    }
}
