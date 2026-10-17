using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class AppConfigSnapshotWslTests
    {
        [Test]
        public void NewSnapshot_HasDefaultWslAppServerConfig()
        {
            var snapshot = new AppConfigSnapshot();

            Assert.That(snapshot.CodexRuntimeSource, Is.EqualTo(CodexRuntimeSources.Bundled));
            Assert.That(snapshot.ShowCommandOutput, Is.True);
            Assert.That(typeof(AppConfigSnapshot).GetProperty("OfficialOfficeCapabilityEnabled"), Is.Null);
            Assert.That(snapshot.WslAppServer, Is.Not.Null);
            Assert.That(snapshot.WslAppServer.DistributionName, Is.EqualTo(WslAppServerConfigSnapshot.DefaultDistributionName));
            Assert.That(snapshot.WslAppServer.CodexCommand, Is.EqualTo(WslAppServerConfigSnapshot.DefaultCodexCommand));
            Assert.That(snapshot.WslAppServer.ShellCommand, Is.EqualTo(WslAppServerConfigSnapshot.DefaultShellCommand));
            Assert.That(snapshot.WslAppServer.CodexHomeStrategy, Is.EqualTo(WslCodexHomeStrategy.WslDefault));
        }

        [Test]
        public void WslAppServer_NullAssignmentRestoresDefaultConfig()
        {
            var snapshot = new AppConfigSnapshot
            {
                WslAppServer = null
            };

            Assert.That(snapshot.WslAppServer, Is.Not.Null);
            Assert.That(snapshot.WslAppServer.DistributionName, Is.EqualTo(WslAppServerConfigSnapshot.DefaultDistributionName));
            Assert.That(snapshot.WslAppServer.CodexCommand, Is.EqualTo(WslAppServerConfigSnapshot.DefaultCodexCommand));
        }

        [Test]
        public void WslAppServer_BlankCommandsFallbackToDefaults()
        {
            var config = new WslAppServerConfigSnapshot
            {
                DistributionName = null,
                CodexCommand = " ",
                ShellCommand = ""
            };

            Assert.That(config.DistributionName, Is.EqualTo(WslAppServerConfigSnapshot.DefaultDistributionName));
            Assert.That(config.CodexCommand, Is.EqualTo(WslAppServerConfigSnapshot.DefaultCodexCommand));
            Assert.That(config.ShellCommand, Is.EqualTo(WslAppServerConfigSnapshot.DefaultShellCommand));
        }
    }
}
