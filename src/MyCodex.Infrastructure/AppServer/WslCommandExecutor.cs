using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Processes;

namespace MyCodex.Infrastructure.AppServer
{
    internal interface IWslCommandExecutor
    {
        Task<WslCommandExecutionResult> ExecuteWithInputAsync(
            WslAppServerConfigSnapshot config, string script, string operationName,
            int timeoutMilliseconds, CancellationToken cancellationToken, string standardInput,
            params string[] arguments);

        Task<WslCommandExecutionResult> ExecuteAsync(
            WslAppServerConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            params string[] arguments);

        Task<WslCommandExecutionResult> ExecuteAsync(
            WslAppServerConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            CancellationToken cancellationToken,
            params string[] arguments);
    }

    internal sealed class WslCommandExecutionResult
    {
        public int ExitCode { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }
    }

    internal sealed class WslCommandExecutor : IWslCommandExecutor
    {
        private const string WslExecutableName = "wsl.exe";

        public Task<WslCommandExecutionResult> ExecuteAsync(
            WslAppServerConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            params string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new ArgumentException("WSL script cannot be empty.", nameof(script));
            }

            return Task.Run(() => Execute(config, script, operationName, timeoutMilliseconds, default, null, arguments), default);
        }

        public Task<WslCommandExecutionResult> ExecuteAsync(
            WslAppServerConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new ArgumentException("WSL script cannot be empty.", nameof(script));
            }

            return Task.Run(
                () => Execute(config, script, operationName, timeoutMilliseconds, cancellationToken, null, arguments),
                cancellationToken);
        }

        public Task<WslCommandExecutionResult> ExecuteWithInputAsync(
            WslAppServerConfigSnapshot config, string script, string operationName,
            int timeoutMilliseconds, CancellationToken cancellationToken, string standardInput,
            params string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(script) || standardInput == null)
            {
                throw new ArgumentException("WSL script and standard input are required.");
            }
            return Task.Run(() => Execute(config, script, operationName, timeoutMilliseconds,
                cancellationToken, standardInput, arguments), cancellationToken);
        }

        private static WslCommandExecutionResult Execute(
            WslAppServerConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            CancellationToken cancellationToken,
            string standardInput,
            string[] arguments)
        {
            config = config ?? new WslAppServerConfigSnapshot();
            var shellCommand = string.IsNullOrWhiteSpace(config.ShellCommand)
                ? WslAppServerConfigSnapshot.DefaultShellCommand
                : config.ShellCommand.Trim();

            var shellArguments = new string[3 + (arguments != null ? arguments.Length : 0)];
            shellArguments[0] = "-c";
            shellArguments[1] = script;
            shellArguments[2] = string.IsNullOrWhiteSpace(operationName) ? "mycodex-wsl" : operationName.Trim();
            if (arguments != null)
            {
                for (var i = 0; i < arguments.Length; i++)
                {
                    shellArguments[3 + i] = arguments[i] ?? string.Empty;
                }
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = WslExecutableName,
                Arguments = WslCommandLine.BuildHeadlessExecArguments(
                    config.DistributionName,
                    shellCommand,
                    shellArguments),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput != null,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var result = MyCodex.Infrastructure.Processes.ProcessExecutionRunner.Run(new MyCodex.Infrastructure.Processes.ProcessExecutionRequest
            {
                StartInfo = startInfo,
                TimeoutMilliseconds = timeoutMilliseconds > 0 ? timeoutMilliseconds : 10000,
                TimeoutMessage = "WSL command timed out.",
                CancellationToken = cancellationToken,
                StandardInput = standardInput,
                OutputTransform = GitOutputSanitizer.RedactSensitiveText
            });
            return new WslCommandExecutionResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = result.StandardOutput,
                StandardError = result.StandardError
            };
        }
    }
}
