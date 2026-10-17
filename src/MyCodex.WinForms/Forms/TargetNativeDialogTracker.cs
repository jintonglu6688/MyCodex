using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace MyCodex.WinForms.Forms
{
    // MessageBox and common dialogs are native windows, not Application.OpenForms.
    // Observe only standard dialogs on this UI thread with an owner in this group.
    internal sealed class TargetNativeDialogTracker : IDisposable
    {
        private readonly TargetWindowGroup _group;
        private readonly Dictionary<IntPtr, DialogTitle> _dialogs = new Dictionary<IntPtr, DialogTitle>();
        private readonly HookProcedure _callback;
        private IntPtr _hook;

        internal TargetNativeDialogTracker(TargetWindowGroup group)
        {
            _group = group;
            _callback = OnWindowEvent;
            _hook = SetWindowsHookEx(5, _callback, IntPtr.Zero, GetCurrentThreadId());
            if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        internal bool HasModal => GetModal() != IntPtr.Zero;

        internal bool TryActivateModal()
        {
            var dialog = GetModal();
            if (dialog == IntPtr.Zero) return false;
            SetForegroundWindow(dialog);
            return true;
        }

        private IntPtr GetModal()
        {
            return _dialogs.Keys.LastOrDefault(handle => IsWindowVisible(handle) && IsWindowEnabled(handle));
        }

        internal void RefreshTitles()
        {
            foreach (var pair in _dialogs.ToArray())
            {
                if (IsWindow(pair.Key)) SetWindowText(pair.Key,
                    MainFormWindowTitleComposer.AddTargetPrefix(pair.Value.Text, _group.PrefixFor(pair.Value.Owner)));
            }
        }

        private IntPtr OnWindowEvent(int code, IntPtr window, IntPtr details)
        {
            if (code == 4) _dialogs.Remove(window); // HCBT_DESTROYWND
            else if (code == 5 && !_dialogs.ContainsKey(window)) // HCBT_ACTIVATE
            {
                var className = new StringBuilder(64);
                GetClassName(window, className, className.Capacity);
                if (className.ToString() == "#32770" && Control.FromHandle(window) == null)
                {
                    var ownerHandle = GetWindow(window, 4); // GW_OWNER
                    var owner = Control.FromHandle(ownerHandle) as Form;
                    if (owner == null && _dialogs.TryGetValue(ownerHandle, out var parent)) owner = parent.Owner;
                    if (ReferenceEquals(TargetWindowGroup.Find(owner), _group))
                    {
                        var text = new StringBuilder(GetWindowTextLength(window) + 1);
                        GetWindowText(window, text, text.Capacity);
                        var prefix = _group.PrefixFor(owner);
                        var baseText = text.ToString();
                        if (!string.IsNullOrEmpty(prefix) && baseText.StartsWith(prefix.Trim() + " - ", StringComparison.Ordinal))
                            baseText = baseText.Substring(prefix.Trim().Length + 3);
                        _dialogs.Add(window, new DialogTitle { Owner = owner, Text = baseText });
                        SetWindowText(window, MainFormWindowTitleComposer.AddTargetPrefix(baseText, prefix));
                    }
                }
            }
            return CallNextHookEx(_hook, code, window, details);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _dialogs.Clear();
            GC.KeepAlive(_callback);
        }

        private sealed class DialogTitle
        {
            internal Form Owner;
            internal string Text;
        }

        private delegate IntPtr HookProcedure(int code, IntPtr window, IntPtr details);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProcedure callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr window, IntPtr details);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr window, string text);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    }
}
