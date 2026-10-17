using System;
using System.Windows.Forms;
using MyCodex.Shared.Constants;

namespace MyCodex.WinForms.Controls
{
    public enum SendMessageShortcutAction
    {
        None,
        NewLine,
        Send
    }

    internal static class SendMessageShortcutPlanner
    {
        internal static SendMessageShortcutAction ResolveAction(string mode, Keys keyCode, bool control)
        {
            if (keyCode != Keys.Enter)
            {
                return SendMessageShortcutAction.None;
            }

            var normalized = SendMessageShortcutMode.Normalize(mode);
            if (string.Equals(normalized, SendMessageShortcutMode.CtrlEnterNewLineEnterSend, StringComparison.OrdinalIgnoreCase))
            {
                return control ? SendMessageShortcutAction.NewLine : SendMessageShortcutAction.Send;
            }

            return control ? SendMessageShortcutAction.Send : SendMessageShortcutAction.NewLine;
        }
    }
}
