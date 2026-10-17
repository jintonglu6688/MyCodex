using System;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal static class CommandOutputAccumulator
    {
        internal static void Append(
            CommandTask commandTask,
            string text,
            string outputStream,
            bool isStreamDelta,
            int maxBufferedCommandOutputCharacters)
        {
            if (commandTask == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            if (string.Equals(outputStream, "stderr", StringComparison.OrdinalIgnoreCase))
            {
                if (isStreamDelta)
                {
                    commandTask.HasStreamedStdErr = true;
                }

                commandTask.StdErr = AppendBounded(commandTask.StdErr, text, maxBufferedCommandOutputCharacters);
            }
            else
            {
                if (isStreamDelta)
                {
                    commandTask.HasStreamedStdOut = true;
                }

                commandTask.StdOut = AppendBounded(commandTask.StdOut, text, maxBufferedCommandOutputCharacters);
            }
        }

        internal static void AppendBuffered(
            CommandTask commandTask,
            string text,
            string outputStream,
            int maxBufferedCommandOutputCharacters)
        {
            if (commandTask == null || string.IsNullOrEmpty(text))
            {
                return;
            }

            var isStdErr = string.Equals(outputStream, "stderr", StringComparison.OrdinalIgnoreCase);
            if ((isStdErr && commandTask.HasStreamedStdErr) || (!isStdErr && commandTask.HasStreamedStdOut))
            {
                return;
            }

            var existing = isStdErr ? commandTask.StdErr : commandTask.StdOut;
            if (string.Equals(existing, text, StringComparison.Ordinal))
            {
                return;
            }

            Append(commandTask, text, outputStream, false, maxBufferedCommandOutputCharacters);
        }

        internal static string AppendBounded(string existing, string text, int maxBufferedCommandOutputCharacters)
        {
            if (string.IsNullOrEmpty(text))
            {
                return existing ?? string.Empty;
            }

            if (maxBufferedCommandOutputCharacters <= 0)
            {
                return text;
            }

            if (text.Length >= maxBufferedCommandOutputCharacters)
            {
                return text.Substring(text.Length - maxBufferedCommandOutputCharacters);
            }

            if (string.IsNullOrEmpty(existing))
            {
                return text;
            }

            if (existing.Length > maxBufferedCommandOutputCharacters - text.Length)
            {
                existing = existing.Substring(existing.Length - (maxBufferedCommandOutputCharacters - text.Length));
            }

            return existing + text;
        }
    }
}
