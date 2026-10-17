using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationMcpCoordinatorTests
    {
        [Test]
        public async Task ArchivedDisabledServersRemainEditableAndRuntimeOnlyEntriesAreReadOnly()
        {
            var calls = new List<string>();
            var runtime = new McpRuntimeStub
            {
                Servers = new List<AppServerMcpServerStatus>
                {
                    new AppServerMcpServerStatus { ServerName = "external", ConfigValues = new Dictionary<string, object> { { "command", "external" } } }
                }
            };
            var coordinator = new AuthenticationMcpCoordinator(Client((method, args) =>
            {
                calls.Add(method);
                return Snapshot();
            }), runtime, new RuntimeOperationGate().TryEnterTransition);
            var rows = await coordinator.ReadAsync(CancellationToken.None);
            Assert.That(rows.Single(s => s.ServerName == "managed").Enabled, Is.False);
            Assert.That(rows.Single(s => s.ServerName == "managed").ConfigValues["command"], Is.EqualTo("original"));
            Assert.That(rows.Single(s => s.ServerName == "external").ConfigValues, Is.Null);
            Assert.That(coordinator.IsManaged("external"), Is.False);
            Assert.That(calls, Is.EqualTo(new[] { "gui/mcp/list" }));
            Assert.That(runtime.ReloadCount, Is.Zero);
        }

        [Test]
        public async Task ToggleUsesArchiveVersionAndPreservesUneditedFieldsUnderRuntimeLease()
        {
            var gate = new RuntimeOperationGate();
            var writes = 0;
            var runtime = new McpRuntimeStub { OnReload = () => Assert.That(gate.TryEnterTransition(), Is.Null) };
            var coordinator = new AuthenticationMcpCoordinator(Client((method, args) =>
            {
                if (method == "gui/mcp/list") return Snapshot();
                Assert.That(method, Is.EqualTo("gui/mcp/save"));
                Assert.That(args["expectedVersion"], Is.EqualTo("v1"));
                Assert.That(args["enabled"], Is.True);
                Assert.That(((IDictionary<string, object>)args["config"])["env"], Is.TypeOf<Dictionary<string, object>>());
                Assert.That(gate.TryEnterTransition(), Is.Null);
                writes++;
                return new Dictionary<string, object> { { "saved", true } };
            }), runtime, gate.TryEnterTransition);
            await coordinator.ReadAsync(CancellationToken.None);
            Assert.That(await coordinator.WriteAsync("managed", new[] { AppServerConfigWriteEdit.CreateReplace("mcp_servers.managed.enabled", true) }, CancellationToken.None), Is.True);
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(runtime.ReloadCount, Is.EqualTo(1));
            Assert.That(coordinator.CanWrite, Is.False, "Must refresh the version after a mutation.");
        }

        [TestCase("version_conflict", false)]
        [TestCase("ipc_timeout", true)]
        [TestCase("operation_unknown", true)]
        public async Task FailedMutationNeverRetriesAndDistinguishesPrewriteConflict(string code, bool unknown)
        {
            var calls = 0;
            var runtime = new McpRuntimeStub();
            var coordinator = new AuthenticationMcpCoordinator(Client((method, args) =>
            {
                if (method == "gui/mcp/list") return Snapshot();
                calls++;
                throw new AuthenticationCenterException(code);
            }), runtime, new RuntimeOperationGate().TryEnterTransition);
            await coordinator.ReadAsync(CancellationToken.None);
            var failure = Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.DeleteAsync("managed", CancellationToken.None));
            Assert.That(failure.Code, Is.EqualTo(unknown ? "operation_unknown" : code));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(coordinator.ReloadPending, Is.EqualTo(unknown));
            Assert.That(coordinator.CanWrite, Is.False);
            Assert.That(runtime.ReloadCount, Is.Zero);
        }

        [Test]
        public async Task ReloadFailureKeepsSuccessfulSaveAndRetryOnlyReloads()
        {
            var writes = 0;
            var runtime = new McpRuntimeStub { OnReload = () => throw new InvalidOperationException() };
            var coordinator = new AuthenticationMcpCoordinator(Client((method, args) =>
            {
                if (method == "gui/mcp/list") return Snapshot();
                writes++;
                return new Dictionary<string, object> { { "imported", 1 } };
            }), runtime, new RuntimeOperationGate().TryEnterTransition);
            await coordinator.ReadAsync(CancellationToken.None);
            Assert.That(await coordinator.ImportAsync(CancellationToken.None), Is.False);
            Assert.That(coordinator.ReloadPending, Is.True);
            runtime.OnReload = null;
            Assert.That(await coordinator.RetryReloadAsync(CancellationToken.None), Is.True);
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(coordinator.ReloadPending, Is.False);
        }

        [Test]
        public async Task BusyRuntimeCannotModifyManagedMcp()
        {
            var coordinator = new AuthenticationMcpCoordinator(Client((method, args) =>
            {
                Assert.That(method, Is.EqualTo("gui/mcp/list"));
                return Snapshot();
            }), new McpRuntimeStub(), () => null);
            await coordinator.ReadAsync(CancellationToken.None);
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.ImportAsync(CancellationToken.None)).Code, Is.EqualTo("runtime_busy"));
        }

        [TestCase("Wsl")]
        [TestCase("RemoteMacSsh")]
        [TestCase("RemoteUnixSsh")]
        public void NativeMcpFactoryNeverRedirectsRemoteTargetToLocal(string source)
        {
            Assert.That(AuthenticationCenterClient.CreateMcpManagementClient(source), Is.Null);
        }

        [TestCase("Wsl")]
        [TestCase("RemoteMacSsh")]
        [TestCase("RemoteUnixSsh")]
        [NonParallelizable]
        public void NativeMcpFactoryUsesConfiguredTargetWithoutOptIn(string source)
        {
            var previous = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_PATH");
            try
            {
                Environment.SetEnvironmentVariable("MYCODEX_AUTH_CORE_PATH", "unused-local-path.exe");
                Assert.That(AuthenticationCenterClient.CreateMcpManagementClient(source), Is.Null);
                var client = AuthenticationCenterClient.CreateMcpManagementClient(source,
                    new WslAppServerConfigSnapshot { DistributionName = "isolated" },
                    new RemoteMacSshConfigSnapshot { HostName = "example.invalid" });
                Assert.That(client, Is.Not.Null);
                Environment.SetEnvironmentVariable("MYCODEX_AUTH_CORE_PATH", null);
                Assert.That(AuthenticationCenterClient.CreateMcpManagementClient(source,
                    new WslAppServerConfigSnapshot(), new RemoteMacSshConfigSnapshot()), Is.Not.Null);
            }
            finally { Environment.SetEnvironmentVariable("MYCODEX_AUTH_CORE_PATH", previous); }
        }

        private static AuthenticationCenterClient Client(Func<string, IDictionary<string, object>, IDictionary<string, object>> handler)
        {
            return new AuthenticationCenterClient("Test", (method, args, token) => Task.FromResult(handler(method, args)));
        }

        private static IDictionary<string, object> Snapshot()
        {
            return new Dictionary<string, object>
            {
                { "version", "v1" }, { "servers", new object[] { new Dictionary<string, object>
                    {
                        { "id", "managed" }, { "enabled", false },
                        { "config", new Dictionary<string, object>
                            {
                                { "command", "original" }, { "env", new Dictionary<string, object> { { "TOKEN", "synthetic" } } }
                            }
                        }
                    }
                } }
            };
        }
    }

    internal sealed class McpRuntimeStub : IAppServerMcpClient
    {
        public List<AppServerMcpServerStatus> Servers = new List<AppServerMcpServerStatus>();
        public int ReloadCount;
        public Action OnReload;
        public Task<List<AppServerMcpServerStatus>> ListMcpServerStatusesAsync() => Task.FromResult(Servers);
        public Task ReloadMcpServerConfigAsync() { ReloadCount++; OnReload?.Invoke(); return Task.CompletedTask; }
        public Task<AppServerMcpServerOauthLoginResult> StartMcpServerOauthLoginAsync(string serverName, int? timeoutSecs = null) => throw new NotSupportedException();
        public Task<AppServerMcpResourceReadResult> ReadMcpResourceAsync(string server, string uri, string threadId = null, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        public Task<AppServerMcpToolCallResult> CallMcpToolAsync(AppServerMcpToolCallRequest request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
    }
}
