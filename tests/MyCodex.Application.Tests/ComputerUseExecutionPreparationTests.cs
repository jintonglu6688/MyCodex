using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ComputerUseExecutionPreparationTests
    {
        [Test]
        public async Task OrdinaryTurn_DoesNotReadPolicyOrStartPolicyWriter()
        {
            var calls = new List<string>();
            using (var current = Process.GetCurrentProcess())
            using (var manager = Manager((method, parameters, token) =>
            {
                calls.Add(method);
                throw new AssertionException("Ordinary sends must not prepare desktop execution.");
            }))
            {
                // An inert host identity: no process is launched, and manager does not own this handle.
                var host = (ComputerUseHostProcess)FormatterServices.GetUninitializedObject(typeof(ComputerUseHostProcess));
                FormTestDriver.SetField(host, "_process", current);
                Register(manager, host);
                var preparationLock = (SemaphoreSlim)manager.GetType().GetField("_prepare", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
                await preparationLock.WaitAsync();
                var send = manager.BeforeTurnAsync("thread", "workspace", CancellationToken.None);
                try { Assert.That(send.IsCompleted, Is.True, "An ordinary turn must not wait for another thread's desktop preparation."); }
                finally { preparationLock.Release(); }
                await send;
                Assert.That(calls, Is.Empty);
                Assert.That(Bindings(manager).ContainsKey("thread"), Is.True);
            }
        }

        [Test]
        public void DisabledPlugin_IsCheckedAtActualExecutionAndFailsClosed()
        {
            var statuses = new List<string>();
            using (var manager = Manager((method, parameters, token) => Task.FromResult<IDictionary<string, object>>(
                new Dictionary<string, object>
                {
                    { "config", new Dictionary<string, object> { { "plugins", new Dictionary<string, object>
                    { { ComputerUseBundle.PluginId, new Dictionary<string, object> { { "enabled", false } } } } } } },
                    { "requirements", null }
                }), (code, thread) => statuses.Add(code)))
            {
                Register(manager, null);
                Assert.ThrowsAsync<InvalidOperationException>(() => Prepare(manager, "turn"));
                Assert.That(statuses, Is.EqualTo(new[] { "PluginDisabled" }));
            }
        }

        [Test]
        public async Task EndTurn_CancelsPendingPreparationBeforeDesktopAcquisition()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = new List<string>();
            using (var manager = Manager(async (method, parameters, token) =>
            {
                calls.Add(method);
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
                return new Dictionary<string, object>();
            }))
            {
                Register(manager, null);
                var preparation = Prepare(manager, "turn");
                await entered.Task;
                manager.End("thread", "turn");
                Assert.CatchAsync<OperationCanceledException>(() => preparation);
                Assert.That(calls, Is.EqualTo(new[] { "config/read" }));
            }
        }

        [Test]
        public void StaleTurn_CannotPrepareForTheReplacementTurn()
        {
            using (var manager = Manager((method, parameters, token) => throw new AssertionException("Stale turn reached configuration.")))
            {
                Register(manager, null);
                Assert.CatchAsync<OperationCanceledException>(() => Prepare(manager, "old-turn"));
            }
        }

        [TestCase("PluginDisabled")]
        [TestCase("NodeReplConflict")]
        public async Task UnavailableNativePreparationDoesNotInheritOfficialDesktopConnection(string failure)
        {
            var command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps", "OpenAI.Codex_26.1103.7773.0_x64__2p2nqsd0c76g0", "app", "resources", "cua_node", "bin", "node_repl.exe");
            var node = new Dictionary<string, object>
            {
                { "command", command }, { "args", new[] { "--unsupported" } },
                { "env", new Dictionary<string, object> { { "SKY_CUA_NATIVE_PIPE", "1" } } }
            };
            var config = new Dictionary<string, object>
            {
                { "mcp_servers", new Dictionary<string, object> { { "node_repl", node } } },
                { "plugins", new Dictionary<string, object> { { ComputerUseBundle.PluginId,
                    new Dictionary<string, object> { { "enabled", failure != "PluginDisabled" } } } } }
            };
            var serializer = new JavaScriptSerializer();
            var before = serializer.Serialize(config);
            var overrides = new Dictionary<string, object> { { "model", "keep" } };
            var parameters = new Dictionary<string, object> { { "cwd", @"C:\fixture" }, { "config", overrides } };
            var statuses = new List<string>();
            using (var manager = new ComputerUseSessionManager("unused", "unused", @"C:\home", (method, args, token) =>
            {
                Assert.That(method, Is.EqualTo("config/read").Or.EqualTo("configRequirements/read"));
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    { { "config", config }, { "requirements", null } });
            }, (code, thread) => statuses.Add(code), browserEnabled: () => false))
            {
                FormTestDriver.SetField(manager, "_components", new ComputerUseComponents { NodeReplPath = @"C:\verified\node_repl.exe" });
                Assert.That(await manager.PrepareAsync(parameters, CancellationToken.None), Is.Null);
            }
            var result = (IDictionary<string, object>)parameters["config"];
            var servers = (IDictionary<string, object>)result["mcp_servers"];
            Assert.That(servers.ContainsKey("node_repl"), Is.True, "The original desktop server would still be inherited.");
            Assert.That(((IDictionary<string, object>)servers["node_repl"])["enabled"], Is.False);
            Assert.That(result["model"], Is.EqualTo("keep"));
            Assert.That(overrides.Count, Is.EqualTo(1));
            Assert.That(serializer.Serialize(config), Is.EqualTo(before));
            Assert.That(statuses, Does.Contain(failure));
        }

        private static ComputerUseSessionManager Manager(
            Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> send,
            Action<string, string> report = null)
        { return new ComputerUseSessionManager("unused", "unused", "unused", send, report, (_, __, ___) => { }); }
        private static void Register(ComputerUseSessionManager manager, ComputerUseHostProcess host)
        {
            Bindings(manager).Add("thread", new ComputerUseSessionBinding
            {
                Thread = "thread", Turn = "turn", TurnLifetime = new CancellationTokenSource(), Host = host, Workspace = "workspace"
            });
        }
        private static Dictionary<string, ComputerUseSessionBinding> Bindings(ComputerUseSessionManager manager)
        { return (Dictionary<string, ComputerUseSessionBinding>)manager.GetType().GetField("_threads", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager); }
        private static Task Prepare(ComputerUseSessionManager manager, string turn)
        { return (Task)manager.GetType().GetMethod("PrepareExecutionAsync", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, new object[] { null, "thread", turn, CancellationToken.None }); }
    }
}
