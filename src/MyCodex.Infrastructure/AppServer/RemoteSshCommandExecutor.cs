using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Processes;

namespace MyCodex.Infrastructure.AppServer
{
    internal interface IRemoteSshCommandExecutor
    {
        Task<RemoteSshCommandExecutionResult> ExecuteWithInputAsync(
            RemoteMacSshConfigSnapshot config, string script, string operationName,
            int timeoutMilliseconds, CancellationToken cancellationToken, string standardInput,
            params string[] arguments);

        Task<RemoteSshCommandExecutionResult> ExecuteAsync(
            RemoteMacSshConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            params string[] arguments);

        Task<RemoteSshCommandExecutionResult> ExecuteAsync(
            RemoteMacSshConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            CancellationToken cancellationToken,
            params string[] arguments);
    }

    internal interface IRemoteSshStreamingCommandExecutor
    {
        Task<RemoteSshCommandExecutionResult> ExecuteStreamingAsync(
            RemoteMacSshConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            CancellationToken cancellationToken,
            Action<string, bool> outputLineReceived,
            params string[] arguments);
    }

    internal sealed class RemoteSshCommandExecutionResult
    {
        public int ExitCode { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }
    }

    internal sealed class RemoteSshCommandExecutor : IRemoteSshCommandExecutor, IRemoteSshStreamingCommandExecutor
    {
        public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
            RemoteMacSshConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            params string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new ArgumentException("Remote script cannot be empty.", nameof(script));
            }

            return Task.Run(() => Execute(config, script, operationName, timeoutMilliseconds, default, null, arguments), default);
        }

        public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
            RemoteMacSshConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new ArgumentException("Remote script cannot be empty.", nameof(script));
            }

            return Task.Run(
                () => Execute(config, script, operationName, timeoutMilliseconds, cancellationToken, null, arguments),
                cancellationToken);
        }

        public Task<RemoteSshCommandExecutionResult> ExecuteWithInputAsync(
            RemoteMacSshConfigSnapshot config, string script, string operationName,
            int timeoutMilliseconds, CancellationToken cancellationToken, string standardInput,
            params string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(script) || standardInput == null)
            {
                throw new ArgumentException("Remote script and standard input are required.");
            }
            return Task.Run(() => Execute(config, script, operationName, timeoutMilliseconds,
                cancellationToken, standardInput, arguments), cancellationToken);
        }

        public Task<RemoteSshCommandExecutionResult> ExecuteStreamingAsync(
            RemoteMacSshConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            CancellationToken cancellationToken,
            Action<string, bool> outputLineReceived,
            params string[] arguments)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                throw new ArgumentException("Remote script cannot be empty.", nameof(script));
            }

            return Task.Run(
                () => ExecuteStreaming(config, script, operationName, timeoutMilliseconds, cancellationToken, outputLineReceived, arguments),
                cancellationToken);
        }

        private static RemoteSshCommandExecutionResult Execute(
            RemoteMacSshConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            CancellationToken cancellationToken,
            string standardInput,
            string[] arguments)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var remoteCommand = BuildRemoteShellCommand(config, script, operationName, arguments);
            string passwordFilePath = null;
            var usePlink = config.AuthenticationMode == RemoteSshAuthenticationMode.Password;
            var plinkPath = usePlink ? PlinkExecutableResolver.Resolve(config.PlinkExecutablePath) : null;
            if (usePlink)
            {
                passwordFilePath = RemoteSshPasswordFile.Create(RemoteSshPasswordProtector.Unprotect(config.ProtectedPassword));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = usePlink ? plinkPath : "ssh",
                Arguments = usePlink
                    ? RemoteSshCommandLine.BuildPlinkArguments(config, remoteCommand, passwordFilePath)
                    : RemoteSshCommandLine.BuildSshArguments(config, remoteCommand),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = standardInput != null,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            try
            {
                var execution = MyCodex.Infrastructure.Processes.ProcessExecutionRunner.Run(new MyCodex.Infrastructure.Processes.ProcessExecutionRequest
                {
                    StartInfo = startInfo,
                    TimeoutMilliseconds = timeoutMilliseconds > 0 ? timeoutMilliseconds : 10000,
                    TimeoutMessage = "Remote SSH command timed out.",
                    CancellationToken = cancellationToken,
                    StandardInput = standardInput,
                    OutputTransform = GitOutputSanitizer.RedactSensitiveText
                });
                return new RemoteSshCommandExecutionResult
                {
                    ExitCode = execution.ExitCode,
                    StandardOutput = execution.StandardOutput,
                    StandardError = execution.StandardError
                };
            }
            finally
            {
                if (passwordFilePath != null)
                {
                    RemoteSshPasswordFile.TryDelete(passwordFilePath);
                }
            }
        }

        private static RemoteSshCommandExecutionResult ExecuteStreaming(
            RemoteMacSshConfigSnapshot config,
            string script,
            string operationName,
            int timeoutMilliseconds,
            CancellationToken cancellationToken,
            Action<string, bool> outputLineReceived,
            string[] arguments)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            var remoteCommand = BuildRemoteShellCommand(config, script, operationName, arguments);
            string passwordFilePath = null;
            var usePlink = config.AuthenticationMode == RemoteSshAuthenticationMode.Password;
            var plinkPath = usePlink ? PlinkExecutableResolver.Resolve(config.PlinkExecutablePath) : null;
            if (usePlink)
            {
                passwordFilePath = RemoteSshPasswordFile.Create(RemoteSshPasswordProtector.Unprotect(config.ProtectedPassword));
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = usePlink ? plinkPath : "ssh",
                Arguments = usePlink
                    ? RemoteSshCommandLine.BuildPlinkArguments(config, remoteCommand, passwordFilePath)
                    : RemoteSshCommandLine.BuildSshArguments(config, remoteCommand),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            try
            {
                using (var process = new Process())
                {
                    var output = new StringBuilder();
                    var error = new StringBuilder();
                    var syncRoot = new object();
                    process.StartInfo = startInfo;
                    process.OutputDataReceived += (sender, e) =>
                    {
                        AppendStreamingLine(output, syncRoot, e.Data);
                        ReportStreamingLine(outputLineReceived, e.Data, false);
                    };
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        AppendStreamingLine(error, syncRoot, e.Data);
                        ReportStreamingLine(outputLineReceived, e.Data, true);
                    };

                    cancellationToken.ThrowIfCancellationRequested();
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    using (cancellationToken.Register(() => TryKill(process)))
                    {
                        var timeout = timeoutMilliseconds > 0 ? timeoutMilliseconds : 10000;
                        var stopwatch = Stopwatch.StartNew();
                        while (!process.WaitForExit(200))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (stopwatch.ElapsedMilliseconds <= timeout)
                            {
                                continue;
                            }

                            TryKill(process);
                            WaitForKilledProcess(process, 5000);
                            throw new TimeoutException("Remote SSH command timed out.");
                        }

                        process.WaitForExit();
                        cancellationToken.ThrowIfCancellationRequested();
                        lock (syncRoot)
                        {
                            return new RemoteSshCommandExecutionResult
                            {
                                ExitCode = process.ExitCode,
                                StandardOutput = output.ToString(),
                                StandardError = error.ToString()
                            };
                        }
                    }
                }
            }
            finally
            {
                if (passwordFilePath != null)
                {
                    RemoteSshPasswordFile.TryDelete(passwordFilePath);
                }
            }
        }

        private static void AppendStreamingLine(StringBuilder builder, object syncRoot, string text)
        {
            if (builder == null || text == null)
            {
                return;
            }

            lock (syncRoot)
            {
                builder.AppendLine(GitOutputSanitizer.RedactSensitiveText(text));
            }
        }

        private static void ReportStreamingLine(Action<string, bool> outputLineReceived, string text, bool isError)
        {
            if (outputLineReceived == null || text == null)
            {
                return;
            }

            outputLineReceived(GitOutputSanitizer.RedactSensitiveText(text), isError);
        }

        private static void WaitForKilledProcess(Process process, int timeoutMilliseconds)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.WaitForExit(timeoutMilliseconds);
                }
            }
            catch
            {
            }
        }

        private static void TryKill(Process process)
        {
            MyCodex.Infrastructure.Processes.ProcessTreeTerminator.TryKill(process);
        }

        private static string BuildRemoteShellCommand(
            RemoteMacSshConfigSnapshot config,
            string script,
            string operationName,
            string[] arguments)
        {
            var builder = new StringBuilder();
            builder.Append(RemoteSshCommandLine.NormalizeShellCommand(config != null ? config.ShellCommand : null));
            builder.Append(" -lc ");
            builder.Append(QuoteRemoteShellArgument(script));
            builder.Append(' ');
            builder.Append(QuoteRemoteShellArgument(string.IsNullOrWhiteSpace(operationName) ? "mycodex-remote" : operationName.Trim()));
            if (arguments != null)
            {
                for (var i = 0; i < arguments.Length; i++)
                {
                    builder.Append(' ');
                    builder.Append(QuoteRemoteShellArgument(arguments[i] ?? string.Empty));
                }
            }

            return builder.ToString();
        }

        private static string QuoteRemoteShellArgument(string value)
        {
            return "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";
        }

    }
}
