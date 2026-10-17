using System;
using System.IO;

namespace MyCodex.Infrastructure.Git
{
    public static class BundledGitRuntime
    {
        public static string ResolveExecutable()
        {
            return ResolveExecutable(AppDomain.CurrentDomain.BaseDirectory);
        }

        internal static string ResolveExecutable(string installation)
        {
            var root = Path.GetFullPath(installation);
            var executable = Path.Combine(root, "runtime", "git", "cmd", "git.exe");
            if (!File.Exists(executable))
            {
                return "git";
            }
            var path = root;
            foreach (var segment in new[] { "runtime", "git", "cmd", "git.exe" })
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Linked bundled Git is unsupported.");
                }
                path = Path.Combine(path, segment);
            }
            if ((File.GetAttributes(executable) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Linked bundled Git is unsupported.");
            }
            return executable;
        }

        public static void ConfigureProcessEnvironment()
        {
            ConfigureProcessEnvironment(AppDomain.CurrentDomain.BaseDirectory);
        }

        internal static void ConfigureProcessEnvironment(string installation)
        {
            var executable = ResolveExecutable(installation);
            if (executable == "git")
            {
                return;
            }
            var directory = Path.GetDirectoryName(executable);
            var inherited = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (string.Equals(inherited.Split(';')[0].Trim().TrimEnd('\\'), directory, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            Environment.SetEnvironmentVariable("PATH", directory + (inherited.Length == 0 ? string.Empty : ";" + inherited), EnvironmentVariableTarget.Process);
        }
    }
}
