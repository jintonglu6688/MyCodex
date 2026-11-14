using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using MyCodex.Application.Interfaces;
using Target = MyCodex.Infrastructure.AuthenticationCenter.AuthenticationInstallationStateStore.Target;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal static class AuthenticationInstallationProcesses
    {
        internal static List<Target> Find(string root)
        {
            var executable = Path.Combine(root, "runtime", "auth-core", "mycodex-auth-host.exe");
            var legacy = Path.Combine(root, "runtime", "auth-center", "mycodex-auth-host.exe");
            var targets = new List<Target>();
            using (var identity = WindowsIdentity.GetCurrent())
            using (var searcher = new ManagementObjectSearcher("SELECT Handle, ProcessId, ExecutablePath, CommandLine FROM Win32_Process WHERE Name = 'mycodex-auth-host.exe'"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject candidate in results)
                using (candidate)
                {
                    var id = Convert.ToInt32(candidate["ProcessId"]);
                    var path = candidate["ExecutablePath"] as string;
                    if (string.IsNullOrWhiteSpace(path)) path = ReadProcessPath(id);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        try { using (Process.GetProcessById(id)) { } }
                        catch (ArgumentException) { continue; }
                    }
                    if (!OwnsPath(path, executable, legacy, () => ImageLocked(executable) || ImageLocked(legacy))) continue;
                    using (var owner = candidate.InvokeMethod("GetOwnerSid", null, null))
                    {
                        if (Convert.ToInt32(owner["ReturnValue"]) != 0 || !Equals(owner["Sid"], identity.User.Value))
                            throw new AuthenticationCenterException("authentication_update_wrong_user");
                    }
                    var target = Parse(candidate["CommandLine"] as string);
                    target.processId = id;
                    targets.Add(target);
                }
            }
            return targets;
        }

        internal static bool OwnsPath(string path, string executable, string legacy, Func<bool> imageLocked)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                if (imageLocked()) throw new AuthenticationCenterException("authentication_process_identity_unavailable");
                return false;
            }
            if (AuthenticationInstallationStateStore.SamePath(path, legacy))
                throw new AuthenticationCenterException("authentication_legacy_backend_running");
            return AuthenticationInstallationStateStore.SamePath(path, executable);
        }

        internal static Target Parse(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine)) throw new AuthenticationCenterException("authentication_update_unknown_process");
            var pointer = CommandLineToArgvW(commandLine, out var count);
            if (pointer == IntPtr.Zero) throw new AuthenticationCenterException("authentication_update_unknown_process");
            try
            {
                var parts = new string[count];
                for (var i = 0; i < count; i++) parts[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size));
                if (count != 6 || parts[1] != "serve") throw new AuthenticationCenterException("authentication_update_unknown_process");
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 2; i < 6; i += 2)
                {
                    if ((parts[i] != "--data-dir" && parts[i] != "--codex-home") || values.ContainsKey(parts[i]))
                        throw new AuthenticationCenterException("authentication_update_invalid_target");
                    values.Add(parts[i], AuthenticationInstallationStateStore.Normalize(parts[i + 1]));
                }
                return new Target { home = values["--codex-home"], store = values["--data-dir"] };
            }
            finally { LocalFree(pointer); }
        }

        private static string ReadProcessPath(int id)
        {
            var handle = OpenProcess(0x1000, false, id);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var size = 32768;
                var path = new StringBuilder(size);
                return QueryFullProcessImageName(handle, 0, path, ref size) ? path.ToString() : null;
            }
            finally { CloseHandle(handle); }
        }

        internal static bool ImageLocked(string path)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) return false;
                }
                catch (FileNotFoundException) { return false; }
                catch (DirectoryNotFoundException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
                catch (IOException error)
                {
                    if ((error.HResult & 0xffff) != 32 && (error.HResult & 0xffff) != 33) throw;
                    if (attempt < 2) System.Threading.Thread.Sleep(200);
                }
            }
            return true;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr pointer);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
