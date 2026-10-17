using System;
using System.Diagnostics;
using System.Threading;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Processes;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ProcessExecutionRunnerTests
    {
        [Test]
        public void Run_WritesUtf8InputAndClosesItWithoutPuttingItInArguments()
        {
            var startInfo = BuildPowerShellStartInfo("[Console]::InputEncoding=[Text.Encoding]::UTF8; [Console]::OutputEncoding=[Text.Encoding]::UTF8; [Console]::Out.Write([Console]::In.ReadToEnd())");
            startInfo.RedirectStandardInput = true;
            startInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            var input = "测试凭据-" + new string('x', 200000);
            var result = ProcessExecutionRunner.Run(new ProcessExecutionRequest { StartInfo = startInfo, StandardInput = input });
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StandardOutput, Is.EqualTo(input));
            Assert.That(startInfo.Arguments, Does.Not.Contain("测试凭据"));
        }

        [Test]
        public void Run_PreservesFailureWhenChildExitsBeforeReadingInput()
        {
            var startInfo = BuildPowerShellStartInfo("[Console]::Error.Write('backend_install_busy'); exit 15");
            startInfo.RedirectStandardInput = true;
            var result = ProcessExecutionRunner.Run(new ProcessExecutionRequest
            {
                StartInfo = startInfo,
                StandardInput = new string('x', 8000000)
            });

            Assert.That(result.ExitCode, Is.EqualTo(15));
            Assert.That(result.StandardError, Is.EqualTo("backend_install_busy"));
        }

        [Test]
        public void Run_DoesNotReportSuccessWhenInputWasNotDelivered()
        {
            var startInfo = BuildPowerShellStartInfo("exit 0");
            startInfo.RedirectStandardInput = true;
            Assert.Throws<System.IO.IOException>(() => ProcessExecutionRunner.Run(new ProcessExecutionRequest
            {
                StartInfo = startInfo,
                StandardInput = new string('x', 8000000)
            }));
        }

        [Test]
        public void Run_CapturesLargeStdoutAndStderrWithoutBlocking()
        {
            var result = MyCodex.Infrastructure.Processes.ProcessExecutionRunner.Run(new MyCodex.Infrastructure.Processes.ProcessExecutionRequest
            {
                StartInfo = BuildPowerShellStartInfo("$s='x' * 200000; [Console]::Out.Write($s); [Console]::Error.Write($s)"),
                TimeoutMilliseconds = 15000,
                TimeoutMessage = "process timed out"
            });

            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.StandardOutput.Length, Is.EqualTo(200000));
            Assert.That(result.StandardError.Length, Is.EqualTo(200000));
        }

        [Test]
        public void Run_CancellationKillsProcessPromptly()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.CancelAfter(250);
                var started = DateTime.UtcNow;

                Assert.Throws<OperationCanceledException>(() => MyCodex.Infrastructure.Processes.ProcessExecutionRunner.Run(new MyCodex.Infrastructure.Processes.ProcessExecutionRequest
                {
                    StartInfo = BuildPowerShellStartInfo("Start-Sleep -Seconds 10"),
                    TimeoutMilliseconds = 15000,
                    TimeoutMessage = "process timed out",
                    CancellationToken = cancellation.Token
                }));

                Assert.That((DateTime.UtcNow - started).TotalSeconds, Is.LessThan(5));
            }
        }

        [Test]
        public void Run_CancellationKillsChildProcessTree()
        {
            int childProcessId = 0;
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    Assert.Throws<OperationCanceledException>(() => MyCodex.Infrastructure.Processes.ProcessExecutionRunner.Run(new MyCodex.Infrastructure.Processes.ProcessExecutionRequest
                    {
                        StartInfo = BuildPowerShellStartInfo(
                            "$child = Start-Process -FilePath powershell.exe -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-Command','Start-Sleep -Seconds 30' -WindowStyle Hidden -PassThru; " +
                            "[Console]::Out.WriteLine($child.Id); " +
                            "Start-Sleep -Seconds 30"),
                        TimeoutMilliseconds = 15000,
                        TimeoutMessage = "process timed out",
                        CancellationToken = cancellation.Token,
                        OutputReceived = (text, isError) =>
                        {
                            var parsed = 0;
                            if (!isError &&
                                childProcessId == 0 &&
                                int.TryParse((text ?? string.Empty).Trim(), out parsed))
                            {
                                childProcessId = parsed;
                                cancellation.Cancel();
                            }
                        }
                    }));

                    Assert.That(childProcessId, Is.GreaterThan(0));
                    Assert.That(WaitForProcessExit(childProcessId, 5000), Is.True);
                }
                finally
                {
                    TryKillProcess(childProcessId);
                }
            }
        }

        [Test]
        public void Run_AppliesOutputTransformToStdoutAndStderr()
        {
            var result = MyCodex.Infrastructure.Processes.ProcessExecutionRunner.Run(new MyCodex.Infrastructure.Processes.ProcessExecutionRequest
            {
                StartInfo = BuildPowerShellStartInfo("[Console]::Out.Write('ghp_SECRET'); [Console]::Error.Write('https://user:pass@example.com/repo.git')"),
                TimeoutMilliseconds = 15000,
                TimeoutMessage = "process timed out",
                OutputTransform = GitOutputSanitizer.RedactSensitiveText
            });

            Assert.That(result.StandardOutput, Is.EqualTo("***"));
            Assert.That(result.StandardError, Is.EqualTo("https://***:***@example.com/repo.git"));
        }

        [Test]
        public void Run_ReportsOutputBeforeProcessExits()
        {
            var stopwatch = Stopwatch.StartNew();
            long firstOutputMilliseconds = -1;

            var result = MyCodex.Infrastructure.Processes.ProcessExecutionRunner.Run(new MyCodex.Infrastructure.Processes.ProcessExecutionRequest
            {
                StartInfo = BuildPowerShellStartInfo("Write-Output 'first'; Start-Sleep -Milliseconds 1200; Write-Output 'second'"),
                TimeoutMilliseconds = 15000,
                TimeoutMessage = "process timed out",
                OutputReceived = (text, isError) =>
                {
                    if (!isError && firstOutputMilliseconds < 0)
                    {
                        firstOutputMilliseconds = stopwatch.ElapsedMilliseconds;
                    }
                }
            });

            stopwatch.Stop();

            Assert.That(result.ExitCode, Is.EqualTo(0));
            Assert.That(result.StandardOutput, Does.Contain("first"));
            Assert.That(result.StandardOutput, Does.Contain("second"));
            Assert.That(firstOutputMilliseconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(firstOutputMilliseconds, Is.LessThan(stopwatch.ElapsedMilliseconds - 500));
        }

        private static ProcessStartInfo BuildPowerShellStartInfo(string command)
        {
            return new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + GitCommandLineBuilder.QuoteArgument(command),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
        }

        private static bool WaitForProcessExit(int processId, int timeoutMilliseconds)
        {
            if (processId <= 0)
            {
                return false;
            }

            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    return process.WaitForExit(timeoutMilliseconds);
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private static void TryKillProcess(int processId)
        {
            if (processId <= 0)
            {
                return;
            }

            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
            }
            catch
            {
            }
        }
    }
}
