using System;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TurnFailureNotifierTests
    {
        [TestCase("turn_failed", true, true, false, true)]
        [TestCase("turn_failed", false, true, false, false)]
        [TestCase("turn_failed", true, false, false, false)]
        [TestCase("turn_failed", true, true, true, false)]
        [TestCase("turn_retrying", true, true, false, false)]
        [TestCase("error", true, true, false, false)]
        [TestCase("turn_interrupted", true, true, false, false)]
        [TestCase("turn_completed", true, true, false, false)]
        public void OnlyNewUnmutedFinalFailure_PlaysSound(string eventType, bool wasBusy, bool soundEnabled, bool historical, bool expected)
        {
            var plays = 0;
            var notification = new AppServerEventArgs(eventType, "status", threadId: "thread", turnId: "turn", isHistorical: historical);
            var notified = TurnFailureNotifier.TryNotify(notification, wasBusy, soundEnabled, () => plays++);
            Assert.That(notified, Is.EqualTo(expected));
            Assert.That(plays, Is.EqualTo(expected ? 1 : 0));
        }

        [Test]
        public void UnavailableAudio_DoesNotInterruptFailureHandling()
        {
            var notification = new AppServerEventArgs("turn_failed", "failed", threadId: "thread", turnId: "turn");
            Assert.That(TurnFailureNotifier.TryNotify(notification, true, true, () => { throw new InvalidOperationException(); }), Is.False);
        }
    }
}
