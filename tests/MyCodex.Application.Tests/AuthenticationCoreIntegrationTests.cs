using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed partial class AuthenticationCoreIntegrationTests
    {
        [Test]
        [Explicit("Requires the native MCP package at MYCODEX_AUTH_CORE_TEST_PATH. Only synthetic files are used.")]
        public async Task ManagedMcpImportAndChangesSurviveProviderSwitches()
        {
            var executable = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            Assert.That(File.Exists(executable), Is.True);
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-core-mcp-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            Directory.CreateDirectory(home);
            var host = NewHost(executable, home, Path.Combine(root, "private"));
            var client = new AuthenticationCenterClient("Test", host.RequestAsync);
            var runtime = new McpRuntimeStub();
            var coordinator = new AuthenticationMcpCoordinator(client, runtime, new RuntimeOperationGate().TryEnterTransition);
            var started = false;
            try
            {
                await client.ReadAsync(CancellationToken.None);
                started = true;
                await client.SaveAsync(Edit("first", "First", "first-model"), CancellationToken.None);
                await client.SaveAsync(Edit("second", "Second", "second-model", "chat_completions"), CancellationToken.None);
                var path = Path.Combine(home, "config.toml");
                // URL-only matches a valid manually registered Codex HTTP server.
                File.AppendAllText(path, "\n[mcp_servers.probe]\nurl = \"http://127.0.0.1:9/mcp\"\nenabled = false\n[mcp_servers.probe.http_headers]\nX-Test = \"synthetic-header\"\n", new UTF8Encoding(false));
                Assert.That(await coordinator.ReadAsync(CancellationToken.None), Is.Empty);
                Assert.That(File.ReadAllText(path), Does.Contain("mcp_servers.probe"), "Read must not write/import.");
                Assert.That(await coordinator.ImportAsync(CancellationToken.None), Is.True);
                await Switch(client, "second");
                await Switch(client, "first");
                Assert.That(File.ReadAllText(path), Does.Contain("mcp_servers.probe"));
                var rows = await coordinator.ReadAsync(CancellationToken.None);
                Assert.That(rows.Single().Enabled, Is.False);
                Assert.That(rows.Single().ConfigValues.ContainsKey("http_headers"), Is.True);

                // Editor/toggle use the same managed write path as the actual form.
                await coordinator.WriteAsync("probe", new[]
                {
                    AppServerConfigWriteEdit.CreateReplace("mcp_servers.probe.url", "http://127.0.0.1:9/changed"),
                    AppServerConfigWriteEdit.CreateReplace("mcp_servers.probe.enabled", true)
                }, CancellationToken.None);
                await Switch(client, "second");
                var live = File.ReadAllText(path);
                Assert.That(live, Does.Contain("127.0.0.1:9/changed").And.Contain("synthetic-header"));

                await coordinator.ReadAsync(CancellationToken.None);
                await coordinator.WriteAsync("probe", new[] { AppServerConfigWriteEdit.CreateReplace("mcp_servers.probe.enabled", false) }, CancellationToken.None);
                await Switch(client, "first");
                Assert.That(File.ReadAllText(path), Does.Not.Contain("mcp_servers.probe"));
                Assert.That((await coordinator.ReadAsync(CancellationToken.None)).Single().Enabled, Is.False, "Disabled archive remains manageable.");
                await coordinator.WriteAsync("probe", new[] { AppServerConfigWriteEdit.CreateReplace("mcp_servers.probe.enabled", true) }, CancellationToken.None);
                Assert.That(File.ReadAllText(path), Does.Contain("127.0.0.1:9/changed"));
                await coordinator.ReadAsync(CancellationToken.None);
                await coordinator.WriteAsync("probe", new[] { AppServerConfigWriteEdit.CreateReplace("mcp_servers.probe", null) }, CancellationToken.None);
                await Switch(client, "second");
                Assert.That(File.ReadAllText(path), Does.Not.Contain("mcp_servers.probe"));
                Assert.That(await coordinator.ReadAsync(CancellationToken.None), Is.Empty);
                Assert.That(runtime.ReloadCount, Is.EqualTo(5));
            }
            finally { if (started) await Call(host, "backend/shutdown"); }
        }

        [Test]
        [Explicit("Requires a protocol-3 GUI package at MYCODEX_AUTH_CORE_TEST_PATH. No external API is contacted.")]
        public async Task GuiClientCanSaveEditSwitchCopyAndDeleteThroughNativeCore()
        {
            var executable = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            Assert.That(File.Exists(executable), Is.True);
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-core-gui-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            Directory.CreateDirectory(home);
            var host = NewHost(executable, home, Path.Combine(root, "private"));
            var client = new AuthenticationCenterClient("Test", host.RequestAsync);
            var started = false;
            try
            {
                var initial = await client.ReadAsync(CancellationToken.None);
                started = true;
                Assert.That(initial.Providers, Is.Empty);
                Assert.That((await client.ReadAccountsAsync(CancellationToken.None)).Accounts, Is.Empty);
                var presets = await client.ReadPresetsAsync(CancellationToken.None);
                Assert.That(presets.Count, Is.GreaterThan(90));
                Assert.That(presets.Any(p => p.Id == "custom" && p.Available), Is.True);
                Assert.That(presets.Any(p => p.Kind == "chatgpt" && p.Available), Is.True);
                var coordinator = new AuthenticationApplyCoordinator(client, new RuntimeOperationGate().TryEnterTransition, () => Task.FromResult(true));
                var edit = new AuthenticationProviderEdit
                {
                    Id = "first", Name = "直连测试", Kind = "responses", PresetId = "custom",
                    BaseUrl = "https://vendor.example.invalid/v1", Model = "first-model", ApiKey = "synthetic-secret",
                    Models = new List<AuthenticationCatalogModel>
                    {
                        new AuthenticationCatalogModel { Model = "first-model", DisplayName = "First model", ContextWindow = 32000,
                            ReasoningLevels = new List<string> { "low", "high" }, DefaultReasoningLevel = "high" }
                    }
                };
                var first = (await coordinator.SaveAsync(edit, CancellationToken.None)).Provider;
                Assert.That(first.GlobalApplied, Is.True);
                Assert.That(first.Models.Single().ContextWindow, Is.EqualTo(32000));
                Assert.That(first.HasApiKey, Is.True);
                var configPath = Path.Combine(home, "config.toml");
                File.WriteAllText(configPath, File.ReadAllText(configPath).Replace("first-model", "external-model"), new UTF8Encoding(false));
                var fresh = await client.ReadForEditAsync(first, CancellationToken.None);
                Assert.That(fresh.Model, Is.EqualTo("external-model"));
                edit.ExpectedVersion = fresh.Version;
                edit.Name = "已编辑直连";
                edit.Model = "external-model";
                edit.ApiKey = null;
                first = (await coordinator.SaveAsync(edit, CancellationToken.None)).Provider;
                Assert.That(first.GlobalApplied && first.HasApiKey, Is.True);

                var second = await client.SaveAsync(new AuthenticationProviderEdit
                {
                    Id = "second", Name = "转换测试", Kind = "chat_completions", PresetId = "custom",
                    BaseUrl = "https://other.example.invalid/v1", Model = "second-model", ApiKey = "synthetic-secret-second"
                }, CancellationToken.None);
                Assert.That(second.GlobalApplied, Is.False);
                Assert.That((await coordinator.ApplyAsync(second, CancellationToken.None)).GlobalApplied, Is.True);
                var routed = await client.ReadAsync(CancellationToken.None);
                Assert.That(routed.CurrentProviderId, Is.EqualTo("second"));
                Assert.That(routed.RouterRunning && routed.RouteRequired, Is.True);
                Assert.That(File.ReadAllText(configPath), Does.Contain("127.0.0.1"));
                first = routed.Providers.Single(p => p.Id == "first");
                await coordinator.ApplyAsync(first, CancellationToken.None);
                var direct = await client.ReadAsync(CancellationToken.None);
                Assert.That(direct.RouterRunning || direct.RouteRequired, Is.False);
                Assert.That(File.ReadAllText(configPath), Does.Not.Contain("127.0.0.1"));
                var copy = await client.CopyAsync(direct.Providers.Single(p => p.Id == "first"), "复制", CancellationToken.None);
                Assert.That(copy.HasApiKey, Is.True);
                Assert.That(await client.DeleteAsync(copy, CancellationToken.None), Is.False);
                Assert.That((await client.ReadAsync(CancellationToken.None)).Providers.Count, Is.EqualTo(2));
            }
            finally { if (started) await Call(host, "backend/shutdown"); }
        }

        [Test]
        [Explicit("Requires a protocol-3 package with fullConfigEditor at MYCODEX_AUTH_CORE_TEST_PATH. Uses isolated synthetic files only.")]
        public async Task FullConfigEditorSplitsGlobalEditsAndRefusesConflictsThroughNativeCore()
        {
            var executable = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            Assert.That(File.Exists(executable), Is.True);
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-core-editor-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            Directory.CreateDirectory(home);
            var host = NewHost(executable, home, Path.Combine(root, "private"));
            var client = new AuthenticationCenterClient("Test", host.RequestAsync);
            var started = false;
            try
            {
                Assert.That((await client.ReadAsync(CancellationToken.None)).SupportsFullConfigEditor, Is.True);
                started = true;
                var first = await client.SaveAsync(Edit("first", "First", "first-model"), CancellationToken.None);
                var configPath = Path.Combine(home, "config.toml");
                File.WriteAllText(configPath, "model_reasoning_summary = \"auto\"\napproval_policy = \"on-request\"\n" + File.ReadAllText(configPath), new UTF8Encoding(false));
                var edit = Edit("first", "First", "first-model");
                edit.ExpectedVersion = first.Version;
                var view = await client.ViewProviderConfigAsync(edit, CancellationToken.None);
                StringAssert.Contains("approval_policy = \"on-request\"", view.Text);
                var text = view.Text.Replace("approval_policy = \"on-request\"", "approval_policy = \"never\"");
                edit.EditorBase = view.Text;
                var preview = await client.PreviewProviderConfigAsync(edit, text, CancellationToken.None);
                Assert.That(preview.GlobalChanges, Is.EqualTo(new[] { "approval_policy" }));
                StringAssert.DoesNotContain("approval_policy", preview.Text);
                // Another program changes the same setting before the provider window is saved.
                File.WriteAllText(configPath, File.ReadAllText(configPath).Replace("approval_policy = \"on-request\"", "approval_policy = \"on-failure\""), new UTF8Encoding(false));
                var before = File.ReadAllText(configPath);
                edit.EditorText = text;
                edit.ConfigToml = preview.Text;
                var conflict = Assert.ThrowsAsync<AuthenticationCenterException>(() => client.SaveAsync(edit, CancellationToken.None));
                Assert.That(conflict.Code, Is.EqualTo("editor_conflict"));
                Assert.That(conflict.Keys, Is.EqualTo(new[] { "approval_policy" }));
                Assert.That(File.ReadAllText(configPath), Is.EqualTo(before));
                edit.EditorConflictPolicy = "keepMine";
                Assert.That((await client.SaveAsync(edit, CancellationToken.None)).GlobalApplied, Is.True);
                var config = File.ReadAllText(configPath);
                StringAssert.Contains("approval_policy = \"never\"", config);
                StringAssert.Contains("model_reasoning_summary = \"auto\"", config);
            }
            finally { if (started) await Call(host, "backend/shutdown"); }
        }

        [Test]
        [Explicit("Requires a protocol-3 package at MYCODEX_AUTH_CORE_TEST_PATH. Uses isolated synthetic files only.")]
        public async Task NativeServicesRoundTripPreservesSaveSwitchAndKeyFieldSemantics()
        {
            var executable = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            Assert.That(File.Exists(executable), Is.True, "Set MYCODEX_AUTH_CORE_TEST_PATH to the packaged native host.");
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-core-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "Codex 配置");
            var store = Path.Combine(root, "private");
            Directory.CreateDirectory(home);
            var host = NewHost(executable, home, store);
            var client = new AuthenticationCenterClient("Test", host.RequestAsync);
            var started = false;
            try
            {
                var status = await Call(host, "status");
                started = true;
                Assert.That(status["protocolVersion"], Is.EqualTo(3));
                Assert.That(status["hostVersion"], Is.EqualTo("0.3.10"));
                Assert.That(((IDictionary<string, object>)status["route"])["running"], Is.False);
                Assert.That(File.Exists(Path.Combine(home, "config.toml")), Is.False);

                // The first provider is applied immediately; later additions stay inactive.
                var first = await client.SaveAsync(Edit("first", "原版配置", "first-model"), CancellationToken.None);
                Assert.That(first.GlobalApplied, Is.True);
                var configPath = Path.Combine(home, "config.toml");
                Assert.That(File.ReadAllText(configPath), Does.Contain("first-model"));
                await client.SaveAsync(Edit("second", "第二套", "second-model"), CancellationToken.None);
                Assert.That(File.ReadAllText(configPath), Does.Not.Contain("second-model"));

                // Saving the current provider also updates live configuration.
                var edited = Edit("first", "已编辑", "edited-model");
                edited.ExpectedVersion = first.Version;
                await client.SaveAsync(edited, CancellationToken.None);
                Assert.That(File.ReadAllText(configPath), Does.Contain("edited-model"));

                // A model chosen in Codex or MyCodex is kept with the provider when switching away, as in 3.x.
                File.WriteAllText(configPath, File.ReadAllText(configPath).Replace("edited-model", "external-model"), new UTF8Encoding(false));
                await Switch(client, "second");
                Assert.That(File.ReadAllText(configPath), Does.Contain("second-model").And.Not.Contain("external-model"));
                await Switch(client, "first");
                Assert.That(File.ReadAllText(configPath), Does.Contain("external-model").And.Not.Contain("edited-model"));

                // A second transport attaches to the same resident, validating its identity.
                var reopened = NewHost(executable, home, store);
                var list = await new AuthenticationCenterClient("Test", reopened.RequestAsync).ReadAsync(CancellationToken.None);
                Assert.That(list.CurrentProviderId, Is.EqualTo("first"));
                Assert.That(list.Providers.Count, Is.EqualTo(2));
                Assert.That(list.Providers.Single(p => p.Id == "first").Name, Is.EqualTo("已编辑"));
                Assert.That(new JavaScriptSerializer().Serialize(await Call(reopened, "gui/provider/list")), Does.Not.Contain("synthetic-secret"));

                var otherStore = Path.Combine(root, "different-store");
                var mismatch = Assert.ThrowsAsync<AuthenticationCenterException>(async () =>
                    await Call(NewHost(executable, home, otherStore), "status"));
                Assert.That(mismatch.Code, Is.EqualTo("target_mismatch"));
                Assert.That(File.ReadAllText(configPath), Does.Contain("external-model"));
            }
            finally
            {
                if (started) await Call(host, "backend/shutdown");
                // Disposable artifacts are retained for inspection; no real home is touched.
            }
        }

        private static LocalAuthenticationHost NewHost(string executable, string home, string store)
        {
            return new LocalAuthenticationHost(CodexRuntimeSources.Bundled, executable, home, store);
        }

        private static Task<IDictionary<string, object>> Call(LocalAuthenticationHost host, string method, string key = null, object value = null)
        {
            var parameters = new Dictionary<string, object>();
            if (key != null) parameters.Add(key, value);
            return host.RequestAsync(method, parameters, CancellationToken.None);
        }

        private static AuthenticationProviderEdit Edit(string id, string name, string model, string kind = "responses")
        {
            return new AuthenticationProviderEdit
            {
                Id = id, Name = name, Kind = kind, PresetId = "custom",
                BaseUrl = "https://vendor.example.invalid/v1", Model = model, ApiKey = "synthetic-secret"
            };
        }

        private static async Task Switch(AuthenticationCenterClient client, string id)
        {
            var provider = (await client.ReadAsync(CancellationToken.None)).Providers.Single(p => p.Id == id);
            await new AuthenticationApplyCoordinator(client, new RuntimeOperationGate().TryEnterTransition, () => Task.FromResult(true))
                .ApplyAsync(provider, CancellationToken.None);
        }
    }
}
