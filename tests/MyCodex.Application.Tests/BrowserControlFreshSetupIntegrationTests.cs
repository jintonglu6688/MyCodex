using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.BrowserUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Explicit("Real pinned CLI in a new temporary home; no account, model, browser or registry changes.")]
    public sealed class BrowserControlFreshSetupIntegrationTests
    {
        [Test]
        public async Task ActualDotNetSetupPreparesPluginAndBridgeInFreshHome()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "browser-fresh-dotnet-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "home");
            Directory.CreateDirectory(home);
            var hostDirectory = Path.Combine(root, "runtime", "browser-host", "h1");
            Directory.CreateDirectory(hostDirectory);
            File.Copy(RepositorySource.GetPath("runtime", "browser-host", "h1", "extension-host.exe"),
                Path.Combine(hostDirectory, "extension-host.exe"));
            foreach (var item in new Dictionary<string, string>
            {
                { ".agents/plugins/marketplace.json", "MyCodex.BrowserUse.PluginMarketplace" },
                { "plugins/browser-use/.codex-plugin/plugin.json", "MyCodex.BrowserUse.PluginManifest" },
                { "plugins/browser-use/skills/browser-use/SKILL.md", "MyCodex.BrowserUse.PluginSkill" }
            })
            {
                var file = Path.Combine(root, "browser-use", item.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                using (var input = typeof(WindowsBrowserExtensionSetupService).Assembly.GetManifestResourceStream(item.Value))
                using (var output = File.Create(file)) input.CopyTo(output);
            }
            var codex = RepositorySource.GetPath("runtime", "codex", "0.164.0", "x86_64-pc-windows-msvc", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
            var start = new ProcessStartInfo(codex, "app-server --listen stdio://")
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.EnvironmentVariables["CODEX_HOME"] = home;
            start.EnvironmentVariables["OTEL_SDK_DISABLED"] = "true";
            foreach (var key in new[] { "OPENAI_API_KEY", "AZURE_OPENAI_API_KEY", "CODEX_API_KEY", "CHATGPT_AUTH_TOKEN" })
                start.EnvironmentVariables.Remove(key);
            using (var process = Process.Start(start))
            {
                var stderr = process.StandardError.ReadToEndAsync();
                try
                {
                    var rpc = new Rpc(process);
                    await rpc.Send("initialize", new Dictionary<string, object>
                    {
                        { "clientInfo", new Dictionary<string, object> { { "name", "mycodex-fresh-dotnet-fixture" }, { "version", "1" } } },
                        { "capabilities", new Dictionary<string, object> { { "experimentalApi", true } } }
                    }, CancellationToken.None, 10000);
                    process.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"initialized\",\"params\":{}}");
                    process.StandardInput.Flush();
                    var operations = new AppServerClientOperations(() => { }, () => new AppServerProtocolCapabilities
                    {
                        SupportsInstalledPluginObservation = true, SupportsMarketplaceRegistration = true,
                        SupportsPluginManagement = true, SupportsPluginMutation = true, SupportsMcpManagement = true
                    }, rpc.Send, (path, _) => path, path => path, path => path, (_, __, ___) => null,
                        (_, __, ___) => { }, _ => { });
                    // Only transport/connection checks are replaced. The facade, request builders and response mappers are real.
                    var client = (AppServerClient)FormatterServices.GetUninitializedObject(typeof(AppServerClient));
                    FormTestDriver.SetField(client, "_operations", operations);
                    var localAppData = Path.Combine(root, "local-app-data");
                    var browser = Path.Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe");
                    Directory.CreateDirectory(Path.GetDirectoryName(browser));
                    File.WriteAllText(browser, "Owned fixture; never executed.");
                    var registration = new Registration();
                    var service = new WindowsBrowserExtensionSetupService(localAppData, key =>
                    {
                        foreach (var family in new[] { BrowserExtensionBrowser.Edge, BrowserExtensionBrowser.Chrome })
                            if (key == "HKEY_CURRENT_USER\\" + BrowserNativeMessagingRegistration.KeyPath(family))
                                return registration.Read(family).Data as string;
                        return null;
                    }, client, root, registration);
                    using (var transition = RuntimeOperationGate.Shared.TryEnterTransition())
                    {
                        Assert.That(transition, Is.Not.Null);
                        Assert.That(await service.PrepareAsync(CancellationToken.None), Is.True);
                    }
                    var report = await service.CheckAsync(BrowserExtensionBrowser.Edge, CancellationToken.None);
                    Assert.That(report.BrowserInstalled, Is.True);
                    Assert.That(report.Bridge, Is.EqualTo(BrowserExtensionBridgeState.Registered));
                    Assert.That((await client.GetInstalledPluginsAsync()).Plugins.Any(plugin =>
                        plugin.PluginId == BrowserUsePluginSetup.PluginId && plugin.Enabled), Is.True);
                    using (var transition = RuntimeOperationGate.Shared.TryEnterTransition())
                        Assert.That(await service.PrepareAsync(CancellationToken.None), Is.False);
                }
                finally
                {
                    process.StandardInput.Close();
                    if (!process.WaitForExit(5000)) process.Kill();
                    await stderr;
                }
            }
        }

        private sealed class Registration : IBrowserNativeMessagingRegistration
        {
            private readonly Dictionary<BrowserExtensionBrowser, BrowserNativeMessagingValue> _values =
                new Dictionary<BrowserExtensionBrowser, BrowserNativeMessagingValue>();
            public BrowserNativeMessagingValue Read(BrowserExtensionBrowser browser)
            {
                return _values.TryGetValue(browser, out var value) ? value : new BrowserNativeMessagingValue();
            }
            public void Write(BrowserExtensionBrowser browser, BrowserNativeMessagingValue value) { _values[browser] = value; }
        }

        private sealed class Rpc
        {
            private readonly Process _process;
            private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            private int _sequence;
            internal Rpc(Process process) { _process = process; }
            internal async Task<IDictionary<string, object>> Send(string method, object parameters, CancellationToken token, int timeout)
            {
                var id = ++_sequence;
                TestContext.Progress.WriteLine("Fresh .NET setup RPC: " + method);
                _process.StandardInput.WriteLine(_json.Serialize(new Dictionary<string, object>
                {
                    { "jsonrpc", "2.0" }, { "id", id }, { "method", method }, { "params", parameters }
                }));
                _process.StandardInput.Flush();
                while (true)
                {
                    var read = _process.StandardOutput.ReadLineAsync();
                    if (await Task.WhenAny(read, Task.Delay(Math.Min(timeout, 10000), token)) != read)
                        throw new TimeoutException(method);
                    var line = await read;
                    if (line == null) throw new IOException("Fixture CLI exited during " + method);
                    var response = _json.Deserialize<Dictionary<string, object>>(line);
                    if (!response.TryGetValue("id", out var responseId) || !Equals(responseId, id)) continue;
                    if (response.TryGetValue("error", out var error))
                        throw new InvalidOperationException(method + ": " + _json.Serialize(error));
                    return (IDictionary<string, object>)response["result"];
                }
            }
        }
    }
}
