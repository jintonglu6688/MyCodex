using System;

namespace MyCodex.Application.Services
{
    public sealed class PlanSlashCommand
    {
        public static readonly PlanSlashCommand None = new PlanSlashCommand(false, null);

        public PlanSlashCommand(bool isPlanCommand, string promptText)
        {
            IsPlanCommand = isPlanCommand;
            PromptText = promptText;
        }

        public bool IsPlanCommand { get; }

        public string PromptText { get; }
    }

    public static class PlanSlashCommandParser
    {
        public static PlanSlashCommand Parse(string text)
        {
            var trimmed = (text ?? string.Empty).TrimStart();
            if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                return PlanSlashCommand.None;
            }

            string promptText;
            if (TryParse(trimmed, "/plan", out promptText) ||
                TryParse(trimmed, "/plan-mode", out promptText))
            {
                return new PlanSlashCommand(true, promptText);
            }

            return PlanSlashCommand.None;
        }

        private static bool TryParse(string text, string command, out string promptText)
        {
            promptText = null;
            if (text.Equals(command, StringComparison.OrdinalIgnoreCase))
            {
                promptText = string.Empty;
                return true;
            }

            if (text.Length <= command.Length ||
                !text.StartsWith(command, StringComparison.OrdinalIgnoreCase) ||
                !char.IsWhiteSpace(text[command.Length]))
            {
                return false;
            }

            promptText = text.Substring(command.Length).TrimStart();
            return true;
        }
    }
}
