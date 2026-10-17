using System;
using System.IO;

namespace MyCodex.Infrastructure.Persistence
{
    public static class UserCacheMaintenance
    {
        private const string LegacyByCtrlLogFileName = "byctrl.log";
        public static void CleanupOnStartup(AppConfigService appConfigService)
        {
            if (appConfigService == null)
            {
                throw new ArgumentNullException(nameof(appConfigService));
            }

            var tempDirectory = appConfigService.GetTempDirectory();
            TryDeleteCacheDirectory(tempDirectory, appConfigService.GetUpdateTempDirectory());
            TryDeleteCacheDirectory(tempDirectory, appConfigService.GetAttachmentTempDirectory());
            TryDeleteFile(Path.Combine(appConfigService.GetLogDirectory(), LegacyByCtrlLogFileName));
        }

        private static void TryDeleteCacheDirectory(string tempDirectory, string cacheDirectory)
        {
            try
            {
                var root = Path.GetFullPath(tempDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                    Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(cacheDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                    !Directory.Exists(candidate))
                {
                    return;
                }

                Directory.Delete(candidate, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }
    }
}
