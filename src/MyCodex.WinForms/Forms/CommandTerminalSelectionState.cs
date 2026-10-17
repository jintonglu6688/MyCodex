using System;
using System.Collections.Generic;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal static class CommandTerminalSelectionState
    {
        internal static CommandTask GetTerminalCommand(CommandTask activeCommandTask, IReadOnlyList<CommandTask> commandTasks)
        {
            if (HasProcessId(activeCommandTask))
            {
                return activeCommandTask;
            }

            if (commandTasks == null)
            {
                return null;
            }

            for (var i = 0; i < commandTasks.Count; i++)
            {
                if (HasProcessId(commandTasks[i]))
                {
                    return commandTasks[i];
                }
            }

            return null;
        }

        internal static bool CanSubmitInput(CommandTask activeCommandTask, CommandTask terminalCommand)
        {
            return HasProcessId(activeCommandTask) &&
                   ReferenceEquals(activeCommandTask, terminalCommand) &&
                   string.Equals(activeCommandTask.Status, "running", StringComparison.OrdinalIgnoreCase);
        }

        internal static CommandTask FindByProcessId(string processId, IReadOnlyList<CommandTask> commandTasks)
        {
            if (string.IsNullOrWhiteSpace(processId) || commandTasks == null)
            {
                return null;
            }

            for (var i = 0; i < commandTasks.Count; i++)
            {
                var commandTask = commandTasks[i];
                if (HasProcessId(commandTask) &&
                    string.Equals(commandTask.ProcessId, processId, StringComparison.OrdinalIgnoreCase))
                {
                    return commandTask;
                }
            }

            return null;
        }

        private static bool HasProcessId(CommandTask commandTask)
        {
            return commandTask != null && !string.IsNullOrWhiteSpace(commandTask.ProcessId);
        }
    }
}
