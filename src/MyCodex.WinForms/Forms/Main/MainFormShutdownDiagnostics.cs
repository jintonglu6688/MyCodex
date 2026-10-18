using System;
using System.Diagnostics;

namespace MyCodex.WinForms.Forms
{
    internal static class MainFormShutdownDiagnostics
    {
        private const int SlowStepThresholdMilliseconds = 1000;

        internal static void RunStep(string stepName, Action action)
        {
            if (action == null)
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            try
            {
                action();
            }
            finally
            {
                stopwatch.Stop();
                LogSlowStep(stepName, stopwatch.Elapsed, message => Trace.TraceWarning(message));
            }
        }

        internal static void RunStep(
            string stepName,
            Action action,
            Func<TimeSpan> timestampProvider,
            Action<string> logWarning)
        {
            if (action == null)
            {
                return;
            }

            var start = timestampProvider == null ? TimeSpan.Zero : timestampProvider();
            try
            {
                action();
            }
            finally
            {
                var end = timestampProvider == null ? start : timestampProvider();
                LogSlowStep(stepName, end - start, logWarning);
            }
        }

        private static void LogSlowStep(string stepName, TimeSpan elapsed, Action<string> logWarning)
        {
            if (elapsed.TotalMilliseconds < SlowStepThresholdMilliseconds || logWarning == null)
            {
                return;
            }

            logWarning(string.Format(
                "MyCodex shutdown step was slow. Step={0}; ElapsedMs={1}",
                string.IsNullOrWhiteSpace(stepName) ? "unknown" : stepName,
                (int)elapsed.TotalMilliseconds));
        }
    }
}
