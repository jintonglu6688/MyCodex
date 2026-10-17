using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Domain.Entities;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WslAuthenticationHostIntegrationTests
    {
        [Test]
        [Explicit("Requires protocol-2 auth-core installed in Ubuntu. Uses only isolated synthetic configuration.")]
        public async Task NativeWslGuiSwitchesRoutesAndSharesManagedMcpInTargetHome()
        {
            var config = new WslAppServerConfigSnapshot { DistributionName = "Ubuntu" };
            var executor = new WslCommandExecutor();
            var root = "/tmp/mycodex-auth-bridge-" + Guid.NewGuid().ToString("N");
            var created = await executor.ExecuteAsync(config,
                "set -eu\nmkdir -m 700 \"$1\" \"$1/codex\"",
                "mycodex-auth-test-create", 10000, CancellationToken.None, root);
            Assert.That(created.ExitCode, Is.Zero);
            var host = new WslAuthenticationHost(config, new IsolatedPathsExecutor(executor, root));
            var client = new AuthenticationCenterClient("WSL isolated", host.RequestAsync);
            var coordinator = new AuthenticationApplyCoordinator(client,
                new RuntimeOperationGate().TryEnterTransition, () => Task.FromResult(true));
            var started = false;
            try
            {
                var initial = await client.ReadAsync(CancellationToken.None);
                started = true;
                Assert.That(initial.CodexHome, Is.EqualTo(root + "/codex"));
                Assert.That(initial.Providers, Is.Empty);
                Assert.That((await client.ReadPresetsAsync(CancellationToken.None)).Any(p => p.Id == "custom"), Is.True);
                var direct = (await coordinator.SaveAsync(new AuthenticationProviderEdit
                {
                    Id = "direct", Name = "Direct", Kind = "responses", PresetId = "custom",
                    BaseUrl = "https://example.invalid/v1", ApiKey = "synthetic-test-key", Model = "direct-model"
                }, CancellationToken.None)).Provider;
                Assert.That(direct.GlobalApplied, Is.True);
                var routed = await client.SaveAsync(new AuthenticationProviderEdit
                {
                    Id = "routed", Name = "Routed", Kind = "chat_completions", PresetId = "custom",
                    BaseUrl = "https://example.invalid/v1", ApiKey = "synthetic-test-key", Model = "routed-model"
                }, CancellationToken.None);
                var mcp = await client.ReadMcpAsync(CancellationToken.None);
                await client.SaveMcpAsync("probe", new Dictionary<string, object>
                {
                    { "command", "/bin/echo" }, { "args", new[] { "synthetic" } }
                }, true, mcp.Version, CancellationToken.None);
                await coordinator.ApplyAsync(routed, CancellationToken.None);
                var active = await client.ReadAsync(CancellationToken.None);
                Assert.That(active.RouteRequired && active.RouterRunning, Is.True);
                var live = await executor.ExecuteAsync(config, "cat \"$1/config.toml\"",
                    "mycodex-auth-test-live", 10000, CancellationToken.None, root + "/codex");
                Assert.That(live.ExitCode, Is.Zero);
                Assert.That(live.StandardOutput, Does.Contain("127.0.0.1").And.Contain("mcp_servers.probe"));
                await coordinator.ApplyAsync(active.Providers.Single(p => p.Id == "direct"), CancellationToken.None);
                active = await client.ReadAsync(CancellationToken.None);
                Assert.That(active.RouteRequired || active.RouterRunning, Is.False);
                var second = new AuthenticationCenterClient("WSL isolated",
                    new WslAuthenticationHost(config, new IsolatedPathsExecutor(executor, root)).RequestAsync);
                Assert.That((await second.ReadAsync(CancellationToken.None)).CurrentProviderId, Is.EqualTo("direct"));
                mcp = await second.ReadMcpAsync(CancellationToken.None);
                Assert.That(mcp.Servers.Single().Id, Is.EqualTo("probe"));
                await second.DeleteMcpAsync("probe", mcp.Version, CancellationToken.None);
                live = await executor.ExecuteAsync(config, "cat \"$1/config.toml\"",
                    "mycodex-auth-test-live", 10000, CancellationToken.None, root + "/codex");
                Assert.That(live.StandardOutput, Does.Not.Contain("127.0.0.1").And.Not.Contain("mcp_servers.probe"));
            }
            finally
            {
                if (started) await host.RequestAsync("backend/shutdown", new Dictionary<string, object>(), CancellationToken.None);
                var cleaned = await executor.ExecuteAsync(config,
                    "set -eu\ncase \"$1\" in /tmp/mycodex-auth-bridge-*) rm -rf -- \"$1\";; *) exit 2;; esac",
                    "mycodex-auth-test-clean", 10000, CancellationToken.None, root);
                Assert.That(cleaned.ExitCode, Is.Zero);
            }
        }

        [Test]
        [Explicit("Requires an Ubuntu WSL distribution with the matching authentication host installed.")]
        public async Task RemoteStartScriptLeavesUnixServiceRunningAfterManagementCommandExits()
        {
            var config = new WslAppServerConfigSnapshot { DistributionName = "Ubuntu" };
            var executor = new WslCommandExecutor();
            var root = "/tmp/mycodex-auth-bridge-" + Guid.NewGuid().ToString("N");
            var created = await executor.ExecuteAsync(config,
                "set -eu\nmkdir -m 700 \"$1\"\nmkdir -m 700 \"$1/codex\" \"$1/private\"",
                "mycodex-auth-test-create", 10000, CancellationToken.None, root);
            Assert.That(created.ExitCode, Is.Zero);
            var host = (await executor.ExecuteAsync(config,
                "printf '%s\\n' \"$HOME/.local/share/MyCodex/" + "auth-core" + "/bin/mycodex-auth-host\"",
                "mycodex-auth-test-path", 10000, CancellationToken.None)).StandardOutput.Trim();
            var started = false;
            try
            {
                var start = await executor.ExecuteAsync(config, RemoteAuthenticationHost.StartScript,
                    "mycodex-auth-test-remote-start", 45000, CancellationToken.None,
                    host, root + "/private", root + "/codex");
                Assert.That(start.ExitCode, Is.Zero, start.StandardError);
                var ready = new JavaScriptSerializer().DeserializeObject(start.StandardOutput) as IDictionary<string, object>;
                Assert.That(ready["event"], Is.EqualTo("ready"));
                started = true;
                var status = await executor.ExecuteAsync(config,
                    "printf '%s' '{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"status\",\"params\":{}}' | " +
                    "\"$1\" rpc --data-dir \"$2\" --codex-home \"$3\"",
                    "mycodex-auth-test-rpc", 15000, CancellationToken.None,
                    host, root + "/private", root + "/codex");
                Assert.That(status.ExitCode, Is.Zero, status.StandardError);
                var response = new JavaScriptSerializer().DeserializeObject(status.StandardOutput) as IDictionary<string, object>;
                Assert.That(response.ContainsKey("result"), Is.True);
                Assert.That(((IDictionary<string, object>)response["result"])["protocolVersion"], Is.EqualTo(2));
            }
            finally
            {
                if (started)
                {
                    await executor.ExecuteAsync(config,
                        "printf '%s' '{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"" + "backend/shutdown" + "\",\"params\":{}}' | " +
                        "\"$1\" rpc --data-dir \"$2\" --codex-home \"$3\"",
                        "mycodex-auth-test-stop", 15000, CancellationToken.None,
                        host, root + "/private", root + "/codex");
                }
                var cleaned = await executor.ExecuteAsync(config,
                    "set -eu\ncase \"$1\" in /tmp/mycodex-auth-bridge-*) rm -rf -- \"$1\";; *) exit 2;; esac",
                    "mycodex-auth-test-clean", 10000, CancellationToken.None, root);
                Assert.That(cleaned.ExitCode, Is.Zero);
            }
        }

        private sealed class IsolatedPathsExecutor : IWslCommandExecutor
        {
            public Task<WslCommandExecutionResult> ExecuteWithInputAsync(
                WslAppServerConfigSnapshot config, string script, string operationName,
                int timeoutMilliseconds, CancellationToken cancellationToken,
                string standardInput, params string[] arguments)
            {
                return _inner.ExecuteWithInputAsync(config, script, operationName, timeoutMilliseconds,
                    cancellationToken, standardInput, arguments);
            }

            private readonly IWslCommandExecutor _inner;
            private readonly string _root;

            internal IsolatedPathsExecutor(IWslCommandExecutor inner, string root)
            {
                _inner = inner;
                _root = root;
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(WslAppServerConfigSnapshot config,
                string script, string operationName, int timeoutMilliseconds, params string[] arguments)
            {
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds,
                    CancellationToken.None, arguments);
            }

            public async Task<WslCommandExecutionResult> ExecuteAsync(WslAppServerConfigSnapshot config,
                string script, string operationName, int timeoutMilliseconds, CancellationToken cancellationToken,
                params string[] arguments)
            {
                if (operationName != "mycodex-auth-paths")
                {
                    return await _inner.ExecuteAsync(config, script, operationName, timeoutMilliseconds,
                        cancellationToken, arguments);
                }
                var result = await _inner.ExecuteAsync(config,
                    "CODEX_HOME=$1\nexport CODEX_HOME\n" + script,
                    operationName, timeoutMilliseconds, cancellationToken, _root + "/codex");
                if (result.ExitCode == 0)
                {
                    var serializer = new JavaScriptSerializer();
                    var paths = serializer.DeserializeObject(result.StandardOutput) as IDictionary<string, object>;
                    paths["dataDir"] = _root + "/private";
                    result.StandardOutput = serializer.Serialize(paths);
                }
                return result;
            }
        }
    }
}
