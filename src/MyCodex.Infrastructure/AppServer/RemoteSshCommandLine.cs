using System;
using System.Collections.Generic;
using System.Text;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AppServer
{
    public static class RemoteSshCommandLine
    {
        public static string BuildSshArguments(RemoteMacSshConfigSnapshot config, string remoteCommand)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var parts = new List<string>
            {
                "-T",
                "-o",
                "StrictHostKeyChecking=accept-new",
                "-o",
                "ServerAliveInterval=30",
                "-p",
                NormalizePort(config.Port).ToString()
            };

            if (config.AuthenticationMode == RemoteSshAuthenticationMode.PrivateKey)
            {
                parts.Add("-o");
                parts.Add("BatchMode=yes");
                if (!string.IsNullOrWhiteSpace(config.PrivateKeyPath))
                {
                    parts.Add("-i");
                    parts.Add(config.PrivateKeyPath.Trim());
                }
            }
            else
            {
                parts.Add("-o");
                parts.Add("BatchMode=no");
                parts.Add("-o");
                parts.Add("PreferredAuthentications=password,keyboard-interactive");
            }

            parts.Add(BuildUserHost(config));
            parts.Add(remoteCommand ?? string.Empty);

            return QuoteWindowsArguments(parts);
        }

        public static string BuildSshArgumentsWithConfigFile(string configFilePath, string hostAlias)
        {
            var parts = new List<string>
            {
                "-F",
                configFilePath ?? string.Empty,
                string.IsNullOrWhiteSpace(hostAlias) ? "mycodex-remote-appserver" : hostAlias.Trim()
            };
            return QuoteWindowsArguments(parts);
        }

        public static string BuildLocalForwardArguments(RemoteMacSshConfigSnapshot config, int localPort, int remotePort)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var normalizedLocalPort = NormalizeLoopbackPort(localPort);
            var normalizedRemotePort = NormalizeLoopbackPort(remotePort);
            var parts = new List<string>
            {
                "-N",
                "-T",
                "-o",
                "StrictHostKeyChecking=accept-new",
                "-o",
                "ExitOnForwardFailure=yes",
                "-o",
                "ServerAliveInterval=30",
                "-p",
                NormalizePort(config.Port).ToString(),
                "-L",
                "localhost:" + normalizedLocalPort + ":localhost:" + normalizedRemotePort
            };

            if (config.AuthenticationMode == RemoteSshAuthenticationMode.PrivateKey)
            {
                parts.Add("-o");
                parts.Add("BatchMode=yes");
                if (!string.IsNullOrWhiteSpace(config.PrivateKeyPath))
                {
                    parts.Add("-i");
                    parts.Add(config.PrivateKeyPath.Trim());
                }
            }
            else
            {
                parts.Add("-o");
                parts.Add("BatchMode=no");
                parts.Add("-o");
                parts.Add("PreferredAuthentications=password,keyboard-interactive");
            }

            parts.Add(BuildUserHost(config));
            return QuoteWindowsArguments(parts);
        }

        public static string BuildReverseForwardArguments(RemoteMacSshConfigSnapshot config, int remotePort, int localPort)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var normalizedRemotePort = NormalizeLoopbackPort(remotePort);
            var normalizedLocalPort = NormalizeLoopbackPort(localPort);
            var parts = new List<string>
            {
                "-N",
                "-T",
                "-o",
                "StrictHostKeyChecking=accept-new",
                "-o",
                "ExitOnForwardFailure=yes",
                "-o",
                "ServerAliveInterval=30",
                "-p",
                NormalizePort(config.Port).ToString(),
                "-R",
                "127.0.0.1:" + normalizedRemotePort + ":127.0.0.1:" + normalizedLocalPort
            };

            if (config.AuthenticationMode == RemoteSshAuthenticationMode.PrivateKey)
            {
                parts.Add("-o");
                parts.Add("BatchMode=yes");
                if (!string.IsNullOrWhiteSpace(config.PrivateKeyPath))
                {
                    parts.Add("-i");
                    parts.Add(config.PrivateKeyPath.Trim());
                }
            }
            else
            {
                parts.Add("-o");
                parts.Add("BatchMode=no");
                parts.Add("-o");
                parts.Add("PreferredAuthentications=password,keyboard-interactive");
            }

            parts.Add(BuildUserHost(config));
            return QuoteWindowsArguments(parts);
        }

        public static string BuildPlinkArguments(RemoteMacSshConfigSnapshot config, string remoteCommand, string passwordFilePath)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var parts = new List<string>
            {
                "-ssh",
                "-batch",
                "-T",
                "-P",
                NormalizePort(config.Port).ToString()
            };

            if (!string.IsNullOrWhiteSpace(config.UserName))
            {
                parts.Add("-l");
                parts.Add(config.UserName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(passwordFilePath))
            {
                parts.Add("-pwfile");
                parts.Add(passwordFilePath.Trim());
            }

            AddPlinkHostKey(parts, config);
            parts.Add(config.HostName ?? string.Empty);
            parts.Add(remoteCommand ?? string.Empty);
            return QuoteWindowsArguments(parts);
        }

        public static string BuildPlinkArgumentsWithCommandFile(
            RemoteMacSshConfigSnapshot config,
            string commandFilePath,
            string passwordFilePath)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var parts = new List<string>
            {
                "-ssh",
                "-batch",
                "-T",
                "-P",
                NormalizePort(config.Port).ToString()
            };

            if (!string.IsNullOrWhiteSpace(config.UserName))
            {
                parts.Add("-l");
                parts.Add(config.UserName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(passwordFilePath))
            {
                parts.Add("-pwfile");
                parts.Add(passwordFilePath.Trim());
            }

            if (!string.IsNullOrWhiteSpace(commandFilePath))
            {
                parts.Add("-m");
                parts.Add(commandFilePath.Trim());
            }

            AddPlinkHostKey(parts, config);
            parts.Add(config.HostName ?? string.Empty);
            return QuoteWindowsArguments(parts);
        }

        public static string BuildPlinkLocalForwardArguments(
            RemoteMacSshConfigSnapshot config,
            int localPort,
            int remotePort,
            string passwordFilePath)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var normalizedLocalPort = NormalizeLoopbackPort(localPort);
            var normalizedRemotePort = NormalizeLoopbackPort(remotePort);
            var parts = new List<string>
            {
                "-ssh",
                "-batch",
                "-N",
                "-T",
                "-P",
                NormalizePort(config.Port).ToString(),
                "-L",
                "localhost:" + normalizedLocalPort + ":localhost:" + normalizedRemotePort
            };

            if (!string.IsNullOrWhiteSpace(config.UserName))
            {
                parts.Add("-l");
                parts.Add(config.UserName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(passwordFilePath))
            {
                parts.Add("-pwfile");
                parts.Add(passwordFilePath.Trim());
            }

            AddPlinkHostKey(parts, config);
            parts.Add(config.HostName ?? string.Empty);
            return QuoteWindowsArguments(parts);
        }

        public static string BuildPlinkReverseForwardArguments(
            RemoteMacSshConfigSnapshot config,
            int remotePort,
            int localPort,
            string passwordFilePath)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var normalizedRemotePort = NormalizeLoopbackPort(remotePort);
            var normalizedLocalPort = NormalizeLoopbackPort(localPort);
            var parts = new List<string>
            {
                "-ssh",
                "-batch",
                "-N",
                "-T",
                "-P",
                NormalizePort(config.Port).ToString(),
                "-R",
                "127.0.0.1:" + normalizedRemotePort + ":127.0.0.1:" + normalizedLocalPort
            };

            if (!string.IsNullOrWhiteSpace(config.UserName))
            {
                parts.Add("-l");
                parts.Add(config.UserName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(passwordFilePath))
            {
                parts.Add("-pwfile");
                parts.Add(passwordFilePath.Trim());
            }

            AddPlinkHostKey(parts, config);
            parts.Add(config.HostName ?? string.Empty);
            return QuoteWindowsArguments(parts);
        }

        public static string BuildShellCommand(RemoteMacSshConfigSnapshot config, params string[] commandParts)
        {
            return BuildShellCommand(config, null, commandParts);
        }

        public static string BuildShellCommand(
            RemoteMacSshConfigSnapshot config,
            IDictionary<string, string> environmentVariables,
            params string[] commandParts)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var command = BuildShellCommandText(environmentVariables, commandParts);
            return NormalizeShellCommand(config.ShellCommand) + " -lc " + QuoteRemoteShellArgument(command);
        }

        private static string BuildShellCommandText(
            IDictionary<string, string> environmentVariables,
            string[] commandParts)
        {
            var parts = new List<string>();
            var variables = new List<KeyValuePair<string, string>>();
            if (environmentVariables != null)
            {
                foreach (var pair in environmentVariables)
                {
                    if (IsValidEnvironmentVariableName(pair.Key))
                    {
                        variables.Add(pair);
                    }
                }
            }

            variables.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Key, right.Key));
            if (variables.Count > 0)
            {
                parts.Add("env");
                foreach (var pair in variables)
                {
                    parts.Add(pair.Key.Trim() + "=" + QuoteRemoteSingle(pair.Value ?? string.Empty));
                }
            }

            if (commandParts != null)
            {
                foreach (var commandPart in commandParts)
                {
                    if (!string.IsNullOrWhiteSpace(commandPart))
                    {
                        parts.Add(commandPart);
                    }
                }
            }

            return string.Join(" ", parts.ToArray());
        }

        private static bool IsValidEnvironmentVariableName(string variableName)
        {
            if (string.IsNullOrWhiteSpace(variableName))
            {
                return false;
            }

            var trimmed = variableName.Trim();
            var first = trimmed[0];
            if (!((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z') || first == '_'))
            {
                return false;
            }

            for (var index = 1; index < trimmed.Length; index++)
            {
                var ch = trimmed[index];
                if (!((ch >= 'A' && ch <= 'Z') ||
                      (ch >= 'a' && ch <= 'z') ||
                      (ch >= '0' && ch <= '9') ||
                      ch == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        public static string NormalizeShellCommand(string shellCommand)
        {
            return string.IsNullOrWhiteSpace(shellCommand)
                ? RemoteMacSshConfigSnapshot.DefaultShellCommand
                : shellCommand.Trim();
        }

        public static string NormalizeCodexCommand(string codexCommand)
        {
            return string.IsNullOrWhiteSpace(codexCommand)
                ? RemoteMacSshConfigSnapshot.DefaultCodexCommand
                : codexCommand.Trim();
        }

        public static int NormalizePort(int port)
        {
            return port > 0 && port <= 65535 ? port : RemoteMacSshConfigSnapshot.DefaultPort;
        }

        private static int NormalizeLoopbackPort(int port)
        {
            if (port <= 0 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            return port;
        }

        private static string BuildUserHost(RemoteMacSshConfigSnapshot config)
        {
            var hostName = config.HostName ?? string.Empty;
            return string.IsNullOrWhiteSpace(config.UserName)
                ? hostName
                : config.UserName.Trim() + "@" + hostName;
        }

        private static void AddPlinkHostKey(ICollection<string> parts, RemoteMacSshConfigSnapshot config)
        {
            if (parts == null || config == null || string.IsNullOrWhiteSpace(config.HostKeyFingerprint))
            {
                return;
            }

            parts.Add("-hostkey");
            parts.Add(config.HostKeyFingerprint.Trim());
        }

        private static string QuoteWindowsArguments(IEnumerable<string> values)
        {
            var builder = new StringBuilder();
            foreach (var value in values)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(WindowsCommandLineArgumentEscaper.Quote(value));
            }

            return builder.ToString();
        }

        private static string QuoteRemoteSingle(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";
        }

        public static string QuoteRemoteShellArgument(string value)
        {
            return QuoteRemoteSingle(value);
        }
    }
}
