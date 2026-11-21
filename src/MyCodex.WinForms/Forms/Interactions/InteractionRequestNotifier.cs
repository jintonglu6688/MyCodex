using System;
using System.Threading.Tasks;
using MyCodex.Application.Services;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class InteractionRequestNotifier
    {
        // Lets the short request sound finish before speech starts, so neither covers the other.
        private const int SoundLeadMilliseconds = 200;

        /// <summary>
        /// Announces a request that opens a dialog: the configured sound in the foreground and
        /// the background, followed by speech while this target is in the background.
        /// </summary>
        internal static async Task Notify(NotificationPromptMode soundMode, string notice, Action playSound, Func<string, bool> speak,
            Func<bool> isForeground = null)
        {
            isForeground = isForeground ?? ForegroundApplicationWindow.BelongsToCurrentProcess;
            var prompt = InteractionRequestPromptPlanner.Plan(isForeground(), soundMode);
            if ((prompt & InteractionRequestPrompt.Sound) != 0)
            {
                playSound();
                if ((prompt & InteractionRequestPrompt.Speech) != 0)
                {
                    await Task.Delay(SoundLeadMilliseconds);
                    // Back in the foreground during the sound: the screen reader reads the dialog instead.
                    if (isForeground())
                    {
                        return;
                    }
                }
            }

            if ((prompt & InteractionRequestPrompt.Speech) != 0)
            {
                speak(notice);
            }
        }
    }
}
