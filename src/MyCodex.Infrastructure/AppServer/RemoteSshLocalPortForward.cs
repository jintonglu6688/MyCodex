using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Processes;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal interface IRemoteSshPortForwardProcess : IDisposable
    {
        bool Start();

        bool WaitForExit(int milliseconds);

        bool HasExited { get; }

        void Kill();
    }

    internal sealed class RemoteSshPortForwardProcess : IRemoteSshPortForwardProcess
    {
        private readonly Process _process;

        public RemoteSshPortForwardProcess(ProcessStartInfo startInfo)
        {
            _process = new Process { StartInfo = startInfo };
        }

        public bool Start()
        {
            return _process.Start();
        }

        public bool WaitForExit(int milliseconds)
        {
            return _process.WaitForExit(milliseconds);
        }

        public bool HasExited
        {
            get { return _process.HasExited; }
        }

        public void Kill()
        {
            MyCodex.Infrastructure.Processes.ProcessTreeTerminator.TryKill(_process);
        }

        public void Dispose()
        {
            _process.Dispose();
        }
    }

    public sealed class RemoteSshLocalPortForward : IDisposable
    {
        private readonly IRemoteSshPortForwardProcess _process;

        private RemoteSshLocalPortForward(IRemoteSshPortForwardProcess process)
        {
            _process = process;
        }

        public static RemoteSshLocalPortForward Start(RemoteMacSshConfigSnapshot config, int port)
        {
            return Start(
                config,
                port,
                startInfo => new RemoteSshPortForwardProcess(startInfo),
                IsLocalPortListening,
                milliseconds => Thread.Sleep(milliseconds));
        }

        internal static RemoteSshLocalPortForward Start(
            RemoteMacSshConfigSnapshot config,
            int port,
            Func<ProcessStartInfo, IRemoteSshPortForwardProcess> processFactory,
            Func<int, bool> isLocalPortListening,
            Action<int> sleep)
        {
            config = config ?? new RemoteMacSshConfigSnapshot();
            processFactory = processFactory ?? (startInfo => new RemoteSshPortForwardProcess(startInfo));
            isLocalPortListening = isLocalPortListening ?? IsLocalPortListening;
            sleep = sleep ?? (milliseconds => Thread.Sleep(milliseconds));
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
                    ? RemoteSshCommandLine.BuildPlinkLocalForwardArguments(config, port, port, passwordFilePath)
                    : RemoteSshCommandLine.BuildLocalForwardArguments(config, port, port),
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = processFactory(startInfo);
            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        "RemoteSshLocalPortForward.Error.StartFailed",
                        "Unable to start SSH port forwarding."));
                }

                if (process.WaitForExit(500))
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        "RemoteSshLocalPortForward.Error.StartFailed",
                        "Unable to start SSH port forwarding."));
                }

                WaitForLocalForward(port, isLocalPortListening, sleep);
                RemoteSshPasswordFile.TryDelete(passwordFilePath);
                passwordFilePath = null;
            }
            catch (Win32Exception ex)
            {
                KillAndDispose(process);
                throw new InvalidOperationException(SharedLocalization.Text(
                    "RemoteSshLocalPortForward.Error.StartFailed",
                    "Unable to start SSH port forwarding."), ex);
            }
            catch
            {
                KillAndDispose(process);
                throw;
            }
            finally
            {
                RemoteSshPasswordFile.TryDelete(passwordFilePath);
            }

            return new RemoteSshLocalPortForward(process);
        }

        private static void WaitForLocalForward(int port, Func<int, bool> isLocalPortListening, Action<int> sleep)
        {
            // About five seconds of 100 ms waits; counting attempts keeps an injected sleep from spinning on the clock.
            for (var attempt = 0; attempt < 50; attempt++)
            {
                if (isLocalPortListening(port))
                {
                    return;
                }

                sleep(100);
            }

            throw new InvalidOperationException(SharedLocalization.Text(
                "RemoteSshLocalPortForward.Error.StartFailed",
                "Unable to start SSH port forwarding."));
        }

        private static void KillAndDispose(IRemoteSshPortForwardProcess process)
        {
            if (process == null)
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

            process.Dispose();
        }

        private static bool IsLocalPortListening(int port)
        {
            try
            {
                var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                foreach (var listener in listeners)
                {
                    if (listener != null && listener.Port == port)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public void Dispose()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill();
                }
            }
            catch
            {
            }

            if (_process != null)
            {
                _process.Dispose();
            }
        }
    }
}
