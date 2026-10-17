using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal static class StartupFastModePromptPlanner
    {
        internal const string FastServiceTierId = "priority";

        internal static bool ShouldPrompt(AppServerConfigSnapshot snapshot)
        {
            return snapshot != null && IsFastServiceTier(snapshot.ServiceTier);
        }

        internal static bool IsFastServiceTier(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            return string.Equals(normalized, FastServiceTierId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "fast", StringComparison.OrdinalIgnoreCase);
        }

        internal static IReadOnlyCollection<AppServerConfigWriteEdit> BuildDisableFastModeEdits(
            AppServerConfigSnapshot snapshot)
        {
            if (!ShouldPrompt(snapshot))
            {
                return new AppServerConfigWriteEdit[0];
            }

            return new[]
            {
                AppServerConfigWriteEdit.CreateReplace("service_tier", null)
            };
        }
    }
}
