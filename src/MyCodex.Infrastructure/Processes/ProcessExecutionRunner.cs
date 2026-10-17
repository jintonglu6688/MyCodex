using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MyCodex.Infrastructure.Processes
{
    public static class ProcessExecutionRunner
    {
        public static ProcessExecutionResult Run(ProcessExecutionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (request.StartInfo == null)
            {
                throw new ArgumentException("Process start info cannot be empty.", nameof(request));
            }

            request.CancellationToken.ThrowIfCancellationRequested();
            if (request.StandardInput != null && !request.StartInfo.RedirectStandardInput)
            {
                throw new ArgumentException("Standard input must be redirected.", nameof(request));
            }
            using (var process = new Process())
            {
                process.StartInfo = request.StartInfo;
                process.Start();
                var syncRoot = new object();
                var stdoutBuilder = new StringBuilder();
                var stderrBuilder = new StringBuilder();
                using (request.CancellationToken.Register(() => TryKill(process)))
                {
                    var stdinTask = request.StandardInput != null
                        ? WriteInputAsync(process, request.StandardInput)
                        : Task.FromResult(0);
                    var stdoutTask = process.StartInfo.RedirectStandardOutput
                        ? ReadStreamAsync(process.StandardOutput, stdoutBuilder, syncRoot, false, request)
                        : Task.FromResult(string.Empty);
                    var stderrTask = process.StartInfo.RedirectStandardError
                        ? ReadStreamAsync(process.StandardError, stderrBuilder, syncRoot, true, request)
                        : Task.FromResult(string.Empty);
                    var timeout = request.TimeoutMilliseconds > 0 ? request.TimeoutMilliseconds : 15000;
                    if (!process.WaitForExit(timeout))
                    {
                        TryKill(process);
                        WaitForKilledProcess(process, request.KillWaitMilliseconds);
                        WaitForStreams(stdoutTask, stderrTask, request.KillWaitMilliseconds);
                        throw new TimeoutException(string.IsNullOrWhiteSpace(request.TimeoutMessage)
                            ? "Process command timed out."
                            : request.TimeoutMessage);
                    }

                    WaitForStreams(stdoutTask, stderrTask, request.KillWaitMilliseconds);
                    request.CancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        stdinTask.GetAwaiter().GetResult();
                    }
                    catch (IOException) when (process.ExitCode != 0)
                    {
                        // A child can reject the request before consuming stdin. Preserve its error.
                    }
                    return new ProcessExecutionResult
                    {
                        ExitCode = process.ExitCode,
                        StandardOutput = Transform(stdoutBuilder.ToString(), request.OutputTransform),
                        StandardError = Transform(stderrBuilder.ToString(), request.OutputTransform)
                    };
                }
            }
        }

        private static async Task WriteInputAsync(Process process, string text)
        {
            try
            {
                var bytes = new UTF8Encoding(false).GetBytes(text);
                await process.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                process.StandardInput.Close();
            }
        }

        private static async Task<string> ReadStreamAsync(
            System.IO.StreamReader reader,
            StringBuilder builder,
            object syncRoot,
            bool isError,
            ProcessExecutionRequest request)
        {
            var buffer = new char[4096];
            while (true)
            {
                var count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (count <= 0)
                {
                    return string.Empty;
                }

                var text = new string(buffer, 0, count);
                lock (syncRoot)
                {
                    builder.Append(text);
                }

                ReportOutput(request, text, isError);
            }
        }

        private static void ReportOutput(ProcessExecutionRequest request, string text, bool isError)
        {
            if (request == null || request.OutputReceived == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                request.OutputReceived(Transform(text, request.OutputTransform), isError);
            }
            catch
            {
            }
        }

        private static string Transform(string value, Func<string, string> transform)
        {
            value = value ?? string.Empty;
            return transform == null ? value : transform(value) ?? string.Empty;
        }

        private static void WaitForStreams(Task stdoutTask, Task stderrTask, int killWaitMilliseconds)
        {
            var timeout = killWaitMilliseconds > 0 ? killWaitMilliseconds : 5000;
            if (!Task.WaitAll(new[] { stdoutTask, stderrTask }, timeout))
            {
                throw new TimeoutException("Process output reading timed out.");
            }
        }

        private static void WaitForKilledProcess(Process process, int killWaitMilliseconds)
        {
            try
            {
                if (process != null && !process.HasExited)
                {
                    process.WaitForExit(killWaitMilliseconds > 0 ? killWaitMilliseconds : 5000);
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
    }
}
