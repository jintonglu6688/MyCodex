using System;
using System.IO;
using System.Text;

namespace MyCodex.Infrastructure.AppServer
{
    public static class RemoteSshPasswordFile
    {
        private const string FileSearchPattern = "mycodex-ssh-password-*.tmp";

        public static string Create(string password)
        {
            return Create(GetDirectory(), password);
        }

        public static string Create(string tempDirectory, string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("Remote SSH password is not available.");
            }

            var directory = NormalizeDirectory(tempDirectory);
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "mycodex-ssh-password-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(path, password, new UTF8Encoding(false));
                return path;
            }
            catch
            {
                TryDelete(path);
                throw;
            }
        }

        public static int CleanupStaleFiles(TimeSpan maxAge)
        {
            return CleanupStaleFiles(GetDirectory(), maxAge);
        }

        public static int CleanupStaleFiles(string tempDirectory, TimeSpan maxAge)
        {
            return RemoteSshSensitiveFileDirectory.CleanupStaleFiles(tempDirectory, FileSearchPattern, maxAge);
        }

        public static void TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }

        private static string GetDirectory()
        {
            return RemoteSshSensitiveFileDirectory.GetDirectory();
        }

        private static string NormalizeDirectory(string tempDirectory)
        {
            return RemoteSshSensitiveFileDirectory.Normalize(tempDirectory);
        }
    }
}
