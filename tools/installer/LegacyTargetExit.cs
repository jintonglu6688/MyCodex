using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace MyCodexInstaller
{
    // Compatibility only: old clients cannot authenticate an external IPC coordinator.
    // Send the ordinary close request once; their own save/confirmation handlers decide.
    public static class LegacyTargetExit
    {
        public static int Run(string installRoot, int timeoutMilliseconds)
        {
            var root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar);
            var targets = new List<Process>();
            try
            {
                foreach (var name in new[] { "MyCodex", "MyCodex.Dev" })
                {
                    foreach (var process in Process.GetProcessesByName(name))
                    {
                        var retained = false;
                        try
                        {
                            if (process.HasExited) continue;
                            // Retain the handle before inspecting identity to prevent PID reuse.
                            var handle = process.Handle;
                            var path = GetProcessPath(handle);
                            if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase)) continue;
                            if (!IsCurrentUser(process)) return 2;
                            targets.Add(process);
                            retained = true;
                        }
                        catch (InvalidOperationException) { if (!process.HasExited) return 2; }
                        // Processes this account cannot open (another account, a protected
                        // process) cannot be asked to close. The final installation guard
                        // still blocks if one of them is using this installation's files.
                        catch (System.ComponentModel.Win32Exception) { }
                        finally { if (!retained) process.Dispose(); }
                    }
                }
                targets.Sort((first, second) => first.Id.CompareTo(second.Id));
                foreach (var process in targets)
                {
                    if (process.HasExited) continue;
                    var windows = FindMainWindows(process.Id);
                    // Shared Relay hosts also use MyCodex.exe but have no main window.
                    // Leave them to normal lease cleanup and the final resource guard.
                    if (windows.Count == 0) continue;
                    // No close request was sent yet. Keep a blocked old window distinct
                    // from a save/cancel refusal (3), which must never be overridden.
                    if (windows.Count != 1 || !IsWindowEnabled(windows[0])) return 5;
                    var window = windows[0];
                    uint owner;
                    GetWindowThreadProcessId(window, out owner);
                    if (process.HasExited) continue;
                    if (owner != process.Id) return 2;
                    AllowSetForegroundWindow(process.Id);
                    UIntPtr result;
                    // WM_CLOSE, not end-session or forced shutdown; includes tray-hidden windows.
                    if (SendMessageTimeout(window, 0x0010, UIntPtr.Zero, IntPtr.Zero, 2,
                        (uint)timeoutMilliseconds, out result) == IntPtr.Zero)
                    {
                        if (!process.HasExited) return 3;
                        continue;
                    }
                    var wait = Stopwatch.StartNew();
                    while (!process.WaitForExit(100))
                    {
                        // Old async save preparation disables the main window. Re-enabling
                        // it after WM_CLOSE means the client refused/cancelled normal exit.
                        if ((IsWindow(window) && IsWindowEnabled(window)) || wait.ElapsedMilliseconds >= timeoutMilliseconds)
                            return 3;
                    }
                }
                return 0;
            }
            catch { return 2; }
            finally { foreach (var process in targets) process.Dispose(); }
        }

        private static List<IntPtr> FindMainWindows(int processId)
        {
            var windows = new List<IntPtr>();
            EnumWindows((window, parameter) =>
            {
                uint owner;
                GetWindowThreadProcessId(window, out owner);
                // A tray-hidden WinForms window may have an internal hidden owner.
                if (owner != processId || GetMenu(window) == IntPtr.Zero) return true;
                var name = new StringBuilder(256);
                GetClassName(window, name, name.Capacity);
                // MainForm is the client's sole top-level WinForms window with a native MainMenu.
                if (name.ToString().StartsWith("WindowsForms10.Window.", StringComparison.Ordinal)) windows.Add(window);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        private static bool IsCurrentUser(Process process)
        {
            IntPtr token;
            if (!OpenProcessToken(process.Handle, 8, out token)) return false;
            try
            {
                using (var owner = new WindowsIdentity(token))
                using (var current = WindowsIdentity.GetCurrent())
                using (var self = Process.GetCurrentProcess())
                    return owner.User == current.User && process.SessionId == self.SessionId;
            }
            finally { CloseHandle(token); }
        }

        private static string GetProcessPath(IntPtr process)
        {
            // MainModule cannot inspect a 64-bit client from 32-bit installer PowerShell.
            var size = 32768;
            var name = new StringBuilder(size);
            if (!QueryFullProcessImageName(process, 0, name, ref size))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return Path.GetFullPath(name.ToString());
        }

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
    }
}
