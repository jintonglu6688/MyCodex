using System;
using System.IO;
using MyCodex.Application.Services;

namespace MyCodex.WinForms.Forms
{
    internal static class OfficeCliWinFormsPathResolver
    {
        public static OfficeCliIntegrationPaths BuildPaths()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(localAppData) && !string.IsNullOrWhiteSpace(userProfile))
            {
                localAppData = Path.Combine(userProfile, "AppData", "Local");
            }

            return OfficeCliIntegrationPlanner.BuildPaths(
                localAppData,
                userProfile,
                Environment.GetEnvironmentVariable("CODEX_HOME"));
        }
    }
}
