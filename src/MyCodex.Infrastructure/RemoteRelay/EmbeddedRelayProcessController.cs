using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class EmbeddedRelayProcessIdentity
    {
        public EmbeddedRelayProcessIdentity(int processId, string executablePath, long startTimeUtcTicks)
        {
            ProcessId = processId;
            ExecutablePath = executablePath;
            StartTimeUtcTicks = startTimeUtcTicks;
        }

        public int ProcessId { get; private set; }

        public string ExecutablePath { get; private set; }

        public long StartTimeUtcTicks { get; private set; }
    }

    internal interface IEmbeddedRelayProcessAccess
    {
        Process Resolve(int processId);

        EmbeddedRelayProcessIdentity Inspect(Process process);

        void Stop(Process process);
    }

    internal sealed class EmbeddedRelayProcessAccess : IEmbeddedRelayProcessAccess
    {
        public Process Resolve(int processId)
        {
            return Process.GetProcessById(processId);
        }

        public EmbeddedRelayProcessIdentity Inspect(Process process)
        {
            if (process.HasExited)
            {
                return null;
            }

            var mainModule = process.MainModule;
            if (mainModule == null || string.IsNullOrWhiteSpace(mainModule.FileName))
            {
                return null;
            }

            return new EmbeddedRelayProcessIdentity(
                process.Id,
                Path.GetFullPath(mainModule.FileName.Trim().Trim('"')),
                process.StartTime.ToUniversalTime().Ticks);
        }

        public void Stop(Process process)
        {
            if (process.HasExited)
            {
                return;
            }

            try
            {
                process.CloseMainWindow();
                if (process.WaitForExit(2000))
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(2000);
            }
        }
    }

    internal interface IEmbeddedRelayProcessController
    {
        EmbeddedRelayProcessIdentity Start(
            string relayExecutablePath,
            string configPath,
            string workingDirectory,
            string stdoutLogPath,
            string stderrLogPath);

        void Stop(EmbeddedRelayProcessIdentity identity);

        bool IsRunning(EmbeddedRelayProcessIdentity identity);
    }

    public sealed class EmbeddedRelayProcessController : IEmbeddedRelayProcessController
    {
        private readonly ConcurrentDictionary<int, Process> _startedProcesses = new ConcurrentDictionary<int, Process>();
        private readonly IEmbeddedRelayProcessAccess _processAccess;

        public EmbeddedRelayProcessController()
            : this(new EmbeddedRelayProcessAccess())
        {
        }

        internal EmbeddedRelayProcessController(IEmbeddedRelayProcessAccess processAccess)
        {
            _processAccess = processAccess ?? throw new ArgumentNullException(nameof(processAccess));
        }

        public EmbeddedRelayProcessIdentity Start(
            string relayExecutablePath,
            string configPath,
            string workingDirectory,
            string stdoutLogPath,
            string stderrLogPath)
        {
            Directory.CreateDirectory(workingDirectory);
            var stdoutWriter = CreateLogWriter(stdoutLogPath);
            var stderrWriter = CreateLogWriter(stderrLogPath);
            var processId = 0;
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = relayExecutablePath,
                    Arguments = "serve --config \"" + (configPath ?? string.Empty).Replace("\"", "\\\"") + "\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                },
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (sender, args) => WriteLine(stdoutWriter, args.Data);
            process.ErrorDataReceived += (sender, args) => WriteLine(stderrWriter, args.Data);
            process.Exited += (sender, args) =>
            {
                _startedProcesses.TryRemove(processId, out _);
                stdoutWriter.Dispose();
                stderrWriter.Dispose();
                process.Dispose();
            };

            try
            {
                process.Start();
                processId = process.Id;
                var identity = CaptureStartedProcess(process, processId, relayExecutablePath, TimeSpan.FromSeconds(3));
                if (!IsComplete(identity) || identity.ProcessId != processId)
                {
                    throw new InvalidOperationException("Unable to capture Embedded Relay process identity.");
                }

                _startedProcesses[processId] = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                return identity;
            }
            catch
            {
                if (processId > 0)
                {
                    try
                    {
                        _processAccess.Stop(process);
                    }
                    catch
                    {
                    }
                }

                _startedProcesses.TryRemove(processId, out _);
                stdoutWriter.Dispose();
                stderrWriter.Dispose();
                process.Dispose();
                throw;
            }
        }

        public void Stop(EmbeddedRelayProcessIdentity identity)
        {
            if (!IsComplete(identity))
            {
                return;
            }

            Process process;
            bool disposeProcess;
            if (!TryResolveProcess(identity.ProcessId, out process, out disposeProcess))
            {
                return;
            }

            try
            {
                var actual = TryInspectProcess(process);
                if (!IdentitiesMatch(identity, actual))
                {
                    return;
                }

                try
                {
                    _processAccess.Stop(process);
                }
                catch
                {
                }
            }
            finally
            {
                _startedProcesses.TryRemove(identity.ProcessId, out _);
                if (disposeProcess)
                {
                    process.Dispose();
                }
            }
        }

        public bool IsRunning(EmbeddedRelayProcessIdentity identity)
        {
            if (!IsComplete(identity))
            {
                return false;
            }

            Process process;
            bool disposeProcess;
            if (!TryResolveProcess(identity.ProcessId, out process, out disposeProcess))
            {
                return false;
            }

            try
            {
                return IdentitiesMatch(identity, TryInspectProcess(process));
            }
            finally
            {
                if (disposeProcess)
                {
                    process.Dispose();
                }
            }
        }

        private bool TryResolveProcess(int processId, out Process process, out bool disposeProcess)
        {
            disposeProcess = false;
            if (_startedProcesses.TryGetValue(processId, out process))
            {
                return true;
            }

            try
            {
                process = _processAccess.Resolve(processId);
                disposeProcess = process != null;
                return process != null;
            }
            catch
            {
                process = null;
                return false;
            }
        }

        private EmbeddedRelayProcessIdentity TryInspectProcess(Process process)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var identity = _processAccess.Inspect(process);
                    if (identity != null)
                    {
                        return identity;
                    }
                }
                catch
                {
                }

                if (attempt < 2)
                {
                    Thread.Sleep(50);
                }
            }

            return null;
        }

        internal EmbeddedRelayProcessIdentity CaptureStartedProcess(
            Process process, int processId, string expectedExecutablePath, TimeSpan timeout)
        {
            var expectedPath = Path.GetFullPath(expectedExecutablePath);
            var elapsed = Stopwatch.StartNew();
            do
            {
                try
                {
                    // MainModule can initially be ntdll; discard Process's cached module list before retrying.
                    process.Refresh();
                    var identity = _processAccess.Inspect(process);
                    if (IsComplete(identity) && identity.ProcessId == processId &&
                        string.Equals(Path.GetFullPath(identity.ExecutablePath), expectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return identity;
                    }
                }
                catch
                {
                    // Startup inspection is bounded; a failure never grants ownership of a different image.
                }

                Thread.Sleep(50);
            }
            while (elapsed.Elapsed < timeout);

            throw new InvalidOperationException("Unable to verify the newly started Relay process executable and creation time.");
        }

        private static bool IdentitiesMatch(EmbeddedRelayProcessIdentity expected, EmbeddedRelayProcessIdentity actual)
        {
            return IsComplete(expected) &&
                   IsComplete(actual) &&
                   expected.ProcessId == actual.ProcessId &&
                   expected.StartTimeUtcTicks == actual.StartTimeUtcTicks &&
                   string.Equals(expected.ExecutablePath, actual.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsComplete(EmbeddedRelayProcessIdentity identity)
        {
            return identity != null &&
                   identity.ProcessId > 0 &&
                   !string.IsNullOrWhiteSpace(identity.ExecutablePath) &&
                   identity.StartTimeUtcTicks > 0;
        }

        private static StreamWriter CreateLogWriter(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        private static void WriteLine(TextWriter writer, string data)
        {
            if (data == null)
            {
                return;
            }

            lock (writer)
            {
                try
                {
                    writer.WriteLine(data);
                }
                catch (ObjectDisposedException)
                {
                    // Process exit can race the final redirected output callback.
                }
                catch (IOException)
                {
                    // Keep draining the pipe when the log volume is unavailable.
                }
            }
        }
    }
}
