using System;
using System.IO;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class BrowserControlPersistenceTests
    {
        [Test]
        public void MasterSwitchRoundtripsAndLegacyAndOtherTargetsRemainIndependent()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "browser-settings-" + Guid.NewGuid().ToString("N"));
            var local = new JsonAppConfigStore(new AppConfigService(Path.Combine(root, "windows")));
            var remote = new JsonAppConfigStore(new AppConfigService(Path.Combine(root, "remote")));
            Assert.That(local.Load().BrowserControlEnabled, Is.Null);
            local.Save(new AppConfigSnapshot { BrowserControlEnabled = true });
            Assert.That(local.Load().BrowserControlEnabled, Is.True);
            Assert.That(remote.Load().BrowserControlEnabled, Is.Null);
            local.Save(new AppConfigSnapshot { BrowserControlEnabled = false });
            Assert.That(local.Load().BrowserControlEnabled, Is.False);
        }
    }
}
