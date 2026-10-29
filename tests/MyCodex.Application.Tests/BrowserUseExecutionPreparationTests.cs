using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.BrowserUse;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class BrowserUseExecutionPreparationTests
    {
        private const string Executor = "64f1084d-52b5-4054-8597-63fe07ecedbb";

        [Test]
        public async Task MasterSwitchRejectsExistingBindingWithoutReadingPolicyAndKeepsNativeTurn()
        {
            var resets = 0;
            using (var manager = Create((method, parameters, _) =>
            {
                Assert.That(method, Is.EqualTo("mcpServer/tool/call"));
                Assert.That(parameters["server"], Is.EqualTo("cua_repl"));
                Assert.That(parameters["tool"], Is.EqualTo("js_reset"));
                resets++;
                return Task.FromResult(Json("{}"));
            }))
            {
                FormTestDriver.SetField(manager, "_browserEnabled", (Func<bool>)(() => false));
                Assert.That(await Authorize(manager, "turn", Executor), Is.False);
                await manager.RevokeBrowserAsync();
                var binding = FormTestDriver.Field<Dictionary<string, ComputerUseSessionBinding>>(manager, "_threads")["thread"];
                Assert.That(binding.BrowserAccess, Is.Null);
                Assert.That(binding.Turn, Is.EqualTo("turn"));
                Assert.That(binding.TurnLifetime.IsCancellationRequested, Is.False);
                Assert.That(resets, Is.EqualTo(1));
            }
        }

        [TestCase("turn", "foreign", false)]
        [TestCase("old-turn", Executor, false)]
        public async Task ForeignExecutionCannotReadOrAuthorizeCurrentPolicy(string turn, string executor, bool expected)
        {
            using (var manager = Create((_, __, ___) => throw new AssertionException("Foreign execution reached policy.")))
                Assert.That(await Authorize(manager, turn, executor), Is.EqualTo(expected));
        }

        [Test]
        public void RevocationFailureStillStopsOtherBindingsAndKeepsFailedBindingRetryable()
        {
            var resets = 0;
            using (var manager = Create((_, __, ___) => Task.FromResult(Json(++resets == 1 ? "{'isError':true}" : "{}"))))
            {
                var threads = FormTestDriver.Field<Dictionary<string, ComputerUseSessionBinding>>(manager, "_threads");
                threads.Add("second", new ComputerUseSessionBinding
                {
                    Thread = "second", Turn = "turn", BrowserAccess = threads["thread"].BrowserAccess,
                    TurnLifetime = new CancellationTokenSource()
                });
                Assert.ThrowsAsync<InvalidOperationException>(async () => await manager.RevokeBrowserAsync());
                Assert.That(resets, Is.EqualTo(2));
                Assert.That(threads.Values.Count(binding => binding.BrowserAccess != null), Is.EqualTo(1));
            }
        }

        [TestCase("{}", "{}", true)]
        [TestCase("{ 'features': { 'browser_use': false } }", "{}", false)]
        [TestCase("{ 'plugins': { 'chrome@openai-bundled': { 'enabled': false } } }", "{}", false)]
        [TestCase("{}", "{ 'allowBrowserAndComputerUse': false }", false)]
        [TestCase("{}", "{ 'browserUse': { 'futureUnenforcedRestriction': true } }", false)]
        [TestCase("{}", "{ 'futureManagedRestriction': { 'deny': true } }", false)]
        public async Task AuthorizationRefreshesEffectiveConfigAndRequirements(string config, string requirements, bool expected)
        {
            var methods = new List<string>();
            using (var manager = Create((method, _, __) =>
            {
                methods.Add(method);
                return Task.FromResult(Json(method == "config/read" ? "{ 'config': " + config + " }" : "{ 'requirements': " + requirements + " }"));
            }))
            {
                Assert.That(await Authorize(manager, "turn", Executor), Is.EqualTo(expected));
                Assert.That(methods, Is.EqualTo(new[] { "config/read", "configRequirements/read" }));
            }
        }

        [Test]
        public async Task EndingTurnCancelsPendingBrowserAuthorization()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var manager = Create(async (_, __, token) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, token);
                return Json("{}");
            }))
            {
                var pending = Authorize(manager, "turn", Executor);
                await entered.Task;
                manager.End("thread", "turn");
                Assert.CatchAsync<OperationCanceledException>(async () => await pending);
                Assert.That(await Authorize(manager, "turn", Executor), Is.False);
            }
        }

        private static ComputerUseSessionManager Create(Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> send)
        {
            var manager = new ComputerUseSessionManager(@"D:\codex.exe", "0.164.0", @"C:\users\test\.codex", send, null, (_, __, ___) => { });
            var access = new BrowserUseAccessBinding
            {
                ExecutorId = Executor, PipePath = @"\\.\pipe\mycodex-browser-access-" + Executor,
                VerifyingIdentity = Convert.ToBase64String(new byte[44])
            };
            FormTestDriver.Field<Dictionary<string, ComputerUseSessionBinding>>(manager, "_threads").Add("thread", new ComputerUseSessionBinding
            {
                Thread = "thread", Turn = "turn", TurnLifetime = new CancellationTokenSource(),
                Workspace = @"E:\workspace", BrowserAccess = access
            });
            const string root = @"C:\mycodex\browser";
            const string modules = root + @"\bin\node_modules";
            FormTestDriver.SetField(manager, "_browserComponents", new BrowserUseComponents
            {
                RuntimeRoot = root, NodePath = root + @"\bin\node.exe", NodeReplPath = root + @"\bin\node_repl.exe",
                NodeModulesPath = modules, CuaReplPath = modules + @"\@oai\cua-repl\bin\cua-repl.mjs",
                BrowserServicePath = modules + @"\@oai\browser-desktop\scripts\browser-service.mjs",
                BrowserClientPath = modules + @"\@oai\browser-desktop\scripts\browser-client.mjs", SelectedCodexPath = @"D:\codex.exe"
            });
            return manager;
        }

        private static Task<bool> Authorize(ComputerUseSessionManager manager, string turn, string executor)
        {
            return FormTestDriver.Invoke<Task<bool>>(manager, "PrepareBrowserExecutionAsync", null, "thread", turn, executor, CancellationToken.None);
        }
        private static IDictionary<string, object> Json(string value)
        {
            return (IDictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(value.Replace('\'', '"'));
        }
    }
}
