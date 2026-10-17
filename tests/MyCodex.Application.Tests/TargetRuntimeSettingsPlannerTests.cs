using System;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetRuntimeSettingsPlannerTests
    {
        private readonly TargetRuntimeSettingsPlanner _planner = new TargetRuntimeSettingsPlanner();

        [Test]
        public void NameOnlyAndInactiveFields_DoNotReconnectWindowsOrMutateDraftInput()
        {
            var target = new TargetProfileSnapshot { Id = "windows", Kind = "Windows", Name = "PC" };
            var config = new AppConfigSnapshot { EnableAutomaticChatReading = true };
            var draft = _planner.Build(target, config);
            draft.Name = "Renamed";
            draft.CodexCustomExecutablePath = "inactive-custom.exe";
            draft.WslAppServer.CodexCommand = "inactive-wsl";
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.False);
            Assert.That(config.WslAppServer.CodexCommand, Is.EqualTo("codex"));
            _planner.Apply(target, config, draft);
            Assert.That(config.EnableAutomaticChatReading, Is.True);
            Assert.That(target.Name, Is.EqualTo("PC"));
        }

        [Test]
        public void WindowsRuntimeOrShellChange_ReconnectsUsingExistingComparison()
        {
            var target = new TargetProfileSnapshot { Id = "windows", Kind = "Windows", Name = "PC" };
            var config = new AppConfigSnapshot();
            var draft = _planner.Build(target, config);
            draft.CodexRuntimeSource = "System";
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.True);
            draft.CodexRuntimeSource = "Custom";
            draft.CodexCustomExecutablePath = "C:\\Tools\\codex.exe";
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.True);
            draft = _planner.Build(target, config);
            draft.WindowsShellPreference = WindowsShellPreferences.PowerShell7;
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.True);
        }

        [Test]
        public void InstalledBundledUpdate_RequiresExplicitReconnectWithoutPersistingTheSignal()
        {
            var target = new TargetProfileSnapshot { Id = "windows", Kind = "Windows", Name = "PC" };
            var config = new AppConfigSnapshot();
            var draft = _planner.Build(target, config);
            var revision = draft.ExpectedRuntimeRevision;
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.False);
            draft.BundledRuntimeUpdated = true;
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.True);
            _planner.Apply(target, config, draft);
            var saved = _planner.Build(target, config);
            Assert.That(saved.BundledRuntimeUpdated, Is.False);
            Assert.That(saved.ExpectedRuntimeRevision, Is.EqualTo(revision));
            Assert.That(config.CodexRuntimeSource, Is.EqualTo("Bundled"));
        }

        [TestCase("Windows", "System")]
        [TestCase("Windows", "Custom")]
        [TestCase("Wsl", "Wsl")]
        [TestCase("Ssh", "RemoteUnixSsh")]
        [TestCase("Ssh", "RemoteMacSsh")]
        public void InstalledBundledUpdate_IsRejectedForOtherLaunchFamiliesBeforeMutation(string kind, string source)
        {
            var target = new TargetProfileSnapshot { Kind = kind, Name = "Target", WslDistributionName = "Ubuntu" };
            var config = new AppConfigSnapshot { CodexRuntimeSource = source };
            config.WslAppServer.DistributionName = "Ubuntu";
            var draft = _planner.Build(target, config);
            draft.BundledRuntimeUpdated = true;
            Assert.Throws<ArgumentException>(() => _planner.RequiresReconnect(target, config, draft));
            Assert.Throws<ArgumentException>(() => _planner.Apply(target, config, draft));
            Assert.That(config.CodexRuntimeSource, Is.EqualTo(source));
        }

        [Test]
        public void WslCommands_AreCaseSensitiveAndIdentityCannotChange()
        {
            var target = new TargetProfileSnapshot { Kind = "Wsl", Name = "Linux", WslDistributionName = "Debian" };
            var config = new AppConfigSnapshot { CodexRuntimeSource = "Wsl", WslAppServer = new WslAppServerConfigSnapshot { DistributionName = "Debian" } };
            var draft = _planner.Build(target, config);
            draft.Name = "Renamed";
            draft.WslAppServer.DistributionName = "debian";
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.False);
            draft.WslAppServer.CodexCommand = "Codex";
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.True);
            draft.WslAppServer.DistributionName = "Ubuntu";
            Assert.Throws<ArgumentException>(() => _planner.Apply(target, config, draft));
        }

        [Test]
        public void SshFamilyAliasDoesNotReconnect_ButSelectedAuthenticationAndCommandChangesDo()
        {
            var target = new TargetProfileSnapshot { Kind = "Ssh", Name = "Build", SshProfileId = "profile" };
            var config = new AppConfigSnapshot { CodexRuntimeSource = "RemoteMacSsh", SelectedRemoteUnixSshProfileId = "profile" };
            config.RemoteUnixSshProfiles.Add(new RemoteUnixSshProfileConfigSnapshot
            {
                Id = "profile", Name = "Build", Ssh = new RemoteMacSshConfigSnapshot { HostName = "host", UserName = "alice", ProtectedPassword = "opaque" }
            });
            var draft = _planner.Build(target, config);
            draft.CodexRuntimeSource = "RemoteUnixSsh";
            draft.Name = "Renamed";
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.False);
            draft.RemoteSsh.ProtectedPassword = "changed-opaque";
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.True);
            draft = _planner.Build(target, config);
            draft.RemoteSsh.CodexCommand = "other-codex";
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.True);
            Assert.That(config.GetSelectedRemoteUnixSshConfig().CodexCommand, Is.EqualTo("codex"));
        }

        [TestCase("old-key", null)]
        [TestCase("new-confirmed-key", "new-confirmed-key")]
        public void SshHostEdit_PreservesProfileAndCredentialsButDropsInheritedHostKey(string requestedKey, string expectedKey)
        {
            var target = new TargetProfileSnapshot { Id = "ssh-test", Kind = "Ssh", Name = "Build", SshProfileId = "profile", RuntimeScopeId = "remote-unix|profile" };
            var config = new AppConfigSnapshot { CodexRuntimeSource = "RemoteUnixSsh", SelectedRemoteUnixSshProfileId = "profile" };
            config.RemoteUnixSshProfiles.Add(new RemoteUnixSshProfileConfigSnapshot
            {
                Id = "profile", Ssh = new RemoteMacSshConfigSnapshot { HostName = "wrong.example", UserName = "alice", ProtectedPassword = "opaque", HostKeyFingerprint = "old-key" }
            });
            var draft = _planner.Build(target, config);
            draft.RemoteSsh.HostName = "correct.example";
            draft.RemoteSsh.HostKeyFingerprint = requestedKey;
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.True);
            _planner.Apply(target, config, draft);
            Assert.That(config.GetSelectedRemoteUnixSshConfig().HostName, Is.EqualTo("correct.example"));
            Assert.That(config.GetSelectedRemoteUnixSshConfig().HostKeyFingerprint, Is.EqualTo(expectedKey));
            Assert.That(config.RemoteMacSsh.HostKeyFingerprint, Is.EqualTo(expectedKey));
            Assert.That(config.GetSelectedRemoteUnixSshConfig().ProtectedPassword, Is.EqualTo("opaque"));
            Assert.That(config.SelectedRemoteUnixSshProfileId, Is.EqualTo("profile"));
            Assert.That(target.Id, Is.EqualTo("ssh-test"));
            Assert.That(target.RuntimeScopeId, Is.EqualTo("remote-unix|profile"));
        }

        [Test]
        public void SshEndpointComparison_NormalizesLiteralIpWithoutResolvingDnsAliases()
        {
            var left = new RemoteMacSshConfigSnapshot { HostName = "[2001:db8::1]", UserName = "alice", Port = 22 };
            var right = new RemoteMacSshConfigSnapshot { HostName = "2001:0db8:0000:0000:0000:0000:0000:0001", UserName = "alice", Port = 22 };
            Assert.That(TargetRuntimeSettingsPlanner.IsSameSshEndpoint(left, right), Is.True);
            right.HostName = "dns-alias";
            Assert.That(TargetRuntimeSettingsPlanner.IsSameSshEndpoint(left, right), Is.False);
        }

        [Test]
        public void EditableRevision_PreservesNewPreferencesButRejectsStaleAuthenticationOrRuntimeChanges()
        {
            var target = new TargetProfileSnapshot { Kind = "Ssh", Name = "Build", SshProfileId = "profile" };
            var config = new AppConfigSnapshot { CodexRuntimeSource = "RemoteUnixSsh", SelectedRemoteUnixSshProfileId = "profile" };
            config.RemoteUnixSshProfiles.Add(new RemoteUnixSshProfileConfigSnapshot
            {
                Id = "profile", Ssh = new RemoteMacSshConfigSnapshot { HostName = "host", UserName = "alice" }
            });
            var draft = _planner.Build(target, config);
            draft.Name = "Rename only";
            config.UiThemePreference = "dark";
            config.EnableAutomaticChatReading = true;
            Assert.That(_planner.RequiresReconnect(target, config, draft), Is.False);
            config.GetSelectedRemoteUnixSshConfig().HostKeyFingerprint = "new-user-confirmed-key";
            Assert.Throws<TargetRuntimeSettingsConflictException>(() => _planner.RequiresReconnect(target, config, draft));
            Assert.Throws<TargetRuntimeSettingsConflictException>(() => _planner.Apply(target, config, draft));
            Assert.That(config.GetSelectedRemoteUnixSshConfig().HostKeyFingerprint, Is.EqualTo("new-user-confirmed-key"));
        }
    }
}
