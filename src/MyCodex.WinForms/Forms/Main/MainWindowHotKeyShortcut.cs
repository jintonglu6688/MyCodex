using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyCodex.Shared.Constants;

namespace MyCodex.WinForms.Forms
{
    internal static class MainWindowHotKeyShortcut
    {
        internal static bool TryCreate(
            Keys keyData,
            bool windowsKeyPressed,
            out int modifiers,
            out int virtualKey)
        {
            modifiers = 0;
            virtualKey = (int)(keyData & Keys.KeyCode);
            if ((keyData & Keys.Control) == Keys.Control)
            {
                modifiers |= MainWindowHotKeyConfig.ModifierControl;
            }

            if ((keyData & Keys.Shift) == Keys.Shift)
            {
                modifiers |= MainWindowHotKeyConfig.ModifierShift;
            }

            if ((keyData & Keys.Alt) == Keys.Alt)
            {
                modifiers |= MainWindowHotKeyConfig.ModifierAlt;
            }

            if (windowsKeyPressed)
            {
                modifiers |= MainWindowHotKeyConfig.ModifierWindows;
            }

            return modifiers != 0 &&
                   virtualKey != (int)Keys.Tab &&
                   MainWindowHotKeyConfig.IsValidVirtualKey(virtualKey);
        }

        internal static string Format(int modifiers, int virtualKey)
        {
            modifiers = MainWindowHotKeyConfig.NormalizeModifiers(modifiers);
            virtualKey = MainWindowHotKeyConfig.NormalizeVirtualKey(virtualKey);
            var parts = new List<string>();
            if ((modifiers & MainWindowHotKeyConfig.ModifierControl) != 0)
            {
                parts.Add("Ctrl");
            }

            if ((modifiers & MainWindowHotKeyConfig.ModifierShift) != 0)
            {
                parts.Add("Shift");
            }

            if ((modifiers & MainWindowHotKeyConfig.ModifierAlt) != 0)
            {
                parts.Add("Alt");
            }

            if ((modifiers & MainWindowHotKeyConfig.ModifierWindows) != 0)
            {
                parts.Add("Win");
            }

            parts.Add(new KeysConverter().ConvertToInvariantString((Keys)virtualKey));
            return string.Join("+", parts);
        }

        internal static bool IsWindowsKeyPressed()
        {
            return GetKeyState((int)Keys.LWin) < 0 || GetKeyState((int)Keys.RWin) < 0;
        }

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int virtualKey);
    }
}
