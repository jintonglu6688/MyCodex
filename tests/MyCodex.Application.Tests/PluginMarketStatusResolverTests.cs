using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PluginMarketStatusResolverTests
    {
        [Test]
        public void ResolveRemoteStatus_ReturnsInstallAvailableWhenPluginIsNotInstalled()
        {
            var status = PluginMarketStatusResolver.ResolveRemoteStatus(false, null, "1.0.0");

            Assert.That(status, Is.EqualTo(PluginMarketAvailabilityStatus.InstallAvailable));
        }

        [Test]
        public void ResolveRemoteStatus_ReturnsUpdateAvailableWhenRemoteVersionIsHigher()
        {
            var status = PluginMarketStatusResolver.ResolveRemoteStatus(true, "1.0.0", "1.1.0");

            Assert.That(status, Is.EqualTo(PluginMarketAvailabilityStatus.UpdateAvailable));
        }

        [Test]
        public void ResolveRemoteStatus_ReturnsNoneWhenInstalledVersionMatchesRemoteVersion()
        {
            var status = PluginMarketStatusResolver.ResolveRemoteStatus(true, "1.0.0", "1.0.0");

            Assert.That(status, Is.EqualTo(PluginMarketAvailabilityStatus.None));
        }

        [Test]
        public void ResolveRemoteStatus_ReturnsNoneWhenInstalledVersionIsUnknown()
        {
            var status = PluginMarketStatusResolver.ResolveRemoteStatus(true, null, "1.1.0");

            Assert.That(status, Is.EqualTo(PluginMarketAvailabilityStatus.None));
        }
    }
}
