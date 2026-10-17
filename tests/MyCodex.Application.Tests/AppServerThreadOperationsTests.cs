using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerThreadOperationsTests
    {
        [Test]
        public async Task ForkThreadAsync_WithLastTurnId_SendsAnchorAndRemembersFork()
        {
            var transportCallCount = 0;
            string method = null;
            IDictionary<string, object> parameters = null;
            string rememberedThreadId = null;
            var operations = CreateOperations(
                new AppServerProtocolCapabilities { SupportsThreadForkLastTurnId = true, SupportsThreadHistoryPagination = true },
                (requestMethod, requestParameters, cancellationToken, timeoutMs) =>
                {
                    transportCallCount++;
                    method = requestMethod;
                    parameters = (IDictionary<string, object>)requestParameters;
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        {
                            "thread",
                            new Dictionary<string, object>
                            {
                                { "id", "fork-1" },
                                { "name", "Fork" },
                                { "cwd", @"E:\Repo" }
                            }
                        }
                    });
                },
                threadId => rememberedThreadId = threadId);

            var result = await operations.ForkThreadAsync("source-1", "turn-2");

            Assert.That(transportCallCount, Is.EqualTo(1));
            Assert.That(method, Is.EqualTo("thread/fork"));
            Assert.That(parameters["threadId"], Is.EqualTo("source-1"));
            Assert.That(parameters["lastTurnId"], Is.EqualTo("turn-2"));
            Assert.That(parameters["excludeTurns"], Is.True);
            Assert.That(result.ThreadId, Is.EqualTo("fork-1"));
            Assert.That(result.WorkingDirectory, Is.EqualTo(@"E:\Repo"));
            Assert.That(rememberedThreadId, Is.EqualTo("fork-1"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public async Task ForkThreadAsync_WithMissingLastTurnId_OmitsAnchor(string lastTurnId)
        {
            var transportCallCount = 0;
            IDictionary<string, object> parameters = null;
            var operations = CreateOperations(
                new AppServerProtocolCapabilities { SupportsThreadForkLastTurnId = true },
                (requestMethod, requestParameters, cancellationToken, timeoutMs) =>
                {
                    transportCallCount++;
                    parameters = (IDictionary<string, object>)requestParameters;
                    return ForkResult("fork-1");
                });

            await operations.ForkThreadAsync("source-1", lastTurnId);

            Assert.That(transportCallCount, Is.EqualTo(1));
            Assert.That(parameters.ContainsKey("lastTurnId"), Is.False);
            Assert.That(parameters.ContainsKey("excludeTurns"), Is.False);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ReadThread_UsesNegotiatedHistoryCapability(bool pagination)
        {
            var methods = new List<string>();
            var operations = CreateOperations(new AppServerProtocolCapabilities { SupportsThreadHistoryPagination = pagination },
                (method, args, token, timeout) =>
                {
                    var parameters = (IDictionary<string, object>)args;
                    if (method == "thread/read")
                    {
                        methods.Add((bool)parameters["includeTurns"] ? "thread/read+turns" : "thread/read");
                        return ForkResult("thread");
                    }
                    methods.Add(method);
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "data", new object[0] }, { "nextCursor", null }
                    });
                });
            Assert.That((await operations.ReadThreadAsync("thread", CancellationToken.None)).ThreadId, Is.EqualTo("thread"));
            // Paginating runtimes try one complete read first; this reply has no turns, so paging confirms it.
            Assert.That(methods, Is.EqualTo(pagination
                ? new[] { "thread/read+turns", "thread/read", "thread/turns/list" }
                : new[] { "thread/read+turns" }));
        }

        [Test]
        public void ForkThreadAsync_WithUnsupportedLastTurnId_FailsBeforeTransport()
        {
            var transportCallCount = 0;
            var operations = CreateOperations(
                new AppServerProtocolCapabilities
                {
                    ProtocolProfile = "modern",
                    SupportsThreadForkLastTurnId = false
                },
                (requestMethod, requestParameters, cancellationToken, timeoutMs) =>
                {
                    transportCallCount++;
                    return ForkResult("fork-1");
                });

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await operations.ForkThreadAsync("source-1", "turn-2"));
            Assert.That(transportCallCount, Is.EqualTo(0));
        }

        [TestCase(@"E:\Repo", "on-request", "danger-full-access", MyCodex.Domain.Enums.ApprovalMode.FullAccessAutoApprove)]
        [TestCase(@"E:\Repo", "on-request", "danger-full-access", MyCodex.Domain.Enums.ApprovalMode.FullAccess)]
        [TestCase("/home/user/repo", "on-request", "workspace-write", MyCodex.Domain.Enums.ApprovalMode.Auto)]
        [TestCase("/Users/user/repo", "on-request", "read-only", MyCodex.Domain.Enums.ApprovalMode.ReadOnly)]
        [TestCase("/srv/repo", "on-request", "read-only", MyCodex.Domain.Enums.ApprovalMode.Custom)]
        public async Task Fork_UsesSettingsAndExistingPathMapping(string path, string approval, string sandbox, MyCodex.Domain.Enums.ApprovalMode mode)
        {
            IDictionary<string, object> sent = null;
            var operations = CreateOperations(new AppServerProtocolCapabilities { SupportsThreadForkLastTurnId = true },
                (method, parameters, ct, timeout) => { sent = (IDictionary<string, object>)parameters; return ForkResult("fork-1"); },
                mapPath: value => "mapped:" + value);
            await operations.ForkThreadAsync("source", "turn-2", path, mode, "configured-model", "high");
            Assert.That(sent["cwd"], Is.EqualTo("mapped:" + path));
            Assert.That(sent["model"], Is.EqualTo("configured-model"));
            Assert.That(sent["approvalPolicy"], Is.EqualTo(approval));
            Assert.That(sent["approvalsReviewer"], Is.EqualTo("user"));
            Assert.That(sent["sandbox"], Is.EqualTo(sandbox));
            Assert.That(((IDictionary<string, object>)sent["config"])["model_reasoning_effort"], Is.EqualTo("high"));
            Assert.That(sent.ContainsKey("effort"), Is.False);
            Assert.That(sent.ContainsKey("experimentalRawEvents"), Is.False);
            Assert.That(sent.ContainsKey("persistExtendedHistory"), Is.False);
        }

        [Test]
        public void ReadDetails_PreservesEachTurnStatusIncludingEmptyTurns()
        {
            var details = AppServerThreadMapper.BuildThreadDetails(new Dictionary<string, object>
            {
                { "id", "source" },
                { "turns", new object[]
                    {
                        new Dictionary<string, object> { { "id", "one" }, { "status", "completed" } },
                        new Dictionary<string, object> { { "id", "two" }, { "status", "inProgress" } }
                    }
                }
            }, path => path);
            Assert.That(details.TurnStatuses["one"], Is.EqualTo("completed"));
            Assert.That(details.TurnStatuses["two"], Is.EqualTo("inProgress"));
        }

        [TestCase(@"C:\sessions\missing.jsonl")]
        [TestCase("/home/user/.codex/sessions/missing.jsonl")]
        [TestCase("/Users/user/.codex/sessions/missing.jsonl")]
        [TestCase("/srv/.codex/sessions/missing.jsonl")]
        public async Task ConfirmMissing_UsesTargetMetadataAndAllSourcesOnEveryPage(string path)
        {
            var requests = 0;
            var operations = CreateOperations(new AppServerProtocolCapabilities(), (method, args, ct, timeout) =>
            {
                var parameters = (IDictionary<string, object>)args;
                if (method == "thread/list")
                {
                    requests++;
                    Assert.That((string[])parameters["sourceKinds"], Does.Contain("appServer").And.Contain("subAgentThreadSpawn"));
                    Assert.That(parameters.ContainsKey("cwd"), Is.False);
                    Assert.That((string[])parameters["modelProviders"], Is.Empty);
                }
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "data", new object[0] },
                    { "nextCursor", method == "thread/list" && !parameters.ContainsKey("cursor") ? "next" : null }
                });
            });
            var missing = await operations.ConfirmThreadMissingAsync("missing", path, actual =>
            {
                Assert.That(actual, Is.EqualTo(path));
                throw new InvalidOperationException("not found (os error 2)");
            });
            Assert.That(missing, Is.True);
            Assert.That(requests, Is.EqualTo(4));
        }

        [TestCase("stored")]
        [TestCase("archived")]
        [TestCase("loaded")]
        [TestCase("file")]
        [TestCase("denied")]
        [TestCase("transport")]
        [TestCase("malformed")]
        public async Task ConfirmMissing_DoesNotTreatExistingOrUncertainThreadsAsAbsent(string scenario)
        {
            var operations = CreateOperations(new AppServerProtocolCapabilities(), (method, args, ct, timeout) =>
            {
                if (scenario == "transport") throw new InvalidOperationException("disconnected");
                var parameters = (IDictionary<string, object>)args;
                object[] data = new object[0];
                if (method == "thread/list" && (scenario == "stored" || (scenario == "archived" && (bool)parameters["archived"])))
                    data = new object[] { new Dictionary<string, object> { { "id", "missing" } } };
                if (method == "thread/loaded/list" && scenario == "loaded") data = new object[] { "missing" };
                return Task.FromResult<IDictionary<string, object>>(scenario == "malformed" ? new Dictionary<string, object>() :
                    new Dictionary<string, object> { { "data", data } });
            });
            Func<Task<bool>> probe = () => operations.ConfirmThreadMissingAsync("missing", "/sessions/missing.jsonl", path =>
            {
                if (scenario == "file") return Task.FromResult(new AppServerFileSystemMetadata { IsFile = true });
                throw new InvalidOperationException(scenario == "denied" ? "denied (os error 5)" : "not found (os error 2)");
            });
            if (scenario == "denied" || scenario == "transport") Assert.ThrowsAsync<InvalidOperationException>(async () => await probe());
            else Assert.That(await probe(), Is.False);
        }

        private static AppServerThreadOperations CreateOperations(
            AppServerProtocolCapabilities capabilities,
            AppServerRequestInvoker requestInvoker,
            Action<string> rememberLoadedThreadId = null,
            Func<string, string> mapPath = null)
        {
            return new AppServerThreadOperations(
                () => { },
                () => capabilities,
                requestInvoker,
                (path, parameterName) => mapPath == null ? path : mapPath(path),
                path => path,
                (threadId, workspacePath, approvalMode) => Task.CompletedTask,
                threadId => Task.CompletedTask,
                () => Task.FromResult<IReadOnlyList<string>>(new string[0]),
                (limit, cancellationToken) => Task.FromResult(new List<AppServerThreadSummary>()),
                (request, cancellationToken) => Task.FromResult<AppServerConversationSummary>(null),
                () => new string[0],
                rememberLoadedThreadId ?? (threadId => { }),
                threadId => { });
        }

        private static Task<IDictionary<string, object>> ForkResult(string threadId)
        {
            return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
            {
                {
                    "thread",
                    new Dictionary<string, object>
                    {
                        { "id", threadId }
                    }
                }
            });
        }
    }
}
