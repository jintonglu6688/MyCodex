using System.Collections.Generic;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteSshCommandLineTests
    {
        [Test]
        public void BuildSshArguments_WithWindowsEscapingEdgeCases_ReturnsCompleteArgumentList()
        {
            var arguments = RemoteSshCommandLine.BuildSshArguments(new RemoteMacSshConfigSnapshot
            {
                HostName = "remote.example",
                UserName = "tester",
                Port = 2222,
                AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey,
                PrivateKeyPath = @"C:\Keys\team key\"
            }, "zsh -lc 'printf \"a\"'");

            Assert.That(
                arguments,
                Is.EqualTo("-T -o StrictHostKeyChecking=accept-new -o ServerAliveInterval=30 -p 2222 -o BatchMode=yes -i \"C:\\Keys\\team key\\\\\" tester@remote.example \"zsh -lc 'printf \\\"a\\\"'\""));
        }

        [Test]
        public void BuildPlinkArgumentsWithCommandFile_WithSensitivePaths_ReturnsCompleteArgumentList()
        {
            var arguments = RemoteSshCommandLine.BuildPlinkArgumentsWithCommandFile(
                new RemoteMacSshConfigSnapshot
                {
                    HostName = "remote.example",
                    UserName = "tester",
                    Port = 2222,
                    AuthenticationMode = RemoteSshAuthenticationMode.Password,
                    HostKeyFingerprint = "SHA256:abc123"
                },
                @"C:\Temp Files\mycodex-command.tmp",
                @"C:\Temp Files\mycodex-password.tmp");

            Assert.That(
                arguments,
                Is.EqualTo("-ssh -batch -T -P 2222 -l tester -pwfile \"C:\\Temp Files\\mycodex-password.tmp\" -m \"C:\\Temp Files\\mycodex-command.tmp\" -hostkey SHA256:abc123 remote.example"));
        }

        [Test]
        public void BuildLocalForwardArguments_ForPrivateKey_AddsLoopbackForwardAndBatchMode()
        {
            var arguments = RemoteSshCommandLine.BuildLocalForwardArguments(new RemoteMacSshConfigSnapshot
            {
                HostName = "192.168.50.2",
                UserName = "tonglujin",
                Port = 2222,
                AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey,
                PrivateKeyPath = @"C:\Users\tester\.ssh\id_ed25519"
            }, 43210, 43210);

            Assert.That(arguments, Does.Contain("-L localhost:43210:localhost:43210"));
            Assert.That(arguments, Does.Contain("-o BatchMode=yes"));
            Assert.That(arguments, Does.Contain("-p 2222"));
            Assert.That(arguments, Does.Contain("tonglujin@192.168.50.2"));
        }

        [Test]
        public void BuildReverseForwardArguments_ForPrivateKey_BindsRemoteLoopback()
        {
            var arguments = RemoteSshCommandLine.BuildReverseForwardArguments(new RemoteMacSshConfigSnapshot
            {
                HostName = "192.168.50.2",
                UserName = "tonglujin",
                Port = 2222,
                AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey,
                PrivateKeyPath = @"C:\Users\tester\.ssh\id_ed25519"
            }, 39442, 38442);

            Assert.That(arguments, Does.Contain("-N -T"));
            Assert.That(arguments, Does.Contain("-o ExitOnForwardFailure=yes"));
            Assert.That(arguments, Does.Contain("-R 127.0.0.1:39442:127.0.0.1:38442"));
            Assert.That(arguments, Does.Contain("-o BatchMode=yes"));
            Assert.That(arguments, Does.Contain("tonglujin@192.168.50.2"));
        }

        [Test]
        public void BuildSshArguments_ForPassword_DoesNotEnableOpenSshConnectionSharing()
        {
            var arguments = RemoteSshCommandLine.BuildSshArguments(new RemoteMacSshConfigSnapshot
            {
                HostName = "192.168.50.2",
                UserName = "tonglujin",
                Port = 2222,
                AuthenticationMode = RemoteSshAuthenticationMode.Password
            }, "zsh -lc 'pwd'");

            Assert.That(arguments, Does.Contain("-o BatchMode=no"));
            Assert.That(arguments, Does.Not.Contain("ControlMaster"));
            Assert.That(arguments, Does.Not.Contain("ControlPersist"));
            Assert.That(arguments, Does.Not.Contain("ControlPath"));
        }

        [Test]
        public void BuildSshArguments_ForPrivateKeyWithHostKeyFingerprint_UsesOpenSshKnownHostsPolicy()
        {
            var arguments = RemoteSshCommandLine.BuildSshArguments(new RemoteMacSshConfigSnapshot
            {
                HostName = "192.168.50.2",
                UserName = "tester",
                Port = 2222,
                AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey,
                PrivateKeyPath = @"C:\Users\tester\.ssh\id_ed25519",
                HostKeyFingerprint = "SHA256:abc123"
            }, "zsh -lc 'pwd'");

            Assert.That(arguments, Does.Contain("StrictHostKeyChecking=accept-new"));
            Assert.That(arguments, Does.Not.Contain("-hostkey"));
            Assert.That(arguments, Does.Not.Contain("SHA256:abc123"));
        }

        [Test]
        public void CreateOpenSshConfigFile_ForPrivateKeyWithHostKeyFingerprint_DoesNotEmbedFingerprint()
        {
            var path = RemoteSshSensitiveCommandFile.CreateOpenSshConfigFile(new RemoteMacSshConfigSnapshot
            {
                HostName = "192.168.50.2",
                UserName = "tester",
                Port = 2222,
                AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey,
                PrivateKeyPath = @"C:\Users\tester\.ssh\id_ed25519",
                HostKeyFingerprint = "SHA256:abc123"
            }, "zsh -lc 'codex app-server'");

            try
            {
                var text = System.IO.File.ReadAllText(path);

                Assert.That(text, Does.Contain("StrictHostKeyChecking accept-new"));
                Assert.That(text, Does.Not.Contain("SHA256:abc123"));
                Assert.That(text, Does.Contain("RemoteCommand"));
            }
            finally
            {
                RemoteSshPasswordFile.TryDelete(path);
            }
        }

        [Test]
        public void BuildPlinkArguments_ForPassword_UsesPasswordFileAndHostKey()
        {
            var arguments = RemoteSshCommandLine.BuildPlinkArguments(new RemoteMacSshConfigSnapshot
            {
                HostName = "192.168.50.2",
                UserName = "tonglujin",
                Port = 2222,
                AuthenticationMode = RemoteSshAuthenticationMode.Password,
                HostKeyFingerprint = "SHA256:abc123"
            }, "bash -lc 'codex --version'", @"C:\Temp\pw.txt");

            Assert.That(arguments, Does.Contain("-ssh -batch -T"));
            Assert.That(arguments, Does.Contain("-P 2222"));
            Assert.That(arguments, Does.Contain("-l tonglujin"));
            Assert.That(arguments, Does.Contain("-pwfile C:\\Temp\\pw.txt"));
            Assert.That(arguments, Does.Contain("-hostkey SHA256:abc123"));
            Assert.That(arguments, Does.Contain("192.168.50.2"));
        }

        [Test]
        public void BuildPlinkReverseForwardArguments_ForPassword_BindsRemoteLoopback()
        {
            var arguments = RemoteSshCommandLine.BuildPlinkReverseForwardArguments(new RemoteMacSshConfigSnapshot
            {
                HostName = "192.168.50.2",
                UserName = "tonglujin",
                Port = 2222,
                AuthenticationMode = RemoteSshAuthenticationMode.Password,
                HostKeyFingerprint = "SHA256:abc123"
            }, 39442, 38442, @"C:\Temp\pw.txt");

            Assert.That(arguments, Does.Contain("-ssh -batch -N -T"));
            Assert.That(arguments, Does.Contain("-R 127.0.0.1:39442:127.0.0.1:38442"));
            Assert.That(arguments, Does.Contain("-pwfile C:\\Temp\\pw.txt"));
            Assert.That(arguments, Does.Contain("-hostkey SHA256:abc123"));
        }

        [Test]
        public void BuildSshArgumentsWithConfigFile_DoesNotIncludeRemoteCommandText()
        {
            var arguments = RemoteSshCommandLine.BuildSshArgumentsWithConfigFile(
                @"C:\Temp\mycodex-ssh-command.cfg",
                "mycodex-remote-appserver");

            Assert.That(arguments, Does.Contain("-F C:\\Temp\\mycodex-ssh-command.cfg"));
            Assert.That(arguments, Does.Contain("mycodex-remote-appserver"));
            Assert.That(arguments, Does.Not.Contain("MYCODEX_COMPAT_API_KEY"));
            Assert.That(arguments, Does.Not.Contain("sk-test"));
            Assert.That(arguments, Does.Not.Contain("codex app-server"));
        }

        [Test]
        public void BuildPlinkArgumentsWithCommandFile_DoesNotIncludeRemoteCommandText()
        {
            var arguments = RemoteSshCommandLine.BuildPlinkArgumentsWithCommandFile(
                new RemoteMacSshConfigSnapshot
                {
                    HostName = "192.168.50.2",
                    UserName = "tester",
                    Port = 2222,
                    AuthenticationMode = RemoteSshAuthenticationMode.Password,
                    HostKeyFingerprint = "SHA256:abc123"
                },
                @"C:\Temp\mycodex-ssh-command.tmp",
                @"C:\Temp\mycodex-ssh-password.tmp");

            Assert.That(arguments, Does.Contain("-m C:\\Temp\\mycodex-ssh-command.tmp"));
            Assert.That(arguments, Does.Contain("-pwfile C:\\Temp\\mycodex-ssh-password.tmp"));
            Assert.That(arguments, Does.Contain("-hostkey SHA256:abc123"));
            Assert.That(arguments, Does.Not.Contain("MYCODEX_COMPAT_API_KEY"));
            Assert.That(arguments, Does.Not.Contain("sk-test"));
            Assert.That(arguments, Does.Not.Contain("codex app-server"));
        }
    }
}
