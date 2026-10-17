using System;
using System.IO;
using System.Threading;
using MyCodex.Infrastructure.Instances;

namespace MyCodex.WinForms
{
    internal static class InstallationExitStartup
    {
        internal static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (args == null || args.Length != 1 || !string.Equals(args[0], "--exit-for-install", StringComparison.Ordinal))
                return false;
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
            {
                exitCode = InstallationTargetExit.RunAsync(new[]
                {
                    Path.Combine(local, "MyCodex"), Path.Combine(local, "MyCodex-Dev")
                }, timeout.Token).GetAwaiter().GetResult();
            }
            return true;
        }
    }
}
