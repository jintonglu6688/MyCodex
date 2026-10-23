using System;
using System.IO;

namespace MyCodex.Infrastructure.AppServer
{
    public static class RemoteSshSensitiveFileDirectory
    {
        private static readonly object SyncRoot = new object();
        private static string _configuredDirectory;

        public static void Configure(string tempDirectory)
        {
            var normalizedDirectory = Normalize(tempDirectory);
            lock (SyncRoot)
            {
                _configuredDirectory = normalizedDirectory;
            }
        }

        internal static string GetDirectory()
        {
            lock (SyncRoot)
            {
                return !string.IsNullOrWhiteSpace(_configuredDirectory)
                    ? _configuredDirectory
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MyCodex",
                        "temp");
            }
        }

        internal static string Normalize(string tempDirectory)
        {
            if (string.IsNullOrWhiteSpace(tempDirectory))
            {
                throw new ArgumentException("Temporary directory cannot be empty.", nameof(tempDirectory));
            }

            return Path.GetFullPath(tempDirectory.Trim());
        }

        internal static int CleanupStaleFiles(string tempDirectory, string searchPattern, TimeSpan maxAge)
        {
            var directory = Normalize(tempDirectory);
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            var deleted = 0;
            var threshold = DateTime.UtcNow.Subtract(maxAge < TimeSpan.Zero ? TimeSpan.Zero : maxAge);
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, searchPattern);
            }
            catch
            {
                return 0;
            }

            foreach (var path in files)
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) <= threshold)
                    {
                        File.Delete(path);
                        deleted++;
                    }
                }
                catch
                {
                }
            }

            return deleted;
        }
    }
}
