using System.Collections.Generic;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficeRuntimeScopeServiceTests
    {
        private readonly OfficeRuntimeScopeService _service = new OfficeRuntimeScopeService();

        [TestCase(CodexRuntimeSources.Bundled)]
        [TestCase(CodexRuntimeSources.System)]
        [TestCase(CodexRuntimeSources.Custom)]
        public void ResolveTarget_WindowsRuntimeSourcesShareWindowsScope(string runtimeSource)
        {
            var target = _service.ResolveTarget(new AppConfigSnapshot
            {
                CodexRuntimeSource = runtimeSource
            });

            Assert.That(target.RuntimeScopeId, Is.EqualTo("windows"));
            Assert.That(target.HostPlatform, Is.EqualTo(AppServerHostPlatform.Windows));
            Assert.That(target.RuntimeSource, Is.EqualTo(runtimeSource));
            Assert.That(target.UsesUnixPaths, Is.False);
        }

        [Test]
        public void ResolveTarget_WslDistributionsKeepSeparateOfficeScopes()
        {
            var ubuntu = CreateWslConfig(" Ubuntu ");
            var debian = CreateWslConfig("Debian");

            Assert.That(_service.ResolveTarget(ubuntu).RuntimeScopeId, Is.EqualTo("wsl|ubuntu"));
            Assert.That(_service.ResolveTarget(debian).RuntimeScopeId, Is.EqualTo("wsl|debian"));
            Assert.That(_service.ResolveTarget(ubuntu).RuntimeScopeId,
                Is.Not.EqualTo(_service.ResolveTarget(debian).RuntimeScopeId));
            Assert.That(_service.ResolveTarget(ubuntu).UsesUnixPaths, Is.True);
        }

        [Test]
        public void ResolveTarget_RemoteProfileIdWinsAndSecretFieldsNeverEnterScope()
        {
            var config = new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                SelectedRemoteUnixSshProfileId = " Work-PROFILE ",
                RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                {
                    new RemoteUnixSshProfileConfigSnapshot
                    {
                        Id = " Work-PROFILE ",
                        Ssh = new RemoteMacSshConfigSnapshot
                        {
                            HostName = "secret-host.example",
                            UserName = "secret-user",
                            Port = 2200,
                            ProtectedPassword = "protected-secret",
                            PrivateKeyPath = @"C:\secret\id_ed25519",
                            AuthenticationMode = RemoteSshAuthenticationMode.Password,
                            ShellCommand = "secret-shell-command"
                        }
                    }
                }
            };

            var target = _service.ResolveTarget(config);

            Assert.That(target.RuntimeScopeId, Is.EqualTo("remote-unix|work-profile"));
            Assert.That(target.HostPlatform, Is.EqualTo(AppServerHostPlatform.RemoteMac));
            Assert.That(target.RuntimeScopeId, Does.Not.Contain("secret"));
            Assert.That(target.RuntimeScopeId, Does.Not.Contain("password"));
            Assert.That(target.RuntimeScopeId, Does.Not.Contain("ed25519"));
        }

        [Test]
        public void ResolveTarget_LegacyRemoteConfigUsesDeterministicUserHostPortFallback()
        {
            var config = new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.RemoteMacSsh,
                RemoteMacSsh = new RemoteMacSshConfigSnapshot
                {
                    UserName = " Alice ",
                    HostName = " EXAMPLE.COM ",
                    Port = 2222,
                    ProtectedPassword = "do-not-use"
                }
            };

            var target = _service.ResolveTarget(config);

            Assert.That(target.RuntimeScopeId, Is.EqualTo("remote-unix|alice|example.com|2222"));
        }

        [Test]
        public void ResolveTarget_RemoteProfilesKeepSeparateOfficeScopes()
        {
            var config = new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                {
                    new RemoteUnixSshProfileConfigSnapshot { Id = "profile-a" },
                    new RemoteUnixSshProfileConfigSnapshot { Id = "profile-b" }
                }
            };

            config.SelectedRemoteUnixSshProfileId = "profile-a";
            var profileA = _service.ResolveTarget(config);
            config.SelectedRemoteUnixSshProfileId = "profile-b";
            var profileB = _service.ResolveTarget(config);

            Assert.That(profileA.RuntimeScopeId, Is.EqualTo("remote-unix|profile-a"));
            Assert.That(profileB.RuntimeScopeId, Is.EqualTo("remote-unix|profile-b"));
            Assert.That(profileA.RuntimeScopeId, Is.Not.EqualTo(profileB.RuntimeScopeId));
        }

        [TestCase("bUnDlEd", "windows", AppServerHostPlatform.Windows, CodexRuntimeSources.Bundled)]
        [TestCase("wSl", "wsl|ubuntu", AppServerHostPlatform.Wsl, CodexRuntimeSources.Wsl)]
        [TestCase("rEmOtEmAcSsH", "remote-unix|profile-a", AppServerHostPlatform.RemoteMac, CodexRuntimeSources.RemoteMacSsh)]
        [TestCase("rEmOtEuNiXsSh", "remote-unix|profile-a", AppServerHostPlatform.RemoteMac, CodexRuntimeSources.RemoteUnixSsh)]
        [TestCase("unknown", "windows", AppServerHostPlatform.Windows, CodexRuntimeSources.Bundled)]
        public void ResolveTarget_CanonicalizesSourceWithoutMergingTargetIdentity(
            string runtimeSource,
            string expectedScopeId,
            AppServerHostPlatform expectedHostPlatform,
            string expectedRuntimeSource)
        {
            var config = new AppConfigSnapshot
            {
                CodexRuntimeSource = runtimeSource,
                WslAppServer = new WslAppServerConfigSnapshot
                {
                    DistributionName = "Ubuntu"
                },
                SelectedRemoteUnixSshProfileId = "profile-a",
                RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                {
                    new RemoteUnixSshProfileConfigSnapshot
                    {
                        Id = "profile-a",
                        Ssh = new RemoteMacSshConfigSnapshot()
                    }
                }
            };

            var target = _service.ResolveTarget(config);

            Assert.That(target.RuntimeScopeId, Is.EqualTo(expectedScopeId));
            Assert.That(target.HostPlatform, Is.EqualTo(expectedHostPlatform));
            Assert.That(target.RuntimeSource, Is.EqualTo(expectedRuntimeSource));
        }

        private static AppConfigSnapshot CreateWslConfig(string distributionName)
        {
            return new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Wsl,
                WslAppServer = new WslAppServerConfigSnapshot
                {
                    DistributionName = distributionName
                }
            };
        }
    }
}
