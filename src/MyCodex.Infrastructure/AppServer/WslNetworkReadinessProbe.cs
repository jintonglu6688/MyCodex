using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    internal interface IWslNetworkReadinessProbe
    {
        Task<WslNetworkReadinessResult> WaitForReadyAsync(
            WslAppServerConfigSnapshot config,
            int timeoutMilliseconds);
    }

    internal sealed class WslNetworkReadinessResult
    {
        public bool IsReady { get; set; }

        public int Attempts { get; set; }

        public string Host { get; set; }

        public string LastError { get; set; }

        public TimeSpan Elapsed { get; set; }
    }

    internal sealed class WslNetworkReadinessProbe : IWslNetworkReadinessProbe
    {
        private const string ProbeHost = "chatgpt.com";
        private const int SingleAttemptTimeoutMilliseconds = 3000;
        private const int InitialDelayMilliseconds = 500;
        private const int MaxDelayMilliseconds = 2000;

        private static readonly string ProbeScript =
            "host=\"$1\"\n" +
            "if command -v getent >/dev/null 2>&1; then\n" +
            "  getent hosts \"$host\" >/dev/null 2>&1 && exit 0\n" +
            "fi\n" +
            "if command -v nslookup >/dev/null 2>&1; then\n" +
            "  nslookup \"$host\" >/dev/null 2>&1 && exit 0\n" +
            "fi\n" +
            "exit 1\n";

        private readonly IWslCommandExecutor _executor;

        public WslNetworkReadinessProbe()
            : this(new WslCommandExecutor())
        {
        }

        internal WslNetworkReadinessProbe(IWslCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public async Task<WslNetworkReadinessResult> WaitForReadyAsync(
            WslAppServerConfigSnapshot config,
            int timeoutMilliseconds)
        {
            var result = new WslNetworkReadinessResult
            {
                Host = ProbeHost
            };

            var stopwatch = Stopwatch.StartNew();
            var timeout = timeoutMilliseconds > 0 ? timeoutMilliseconds : SingleAttemptTimeoutMilliseconds;
            var delay = InitialDelayMilliseconds;

            do
            {
                result.Attempts++;
                var remainingMilliseconds = Math.Max(1, timeout - (int)stopwatch.ElapsedMilliseconds);
                var attemptTimeout = Math.Min(SingleAttemptTimeoutMilliseconds, remainingMilliseconds);

                try
                {
                    var execution = await _executor.ExecuteAsync(
                        config ?? new WslAppServerConfigSnapshot(),
                        ProbeScript,
                        "mycodex-wsl-network-probe",
                        attemptTimeout,
                        ProbeHost).ConfigureAwait(false);

                    if (execution != null && execution.ExitCode == 0)
                    {
                        result.IsReady = true;
                        result.Elapsed = stopwatch.Elapsed;
                        return result;
                    }

                    result.LastError = BuildExecutionError(execution);
                }
                catch (Exception ex)
                {
                    result.LastError = ex.Message;
                }

                remainingMilliseconds = timeout - (int)stopwatch.ElapsedMilliseconds;
                if (remainingMilliseconds <= 0)
                {
                    break;
                }

                await Task.Delay(Math.Min(delay, remainingMilliseconds)).ConfigureAwait(false);
                delay = Math.Min(delay * 2, MaxDelayMilliseconds);
            }
            while (stopwatch.ElapsedMilliseconds < timeout);

            result.Elapsed = stopwatch.Elapsed;
            return result;
        }

        private static string BuildExecutionError(WslCommandExecutionResult execution)
        {
            if (execution == null)
            {
                return "WSL DNS probe did not return a result.";
            }

            if (!string.IsNullOrWhiteSpace(execution.StandardError))
            {
                return execution.StandardError.Trim();
            }

            if (!string.IsNullOrWhiteSpace(execution.StandardOutput))
            {
                return execution.StandardOutput.Trim();
            }

            return "WSL DNS probe exited with code " + execution.ExitCode + ".";
        }
    }
}
