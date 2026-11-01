using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal static class WindowDarkMode
    {
        private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaBorderColor = 34;
        private const int DwmwaCaptionColor = 35;
        private const int DwmwaTextColor = 36;
        private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
        private const int WindowNcPaint = 0x0085;
        private const int WindowNcActivate = 0x0086;
        private const int WindowThemeChanged = 0x031A;
        private const int SwpNoSize = 0x0001;
        private const int SwpNoMove = 0x0002;
        private const int SwpNoZOrder = 0x0004;
        private const int SwpNoActivate = 0x0010;
        private const int SwpFrameChanged = 0x0020;
        private const int RdwInvalidate = 0x0001;
        private const int RdwErase = 0x0004;
        private const int RdwAllChildren = 0x0080;
        private const int RdwFrame = 0x0400;
        private static readonly ConditionalWeakTable<Form, FormDarkModeState> InitializedForms =
            new ConditionalWeakTable<Form, FormDarkModeState>();

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern IntPtr GetMenu(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool GetMenuItemRect(IntPtr hwnd, IntPtr menu, uint item, out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRectangle rectangle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("user32.dll")]
        private static extern int FillRect(IntPtr hdc, ref NativeRectangle rectangle, IntPtr brush);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(int color);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(
            IntPtr hwnd,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            int flags);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRectangle, IntPtr updateRegion, int flags);

        [DllImport("user32.dll")]
        private static extern bool DrawMenuBar(IntPtr hwnd);

        public static void TryApply(Form form, bool enableDarkMode, bool usePaletteColors)
        {
            if (form == null)
            {
                return;
            }

            FormDarkModeState state;
            if (!InitializedForms.TryGetValue(form, out state))
            {
                state = new FormDarkModeState();
                InitializedForms.Add(form, state);
                form.HandleCreated += (s, e) => Apply(form, state);
                form.Disposed += (s, e) => state.Release();
            }

            state.Enabled = enableDarkMode;
            state.UsePaletteColors = usePaletteColors;
            Apply(form, state);
        }

        private static void Apply(Form form, FormDarkModeState state)
        {
            if (!form.IsHandleCreated)
            {
                return;
            }

            try
            {
                var value = state.Enabled ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                {
                    DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
                }

                var borderColor = state.UsePaletteColors ? ColorTranslator.ToWin32(UiTheme.WindowBackColor) : DwmColorDefault;
                DwmSetWindowAttribute(form.Handle, DwmwaBorderColor, ref borderColor, sizeof(int));
                var captionColor = state.UsePaletteColors ? ColorTranslator.ToWin32(UiTheme.WindowBackColor) : DwmColorDefault;
                DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
                var textColor = state.UsePaletteColors ? ColorTranslator.ToWin32(UiTheme.TextColor) : DwmColorDefault;
                DwmSetWindowAttribute(form.Handle, DwmwaTextColor, ref textColor, sizeof(int));
            }
            catch
            {
            }

            state.AttachMenuBar(form.Handle);
            SetWindowPos(
                form.Handle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
            RedrawWindow(
                form.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                RdwInvalidate | RdwErase | RdwFrame | RdwAllChildren);
            DrawMenuBar(form.Handle);
        }

        private sealed class FormDarkModeState : IDisposable
        {
            private MenuBarWindow _menuBarWindow;

            public bool Enabled { get; set; }

            public bool UsePaletteColors { get; set; }

            public void AttachMenuBar(IntPtr handle)
            {
                if (handle == IntPtr.Zero || _menuBarWindow != null && _menuBarWindow.Handle == handle)
                {
                    return;
                }

                Release();
                _menuBarWindow = new MenuBarWindow(handle, this);
            }

            public void Release()
            {
                if (_menuBarWindow != null)
                {
                    _menuBarWindow.Release();
                    _menuBarWindow = null;
                }
            }

            public void Dispose()
            {
                Release();
            }
        }

        private sealed class MenuBarWindow : NativeWindow
        {
            private readonly FormDarkModeState _state;

            public MenuBarWindow(IntPtr handle, FormDarkModeState state)
            {
                _state = state;
                AssignHandle(handle);
            }

            public void Release()
            {
                ReleaseHandle();
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (_state.UsePaletteColors &&
                    (m.Msg == WindowNcPaint || m.Msg == WindowNcActivate || m.Msg == WindowThemeChanged))
                {
                    PaintMenuRemainder(Handle);
                }
            }

            private static void PaintMenuRemainder(IntPtr hwnd)
            {
                try
                {
                    var menu = GetMenu(hwnd);
                    NativeRectangle windowRectangle;
                    if (menu == IntPtr.Zero || !GetWindowRect(hwnd, out windowRectangle))
                    {
                        return;
                    }

                    NativeRectangle lastItem = default(NativeRectangle);
                    var rightmost = 0;
                    for (uint index = 0; ; index++)
                    {
                        NativeRectangle itemRectangle;
                        if (!GetMenuItemRect(hwnd, menu, index, out itemRectangle))
                        {
                            break;
                        }

                        lastItem = itemRectangle;
                        rightmost = Math.Max(rightmost, itemRectangle.Right);
                    }

                    var windowRight = windowRectangle.Right - 1;
                    if (rightmost <= 0 || rightmost >= windowRight || lastItem.Bottom <= lastItem.Top)
                    {
                        return;
                    }

                    var fillRectangle = new NativeRectangle
                    {
                        Left = rightmost - windowRectangle.Left,
                        Top = lastItem.Top - windowRectangle.Top,
                        Right = windowRight - windowRectangle.Left,
                        Bottom = lastItem.Bottom - windowRectangle.Top
                    };
                    var windowDc = GetWindowDC(hwnd);
                    if (windowDc == IntPtr.Zero)
                    {
                        return;
                    }

                    var brush = CreateSolidBrush(ColorTranslator.ToWin32(UiTheme.PanelBackColor));
                    if (brush != IntPtr.Zero)
                    {
                        FillRect(windowDc, ref fillRectangle, brush);
                        DeleteObject(brush);
                    }

                    ReleaseDC(hwnd, windowDc);
                }
                catch
                {
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

    }
}
