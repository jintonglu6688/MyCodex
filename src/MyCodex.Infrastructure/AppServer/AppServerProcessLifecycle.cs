using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using MyCodex.Infrastructure.Processes;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerProcessResources
    {
        public Process Process { get; set; }

        public TextWriter InputWriter { get; set; }

        public CancellationTokenSource LifetimeCts { get; set; }
        public IDisposable ComputerUse { get; set; }
    }

    internal static class AppServerProcessLifecycle
    {
        public static ProcessStartInfo BuildStartInfo(
            string fileName,
            string arguments,
            string workingDirectory,
            IDictionary<string, string> environmentVariables,
            ICollection<string> appliedEnvironmentVariableNames)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            ApplyEnvironmentVariables(startInfo, environmentVariables, appliedEnvironmentVariableNames);
            return startInfo;
        }

        public static void CleanupResources(
            AppServerProcessResources resources,
            Action<Process> unbindExitedHandler)
        {
            if (resources == null)
            {
                return;
            }

            var process = resources.Process;
            TryUnbindExitedHandler(process, unbindExitedHandler);
            TryCancel(resources.LifetimeCts);
            TryDisposeWriter(resources.InputWriter);
            TryKill(process);
            TryDisposeProcess(process);
            TryDisposeCts(resources.LifetimeCts);
        }

        private static void ApplyEnvironmentVariables(
            ProcessStartInfo startInfo,
            IDictionary<string, string> environmentVariables,
            ICollection<string> appliedEnvironmentVariableNames)
        {
            if (startInfo == null || environmentVariables == null)
            {
                return;
            }

            foreach (var pair in environmentVariables)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                startInfo.EnvironmentVariables[pair.Key] = pair.Value ?? string.Empty;
                if (appliedEnvironmentVariableNames != null)
                {
                    appliedEnvironmentVariableNames.Add(pair.Key);
                }
            }
        }

        private static void TryUnbindExitedHandler(Process process, Action<Process> unbindExitedHandler)
        {
            try
            {
                if (process != null && unbindExitedHandler != null)
                {
                    unbindExitedHandler(process);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceInformation("Failed to unbind App Server process-exit event: {0}", ex.Message);
            }
        }

        private static void TryCancel(CancellationTokenSource lifetimeCts)
        {
            try
            {
                lifetimeCts?.Cancel();
            }
            catch (Exception ex)
            {
                Trace.TraceInformation("Exception while canceling App Server lifecycle token (ignored): {0}", ex.Message);
            }
        }

        private static void TryDisposeWriter(TextWriter inputWriter)
        {
            try
            {
                inputWriter?.Dispose();
            }
            catch (Exception ex)
            {
                Trace.TraceInformation("Exception while disposing App Server input writer (ignored): {0}", ex.Message);
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    ProcessTreeTerminator.TryKill(process);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceInformation("Exception while terminating App Server process (ignored): {0}", ex.Message);
            }
        }

        private static void TryDisposeProcess(Process process)
        {
            try
            {
                process?.Dispose();
            }
            catch (Exception ex)
            {
                Trace.TraceInformation("Exception while disposing App Server process object (ignored): {0}", ex.Message);
            }
        }

        private static void TryDisposeCts(CancellationTokenSource lifetimeCts)
        {
            try
            {
                lifetimeCts?.Dispose();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to dispose App Server lifecycle token: {0}", ex.Message);
            }
        }
    }
}
