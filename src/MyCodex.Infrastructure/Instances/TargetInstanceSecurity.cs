using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MyCodex.Infrastructure.Instances
{
    internal static class TargetInstanceSecurity
    {
        internal static string CurrentSid
        {
            get { using (var identity = WindowsIdentity.GetCurrent()) return identity.User.Value; }
        }

        internal static string Route(string channelId, string targetId)
        {
            if (string.IsNullOrWhiteSpace(channelId) || channelId.Length > 64 ||
                string.IsNullOrWhiteSpace(targetId) || targetId.Length > 128)
            {
                throw new ArgumentException("A bounded channel and stable target ID are required.");
            }
            using (var hash = SHA256.Create())
            {
                var data = Encoding.UTF8.GetBytes(CurrentSid + "\n" + channelId.ToUpperInvariant() + "\n" + targetId.ToLowerInvariant());
                return "MyCodex.Target." + BitConverter.ToString(hash.ComputeHash(data)).Replace("-", string.Empty);
            }
        }

        internal static TargetInstanceRecord Capture(string channelId, string targetId)
        {
            using (var process = Process.GetCurrentProcess())
            {
                return new TargetInstanceRecord
                {
                    ProcessId = process.Id,
                    ExecutablePath = Path.GetFullPath(process.MainModule.FileName),
                    StartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks,
                    UserSid = CurrentSid,
                    ChannelId = channelId,
                    TargetId = targetId,
                    InstanceId = Guid.NewGuid().ToString("N")
                };
            }
        }

        // null means inspection failed, never proof that an owner has exited.
        internal static bool? IsAlive(TargetInstanceRecord record)
        {
            if (record == null || record.ProcessId <= 0 || record.StartTimeUtcTicks <= 0 ||
                string.IsNullOrWhiteSpace(record.ExecutablePath) || record.UserSid != CurrentSid)
            {
                return null;
            }
            try
            {
                using (var process = Process.GetProcessById(record.ProcessId))
                {
                    if (process.HasExited) return false;
                    return process.StartTime.ToUniversalTime().Ticks == record.StartTimeUtcTicks &&
                        string.Equals(Path.GetFullPath(process.MainModule.FileName), record.ExecutablePath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(ProcessSid(process), record.UserSid, StringComparison.Ordinal);
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return null; }
            catch (System.ComponentModel.Win32Exception) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal static bool IsSameInstallation(TargetInstanceRecord record, bool allowOtherEdition = false)
        {
            using (var process = Process.GetCurrentProcess())
            {
                return IsSameInstallationPath(process.MainModule.FileName, record.ExecutablePath, allowOtherEdition);
            }
        }

        internal static bool IsSameInstallationPath(string caller, string owner, bool allowOtherEdition)
        {
            caller = Path.GetFullPath(caller);
            owner = Path.GetFullPath(owner);
            if (string.Equals(caller, owner, StringComparison.OrdinalIgnoreCase)) return true;
            return allowOtherEdition && IsClientExecutable(caller) && IsClientExecutable(owner) &&
                string.Equals(Path.GetDirectoryName(caller), Path.GetDirectoryName(owner), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsClientExecutable(string path)
        {
            var name = Path.GetFileName(path);
            return string.Equals(name, "MyCodex.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "MyCodex.Dev.exe", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool VerifyPeer(PipeStream pipe, TargetInstanceRecord record, bool server, bool allowOtherEdition = false)
        {
            uint processId;
            var found = server
                ? GetNamedPipeClientProcessId(pipe.SafePipeHandle, out processId)
                : GetNamedPipeServerProcessId(pipe.SafePipeHandle, out processId);
            return found && record != null && processId == record.ProcessId &&
                IsSameInstallation(record, allowOtherEdition) && IsAlive(record) == true;
        }

        internal static void AllowActivation(int processId)
        {
            AllowSetForegroundWindow(processId);
        }

        internal static MutexSecurity MutexSecurity()
        {
            var security = new MutexSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(CurrentSid), MutexRights.FullControl, AccessControlType.Allow));
            return security;
        }

        internal static NamedPipeServerStream CreatePipe(string route)
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(CurrentSid), PipeAccessRights.FullControl, AccessControlType.Allow));
            return new NamedPipeServerStream(route, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous, 4096, 4096, security);
        }

        private static string ProcessSid(Process process)
        {
            IntPtr token;
            if (!OpenProcessToken(process.Handle, 8, out token))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            try
            {
                using (var identity = new WindowsIdentity(token)) return identity.User.Value;
            }
            finally { CloseHandle(token); }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);
    }
}
