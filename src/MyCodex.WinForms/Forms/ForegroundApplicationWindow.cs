using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MyCodex.WinForms.Forms
{
    internal static class ForegroundApplicationWindow
    {
        /// <summary>
        /// True when the foreground window, including a native message box, belongs to this
        /// MyCodex process. Each target runs in its own process.
        /// </summary>
        internal static bool BelongsToCurrentProcess()
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero)
            {
                return false;
            }

            uint processId;
            GetWindowThreadProcessId(window, out processId);
            using (var current = Process.GetCurrentProcess())
            {
                return processId == (uint)current.Id;
            }
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
