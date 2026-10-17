using MyCodex.Domain.Enums;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCommandLinePolicyTests
    {
        [Test]
        public void WindowsPolicy_UsesCmdShell()
        {
            var policy = AppServerCommandLinePolicy.ForHostPlatform(AppServerHostPlatform.Windows, null);

            var command = policy.BuildStandaloneCommand("git status --short");

            Assert.That(command, Is.EqualTo(new[] { "cmd.exe", "/c", "git status --short" }));
        }

        [Test]
        public void WindowsPolicy_CanUsePowerShell7()
        {
            var policy = AppServerCommandLinePolicy.ForHostPlatform(
                AppServerHostPlatform.Windows,
                WindowsShellPreferences.PowerShell7,
                null);

            var command = policy.BuildStandaloneCommand("$PSVersionTable.PSVersion");

            Assert.That(command, Is.EqualTo(new[] { "pwsh.exe", "-NoLogo", "-NoProfile", "-Command", "$PSVersionTable.PSVersion" }));
        }

        [Test]
        public void WindowsPolicy_CanUseWindowsPowerShell()
        {
            var policy = AppServerCommandLinePolicy.ForHostPlatform(
                AppServerHostPlatform.Windows,
                WindowsShellPreferences.WindowsPowerShell,
                null);

            var command = policy.BuildStandaloneCommand("$PSVersionTable.PSVersion");

            Assert.That(command, Is.EqualTo(new[] { "powershell.exe", "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", "$PSVersionTable.PSVersion" }));
        }

        [Test]
        public void WslPolicy_UsesDefaultShell()
        {
            var policy = AppServerCommandLinePolicy.ForHostPlatform(AppServerHostPlatform.Wsl, null);

            var command = policy.BuildStandaloneCommand("pwd");

            Assert.That(command, Is.EqualTo(new[] { "/bin/sh", "-lc", "pwd" }));
        }

        [Test]
        public void WslPolicy_UsesConfiguredShell()
        {
            var policy = AppServerCommandLinePolicy.ForHostPlatform(AppServerHostPlatform.Wsl, "bash");

            var command = policy.BuildStandaloneCommand("npm test");

            Assert.That(command, Is.EqualTo(new[] { "bash", "-lc", "npm test" }));
        }

        [Test]
        public void WslPolicy_PreservesCommandText()
        {
            var policy = AppServerCommandLinePolicy.ForHostPlatform(AppServerHostPlatform.Wsl, "/bin/sh");

            var command = policy.BuildStandaloneCommand("printf \"hello world\"");

            Assert.That(command[2], Is.EqualTo("printf \"hello world\""));
        }
    }
}
