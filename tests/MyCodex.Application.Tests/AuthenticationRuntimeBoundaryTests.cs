using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class AuthenticationRuntimeBoundaryTests
    {
        [TestCase("initialize", false)]
        [TestCase("model/list", false)]
        [TestCase("account/read", false)]
        [TestCase("config/read", false)]
        [TestCase("fs/watch", false)]
        [TestCase("turn/start", true)]
        [TestCase("command/exec", true)]
        [TestCase("config/batchWrite", true)]
        [TestCase("config/mcpServer/reload", true)]
        [TestCase("account/login/start", true)]
        [TestCase("unknown/mutation", true)]
        public void InitializationAndObservationsRemainAvailableButWritesRequireTheGate(string method, bool expected)
        {
            Assert.That(AppServerClient.RequiresAuthenticationGate(method), Is.EqualTo(expected));
        }

        [Test]
        public void TransitionBlocksCommandsTurnsCompactionRefreshAndAutomationBeforeAnyOtherWork()
        {
            var client = (AppServerClient)FormatterServices.GetUninitializedObject(typeof(AppServerClient));
            var automation = (AutomationRunner)FormatterServices.GetUninitializedObject(typeof(AutomationRunner));
            using (var transition = RuntimeOperationGate.Shared.TryEnterTransition())
            {
                Assert.That(transition, Is.Not.Null);
                AssertBlocked(() => client.ExecuteCommandAsync("unused", "unused", ApprovalMode.FullAccess, "unused", 1, (id, text) => { }));
                AssertBlocked(() => client.SendUserMessageAsync("unused", "unused", null, null, null, null));
                AssertBlocked(() => client.SubmitCompactThreadRequestAsync("unused", "unused", null, null));
                AssertBlocked(() => client.GetAuthenticationStateAsync(true));
                AssertBlocked(() => automation.StartAsync(null));
            }
        }

        private static void AssertBlocked(Func<Task> action)
        {
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(async () => await action());
            Assert.That(error.Code, Is.EqualTo("runtime_busy"));
        }

        [Test]
        public async Task ManagedMcpImportAndRetryReachTransportWhileOtherOperationsStayBlocked()
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var runtime = (AppServerClient)FormatterServices.GetUninitializedObject(typeof(AppServerClient));
            var send = (AppServerRequestInvoker)Delegate.CreateDelegate(typeof(AppServerRequestInvoker), runtime,
                typeof(AppServerClient).GetMethod("SendRequestAsync", flags, null,
                    new[] { typeof(string), typeof(object), typeof(CancellationToken), typeof(int) }, null));
            var reloads = 0;
            var failReload = true;
            var writes = 0;
            var writer = new ReplyWriter();
            var transport = new AppServerProtocolTransport(() => writer, new JavaScriptSerializer(),
                new SemaphoreSlim(1), new object(), (method, ms) => new TimeoutException());
            writer.Reply = json =>
            {
                var request = AppServerJsonValue.DeserializeDictionary(json);
                var reload = (string)request["method"] == "config/mcpServer/reload";
                if (reload)
                {
                    reloads++;
                    Assert.That(RuntimeOperationGate.Shared.TryEnterTransition(), Is.Null);
                    Assert.Throws<AuthenticationCenterException>(() => RuntimeOperationGate.Shared.EnterOperation());
                    AssertBlocked(() => send("config/batchWrite", new Dictionary<string, object>(), CancellationToken.None, 1000));
                }
                string error;
                transport.TryHandleResponse(new Dictionary<string, object>
                {
                    { "id", request["id"] },
                    { reload && failReload ? "error" : "result", reload && failReload
                        ? new Dictionary<string, object> { { "message", "Synthetic reload failure" } }
                        : new Dictionary<string, object>() }
                }, out error);
            };
            typeof(AppServerClient).GetField("_protocolTransport", flags).SetValue(runtime, transport);
            typeof(AppServerClient).GetField("_operations", flags).SetValue(runtime,
                new AppServerClientOperations(() => { }, () => new AppServerProtocolCapabilities { SupportsMcpManagement = true },
                    send, (path, name) => path, path => path, path => path, (a, b, c) => c, (a, b, c) => { }, a => { }));
            var backend = new AuthenticationCenterClient("Test", async (method, args, token) =>
            {
                await Task.Yield(); // Ownership must survive asynchronous backend I/O.
                if (method == "gui/mcp/list") return new Dictionary<string, object>
                    { { "version", "v1" }, { "servers", new object[0] } };
                writes++;
                return new Dictionary<string, object> { { "imported", 1 } };
            });
            var coordinator = new AuthenticationMcpCoordinator(backend, runtime, RuntimeOperationGate.Shared.TryEnterTransition);
            await coordinator.ReadAsync(CancellationToken.None);
            Assert.That(await coordinator.ImportAsync(CancellationToken.None), Is.False);
            Assert.That(reloads, Is.EqualTo(1), "Reload must reach the transport, not be rejected by its own transition.");
            failReload = false;
            Assert.That(await coordinator.RetryReloadAsync(CancellationToken.None), Is.True);
            Assert.That(reloads, Is.EqualTo(2));
            Assert.That(writes, Is.EqualTo(1), "Retry must never repeat the import.");
            Assert.That(coordinator.ReloadPending, Is.False);
        }

        [Test]
        public async Task UnrelatedMcpReloadRemainsBlockedDuringTransition()
        {
            var runtime = (AppServerClient)FormatterServices.GetUninitializedObject(typeof(AppServerClient));
            var send = (AppServerRequestInvoker)Delegate.CreateDelegate(typeof(AppServerRequestInvoker), runtime,
                typeof(AppServerClient).GetMethod("SendRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(string), typeof(object), typeof(CancellationToken), typeof(int) }, null));
            using (var transition = RuntimeOperationGate.Shared.TryEnterTransition())
            {
                Assert.That(transition, Is.Not.Null);
                Task otherCaller;
                using (ExecutionContext.SuppressFlow())
                    otherCaller = Task.Run(() => AssertBlocked(() => send("config/mcpServer/reload",
                        new Dictionary<string, object>(), CancellationToken.None, 1000)));
                await otherCaller;
            }
        }

        [Test]
        public async Task ExpiredOwnerCannotEnterLaterTransitionAndPendingReloadKeepsGateBusy()
        {
            var gate = new RuntimeOperationGate();
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task inheritedCaller;
            using (var first = gate.TryEnterTransition())
            {
                inheritedCaller = Task.Run(async () =>
                {
                    await resume.Task;
                    Assert.Throws<AuthenticationCenterException>(() => gate.EnterOperation(true));
                });
            }
            using (var second = gate.TryEnterTransition())
            {
                Assert.That(second, Is.Not.Null);
                resume.SetResult(true);
                await inheritedCaller;
                using (var reload = gate.EnterOperation(true))
                {
                    second.Dispose();
                    Assert.That(gate.TryEnterTransition(), Is.Null, "An in-flight reload still protects the runtime.");
                }
            }
            using (var next = gate.TryEnterTransition()) Assert.That(next, Is.Not.Null);
        }

        private sealed class ReplyWriter : StringWriter
        {
            public Action<string> Reply;
            public override Task WriteLineAsync(string value)
            {
                Reply(value);
                return Task.CompletedTask;
            }
        }

        [Test]
        public void PendingLoginHandlesEarlyCompletionAndOnlyMatchingIdsReleaseIt()
        {
            var client = (AppServerClient)FormatterServices.GetUninitializedObject(typeof(AppServerClient));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(AppServerClient).GetField("_syncRoot", flags).SetValue(client, new object());
            typeof(AppServerClient).GetField("_authenticationEarlyLoginCompletions", flags)
                .SetValue(client, new HashSet<string>(StringComparer.Ordinal));
            var start = typeof(AppServerClient).GetMethod("StartAuthenticationLoginWithGateAsync", flags);
            var complete = typeof(AppServerClient).GetMethod("ObserveAuthenticationLoginCompletion", flags);
            var pending = typeof(AppServerClient).GetField("_authenticationLoginPending", flags);

            var response = new TaskCompletionSource<AppServerLoginResult>();
            var first = (Task<AppServerLoginResult>)start.Invoke(client,
                new object[] { new Func<Task<AppServerLoginResult>>(() => response.Task) });
            complete.Invoke(client, new object[] { "unrelated-old-login" });
            Assert.That(pending.GetValue(client), Is.True);
            complete.Invoke(client, new object[] { "first-login" });
            response.SetResult(new AppServerLoginResult { LoginId = "first-login" });
            first.GetAwaiter().GetResult();
            Assert.That(pending.GetValue(client), Is.False);

            var second = (Task<AppServerLoginResult>)start.Invoke(client,
                new object[] { new Func<Task<AppServerLoginResult>>(() => Task.FromResult(new AppServerLoginResult { LoginId = "second-login" })) });
            second.GetAwaiter().GetResult();
            complete.Invoke(client, new object[] { "first-login" });
            Assert.That(pending.GetValue(client), Is.True);
            complete.Invoke(client, new object[] { "second-login" });
            Assert.That(pending.GetValue(client), Is.False);
        }
    }
}
