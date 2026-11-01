using System;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal static class NativeWindowTheme
    {
        private const int LvmGetHeader = 0x101F;
        private const int WmSetRedraw = 0x000B;
        private const int WmThemeChanged = 0x031A;
        private static readonly ConditionalWeakTable<Control, NativeThemeState> States =
            new ConditionalWeakTable<Control, NativeThemeState>();

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetComboBoxInfo(IntPtr hwnd, ref ComboBoxInfo info);

        public static void Apply(Control control, bool dark)
        {
            if (control == null)
            {
                return;
            }

            NativeThemeState state;
            if (!States.TryGetValue(control, out state))
            {
                state = new NativeThemeState();
                States.Add(control, state);
                control.HandleCreated += (s, e) => ApplyHandle(control, state.Dark);
            }

            state.Dark = dark;
            ApplyHandle(control, dark);
        }

        internal static void SuspendRedraw(Control control)
        {
            if (control != null && control.IsHandleCreated && !control.IsDisposed)
            {
                SendMessage(control.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            }
        }

        internal static void ResumeRedraw(Control control)
        {
            if (control != null && control.IsHandleCreated && !control.IsDisposed)
            {
                SendMessage(control.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
            }
        }

        internal static bool TryGetComboBoxButtonBounds(ComboBox comboBox, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (comboBox == null || !comboBox.IsHandleCreated)
            {
                return false;
            }

            var info = new ComboBoxInfo();
            info.Size = Marshal.SizeOf(typeof(ComboBoxInfo));
            if (!GetComboBoxInfo(comboBox.Handle, ref info))
            {
                return false;
            }

            bounds = Rectangle.FromLTRB(
                info.ButtonRectangle.Left,
                info.ButtonRectangle.Top,
                info.ButtonRectangle.Right,
                info.ButtonRectangle.Bottom);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        private static void ApplyHandle(Control control, bool dark)
        {
            if (!control.IsHandleCreated || control.IsDisposed)
            {
                return;
            }

            var themeName = dark ? "DarkMode_Explorer" : null;
            SetWindowTheme(control.Handle, themeName, null);

            var listView = control as ListView;
            if (listView != null)
            {
                var header = SendMessage(listView.Handle, LvmGetHeader, IntPtr.Zero, IntPtr.Zero);
                if (header != IntPtr.Zero)
                {
                    SetWindowTheme(header, dark ? "DarkMode_ItemsView" : null, null);
                    SendMessage(header, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
                    listView.Invalidate();
                }
            }

            var comboBox = control as ComboBox;
            if (comboBox != null && comboBox.IsHandleCreated)
            {
                var info = new ComboBoxInfo();
                info.Size = Marshal.SizeOf(typeof(ComboBoxInfo));
                if (GetComboBoxInfo(comboBox.Handle, ref info))
                {
                    ApplyChildTheme(info.ItemHandle, themeName);
                    ApplyChildTheme(info.ListHandle, themeName);
                }
            }

            if (control is UpDownBase || control is DateTimePicker)
            {
                ApplyNativeDescendantThemes(control, dark);
            }

            SendMessage(control.Handle, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
            control.Invalidate();
        }

        private static void ApplyNativeDescendantThemes(Control control, bool dark)
        {
            foreach (Control child in control.Controls)
            {
                Apply(child, dark);
                ApplyNativeDescendantThemes(child, dark);
            }
        }

        private static void ApplyChildTheme(IntPtr hwnd, string themeName)
        {
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            SetWindowTheme(hwnd, themeName, null);
            SendMessage(hwnd, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
        }

        private sealed class NativeThemeState
        {
            public bool Dark { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ComboBoxInfo
        {
            public int Size;
            public NativeRectangle ItemRectangle;
            public NativeRectangle ButtonRectangle;
            public IntPtr ComboHandle;
            public IntPtr ItemHandle;
            public IntPtr ListHandle;
        }
    }
}
