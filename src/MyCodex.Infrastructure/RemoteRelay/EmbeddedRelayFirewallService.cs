using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class EmbeddedRelayFirewallService : IEmbeddedRelayFirewallService
    {
        internal const string RuleName = "MyCodex Embedded Relay (TCP-In)";
        private const int PrivateProfile = 2;
        private const int InboundDirection = 1;
        private const int AllowAction = 1;
        private const int TcpProtocol = 6;
        private const int CanceledExitCode = int.MinValue;
        private readonly Func<string> _executablePathResolver;
        private readonly Func<string, EmbeddedRelayFirewallStatus> _statusProvider;
        private readonly Func<ProcessStartInfo, CancellationToken, int> _elevatedProcessRunner;

        public EmbeddedRelayFirewallService()
            : this(
                () => new EmbeddedRelayBinaryLocator().Resolve(),
                InspectWindowsFirewall,
                RunElevatedProcess)
        {
        }

        internal EmbeddedRelayFirewallService(
            Func<string> executablePathResolver,
            Func<string, EmbeddedRelayFirewallStatus> statusProvider,
            Func<ProcessStartInfo, CancellationToken, int> elevatedProcessRunner)
        {
            _executablePathResolver = executablePathResolver ?? throw new ArgumentNullException(nameof(executablePathResolver));
            _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
            _elevatedProcessRunner = elevatedProcessRunner ?? throw new ArgumentNullException(nameof(elevatedProcessRunner));
        }

        public EmbeddedRelayFirewallStatus GetStatus()
        {
            try
            {
                return _statusProvider(ResolveExecutablePath());
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "[RemoteRelay] Windows Firewall status check failed. {0}",
                    RemoteRelayDiagnostics.ExceptionSummary(ex));
                return Unavailable(ex.Message, null);
            }
        }

        public Task<EmbeddedRelayFirewallAllowResult> AllowPrivateLocalSubnetAsync(
            CancellationToken cancellationToken)
        {
            return Task.Run(
                () => AllowPrivateLocalSubnet(cancellationToken),
                CancellationToken.None);
        }

        internal static ProcessStartInfo BuildAllowProcessStartInfo(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new ArgumentException("Relay executable path cannot be empty.", nameof(executablePath));
            }

            var normalizedExecutablePath = Path.GetFullPath(executablePath);
            var netshPath = System.IO.Path.Combine(Environment.SystemDirectory, "netsh.exe");
            return new ProcessStartInfo
            {
                FileName = netshPath,
                Arguments =
                    "advfirewall firewall add rule " +
                    "name=\"" + RuleName + "\" " +
                    "description=\"Allows MyCodex mobile devices on the private local subnet to reach the embedded Relay.\" " +
                    "dir=in action=allow enable=yes profile=private " +
                    "program=\"" + normalizedExecutablePath + "\" protocol=TCP " +
                    "remoteip=LocalSubnet",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Environment.SystemDirectory
            };
        }

        private EmbeddedRelayFirewallAllowResult AllowPrivateLocalSubnet(
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return CanceledResult();
            }

            var before = GetStatus();
            if (before.State == EmbeddedRelayFirewallState.Allowed)
            {
                return new EmbeddedRelayFirewallAllowResult
                {
                    Success = true,
                    Status = before
                };
            }

            if (before.State == EmbeddedRelayFirewallState.NonPrivateNetwork)
            {
                return new EmbeddedRelayFirewallAllowResult
                {
                    Status = before,
                    ErrorMessage = "The active Windows network profile is not Private."
                };
            }

            if (before.State == EmbeddedRelayFirewallState.Unavailable)
            {
                return new EmbeddedRelayFirewallAllowResult
                {
                    Status = before,
                    ErrorMessage = before.ErrorMessage
                };
            }

            Trace.TraceInformation(
                "[RemoteRelay] Windows Firewall allow requested. Direction=in; Protocol=TCP; Program={0}; RemoteAddress=LocalSubnet; Profile=Private",
                Path.GetFileName(before.ExecutablePath));
            try
            {
                var exitCode = _elevatedProcessRunner(
                    BuildAllowProcessStartInfo(before.ExecutablePath),
                    cancellationToken);
                if (exitCode == CanceledExitCode || cancellationToken.IsCancellationRequested)
                {
                    Trace.TraceWarning("[RemoteRelay] Windows Firewall allow canceled.");
                    return CanceledResult();
                }

                if (exitCode != 0)
                {
                    Trace.TraceWarning(
                        "[RemoteRelay] Windows Firewall allow failed. ExitCode={0}",
                        exitCode);
                    return new EmbeddedRelayFirewallAllowResult
                    {
                        Status = GetStatus(),
                        ErrorMessage = "Windows Firewall returned exit code " +
                            exitCode.ToString(CultureInfo.InvariantCulture) + "."
                    };
                }

                var after = GetStatus();
                if (after.State != EmbeddedRelayFirewallState.Allowed)
                {
                    Trace.TraceWarning(
                        "[RemoteRelay] Windows Firewall rule command completed but verification failed. State={0}",
                        after.State);
                    return new EmbeddedRelayFirewallAllowResult
                    {
                        Status = after,
                        ErrorMessage = "The Windows Firewall rule could not be verified."
                    };
                }

                Trace.TraceInformation(
                    "[RemoteRelay] Windows Firewall allow succeeded. Rule={0}; Program={1}",
                    RuleName,
                    Path.GetFileName(after.ExecutablePath));
                return new EmbeddedRelayFirewallAllowResult
                {
                    Success = true,
                    Status = after
                };
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Trace.TraceWarning("[RemoteRelay] Windows Firewall administrator prompt was canceled.");
                return CanceledResult();
            }
            catch (Exception ex)
            {
                Trace.TraceError(
                    "[RemoteRelay] Windows Firewall allow failed. {0}",
                    RemoteRelayDiagnostics.ExceptionSummary(ex));
                return new EmbeddedRelayFirewallAllowResult
                {
                    Status = GetStatusSafely(),
                    ErrorMessage = ex.Message
                };
            }
        }

        private static EmbeddedRelayFirewallAllowResult CanceledResult()
        {
            return new EmbeddedRelayFirewallAllowResult
            {
                Canceled = true,
                Status = new EmbeddedRelayFirewallStatus
                {
                    State = EmbeddedRelayFirewallState.Unknown
                }
            };
        }

        private EmbeddedRelayFirewallStatus GetStatusSafely()
        {
            try
            {
                return GetStatus();
            }
            catch
            {
                return new EmbeddedRelayFirewallStatus
                {
                    State = EmbeddedRelayFirewallState.Unavailable
                };
            }
        }

        private static int RunElevatedProcess(
            ProcessStartInfo startInfo,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return -1;
                }

                while (!process.WaitForExit(250))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return CanceledExitCode;
                    }
                }

                return process.ExitCode;
            }
        }

        private static EmbeddedRelayFirewallStatus InspectWindowsFirewall(string executablePath)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return Unavailable("Windows Firewall is available only on Windows.", executablePath);
            }

            object policy = null;
            object rules = null;
            try
            {
                var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (policyType == null)
                {
                    return Unavailable("Windows Firewall policy is unavailable.", executablePath);
                }

                policy = Activator.CreateInstance(policyType);
                var currentProfiles = Convert.ToInt32(
                    GetComProperty(policy, "CurrentProfileTypes"),
                    CultureInfo.InvariantCulture);
                rules = GetComProperty(policy, "Rules");
                var ruleAllowed = ContainsCompliantRule(rules, executablePath);
                if ((currentProfiles & PrivateProfile) == 0)
                {
                    return new EmbeddedRelayFirewallStatus
                    {
                        State = EmbeddedRelayFirewallState.NonPrivateNetwork,
                        ExecutablePath = executablePath
                    };
                }

                return new EmbeddedRelayFirewallStatus
                {
                    State = ruleAllowed
                        ? EmbeddedRelayFirewallState.Allowed
                        : EmbeddedRelayFirewallState.RuleMissing,
                    ExecutablePath = executablePath
                };
            }
            catch (Exception ex)
            {
                Trace.TraceWarning(
                    "[RemoteRelay] Windows Firewall status check failed. {0}",
                    RemoteRelayDiagnostics.ExceptionSummary(ex));
                return Unavailable(ex.Message, executablePath);
            }
            finally
            {
                ReleaseComObject(rules);
                ReleaseComObject(policy);
            }
        }

        private static bool ContainsCompliantRule(object rules, string executablePath)
        {
            var enumerable = rules as IEnumerable;
            if (enumerable == null)
            {
                return false;
            }

            foreach (var rule in enumerable)
            {
                try
                {
                    if (IsCompliantRule(rule, executablePath))
                    {
                        return true;
                    }
                }
                finally
                {
                    ReleaseComObject(rule);
                }
            }

            return false;
        }

        private static bool IsCompliantRule(object rule, string executablePath)
        {
            if (rule == null ||
                !string.Equals(
                    Convert.ToString(GetComProperty(rule, "Name"), CultureInfo.InvariantCulture),
                    RuleName,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var enabled = Convert.ToBoolean(GetComProperty(rule, "Enabled"), CultureInfo.InvariantCulture);
            var direction = Convert.ToInt32(GetComProperty(rule, "Direction"), CultureInfo.InvariantCulture);
            var action = Convert.ToInt32(GetComProperty(rule, "Action"), CultureInfo.InvariantCulture);
            var protocol = Convert.ToInt32(GetComProperty(rule, "Protocol"), CultureInfo.InvariantCulture);
            var profiles = Convert.ToInt32(GetComProperty(rule, "Profiles"), CultureInfo.InvariantCulture);
            var applicationName = Convert.ToString(GetComProperty(rule, "ApplicationName"), CultureInfo.InvariantCulture);
            var remoteAddresses = Convert.ToString(GetComProperty(rule, "RemoteAddresses"), CultureInfo.InvariantCulture);
            return enabled &&
                direction == InboundDirection &&
                action == AllowAction &&
                protocol == TcpProtocol &&
                (profiles & PrivateProfile) != 0 &&
                PathsEqual(applicationName, executablePath) &&
                ContainsCommaSeparatedToken(remoteAddresses, "LocalSubnet");
        }

        private static bool PathsEqual(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(first.Trim()),
                    Path.GetFullPath(second.Trim()),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsCommaSeparatedToken(string value, string expected)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (var token in value.Split(','))
            {
                if (string.Equals(token.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static object GetComProperty(object target, string name)
        {
            return target.GetType().InvokeMember(
                name,
                BindingFlags.GetProperty,
                null,
                target,
                null,
                CultureInfo.InvariantCulture);
        }

        private static EmbeddedRelayFirewallStatus Unavailable(
            string errorMessage,
            string executablePath)
        {
            return new EmbeddedRelayFirewallStatus
            {
                State = EmbeddedRelayFirewallState.Unavailable,
                ExecutablePath = executablePath,
                ErrorMessage = errorMessage
            };
        }

        private string ResolveExecutablePath()
        {
            var path = _executablePathResolver();
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new FileNotFoundException("Unable to locate mycodex-relay.exe.");
            }

            return Path.GetFullPath(path);
        }

        private static void ReleaseComObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value))
            {
                return;
            }

            try
            {
                Marshal.FinalReleaseComObject(value);
            }
            catch
            {
            }
        }
    }
}
