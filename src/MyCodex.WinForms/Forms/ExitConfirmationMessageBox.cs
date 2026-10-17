using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyCodex.WinForms.Forms
{
    internal static class ExitConfirmationMessageBox
    {
        internal static DialogResult Show(IWin32Window owner, string text, string caption)
        {
            // Native Yes/No ignores Escape; handle it only while this confirmation is open.
            var ownerHandle = owner == null ? IntPtr.Zero : owner.Handle;
            var hook = IntPtr.Zero;
            HookProcedure callback = (code, wParam, lParam) =>
            {
                if (code == 0) // MSGF_DIALOGBOX
                {
                    var message = (NativeMessage)Marshal.PtrToStructure(lParam, typeof(NativeMessage));
                    if (message.Id == 0x0100 && message.WParam == new IntPtr((int)Keys.Escape)) // WM_KEYDOWN
                    {
                        var dialog = GetAncestor(message.Window, 2); // GA_ROOT
                        if ((ownerHandle == IntPtr.Zero || GetWindow(dialog, 4) == ownerHandle) && // GW_OWNER
                            GetDlgItem(dialog, 6) != IntPtr.Zero && GetDlgItem(dialog, 7) != IntPtr.Zero &&
                            GetDlgItem(dialog, 2) == IntPtr.Zero &&
                            PostMessage(dialog, 0x0111, new IntPtr(7), IntPtr.Zero)) // WM_COMMAND, IDNO
                        {
                            return new IntPtr(1);
                        }
                    }
                }

                return CallNextHookEx(hook, code, wParam, lParam);
            };
            hook = SetWindowsHookEx(-1, callback, IntPtr.Zero, GetCurrentThreadId()); // WH_MSGFILTER
            if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                return MessageBox.Show(owner, text, caption, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            }
            finally
            {
                UnhookWindowsHookEx(hook);
                GC.KeepAlive(callback);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            internal IntPtr Window;
            internal uint Id;
            internal IntPtr WParam;
            internal IntPtr LParam;
            internal uint Time;
            internal int X;
            internal int Y;
            internal uint Private;
        }

        private delegate IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProcedure callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr dialog, int id);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}
