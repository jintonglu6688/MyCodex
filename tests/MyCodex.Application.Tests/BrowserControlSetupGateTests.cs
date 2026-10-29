using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.BrowserUse;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public sealed class BrowserControlSetupGateTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task PluginPreparationCanUseItsOwnTransitionWithoutUnlockingOtherWork(bool throughWaitDialog)
        {
            var installation = Path.Combine(TestContext.CurrentContext.WorkDirectory, "browser-gate-" + Guid.NewGuid().ToString("N"));
            var files = new Dictionary<string, string>
            {
                { ".agents/plugins/marketplace.json", "MyCodex.BrowserUse.PluginMarketplace" },
                { "plugins/browser-use/.codex-plugin/plugin.json", "MyCodex.BrowserUse.PluginManifest" },
                { "plugins/browser-use/skills/browser-use/SKILL.md", "MyCodex.BrowserUse.PluginSkill" }
            };
            foreach (var item in files)
            {
                var path = Path.Combine(installation, "browser-use", item.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var source = typeof(WindowsBrowserExtensionSetupService).Assembly.GetManifestResourceStream(item.Value))
                using (var output = File.Create(path)) source.CopyTo(output);
            }
            var runtime = (AppServerClient)FormatterServices.GetUninitializedObject(typeof(AppServerClient));
            var send = (AppServerRequestInvoker)Delegate.CreateDelegate(typeof(AppServerRequestInvoker), runtime,
                typeof(AppServerClient).GetMethod("SendRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(string), typeof(object), typeof(CancellationToken), typeof(int) }, null));
            var writer = new Writer();
            var transport = new AppServerProtocolTransport(() => writer, new JavaScriptSerializer(),
                new SemaphoreSlim(1), new object(), (_, __) => new TimeoutException());
            var installed = false;
            var methods = new List<string>();
            writer.Reply = json =>
            {
                var request = AppServerJsonValue.DeserializeDictionary(json);
                var method = (string)request["method"];
                methods.Add(method);
                Assert.Throws<MyCodex.Application.Interfaces.AuthenticationCenterException>(() => RuntimeOperationGate.Shared.EnterOperation());
                IDictionary<string, object> result = new Dictionary<string, object>();
                if (method == "plugin/installed")
                    result["marketplaces"] = new object[] { new Dictionary<string, object>
                    {
                        { "name", BrowserUsePluginSetup.Marketplace },
                        { "plugins", installed ? new object[] { new Dictionary<string, object>
                            {
                                { "id", BrowserUsePluginSetup.PluginId }, { "name", "browser-use" },
                                { "installed", true }, { "enabled", true },
                                { "source", new Dictionary<string, object>
                                    {
                                        { "type", "local" }, { "path", Path.Combine(installation, "browser-use", "plugins", "browser-use") }
                                    }
                                }
                            }
                        } : new object[0] }
                    } };
                else if (method == "marketplace/add") result["marketplaceName"] = BrowserUsePluginSetup.Marketplace;
                else if (method == "plugin/install") installed = true;
                else Assert.That(method, Is.EqualTo("config/batchWrite"));
                transport.TryHandleResponse(new Dictionary<string, object> { { "id", request["id"] }, { "result", result } }, out _);
            };
            FormTestDriver.SetField(runtime, "_protocolTransport", transport);
            FormTestDriver.SetField(runtime, "_operations", new AppServerClientOperations(() => { }, () => new AppServerProtocolCapabilities
            {
                SupportsInstalledPluginObservation = true, SupportsMarketplaceRegistration = true,
                SupportsPluginManagement = true, SupportsPluginMutation = true
            }, send, (path, _) => path, path => path, path => path, (_, __, ___) => null, (_, __, ___) => { }, _ => { }));
            using (var transition = RuntimeOperationGate.Shared.TryEnterTransition())
            {
                Assert.That(transition, Is.Not.Null);
                Func<Task> prepare = async () =>
                {
                    await Task.Yield();
                    Assert.That(await BrowserUsePluginSetup.EnableAsync(runtime, installation, CancellationToken.None), Is.True);
                    Assert.That(methods, Is.EqualTo(new[] { "plugin/installed", "marketplace/add", "plugin/install", "config/batchWrite", "plugin/installed" }));
                    Assert.That(await BrowserUsePluginSetup.EnableAsync(runtime, installation, CancellationToken.None), Is.False);
                    Assert.ThrowsAsync<MyCodex.Application.Interfaces.AuthenticationCenterException>(async () =>
                        await send("plugin/installed", new Dictionary<string, object>(), CancellationToken.None, 1000));
                };
                if (throughWaitDialog)
                {
                    var context = SynchronizationContext.Current;
                    try
                    {
                        SynchronizationContext.SetSynchronizationContext(new System.Windows.Forms.WindowsFormsSynchronizationContext());
                        using (var wait = new RuntimeSwitchWaitForm("Owned setup fixture", "No real browser or registry changes", prepare))
                        {
                            wait.ShowDialog();
                            Assert.That(wait.OperationException, Is.Null);
                        }
                    }
                    finally { SynchronizationContext.SetSynchronizationContext(context); }
                }
                else await prepare();
            }
        }

        private sealed class Writer : StringWriter
        {
            internal Action<string> Reply;
            public override Encoding Encoding => Encoding.UTF8;
            public override Task WriteLineAsync(string value) { Reply(value); return Task.CompletedTask; }
        }

        [Test]
        public async Task SetupScopeRejectsOtherClientsSettingsMarketplacesAndCallers()
        {
            var runtime = (AppServerClient)FormatterServices.GetUninitializedObject(typeof(AppServerClient));
            var other = (AppServerClient)FormatterServices.GetUninitializedObject(typeof(AppServerClient));
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "owned-browser-marketplace");
            var ownEdit = new Dictionary<string, object>
            {
                { "keyPath", BrowserUsePluginSetup.EnableKey }, { "mergeStrategy", "replace" }, { "value", true }
            };
            var editRequest = new Dictionary<string, object> { { "edits", new object[] { ownEdit } }, { "reloadUserConfig", true } };
            using (var transition = RuntimeOperationGate.Shared.TryEnterTransition())
            using (runtime.BeginBrowserControlSetup(root))
            {
                Assert.That(AppServerBrowserSetupScope.Allows(runtime, "config/batchWrite", editRequest), Is.True);
                Assert.That(AppServerBrowserSetupScope.Allows(other, "config/batchWrite", editRequest), Is.False);
                ownEdit["keyPath"] = "model_provider";
                Assert.That(AppServerBrowserSetupScope.Allows(runtime, "config/batchWrite", editRequest), Is.False);
                ownEdit["keyPath"] = BrowserUsePluginSetup.EnableKey;
                editRequest["edits"] = new object[] { ownEdit, null };
                Assert.That(AppServerBrowserSetupScope.Allows(runtime, "config/batchWrite", editRequest), Is.False);
                editRequest["edits"] = new object[] { ownEdit };
                editRequest["filePath"] = "other-config";
                Assert.That(AppServerBrowserSetupScope.Allows(runtime, "config/batchWrite", editRequest), Is.False);
                Assert.That(AppServerBrowserSetupScope.Allows(runtime, "marketplace/add", new Dictionary<string, object> { { "source", "foreign" } }), Is.False);
                Assert.That(AppServerBrowserSetupScope.Allows(runtime, "turn/start", new Dictionary<string, object>()), Is.False);
                Task unrelated;
                using (ExecutionContext.SuppressFlow()) unrelated = Task.Run(() =>
                {
                    Assert.That(AppServerBrowserSetupScope.Allows(runtime, "plugin/installed", new Dictionary<string, object>()), Is.False);
                    Assert.Throws<MyCodex.Application.Interfaces.AuthenticationCenterException>(() => runtime.BeginBrowserControlSetup(root));
                });
                await unrelated;
            }
            Assert.That(AppServerBrowserSetupScope.Allows(runtime, "plugin/installed", new Dictionary<string, object>()), Is.False);
        }
    }
}
