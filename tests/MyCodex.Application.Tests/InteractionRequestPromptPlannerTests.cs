using MyCodex.Application.Services;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class InteractionRequestPromptPlannerTests
    {
        [TestCase(NotificationPromptMode.Sound, InteractionRequestPrompt.Sound)]
        [TestCase(NotificationPromptMode.SoundAndSpeech, InteractionRequestPrompt.Sound)]
        [TestCase(NotificationPromptMode.Speech, InteractionRequestPrompt.None)]
        [TestCase(NotificationPromptMode.Off, InteractionRequestPrompt.None)]
        public void Foreground_PlaysOnlyTheConfiguredSoundAndLeavesReadingToTheScreenReader(
            NotificationPromptMode mode, InteractionRequestPrompt expected)
        {
            Assert.That(InteractionRequestPromptPlanner.Plan(true, mode), Is.EqualTo(expected));
        }

        [TestCase(NotificationPromptMode.Sound, InteractionRequestPrompt.Sound | InteractionRequestPrompt.Speech)]
        [TestCase(NotificationPromptMode.Off, InteractionRequestPrompt.Speech)]
        public void Background_AlwaysSpeaksAndKeepsTheConfiguredSound(
            NotificationPromptMode mode, InteractionRequestPrompt expected)
        {
            Assert.That(InteractionRequestPromptPlanner.Plan(false, mode), Is.EqualTo(expected));
        }
    }
}
