using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationCoreSshIntegrationTests
    {
        [Test]
        [Explicit("Requires a deployed native core and MYCODEX_AUTH_CORE_SSH_TEST_HOST/USER. Uses an isolated remote HOME.")]
        public async Task NativeSshGuiSwitchesRoutesAndSharesMcpWithoutTouchingUserHome()
        {
            var config = new RemoteMacSshConfigSnapshot
            {
                HostName = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_SSH_TEST_HOST"),
                UserName = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_SSH_TEST_USER"),
                PrivateKeyPath = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_SSH_TEST_KEY"),
                ShellCommand = "/bin/sh"
            };
            Assert.That(config.HostName, Is.Not.Null.And.Not.Empty);
            Assert.That(config.UserName, Is.Not.Null.And.Not.Empty);
            var executor = new RemoteSshCommandExecutor();
            var root = "/tmp/mycodex-auth-bridge-" + Guid.NewGuid().ToString("N");
            var created = await executor.ExecuteAsync(config,
                "set -eu\numask 077\nmkdir \"$1\" \"$1/codex\"\n" +
                "mkdir -p \"$1/.local/share/MyCodex/auth-core/bin\"\n" +
                "ln -s \"$HOME/.local/share/MyCodex/auth-core/bin/mycodex-auth-host\" " +
                "\"$1/.local/share/MyCodex/auth-core/bin/mycodex-auth-host\"\n" +
                "cp \"$HOME/.local/share/MyCodex/auth-core/bin/mycodex-auth-host.manifest.json\" " +
                "\"$1/.local/share/MyCodex/auth-core/bin/\"",
                "mycodex-auth-test-create", 30000, CancellationToken.None, root);
            Assert.That(created.ExitCode, Is.Zero);
            config.ShellCommand = "env HOME=" + root + " CODEX_HOME=" + root + "/codex /bin/sh";
            var host = new RemoteAuthenticationHost(config);
            var client = new AuthenticationCenterClient("SSH isolated", host.RequestAsync);
            var coordinator = new AuthenticationApplyCoordinator(client,
                new RuntimeOperationGate().TryEnterTransition, () => Task.FromResult(true));
            var started = false;
            try
            {
                var initial = await client.ReadAsync(CancellationToken.None);
                started = true;
                Assert.That(initial.CodexHome, Does.EndWith(root + "/codex"));
                Assert.That(initial.Providers, Is.Empty);
                Assert.That((await client.ReadPresetsAsync(CancellationToken.None)).Any(p => p.Id == "custom"), Is.True);
                var direct = (await coordinator.SaveAsync(Edit("direct", "responses"), CancellationToken.None)).Provider;
                Assert.That(direct.GlobalApplied, Is.True);
                var routed = await client.SaveAsync(Edit("routed", "chat_completions"), CancellationToken.None);
                var mcp = await client.ReadMcpAsync(CancellationToken.None);
                await client.SaveMcpAsync("probe", new Dictionary<string, object>
                {
                    { "command", "/bin/echo" }, { "args", new[] { "synthetic" } }
                }, true, mcp.Version, CancellationToken.None);
                await coordinator.ApplyAsync(routed, CancellationToken.None);
                var active = await client.ReadAsync(CancellationToken.None);
                Assert.That(active.RouteRequired && active.RouterRunning, Is.True);
                var live = await executor.ExecuteAsync(config, "cat \"$1/config.toml\"",
                    "mycodex-auth-test-live", 30000, CancellationToken.None, root + "/codex");
                Assert.That(live.ExitCode, Is.Zero);
                Assert.That(live.StandardOutput, Does.Contain("127.0.0.1").And.Contain("mcp_servers.probe"));
                await coordinator.ApplyAsync(active.Providers.Single(p => p.Id == "direct"), CancellationToken.None);
                var reopened = new AuthenticationCenterClient("SSH isolated",
                    new RemoteAuthenticationHost(config).RequestAsync);
                active = await reopened.ReadAsync(CancellationToken.None);
                Assert.That(active.CurrentProviderId, Is.EqualTo("direct"));
                Assert.That(active.RouteRequired || active.RouterRunning, Is.False);
                mcp = await reopened.ReadMcpAsync(CancellationToken.None);
                Assert.That(mcp.Servers.Single().Id, Is.EqualTo("probe"));
                await reopened.DeleteMcpAsync("probe", mcp.Version, CancellationToken.None);
                live = await executor.ExecuteAsync(config, "cat \"$1/config.toml\"",
                    "mycodex-auth-test-live", 30000, CancellationToken.None, root + "/codex");
                Assert.That(live.ExitCode, Is.Zero);
                Assert.That(live.StandardOutput, Does.Not.Contain("127.0.0.1").And.Not.Contain("mcp_servers.probe"));
            }
            finally
            {
                if (started) await host.RequestAsync("backend/shutdown", new Dictionary<string, object>(), CancellationToken.None);
                var cleaned = await executor.ExecuteAsync(config,
                    "set -eu\ncase \"$1\" in /tmp/mycodex-auth-bridge-*) rm -rf -- \"$1\";; *) exit 2;; esac",
                    "mycodex-auth-test-clean", 30000, CancellationToken.None, root);
                Assert.That(cleaned.ExitCode, Is.Zero);
            }
        }

        private static AuthenticationProviderEdit Edit(string id, string kind)
        {
            return new AuthenticationProviderEdit
            {
                Id = id, Name = id, Kind = kind, PresetId = "custom",
                BaseUrl = "https://example.invalid/v1", ApiKey = "synthetic-test-key", Model = id + "-model"
            };
        }
    }
}
