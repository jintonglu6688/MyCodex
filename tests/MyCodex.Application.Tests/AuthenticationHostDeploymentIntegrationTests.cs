using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationHostDeploymentIntegrationTests
    {
        [TestCase("/bin/zsh")]
        [TestCase("/bin/bash")]
        [Explicit("Requires MYCODEX_AUTH_CORE_SSH_TEST_HOST/USER and optional KEY. Installs only in a temporary HOME.")]
        public Task InstallAndUpdatePreserveShellEnvironmentAndCleanUpFailures(string shell)
        {
            var config = new RemoteMacSshConfigSnapshot
            {
                HostName = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_SSH_TEST_HOST"),
                UserName = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_SSH_TEST_USER"),
                PrivateKeyPath = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_SSH_TEST_KEY"),
                ShellCommand = shell
            };
            Assert.That(config.HostName, Is.Not.Null.And.Not.Empty);
            Assert.That(config.UserName, Is.Not.Null.And.Not.Empty);
            return VerifyDeploymentAsync(config);
        }

        internal static async Task VerifyDeploymentAsync(RemoteMacSshConfigSnapshot config)
        {
            var executor = new RemoteSshCommandExecutor();
            var token = CancellationToken.None;
            var shell = config.ShellCommand;
            var home = "/tmp/mycodex-auth-deployment-" + Guid.NewGuid().ToString("N");
            await CheckAsync(executor, config, "set -eu\numask 077\nmkdir \"$1\" \"$1/codex\"", home);
            try
            {
                config.ShellCommand = "env HOME=" + home + " CODEX_HOME=" + home + "/codex " + shell;
                var archive = await AuthenticationHostDeployment.ProbeRemoteAsync(config, executor, token);
                var input = await AuthenticationHostRelease.ReadVerifiedBase64Async(archive, token);
                await AuthenticationHostDeployment.InstallRemoteAsync(config, executor, archive, false, token, input);
                await AssertCleanAsync(executor, config);
                await CheckAsync(executor, config,
                    "printf previous > \"$HOME/.local/share/MyCodex/auth-core/bin/RUNTIME.md\"");

                // Updating a normal package with no hidden files must work under zsh too.
                await AuthenticationHostDeployment.InstallRemoteAsync(config, executor, archive, true, token, input);
                await AuthenticationHostDeployment.FinalizeRemoteAsync(config, executor, archive, true, token);
                await CheckAsync(executor, config,
                    "[ \"$(cat \"$HOME/.local/share/MyCodex/auth-core/bin/RUNTIME.md\")\" = previous ]");
                await AuthenticationHostDeployment.InstallRemoteAsync(config, executor, archive, true, token, input);
                await AuthenticationHostDeployment.FinalizeRemoteAsync(config, executor, archive, false, token);
                await CheckAsync(executor, config,
                    "[ ! -e \"$HOME/.local/share/MyCodex/auth-core/.previous\" ]");
                await AssertCleanAsync(executor, config);

                var error = Assert.ThrowsAsync<AuthenticationCenterException>(async () =>
                    await AuthenticationHostDeployment.InstallRemoteAsync(config, executor, archive, true, token, "AAAA"));
                Assert.That(error.Code, Is.EqualTo("backend_integrity_failed"));
                await AssertCleanAsync(executor, config);

                await CheckAsync(executor, config,
                    "touch \"$HOME/.local/share/MyCodex/auth-core/bin/.unexpected\"");
                error = Assert.ThrowsAsync<AuthenticationCenterException>(async () =>
                    await AuthenticationHostDeployment.InstallRemoteAsync(config, executor, archive, true, token, input));
                Assert.That(error.Code, Is.EqualTo("backend_install_unsafe"));
                await CheckAsync(executor, config,
                    "set -eu\n[ -f \"$HOME/.local/share/MyCodex/auth-core/bin/.unexpected\" ]\n" +
                    "rm \"$HOME/.local/share/MyCodex/auth-core/bin/.unexpected\"");
                await AssertCleanAsync(executor, config);

                // A held lock must survive the rejected attempt, including a broken stdin pipe.
                await CheckAsync(executor, config, "mkdir \"$HOME/.local/share/MyCodex/auth-core/.install-lock\"");
                error = Assert.ThrowsAsync<AuthenticationCenterException>(async () =>
                    await AuthenticationHostDeployment.InstallRemoteAsync(config, executor, archive, true, token, input));
                Assert.That(error.Code, Is.EqualTo("backend_install_busy"));
                await CheckAsync(executor, config,
                    "rmdir \"$HOME/.local/share/MyCodex/auth-core/.install-lock\"");
                await AssertCleanAsync(executor, config);
            }
            finally
            {
                config.ShellCommand = shell;
                await CheckAsync(executor, config,
                    "set -eu\ncase \"$1\" in /tmp/mycodex-auth-deployment-*) ;; *) exit 2;; esac\n" +
                    "[ -d \"$1\" ] && [ ! -L \"$1\" ]\nrm -rf -- \"$1\"", home);
            }
        }

        private static Task AssertCleanAsync(RemoteSshCommandExecutor executor, RemoteMacSshConfigSnapshot config)
        {
            return CheckAsync(executor, config,
                "set -eu\nroot=\"$HOME/.local/share/MyCodex/auth-core\"\n" +
                "[ ! -e \"$root/.install-lock\" ]\n" +
                "[ -z \"$(find \"$root\" -maxdepth 1 -name '.stage.*' -print)\" ]");
        }

        private static async Task CheckAsync(RemoteSshCommandExecutor executor,
            RemoteMacSshConfigSnapshot config, string script, params string[] arguments)
        {
            var result = await executor.ExecuteAsync(config, script, "mycodex-auth-deployment-test",
                30000, CancellationToken.None, arguments);
            Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        }
    }
}
