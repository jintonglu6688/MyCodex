using System;
using System.IO;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class PlinkExecutableResolver
    {
        public static string Resolve(string configuredPath)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                var trimmed = configuredPath.Trim();
                if (File.Exists(trimmed))
                {
                    return trimmed;
                }
            }

            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(appDirectory, "runtime", "putty", "plink.exe"),
                Path.Combine(appDirectory, "plink.exe"),
                Path.Combine(appDirectory, "tools", "putty", "plink.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PuTTY", "plink.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PuTTY", "plink.exe"),
                FindOnPath("plink.exe")
            };

            for (var i = 0; i < candidates.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(candidates[i]) && File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            throw new FileNotFoundException(
                "plink.exe was not found. Install PuTTY, add plink.exe to PATH, or place plink.exe next to MyCodex.exe.");
        }

        private static string FindOnPath(string fileName)
        {
            var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var paths = pathValue.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < paths.Length; i++)
            {
                try
                {
                    var candidate = Path.Combine(paths[i].Trim(), fileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
