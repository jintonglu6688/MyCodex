using System;

namespace MyCodex.WinForms.Forms
{
    internal enum ManualReadTarget
    {
        Message = 0,
        Terminal = 1,
        Activity = 2
    }

    internal static class ManualReadTargetState
    {
        internal static ManualReadTarget Toggle(ManualReadTarget current)
        {
            switch (current)
            {
                case ManualReadTarget.Message:
                    return ManualReadTarget.Terminal;
                case ManualReadTarget.Terminal:
                    return ManualReadTarget.Activity;
                default:
                    return ManualReadTarget.Message;
            }
        }

        internal static string[] GetReadableTerminalLines(CommandTerminalBuffer buffer)
        {
            return buffer == null ? Array.Empty<string>() : buffer.GetReadableLines();
        }
    }
}
