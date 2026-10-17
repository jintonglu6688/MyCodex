using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using NUnit.Framework;
using System.Collections.Generic;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RuntimeProfileServiceTests
    {
        [Test]
        public void GetCurrentRuntimeProfileId_ReturnsWindowsProfile_ForNonWslRuntime()
        {
            var service = new RuntimeProfileService();

            var runtimeProfileId = service.GetCurrentRuntimeProfileId(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Bundled
            });

            Assert.That(runtimeProfileId, Is.EqualTo(RuntimeProfileService.WindowsRuntimeProfileId));
        }

        [Test]
        public void GetCurrentRuntimeProfileId_ReturnsWindowsProfile_ForRemovedBundledRuntime()
        {
            var service = new RuntimeProfileService();

            var runtimeProfileId = service.GetCurrentRuntimeProfileId(new AppConfigSnapshot
            {
                CodexRuntimeSource = "Bundled" + "Compatible"
            });

            Assert.That(runtimeProfileId, Is.EqualTo(RuntimeProfileService.WindowsRuntimeProfileId));
        }

        [Test]
        public void GetCurrentRuntimeProfileId_ReturnsDistinctWslProfile()
        {
            var service = new RuntimeProfileService();

            var runtimeProfileId = service.GetCurrentRuntimeProfileId(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Wsl,
                WslAppServer = new WslAppServerConfigSnapshot()
            });

            Assert.That(runtimeProfileId, Is.EqualTo("wsl|ubuntu"));
        }

        [Test]
        public void GetCurrentRuntimeProfileId_ReturnsDistinctWslProfile_WhenWslUsesOwnHome()
        {
            var service = new RuntimeProfileService();

            var runtimeProfileId = service.GetCurrentRuntimeProfileId(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Wsl,
                WslAppServer = new WslAppServerConfigSnapshot
                {
                    DistributionName = "Ubuntu-24.04",
                    CodexHomeStrategy = WslCodexHomeStrategy.WslDefault
                }
            });

            Assert.That(runtimeProfileId, Is.EqualTo("wsl|ubuntu-24.04"));
        }

        [Test]
        public void IsSameRuntimeProfile_TreatsBlankProfileAsWindowsProfile()
        {
            var service = new RuntimeProfileService();

            var isSame = service.IsSameRuntimeProfile(null, RuntimeProfileService.WindowsRuntimeProfileId);

            Assert.That(isSame, Is.True);
        }

        [Test]
        public void GetCurrentRuntimeProfileId_UsesSelectedRemoteUnixProfileId()
        {
            var service = new RuntimeProfileService();

            var runtimeProfileId = service.GetCurrentRuntimeProfileId(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                SelectedRemoteUnixSshProfileId = "linux-dev",
                RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                {
                    new RemoteUnixSshProfileConfigSnapshot
                    {
                        Id = "mac-mini",
                        Name = "Mac mini",
                        Ssh = new RemoteMacSshConfigSnapshot { HostName = "192.168.50.2" }
                    },
                    new RemoteUnixSshProfileConfigSnapshot
                    {
                        Id = "linux-dev",
                        Name = "Linux dev",
                        Ssh = new RemoteMacSshConfigSnapshot { HostName = "192.168.50.8" }
                    }
                }
            });

            Assert.That(runtimeProfileId, Is.EqualTo("remote-unix|linux-dev"));
        }
    }
}
