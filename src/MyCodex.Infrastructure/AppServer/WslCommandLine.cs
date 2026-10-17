using System;
using System.Collections.Generic;
using System.Text;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class WslCommandLine
    {
        private static readonly string[] HeadlessEnvironmentVariables =
        {
            "DISPLAY",
            "WAYLAND_DISPLAY",
            "PULSE_SERVER",
            "WSLG_RUNTIME_DIR"
        };

        public static string BuildExecArguments(string distributionName, string executable, params string[] arguments)
        {
            var parts = new List<string>
            {
                "-d",
                WindowsCommandLineArgumentEscaper.Quote(NormalizeDistributionName(distributionName)),
                "--exec",
                WindowsCommandLineArgumentEscaper.Quote(NormalizeExecutable(executable))
            };

            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    parts.Add(WindowsCommandLineArgumentEscaper.Quote(argument));
                }
            }

            return string.Join(" ", parts);
        }

        public static string BuildHeadlessExecArguments(string distributionName, string executable, params string[] arguments)
        {
            var headlessArguments = new List<string>();
            foreach (var variableName in HeadlessEnvironmentVariables)
            {
                headlessArguments.Add("-u");
                headlessArguments.Add(variableName);
            }

            headlessArguments.Add(NormalizeExecutable(executable));
            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    headlessArguments.Add(argument ?? string.Empty);
                }
            }

            return BuildExecArguments(distributionName, "env", headlessArguments.ToArray());
        }

        public static string BuildHeadlessShellExecArguments(
            string distributionName,
            string shellCommand,
            string executable,
            params string[] arguments)
        {
            var shellText = BuildShellExecText(executable, arguments);
            return BuildHeadlessExecArguments(
                distributionName,
                string.IsNullOrWhiteSpace(shellCommand) ? WslAppServerConfigSnapshot.DefaultShellCommand : shellCommand.Trim(),
                "-lc",
                shellText);
        }

        public static string NormalizeDistributionName(string distributionName)
        {
            return string.IsNullOrWhiteSpace(distributionName)
                ? WslAppServerConfigSnapshot.DefaultDistributionName
                : distributionName.Trim();
        }

        public static string NormalizeExecutable(string executable)
        {
            return string.IsNullOrWhiteSpace(executable)
                ? WslAppServerConfigSnapshot.DefaultCodexCommand
                : executable.Trim();
        }

        private static string BuildShellExecText(string executable, string[] arguments)
        {
            var builder = new StringBuilder();
            builder.Append("export PATH=\"$HOME/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:$PATH\"; exec ");
            builder.Append(QuoteShellArgument(NormalizeExecutable(executable)));
            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    builder.Append(' ');
                    builder.Append(QuoteShellArgument(argument ?? string.Empty));
                }
            }

            return builder.ToString();
        }

        private static string QuoteShellArgument(string value)
        {
            value = value ?? string.Empty;
            if (value.Length == 0)
            {
                return "''";
            }

            return "'" + value.Replace("'", "'\"'\"'") + "'";
        }

    }
}
