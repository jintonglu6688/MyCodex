using System;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerCommandLinePolicy
    {
        private const string WindowsShellCommand = "cmd.exe";
        private const string PowerShell7Command = "pwsh.exe";
        private const string WindowsPowerShellCommand = "powershell.exe";

        private readonly AppServerHostPlatform _hostPlatform;
        private readonly string _windowsShellPreference;
        private readonly string _unixShellCommand;

        private AppServerCommandLinePolicy(AppServerHostPlatform hostPlatform, string windowsShellPreference, string unixShellCommand)
        {
            _hostPlatform = hostPlatform;
            _windowsShellPreference = WindowsShellPreferences.Normalize(windowsShellPreference);
            _unixShellCommand = NormalizeUnixShellCommand(hostPlatform, unixShellCommand);
        }

        public static AppServerCommandLinePolicy ForHostPlatform(AppServerHostPlatform hostPlatform, string unixShellCommand)
        {
            return new AppServerCommandLinePolicy(hostPlatform, WindowsShellPreferences.Auto, unixShellCommand);
        }

        public static AppServerCommandLinePolicy ForHostPlatform(
            AppServerHostPlatform hostPlatform,
            string windowsShellPreference,
            string unixShellCommand)
        {
            return new AppServerCommandLinePolicy(hostPlatform, windowsShellPreference, unixShellCommand);
        }

        public string[] BuildStandaloneCommand(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText))
            {
                throw new ArgumentException("Command text cannot be empty.", nameof(commandText));
            }

            switch (_hostPlatform)
            {
                case AppServerHostPlatform.Wsl:
                case AppServerHostPlatform.RemoteMac:
                    return new[] { _unixShellCommand, "-lc", commandText };
                default:
                    return BuildWindowsCommand(commandText);
            }
        }

        private string[] BuildWindowsCommand(string commandText)
        {
            if (string.Equals(_windowsShellPreference, WindowsShellPreferences.PowerShell7, System.StringComparison.Ordinal))
            {
                return new[] { PowerShell7Command, "-NoLogo", "-NoProfile", "-Command", commandText };
            }

            if (string.Equals(_windowsShellPreference, WindowsShellPreferences.WindowsPowerShell, System.StringComparison.Ordinal))
            {
                return new[] { WindowsPowerShellCommand, "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", commandText };
            }

            return new[] { WindowsShellCommand, "/c", commandText };
        }

        private static string NormalizeUnixShellCommand(AppServerHostPlatform hostPlatform, string shellCommand)
        {
            if (!string.IsNullOrWhiteSpace(shellCommand))
            {
                return shellCommand.Trim();
            }

            return hostPlatform == AppServerHostPlatform.RemoteMac
                ? RemoteMacSshConfigSnapshot.DefaultShellCommand
                : WslAppServerConfigSnapshot.DefaultShellCommand;
        }
    }
}
