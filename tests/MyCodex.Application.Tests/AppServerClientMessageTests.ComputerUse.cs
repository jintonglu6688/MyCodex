using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class AppServerClientMessageTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task ComputerUsePreparation_RespectsDisabledSettingBeforeCheckingSelectedRuntime(bool enabled)
        {
            var statuses = new List<string>();
            var config = AppServerJsonValue.CreateSerializer().DeserializeObject(
                "{\"plugins\":{\"computer-use@mycodex-computer-use\":{\"enabled\":" + (enabled ? "true" : "false") + "}}}")
                as IDictionary<string, object>;
            using (var manager = new ComputerUseSessionManager("missing-selected-runtime.exe", "future", "unused-home",
                (method, parameters, token) => Task.FromResult<IDictionary<string, object>>(
                    new Dictionary<string, object>
                    {
                        { "config", config }, { "requirements", null }
                    }), (code, thread) => statuses.Add(code)))
            {
                await manager.PrepareAsync(new Dictionary<string, object> { { "cwd", TestContext.CurrentContext.WorkDirectory } }, CancellationToken.None);
                Assert.That(statuses, Is.EqualTo(new[] { enabled ? "SelectedRuntimeMissing" : "PluginDisabled" }));
            }
        }

        [TestCase("thread/start")]
        [TestCase("thread/resume")]
        [TestCase("thread/fork")]
        [TestCase("turn/start")]
        public async Task ComputerUseUnavailable_PreservesOriginalRequestAndOrdinaryChat(string method)
        {
            var client = CreateClient();
            var statuses = new List<string>();
            var lookups = new List<string>();
            using (var manager = new ComputerUseSessionManager("unused.exe", "unverified", "unused-home",
                (operation, parameters, token) =>
                {
                    lookups.Add(operation);
                    if (operation == "thread/loaded/list")
                        return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "data", new object[0] } });
                    return Task.FromResult<IDictionary<string, object>>(DisabledComputerUseConfig());
                }, (code, thread) => statuses.Add(code)))
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                FormTestDriver.SetField(client, "_computerUse", manager);
                FormTestDriver.SetField(client, "_inputWriter", writer);
                var parameters = new Dictionary<string, object>
                {
                    { "threadId", "original-thread" }, { "cwd", TestContext.CurrentContext.WorkDirectory },
                    { "model", "original-model" }, { "approvalPolicy", "on-request" },
                    { "config", new Dictionary<string, object> { { "model_reasoning_effort", "high" } } }
                };
                var expected = AppServerJsonValue.CreateSerializer().Serialize(parameters);
                try
                {
                    var operation = InvokeComputerUseRequest(client, method, parameters, CancellationToken.None);
                    if (method == "thread/resume") await CompleteResumeProviderReadAsync(client, stream, 1);
                    var request = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, method == "thread/resume" ? 2 : 1));
                    Assert.That(request["method"], Is.EqualTo(method));
                    if (method == "thread/resume")
                        expected = AppServerJsonValue.CreateSerializer().Serialize(new Dictionary<string, object>(parameters) { ["modelProvider"] = "current-provider" });
                    Assert.That(AppServerJsonValue.CreateSerializer().Serialize(AppServerJsonValue.GetDictionary(request, "params")), Is.EqualTo(expected));
                    CompleteRequest(client, Convert.ToString(request["id"], CultureInfo.InvariantCulture),
                        new Dictionary<string, object> { { "resultMarker", "completed" } });
                    Assert.That((await operation)["resultMarker"], Is.EqualTo("completed"));
                    Assert.That(lookups, Does.Contain("config/read"));
                    Assert.That(lookups, Has.All.Matches<string>(lookup => lookup == "config/read" || lookup == "thread/loaded/list"));
                    Assert.That(statuses, Does.Contain("PluginDisabled"));
                }
                finally
                {
                    FormTestDriver.SetField(client, "_computerUse", null);
                    FormTestDriver.SetField(client, "_inputWriter", null);
                    client.Dispose();
                }
            }
        }

        [Test]
        public async Task ComputerUseMetadataFailure_DoesNotBlockOrdinaryTurnStart()
        {
            var client = CreateClient();
            var statuses = new List<string>();
            using (var manager = new ComputerUseSessionManager("unused.exe", "unverified", "unused-home",
                (operation, parameters, token) => Task.FromException<IDictionary<string, object>>(new InvalidOperationException("unsupported lookup")),
                (code, thread) => statuses.Add(code)))
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                FormTestDriver.SetField(client, "_computerUse", manager);
                FormTestDriver.SetField(client, "_inputWriter", writer);
                try
                {
                    var operation = InvokeComputerUseRequest(client, "turn/start",
                        new Dictionary<string, object> { { "threadId", "chat-thread" }, { "cwd", TestContext.CurrentContext.WorkDirectory } }, CancellationToken.None);
                    var request = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 1));
                    Assert.That(request["method"], Is.EqualTo("turn/start"));
                    CompleteRequest(client, Convert.ToString(request["id"], CultureInfo.InvariantCulture), new Dictionary<string, object>());
                    await operation;
                    Assert.That(statuses, Does.Contain("PreparationFailed"));
                }
                finally
                {
                    FormTestDriver.SetField(client, "_computerUse", null);
                    FormTestDriver.SetField(client, "_inputWriter", null);
                    client.Dispose();
                }
            }
        }

        [Test]
        public async Task ComputerUsePreparationCancelled_DoesNotSendOriginalThreadRequest()
        {
            var client = CreateClient();
            var entered = new TaskCompletionSource<bool>();
            using (var cancellation = new CancellationTokenSource())
            using (var manager = new ComputerUseSessionManager("unused.exe", "unverified", "unused-home",
                async (operation, parameters, token) =>
                {
                    entered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, token);
                    return DisabledComputerUseConfig();
                }, (code, thread) => { }))
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                FormTestDriver.SetField(client, "_computerUse", manager);
                FormTestDriver.SetField(client, "_inputWriter", writer);
                try
                {
                    var operation = InvokeComputerUseRequest(client, "thread/start",
                        new Dictionary<string, object> { { "cwd", TestContext.CurrentContext.WorkDirectory } }, cancellation.Token);
                    await entered.Task;
                    cancellation.Cancel();
                    Assert.CatchAsync<OperationCanceledException>(async () => await operation);
                    Assert.That(stream.Length, Is.Zero);
                }
                finally
                {
                    FormTestDriver.SetField(client, "_computerUse", null);
                    FormTestDriver.SetField(client, "_inputWriter", null);
                    client.Dispose();
                }
            }
        }

        [Test]
        public async Task ComputerUsePreparationFromOldConnection_DoesNotWriteIntoReplacementConnection()
        {
            var client = CreateClient();
            var entered = new TaskCompletionSource<bool>();
            var continuePreparation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var manager = new ComputerUseSessionManager("unused.exe", "unverified", "unused-home",
                async (operation, parameters, token) =>
                {
                    entered.TrySetResult(true);
                    await continuePreparation.Task;
                    return DisabledComputerUseConfig();
                }, (code, thread) => { }))
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                FormTestDriver.SetField(client, "_computerUse", manager);
                FormTestDriver.SetField(client, "_inputWriter", writer);
                FormTestDriver.SetField(client, "_connectionEpoch", 1L);
                try
                {
                    var operation = InvokeComputerUseRequest(client, "thread/fork",
                        new Dictionary<string, object> { { "threadId", "old-thread" }, { "cwd", TestContext.CurrentContext.WorkDirectory } }, CancellationToken.None);
                    await entered.Task;
                    FormTestDriver.SetField(client, "_connectionEpoch", 2L);
                    continuePreparation.TrySetResult(true);
                    var failure = Assert.CatchAsync(async () => await operation);
                    Assert.That(failure, Is.TypeOf<OperationCanceledException>().Or.TypeOf<InvalidOperationException>());
                    Assert.That(stream.Length, Is.Zero, "An old target's operation must never reach the replacement runtime.");
                }
                finally
                {
                    FormTestDriver.SetField(client, "_computerUse", null);
                    FormTestDriver.SetField(client, "_inputWriter", null);
                    client.Dispose();
                }
            }
        }

        [TestCase(null)]
        [TestCase("not-a-table")]
        public async Task ComputerUseMalformedConfig_FailsClosedBeforePluginOrComponentWork(object config)
        {
            var lookups = new List<string>();
            var statuses = new List<string>();
            using (var manager = new ComputerUseSessionManager("unused.exe", "unverified", "unused-home",
                (operation, parameters, token) =>
                {
                    lookups.Add(operation);
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "config", config } });
                }, (code, thread) => statuses.Add(code)))
            {
                var binding = await manager.PrepareAsync(new Dictionary<string, object> { { "cwd", TestContext.CurrentContext.WorkDirectory } }, CancellationToken.None);
                Assert.That(binding, Is.Null);
                Assert.That(lookups, Is.EqualTo(new[] { "config/read" }));
                Assert.That(statuses, Is.EqualTo(new[] { "PreparationFailed" }));
            }
        }

        private static IDictionary<string, object> DisabledComputerUseConfig()
        {
            return new Dictionary<string, object>
            {
                { "config", new Dictionary<string, object>
                    { { "plugins", new Dictionary<string, object>
                        { { "computer-use@openai-bundled", new Dictionary<string, object> { { "enabled", false } } } } } } }
            };
        }

        [Test]
        public async Task ComputerUseConcurrentStop_CannotOvertakeBeginControl()
        {
            var commands = new List<string>();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stopping = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var release = new ManualResetEventSlim())
            using (var manager = TestComputerUseManager((binding, command, turn) =>
            {
                if (command == "begin")
                {
                    entered.TrySetResult(Monitor.IsEntered(binding.Sync));
                    release.Wait(5000);
                }
                lock (commands) commands.Add(command + ":" + turn);
            }))
            {
                var binding = RegisterComputerUseBinding(manager, "thread");
                var begin = Task.Run(() => manager.Begin("thread", "turn"));
                var beginHeldSessionLock = await entered.Task;
                var stop = Task.Run(() => { stopping.TrySetResult(true); manager.End("thread", "turn"); });
                await stopping.Task;
                release.Set();
                await Task.WhenAll(begin, stop);
                Assert.That(beginHeldSessionLock, Is.True, "State update and control write must hold the same per-session lock.");
                Assert.That(commands, Is.EqualTo(new[] { "begin:turn", "end:turn" }));
                Assert.That(binding.Turn, Is.Null);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ComputerUseResumeAlreadyLoaded_DoesNotBindAnUnappliedPipe(bool registered)
        {
            var client = CreateClient();
            var statuses = new List<string>();
            var controls = new List<string>();
            var lookups = new List<string>();
            using (var manager = new ComputerUseSessionManager("unused.exe", "unverified", "unused-home",
                (operation, parameters, token) =>
                {
                    lookups.Add(operation);
                    Assert.That(operation, Is.EqualTo("thread/loaded/list"));
                    return Task.FromResult<IDictionary<string, object>>(parameters.ContainsKey("cursor")
                        ? new Dictionary<string, object> { { "data", new[] { "loaded-thread" } }, { "nextCursor", null } }
                        : new Dictionary<string, object> { { "data", new[] { "other-thread" } }, { "nextCursor", "second-page" } });
                }, (code, thread) => statuses.Add(code), (binding, command, turn) => controls.Add(command)))
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                var existing = registered ? RegisterComputerUseBinding(manager, "loaded-thread") : null;
                if (existing != null) existing.Pipe = @"\\.\pipe\mycodex-cua-existing";
                FormTestDriver.SetField(client, "_computerUse", manager);
                FormTestDriver.SetField(client, "_inputWriter", writer);
                try
                {
                    var parameters = new Dictionary<string, object>
                    {
                        { "threadId", "loaded-thread" }, { "cwd", TestContext.CurrentContext.WorkDirectory },
                        { "config", new Dictionary<string, object> { { "model_reasoning_effort", "high" } } }
                    };
                    var original = AppServerJsonValue.CreateSerializer().Serialize(parameters);
                    var operation = InvokeComputerUseRequest(client, "thread/resume", parameters, CancellationToken.None);
                    await CompleteResumeProviderReadAsync(client, stream, 1);
                    var request = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 2));
                    Assert.That(request["method"], Is.EqualTo("thread/resume"));
                    Assert.That(AppServerJsonValue.CreateSerializer().Serialize(parameters), Is.EqualTo(original));
                    Assert.That(AppServerJsonValue.GetDictionary(request, "params")["modelProvider"], Is.EqualTo("current-provider"));
                    Assert.That(AppServerJsonValue.CreateSerializer().Serialize(AppServerJsonValue.GetDictionary(request, "params")),
                        Is.EqualTo(AppServerJsonValue.CreateSerializer().Serialize(new Dictionary<string, object>(parameters) { ["modelProvider"] = "current-provider" })));
                    CompleteRequest(client, Convert.ToString(request["id"], CultureInfo.InvariantCulture),
                        new Dictionary<string, object> { { "thread", new Dictionary<string, object> { { "id", "loaded-thread" } } } });
                    await operation;
                    Assert.That(lookups, Is.EqualTo(new[] { "thread/loaded/list", "thread/loaded/list" }));
                    Assert.That(controls, Is.Empty, "The running MCP must retain the pipe it actually loaded.");
                    Assert.That(statuses, Is.EqualTo(registered ? new string[0] : new[] { "ReconnectRequired" }));
                    var bindings = FormTestDriver.Field<Dictionary<string, ComputerUseSessionBinding>>(manager, "_threads");
                    if (registered)
                    {
                        Assert.That(bindings["loaded-thread"], Is.SameAs(existing));
                        Assert.That(existing.Pipe, Is.EqualTo(@"\\.\pipe\mycodex-cua-existing"));
                    }
                    else Assert.That(bindings, Is.Empty);
                }
                finally
                {
                    FormTestDriver.SetField(client, "_computerUse", null);
                    FormTestDriver.SetField(client, "_inputWriter", null);
                    client.Dispose();
                }
            }
        }

        [Test]
        public async Task ComputerUseRemovedBinding_CannotBeActivatedByWaitingBegin()
        {
            var commands = new List<string>();
            var attempted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var manager = TestComputerUseManager((binding, command, turn) => commands.Add(command)))
            {
                var binding = RegisterComputerUseBinding(manager, "thread");
                Task begin;
                lock (binding.Sync)
                {
                    begin = Task.Run(() => { attempted.TrySetResult(true); manager.Begin("thread", "turn"); });
                    Assert.That(attempted.Task.Wait(1000), Is.True);
                    manager.Remove("thread");
                }
                await begin;
                Assert.That(commands, Is.EqualTo(new[] { "remove" }));
                Assert.That(binding.Turn, Is.Null);
            }
        }

        [Test]
        public void ComputerUseDuplicateBeginAndStaleEnd_DoNotChangeActiveTurn()
        {
            var commands = new List<string>();
            using (var manager = TestComputerUseManager((binding, command, turn) => commands.Add(command + ":" + turn)))
            {
                var binding = RegisterComputerUseBinding(manager, "thread");
                manager.Begin("thread", "current");
                manager.Begin("thread", "current");
                manager.End("thread", "old");
                Assert.That(commands, Is.EqualTo(new[] { "begin:current" }));
                Assert.That(binding.Turn, Is.EqualTo("current"));
            }
        }

        [Test]
        public void ComputerUseStoppingAnotherThread_LeavesOwnerActive()
        {
            var commands = new List<string>();
            using (var manager = TestComputerUseManager((binding, command, turn) => commands.Add(binding.Thread + ":" + command)))
            {
                var owner = RegisterComputerUseBinding(manager, "owner");
                RegisterComputerUseBinding(manager, "other");
                manager.Begin("owner", "owner-turn");
                manager.End("other", "other-turn");
                manager.Remove("other");
                Assert.That(owner.Turn, Is.EqualTo("owner-turn"));
                Assert.That(commands, Is.EqualTo(new[] { "owner:begin", "other:remove" }));
            }
        }

        private static ComputerUseSessionManager TestComputerUseManager(Action<ComputerUseSessionBinding, string, string> control)
        {
            return new ComputerUseSessionManager("unused.exe", "unverified", "unused-home",
                (method, parameters, token) => Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>()),
                (code, thread) => { }, control);
        }

        private static ComputerUseSessionBinding RegisterComputerUseBinding(ComputerUseSessionManager manager, string thread)
        {
            var binding = new ComputerUseSessionBinding { Slot = "slot-" + thread, Thread = thread };
            FormTestDriver.Field<Dictionary<string, ComputerUseSessionBinding>>(manager, "_threads").Add(thread, binding);
            return binding;
        }

        private static Task<IDictionary<string, object>> InvokeComputerUseRequest(AppServerClient client,
            string method, IDictionary<string, object> parameters, CancellationToken token)
        {
            var send = typeof(AppServerClient).GetMethod("SendRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(string), typeof(object), typeof(CancellationToken), typeof(int) }, null);
            return (Task<IDictionary<string, object>>)send.Invoke(client, new object[] { method, parameters, token, 1000 });
        }
    }
}
