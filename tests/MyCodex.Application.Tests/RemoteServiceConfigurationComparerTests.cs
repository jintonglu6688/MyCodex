using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteServiceConfigurationComparerTests
    {
        [Test]
        public void ConnectionChanged_NullEnabledRetainsLegacyEnabledMeaning()
        {
            var legacy = new RemoteServiceConfigSnapshot { RemoteEnabled = null };
            var enabled = new RemoteServiceConfigSnapshot { RemoteEnabled = true };
            var disabled = new RemoteServiceConfigSnapshot { RemoteEnabled = false };

            Assert.That(RemoteServiceConfigurationComparer.ConnectionChanged(legacy, enabled), Is.False);
            Assert.That(RemoteServiceConfigurationComparer.ConnectionChanged(enabled, legacy), Is.False);
            Assert.That(RemoteServiceConfigurationComparer.ConnectionChanged(legacy, disabled), Is.True);
        }

        [Test]
        public void ConnectionChanged_InactiveProfileAndStartupPreferenceDoNotChangeActiveConnection()
        {
            var previous = new RemoteServiceConfigSnapshot { RemoteServiceMode = "Embedded", RelayHost = "local.example" };
            var current = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "Embedded",
                RelayHost = "local.example",
                StartEmbeddedRelayWithApplication = true,
                ExternalProfile = new RemoteServiceProfileSnapshot { RelayHost = "external.example" }
            };

            Assert.That(RemoteServiceConfigurationComparer.ConnectionChanged(previous, current), Is.False);
            current.EmbeddedRelayCertificateSha256 = "changed-certificate";
            Assert.That(RemoteServiceConfigurationComparer.ConnectionChanged(previous, current), Is.True);
        }
    }
}
