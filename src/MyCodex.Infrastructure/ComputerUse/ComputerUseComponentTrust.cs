using System;
using System.Diagnostics;
using System.IO;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal static class ComputerUseComponentTrust
    {
        internal static bool Verify(string root)
        {
            var script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "computer-use", "verify-components.ps1");
            if (!File.Exists(script)) return false;
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + WindowsCommandLineArgumentEscaper.Quote(script),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            // Paths are data, never PowerShell source or command substitutions.
            start.EnvironmentVariables["MYCODEX_CUA_VERIFY_ROOT"] = root;
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(15000))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                    return false;
                }
                return process.ExitCode == 0 && output.GetAwaiter().GetResult().Trim() == "OPENAI_COMPONENTS_VERIFIED";
            }
        }
    }
}
