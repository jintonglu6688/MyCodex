using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MyCodex.WinForms.Theming
{
    internal static class WindowDarkMode
    {
        private const int DwmwaUseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void TryApply(Form form, bool enableDarkMode)
        {
            if (form == null || !form.IsHandleCreated)
            {
                return;
            }

            try
            {
                var value = enableDarkMode ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
            }
            catch
            {
            }
        }
    }
}
