using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public static class StartupFastModePromptPlanner
    {
        public const string FastServiceTierId = "priority";

        public static bool ShouldPrompt(AppServerConfigSnapshot snapshot)
        {
            return snapshot != null && IsFastServiceTier(snapshot.ServiceTier);
        }

        public static bool IsFastServiceTier(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            return string.Equals(normalized, FastServiceTierId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "fast", StringComparison.OrdinalIgnoreCase);
        }

        public static IReadOnlyCollection<AppServerConfigWriteEdit> BuildDisableFastModeEdits(
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
