using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Skills;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class AuthenticationCoreIntegrationTests
    {
        [Test]
        [NonParallelizable]
        [Explicit("Requires MYCODEX_AUTH_CORE_TEST_PATH and MYCODEX_CODEX_TEST_PATH. Uses only a local synthetic plugin and isolated home; no model requests.")]
        public async Task WindowsPluginAndSkillWritesSurviveNativeProviderSwitches()
        {
            var executable = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            var codex = Environment.GetEnvironmentVariable("MYCODEX_CODEX_TEST_PATH");
            Assert.That(File.Exists(executable) && File.Exists(codex), Is.True);
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-core-tools-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            var workspace = Path.Combine(root, "workspace");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(workspace);
            var host = NewHost(executable, home, Path.Combine(root, "private"));
            var oldHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            AppServerClient runtime = null;
            var started = false;
            try
            {
                await Call(host, "status");
                started = true;
                var client = new AuthenticationCenterClient("Test", host.RequestAsync);
                foreach (var id in new[] { "a", "b", "other" })
                {
                    await client.SaveAsync(Edit(id, id, "synthetic-model", id == "b" ? "chat_completions" : "responses"), CancellationToken.None);
                }
                Environment.SetEnvironmentVariable("CODEX_HOME", home);
                var configService = new AppConfigService(Path.Combine(root, "app"));
                var store = new JsonAppConfigStore(configService);
                store.Save(new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Custom, CodexCustomExecutablePath = codex });
                runtime = new AppServerClient(new CodexRuntimeManager(configService), store);
                await runtime.ConnectAsync();
                var coordinator = new AuthenticationApplyCoordinator(client, RuntimeOperationGate.Shared.TryEnterTransition,
                    async () => { await runtime.RestartAsync(); return true; });
                Func<string, Task> switchProvider = async id =>
                {
                    var provider = (await client.ReadAsync(CancellationToken.None)).Providers.Single(p => p.Id == id);
                    await coordinator.ApplyAsync(provider, CancellationToken.None);
                };

                // Seed one observed user skill so the existing editor can discover its approved root.
                var seed = Path.Combine(home, "skills", "seed", "SKILL.md");
                Directory.CreateDirectory(Path.GetDirectoryName(seed));
                File.WriteAllText(seed, "---\nname: seed\ndescription: Isolated test seed\n---\nNo actions.\n", new UTF8Encoding(false));
                var files = new OfficialSkillFileOperations(runtime, workspace);
                files.RefreshObservations((await runtime.ListSkillsAsync(new[] { workspace }, true)).SelectMany(s => s.Skills));
                var model = SkillFileEditor.CreateNewModel(files.GetSaveLocations());
                // App Server may also expose the user's .agents/skills; only write to this fixture.
                var userSkillRoot = Path.GetDirectoryName(Path.GetDirectoryName(seed));
                model.Location = files.GetSaveLocations().Single(l => l.Kind == SkillSaveLocationKind.User &&
                    string.Equals(Path.GetFullPath(l.RootPath), userSkillRoot, StringComparison.OrdinalIgnoreCase));
                model.Name = "auth-tool-probe";
                model.Description = "Isolated test skill";
                model.Body = "No actions.";
                var skillDirectory = await files.SaveAsync(model, true);
                var skill = (await runtime.ListSkillsAsync(new[] { workspace }, true)).SelectMany(s => s.Skills).Single(s => s.Name == model.Name);
                Assert.That(await runtime.WriteSkillEnabledAsync(skill.Path, false), Is.False);

                // Exercise the same official local-marketplace install path as Plugin Management.
                var market = Path.Combine(root, "market");
                var plugin = Path.Combine(market, "plugins", "auth-probe");
                Directory.CreateDirectory(Path.Combine(plugin, ".codex-plugin"));
                File.WriteAllText(Path.Combine(plugin, ".codex-plugin", "plugin.json"),
                    "{\"name\":\"auth-probe\",\"version\":\"1.0.0\",\"description\":\"Isolated test plugin\"}", new UTF8Encoding(false));
                var manifest = Path.Combine(market, ".agents", "plugins", "marketplace.json");
                Directory.CreateDirectory(Path.GetDirectoryName(manifest));
                File.WriteAllText(manifest, new JavaScriptSerializer().Serialize(new
                {
                    name = "auth-test",
                    plugins = new[] { new { name = "auth-probe", source = new { source = "local", path = "./plugins/auth-probe" },
                        policy = new { installation = "AVAILABLE", authentication = "ON_INSTALL" } } }
                }), new UTF8Encoding(false));
                await runtime.AddMarketplaceAsync(market);
                var listed = (await runtime.ListPluginsAsync()).Single(p => p.Name == "auth-probe");
                await runtime.InstallPluginAsync(listed.MarketplacePath, "auth-probe");
                var installed = (await runtime.GetInstalledPluginsAsync()).Plugins.Single(p => p.Name == "auth-probe");
                Assert.That(installed.Installed && installed.Enabled, Is.True);
                await switchProvider("b");
                await runtime.ReloadMcpServerConfigAsync();
                Assert.That(File.ReadAllText(Path.Combine(home, "config.toml")), Does.Contain("auth-probe@auth-test").And.Contain("auth-tool-probe"));
                Assert.That((await runtime.ListSkillsAsync(new[] { workspace }, true)).SelectMany(s => s.Skills).Single(s => s.Name == model.Name).Enabled, Is.False);

                // Plugin and skill settings are global Codex settings, not provider fields.
                await switchProvider("other");
                Assert.That(File.ReadAllText(Path.Combine(home, "config.toml")), Does.Contain("auth-probe@auth-test").And.Contain("auth-tool-probe"));
                Assert.That(File.Exists(Path.Combine(skillDirectory, "SKILL.md")), Is.True);
                await switchProvider("a");
                await runtime.ReloadMcpServerConfigAsync();
                Assert.That((await runtime.GetInstalledPluginsAsync()).Plugins.Single(p => p.Name == "auth-probe").Enabled, Is.True);
                Assert.That(await runtime.WriteSkillEnabledAsync(skill.Path, true), Is.True);
                files.RefreshObservations((await runtime.ListSkillsAsync(new[] { workspace }, true)).SelectMany(s => s.Skills));
                var edited = await files.LoadForEditAsync(skill);
                edited.Body = "Updated isolated test content.";
                await files.SaveAsync(edited, false);
                Assert.That((await runtime.ListSkillsAsync(new[] { workspace }, true)).SelectMany(s => s.Skills).Single(s => s.Name == model.Name).Enabled, Is.True, "Skill must be enabled before switching.");
                await switchProvider("b");
                Assert.That(File.ReadAllText(Path.Combine(skillDirectory, "SKILL.md")), Does.Contain(edited.Body));
                await runtime.ReloadMcpServerConfigAsync();
                Assert.That((await runtime.ListSkillsAsync(new[] { workspace }, true)).SelectMany(s => s.Skills).Single(s => s.Name == model.Name).Enabled, Is.True);

                await runtime.UninstallPluginAsync(installed.PluginId);
                await files.DeleteAsync(skill);
                await switchProvider("a");
                await runtime.ReloadMcpServerConfigAsync();
                Assert.That(Directory.Exists(skillDirectory), Is.False);
                Assert.That((await runtime.GetInstalledPluginsAsync()).Plugins.Any(p => p.Name == "auth-probe" && p.Installed), Is.False);
                Assert.That((await runtime.ListSkillsAsync(new[] { workspace }, true)).SelectMany(s => s.Skills).Any(s => s.Name == model.Name), Is.False);
            }
            finally
            {
                runtime?.Dispose();
                Environment.SetEnvironmentVariable("CODEX_HOME", oldHome);
                if (started) await Call(host, "backend/shutdown");
            }
        }
    }
}
