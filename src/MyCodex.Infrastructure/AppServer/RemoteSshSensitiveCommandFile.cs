using System;
using System.IO;
using System.Text;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.AppServer
{
    public static class RemoteSshSensitiveCommandFile
    {
        private const string CommandFileSearchPattern = "mycodex-ssh-command-*.tmp";

        public static string CreateCommandFile(string remoteCommand)
        {
            return CreateCommandFile(GetDirectory(), remoteCommand);
        }

        public static string CreateCommandFile(string tempDirectory, string remoteCommand)
        {
            if (string.IsNullOrWhiteSpace(remoteCommand))
            {
                throw new InvalidOperationException("Remote SSH command is not available.");
            }

            var directory = NormalizeDirectory(tempDirectory);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "mycodex-ssh-command-" + Guid.NewGuid().ToString("N") + ".tmp");
            return WriteSensitiveFile(path, remoteCommand);
        }

        public static string CreateOpenSshConfigFile(RemoteMacSshConfigSnapshot config, string remoteCommand)
        {
            return CreateOpenSshConfigFile(GetDirectory(), config, remoteCommand);
        }

        public static string CreateOpenSshConfigFile(
            string tempDirectory,
            RemoteMacSshConfigSnapshot config,
            string remoteCommand)
        {
            if (string.IsNullOrWhiteSpace(remoteCommand))
            {
                throw new InvalidOperationException("Remote SSH command is not available.");
            }

            var directory = NormalizeDirectory(tempDirectory);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "mycodex-ssh-command-" + Guid.NewGuid().ToString("N") + ".tmp");
            return WriteSensitiveFile(
                path,
                BuildOpenSshConfig(config ?? new RemoteMacSshConfigSnapshot(), remoteCommand));
        }

        public static int CleanupStaleFiles(TimeSpan maxAge)
        {
            return CleanupStaleFiles(GetDirectory(), maxAge);
        }

        public static int CleanupStaleFiles(string tempDirectory, TimeSpan maxAge)
        {
            return RemoteSshSensitiveFileDirectory.CleanupStaleFiles(tempDirectory, CommandFileSearchPattern, maxAge);
        }

        private static string BuildOpenSshConfig(RemoteMacSshConfigSnapshot config, string remoteCommand)
        {
            var builder = new StringBuilder();
            builder.AppendLine("Host mycodex-remote-appserver");
            builder.AppendLine("  HostName " + EscapeOpenSshConfigValue(config.HostName ?? string.Empty));
            if (!string.IsNullOrWhiteSpace(config.UserName))
            {
                builder.AppendLine("  User " + EscapeOpenSshConfigValue(config.UserName.Trim()));
            }

            builder.AppendLine("  Port " + RemoteSshCommandLine.NormalizePort(config.Port));
            builder.AppendLine("  StrictHostKeyChecking accept-new");
            builder.AppendLine("  ServerAliveInterval 30");
            builder.AppendLine("  RequestTTY no");
            if (config.AuthenticationMode == RemoteSshAuthenticationMode.PrivateKey)
            {
                builder.AppendLine("  BatchMode yes");
                if (!string.IsNullOrWhiteSpace(config.PrivateKeyPath))
                {
                    builder.AppendLine("  IdentityFile " + EscapeOpenSshConfigValue(config.PrivateKeyPath.Trim()));
                }
            }
            else
            {
                builder.AppendLine("  BatchMode no");
                builder.AppendLine("  PreferredAuthentications password,keyboard-interactive");
            }

            builder.AppendLine("  RemoteCommand " + EscapeOpenSshConfigValue(remoteCommand));
            return builder.ToString();
        }

        private static string EscapeOpenSshConfigValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("%", "%%")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private static string GetDirectory()
        {
            return RemoteSshSensitiveFileDirectory.GetDirectory();
        }

        private static string NormalizeDirectory(string tempDirectory)
        {
            return RemoteSshSensitiveFileDirectory.Normalize(tempDirectory);
        }

        private static string WriteSensitiveFile(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content, new UTF8Encoding(false));
                return path;
            }
            catch
            {
                RemoteSshPasswordFile.TryDelete(path);
                throw;
            }
        }
    }
}
