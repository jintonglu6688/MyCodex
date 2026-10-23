using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class VoiceControlCommandTests
    {
        [Test]
        public void EmptyArguments_ReadStatusWithoutRequestingMicrophoneOrSettingsChanges()
        {
            var command = VoiceControlCommand.Parse(new Dictionary<string, object>());
            Assert.That(command.HasChanges, Is.False);
        }

        [TestCase("enabled", "true")]
        [TestCase("voice_number", 0)]
        [TestCase("voice_number", 1.2)]
        [TestCase("output_percent", 151)]
        [TestCase("thread_id", "other-thread")]
        public void InvalidArguments_AreRejected(string key, object value)
        {
            Assert.Throws<ArgumentException>(() => VoiceControlCommand.Parse(new Dictionary<string, object> { { key, value } }));
        }

        [Test]
        public void ExplicitArguments_UseSupportedOutputLevels()
        {
            var command = VoiceControlCommand.Parse(new Dictionary<string, object>
            { { "enabled", true }, { "microphone_muted", true }, { "voice_number", 2 }, { "output_percent", 150 } });
            Assert.That(command.Enabled, Is.True);
            Assert.That(command.MicrophoneMuted, Is.True);
            Assert.That(command.VoiceNumber, Is.EqualTo(2));
            Assert.That(command.OutputLevel, Is.EqualTo(VoiceOutputLevel.Boost));
        }
    }
}
