using System;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    [Flags]
    public enum InteractionRequestPrompt
    {
        None = 0,
        Sound = 1,
        Speech = 2
    }

    /// <summary>
    /// Chooses how to announce a request that opens a dialog, such as an approval or a question.
    /// The sound switch applies in the foreground and the background. Speech is added only in the
    /// background: in the foreground the screen reader reads the dialog itself.
    /// </summary>
    public static class InteractionRequestPromptPlanner
    {
        public static InteractionRequestPrompt Plan(bool isApplicationForeground, NotificationPromptMode soundMode)
        {
            var prompt = soundMode == NotificationPromptMode.Sound || soundMode == NotificationPromptMode.SoundAndSpeech
                ? InteractionRequestPrompt.Sound
                : InteractionRequestPrompt.None;
            return isApplicationForeground ? prompt : prompt | InteractionRequestPrompt.Speech;
        }
    }
}
