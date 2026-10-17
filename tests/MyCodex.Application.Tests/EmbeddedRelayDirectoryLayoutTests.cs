using System;
using System.IO;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class EmbeddedRelayDirectoryLayoutTests
    {
        [Test]
        public void Create_BuildsRelayPathsUnderAppDataDirectory()
        {
            var appDataDirectory = Path.Combine(Path.GetTempPath(), "mycodex-relay-layout");

            var layout = EmbeddedRelayDirectoryLayout.Create(appDataDirectory);

            Assert.That(layout.RootDirectory, Is.EqualTo(Path.Combine(appDataDirectory, "Relay")));
            Assert.That(layout.ConfigPath, Is.EqualTo(Path.Combine(appDataDirectory, "Relay", "relay-config.json")));
            Assert.That(layout.StatePath, Is.EqualTo(Path.Combine(appDataDirectory, "Relay", "relay-state.db")));
            Assert.That(layout.StdoutLogPath, Is.EqualTo(Path.Combine(appDataDirectory, "Relay", "relay.out.log")));
            Assert.That(layout.StderrLogPath, Is.EqualTo(Path.Combine(appDataDirectory, "Relay", "relay.err.log")));
        }

        [Test]
        public void Create_WhenAppDataDirectoryIsEmpty_ThrowsArgumentException()
        {
            Assert.That(() => EmbeddedRelayDirectoryLayout.Create(" "), Throws.TypeOf<ArgumentException>());
        }
    }
}
