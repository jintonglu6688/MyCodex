using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class InteractionRequestNotifierTests
    {
        [Test]
        public async Task BackgroundRequest_SpeaksAfterTheSound()
        {
            var spoken = new List<string>();
            var sounds = 0;

            await InteractionRequestNotifier.Notify(NotificationPromptMode.Sound, "Approval needed", () => sounds++,
                text => { spoken.Add(text); return true; }, () => false);

            Assert.That(sounds, Is.EqualTo(1));
            Assert.That(spoken, Is.EqualTo(new[] { "Approval needed" }));
        }

        [Test]
        public async Task ReturningToTheForegroundDuringTheSound_SkipsSpeech()
        {
            var spoken = new List<string>();
            var checks = 0;

            await InteractionRequestNotifier.Notify(NotificationPromptMode.Sound, "Approval needed", () => { },
                text => { spoken.Add(text); return true; }, () => checks++ > 0);

            Assert.That(spoken, Is.Empty, "In the foreground the screen reader reads the dialog.");
        }
    }
}
