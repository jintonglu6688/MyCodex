using System;

namespace MyCodex.Shared.Constants
{
    public static class SendMessageShortcutMode
    {
        public const string EnterNewLineCtrlEnterSend = "enterNewLineCtrlEnterSend";
        public const string CtrlEnterNewLineEnterSend = "ctrlEnterNewLineEnterSend";

        public static string Normalize(string mode)
        {
            var value = string.IsNullOrWhiteSpace(mode) ? EnterNewLineCtrlEnterSend : mode.Trim();
            return string.Equals(value, CtrlEnterNewLineEnterSend, StringComparison.OrdinalIgnoreCase)
                ? CtrlEnterNewLineEnterSend
                : EnterNewLineCtrlEnterSend;
        }
    }
}
