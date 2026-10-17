using System;

namespace MyCodex.WinForms
{
    internal sealed class StartupCommandLineOptions
    {
        private StartupCommandLineOptions(string workspacePath, string targetId = null)
        {
            WorkspacePath = string.IsNullOrWhiteSpace(workspacePath)
                ? string.Empty
                : workspacePath.Trim();
            TargetId = string.IsNullOrWhiteSpace(targetId) ? string.Empty : targetId.Trim();
        }

        public static StartupCommandLineOptions Empty
        {
            get { return new StartupCommandLineOptions(string.Empty); }
        }

        public string WorkspacePath { get; private set; }

        public string TargetId { get; private set; }

        public bool HasWorkspacePath
        {
            get { return !string.IsNullOrWhiteSpace(WorkspacePath); }
        }

        public static StartupCommandLineOptions Parse(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Empty;
            }

            var workspace = string.Empty;
            var target = string.Empty;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                var trimmed = arg.Trim();
                if (trimmed.StartsWith("--workspace=", StringComparison.OrdinalIgnoreCase))
                {
                    workspace = trimmed.Substring("--workspace=".Length);
                    continue;
                }

                if (string.Equals(trimmed, "--workspace", StringComparison.OrdinalIgnoreCase))
                {
                    workspace = i + 1 < args.Length ? args[++i] : string.Empty;
                    continue;
                }

                if (trimmed.StartsWith("--target=", StringComparison.OrdinalIgnoreCase))
                {
                    target = trimmed.Substring("--target=".Length);
                    if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("A target ID is required.");
                    continue;
                }

                if (string.Equals(trimmed, "--target", StringComparison.OrdinalIgnoreCase))
                {
                    target = i + 1 < args.Length ? args[++i] : string.Empty;
                    if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("A target ID is required.");
                    continue;
                }

                if (trimmed.StartsWith("--channel=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(trimmed, "--channel", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    continue;
                }

                if (trimmed.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(workspace)) workspace = trimmed;
            }

            return new StartupCommandLineOptions(workspace, target);
        }
    }
}
