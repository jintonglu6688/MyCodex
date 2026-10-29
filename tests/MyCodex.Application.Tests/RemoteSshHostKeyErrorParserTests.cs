using MyCodex.Infrastructure.AppServer;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteSshHostKeyErrorParserTests
    {
        [Test]
        public void TryExtractFingerprint_FromPlinkSecurityBreachMessage_ReturnsSha256Fingerprint()
        {
            const string message =
                "WARNING - POTENTIAL SECURITY BREACH!\r\n" +
                "The host key does not match the one Plink has cached for\r\n" +
                "this server:\r\n" +
                "  192.168.33.4 (port 22)\r\n" +
                "The new ssh-ed25519 key fingerprint is:\r\n" +
                " ssh-ed25519 255 SHA256:+DL4LIVPOnNqOQ/u7qJ0t3MU5nQ6xUz4KMc4idftizw\r\n" +
                "Connection abandoned.\r\n" +
                "FATAL ERROR: Cannot confirm a host key in batch mode";

            string fingerprint;
            var extracted = RemoteSshHostKeyErrorParser.TryExtractFingerprint(message, out fingerprint);

            Assert.That(extracted, Is.True);
            Assert.That(fingerprint, Is.EqualTo("SHA256:+DL4LIVPOnNqOQ/u7qJ0t3MU5nQ6xUz4KMc4idftizw"));
        }

        [Test]
        public void TryExtractFingerprint_ForUnrelatedMessage_ReturnsFalse()
        {
            string fingerprint;
            var extracted = RemoteSshHostKeyErrorParser.TryExtractFingerprint("Remote directory was not found.", out fingerprint);

            Assert.That(extracted, Is.False);
            Assert.That(fingerprint, Is.Null);
        }

        [Test]
        public void ShouldPromptForTrust_ForPasswordAuthWithUntrustedHostKey_ReturnsTrue()
        {
            const string message = "The new ssh-ed25519 key fingerprint is:\n ssh-ed25519 255 SHA256:newkey";
            var config = new RemoteMacSshConfigSnapshot
            {
                AuthenticationMode = RemoteSshAuthenticationMode.Password
            };

            string fingerprint;
            var shouldPrompt = RemoteSshHostKeyTrustPlanner.ShouldPromptForTrust(config, message, out fingerprint);

            Assert.That(shouldPrompt, Is.True);
            Assert.That(fingerprint, Is.EqualTo("SHA256:newkey"));
        }

        [Test]
        public void ShouldPromptForTrust_ForPasswordAuthWithChangedPinnedHostKey_ReturnsFalse()
        {
            const string message = "The new ssh-ed25519 key fingerprint is:\n ssh-ed25519 255 SHA256:newkey";
            var config = new RemoteMacSshConfigSnapshot
            {
                AuthenticationMode = RemoteSshAuthenticationMode.Password,
                HostKeyFingerprint = "SHA256:oldkey"
            };

            string fingerprint;
            var shouldPrompt = RemoteSshHostKeyTrustPlanner.ShouldPromptForTrust(config, message, out fingerprint);

            Assert.That(shouldPrompt, Is.False);
            Assert.That(fingerprint, Is.Null);
        }

        [Test]
        public void ShouldPromptForTrust_ForPrivateKeyAuth_ReturnsFalse()
        {
            const string message = "The new ssh-ed25519 key fingerprint is:\n ssh-ed25519 255 SHA256:newkey";
            var config = new RemoteMacSshConfigSnapshot
            {
                AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey
            };

            string fingerprint;
            var shouldPrompt = RemoteSshHostKeyTrustPlanner.ShouldPromptForTrust(config, message, out fingerprint);

            Assert.That(shouldPrompt, Is.False);
            Assert.That(fingerprint, Is.Null);
        }
    }
}
