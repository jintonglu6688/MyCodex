using MyCodex.Application.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class RelaySecretRedactorTests
    {
        [Test]
        public void Redact_RemovesRelayCredentialsFromDiagnostics()
        {
            var text = "Authorization=Bearer tenant-secret-value tenantSecret=abc deviceToken=device-token-value oneTimePairingToken=pair-token";

            var redacted = RelaySecretRedactor.Redact(text);

            Assert.That(redacted, Does.Not.Contain("tenant-secret-value"));
            Assert.That(redacted, Does.Not.Contain("abc"));
            Assert.That(redacted, Does.Not.Contain("device-token-value"));
            Assert.That(redacted, Does.Not.Contain("pair-token"));
            Assert.That(redacted, Does.Contain("Authorization=Bearer [REDACTED]"));
            Assert.That(redacted, Does.Contain("tenantSecret=[REDACTED]"));
            Assert.That(redacted, Does.Contain("deviceToken=[REDACTED]"));
            Assert.That(redacted, Does.Contain("oneTimePairingToken=[REDACTED]"));
        }
    }
}

