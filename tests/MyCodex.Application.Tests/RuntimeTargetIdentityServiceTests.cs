using System.Collections.Generic;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RuntimeTargetIdentityServiceTests
    {
        [Test]
        public void ResolveIdentity_DistinguishesWindowsSourcesAndCustomExecutables()
        {
            var service = new RuntimeTargetIdentityService();
            var bundled = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Bundled,
                WindowsShellPreference = WindowsShellPreferences.PowerShell7
            });
            var system = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.System,
                WindowsShellPreference = WindowsShellPreferences.WindowsPowerShell
            });
            var customA = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Custom,
                CodexCustomExecutablePath = @"C:\Tools\Codex\codex.exe"
            });
            var customAWithDifferentCase = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Custom,
                CodexCustomExecutablePath = @"c:\tools\codex\CODEX.EXE"
            });
            var customB = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Custom,
                CodexCustomExecutablePath = @"C:\Other\codex.exe"
            });

            Assert.That(bundled, Is.Not.EqualTo(system));
            Assert.That(customA, Is.EqualTo(customAWithDifferentCase));
            Assert.That(customA, Is.Not.EqualTo(customB));
        }

        [Test]
        public void ResolveIdentity_WslUsesDistributionButIgnoresCommandsAndHomeStrategy()
        {
            var service = new RuntimeTargetIdentityService();
            var first = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Wsl,
                WslAppServer = new WslAppServerConfigSnapshot
                {
                    DistributionName = "Ubuntu",
                    ShellCommand = "/bin/sh",
                    CodexCommand = "codex",
                    CodexHomeStrategy = WslCodexHomeStrategy.WslDefault
                }
            });
            var sameTarget = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Wsl,
                WslAppServer = new WslAppServerConfigSnapshot
                {
                    DistributionName = "ubuntu",
                    ShellCommand = "/bin/bash",
                    CodexCommand = "/opt/codex"
                }
            });
            var otherDistribution = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Wsl,
                WslAppServer = new WslAppServerConfigSnapshot { DistributionName = "Debian" }
            });

            Assert.That(first, Is.EqualTo(sameTarget));
            Assert.That(first, Is.Not.EqualTo(otherDistribution));
        }

        [Test]
        public void ResolveIdentity_SshProfilesUseProfileIdAndIgnoreSecretOrConnectionEdits()
        {
            var service = new RuntimeTargetIdentityService();
            var first = service.ResolveIdentity(ProfileConfig("server-a", "first.example", "secret-a"));
            var sameProfile = service.ResolveIdentity(ProfileConfig("SERVER-A", "second.example", "secret-b"));
            var otherProfile = service.ResolveIdentity(ProfileConfig("server-b", "first.example", "secret-a"));

            Assert.That(first, Is.EqualTo(sameProfile));
            Assert.That(first, Is.Not.EqualTo(otherProfile));
            Assert.That(first, Does.Not.Contain("secret"));
            Assert.That(first, Does.Not.Contain("example"));
        }

        [Test]
        public void ResolveIdentity_LegacySshUsesNormalizedUserHostAndPortButIgnoresPassword()
        {
            var service = new RuntimeTargetIdentityService();
            var first = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.RemoteMacSsh,
                RemoteMacSsh = new RemoteMacSshConfigSnapshot
                {
                    UserName = "Alice",
                    HostName = "Example.COM",
                    Port = 2200,
                    ProtectedPassword = "secret-a",
                    ShellCommand = "zsh"
                }
            });
            var sameTarget = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                RemoteMacSsh = new RemoteMacSshConfigSnapshot
                {
                    UserName = "alice",
                    HostName = "example.com",
                    Port = 2200,
                    ProtectedPassword = "secret-b",
                    ShellCommand = "bash"
                }
            });
            var otherPort = service.ResolveIdentity(new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                RemoteMacSsh = new RemoteMacSshConfigSnapshot
                {
                    UserName = "alice",
                    HostName = "example.com",
                    Port = 22
                }
            });

            Assert.That(first, Is.EqualTo(sameTarget));
            Assert.That(first, Is.Not.EqualTo(otherPort));
            Assert.That(first, Does.Not.Contain("secret"));
        }

        private static AppConfigSnapshot ProfileConfig(string profileId, string hostName, string protectedPassword)
        {
            return new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                SelectedRemoteUnixSshProfileId = profileId,
                RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                {
                    new RemoteUnixSshProfileConfigSnapshot
                    {
                        Id = profileId,
                        Ssh = new RemoteMacSshConfigSnapshot
                        {
                            UserName = "alice",
                            HostName = hostName,
                            Port = 22,
                            ProtectedPassword = protectedPassword
                        }
                    }
                }
            };
        }
    }
}
