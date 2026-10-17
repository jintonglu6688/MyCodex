using System;
using System.Diagnostics;

namespace MyCodex.Infrastructure.Processes
{
    public static class ProcessTreeTerminator
    {
        public static void TryKill(Process process)
        {
            if (process == null)
            {
                return;
            }

            int processId;
            try
            {
                if (process.HasExited)
                {
                    return;
                }

                processId = process.Id;
            }
            catch
            {
                return;
            }

            if (TryKillWindowsProcessTree(processId))
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
            }
        }

        private static bool TryKillWindowsProcessTree(int processId)
        {
            if (processId <= 0 || Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return false;
            }

            try
            {
                using (var killer = new Process())
                {
                    killer.StartInfo = new ProcessStartInfo
                    {
                        FileName = "taskkill.exe",
                        Arguments = "/PID " + processId.ToString(System.Globalization.CultureInfo.InvariantCulture) + " /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    killer.Start();
                    if (!killer.WaitForExit(5000))
                    {
                        try
                        {
                            killer.Kill();
                        }
                        catch
                        {
                        }

                        return false;
                    }

                    return killer.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
