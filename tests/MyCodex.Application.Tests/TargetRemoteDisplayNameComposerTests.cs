using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetRemoteDisplayNameComposerTests
    {
        [TestCase("Windows", "Windows", "WORKSTATION · Windows")]
        [TestCase("Windows", "My PC", "WORKSTATION · Windows · My PC")]
        [TestCase("Wsl", "Ubuntu development", "WORKSTATION · WSL · Ubuntu development")]
        [TestCase("Ssh", "开发机", "WORKSTATION · SSH · 开发机")]
        public void Compose_ContainsComputerAndFullTargetName(string kind, string name, string expected)
        {
            var target = new TargetProfileSnapshot { Id = "target-id", Kind = kind, Name = name };
            Assert.That(TargetRemoteDisplayNameComposer.Compose(" WORKSTATION ", target), Is.EqualTo(expected));
            Assert.That(target.Id, Is.EqualTo("target-id"));
            Assert.That(target.Name, Is.EqualTo(name));
        }

        [Test]
        public void BuildConfig_MapsDisplayWithoutChangingSecurityRoute()
        {
            var snapshot = new RemoteServiceConfigSnapshot
            {
                HostId = "original-host-id", TenantId = "tenant", ThisPcName = "PC · WSL · Ubuntu",
                RelayHost = "relay.example.com", RelayPort = 443, UseTls = true,
                RelaySigningPublicKey = "original-key", RelaySigningKeyFingerprint = "original-pin"
            };
            var config = RemoteServiceConfigPlanner.BuildRelayHostConfig(snapshot, "original-secret");
            snapshot.ThisPcName = "PC · WSL · Renamed Ubuntu";
            var renamed = RemoteServiceConfigPlanner.BuildRelayHostConfig(snapshot, "original-secret");

            Assert.That(config.HostDisplayName, Is.EqualTo("PC · WSL · Ubuntu"));
            Assert.That(renamed.HostDisplayName, Is.EqualTo("PC · WSL · Renamed Ubuntu"));
            Assert.That(snapshot.HostId, Is.EqualTo("original-host-id"));
            Assert.That(renamed.TenantId, Is.EqualTo(config.TenantId));
            Assert.That(renamed.RelayHost, Is.EqualTo(config.RelayHost));
            Assert.That(renamed.RelayPort, Is.EqualTo(config.RelayPort));
            Assert.That(renamed.TenantSecret, Is.EqualTo(config.TenantSecret));
            Assert.That(renamed.RelaySigningPublicKey, Is.EqualTo(config.RelaySigningPublicKey));
            Assert.That(renamed.RelaySigningKeyFingerprint, Is.EqualTo(config.RelaySigningKeyFingerprint));
        }
    }
}
