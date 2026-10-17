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
    }
}
