using System;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal static class CommandTerminalInteractionState
    {
        internal static bool TryMarkWaitingForInput(CommandTask commandTask, AppServerEventArgs e, DateTime now)
        {
            if (commandTask == null ||
                e == null ||
                string.IsNullOrWhiteSpace(commandTask.ProcessId) ||
                string.IsNullOrWhiteSpace(e.ProcessId) ||
                !string.Equals(commandTask.ProcessId, e.ProcessId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            commandTask.IsWaitingForInput = true;
            commandTask.LastInteractionText = e.Content ?? string.Empty;
            commandTask.LastInteractionAt = now;
            return true;
        }

        internal static void MarkInputSent(CommandTask commandTask, DateTime now)
        {
            if (commandTask == null)
            {
                return;
            }

            commandTask.IsWaitingForInput = false;
            commandTask.LastInputSentAt = now;
        }

        internal static string BuildUnmatchedNotice(AppServerEventArgs e, string template)
        {
            if (e == null)
            {
                return null;
            }

            var content = string.IsNullOrWhiteSpace(e.Content)
                ? string.Empty
                : e.Content.Trim();
            if (content.Length <= 0)
            {
                content = string.IsNullOrWhiteSpace(e.CommandText)
                    ? string.Empty
                    : e.CommandText.Trim();
            }

            if (content.Length <= 0)
            {
                return null;
            }

            return string.Format(template ?? "{0}", content);
        }
    }
}
