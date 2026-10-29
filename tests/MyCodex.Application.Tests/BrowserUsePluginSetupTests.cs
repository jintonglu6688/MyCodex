using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.BrowserUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class BrowserUsePluginSetupTests
    {
        [Test]
        public void OwnedPluginRoutesStopInterruptAndSubagentCleanupToTheBrowserRuntime()
        {
            using (var resource = typeof(WindowsBrowserExtensionSetupService).Assembly.GetManifestResourceStream("MyCodex.BrowserUse.PluginManifest"))
            using (var reader = new StreamReader(resource))
            {
                var plugin = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                var hooks = (IDictionary<string, object>)((IDictionary<string, object>)plugin["hooks"])["hooks"];
                foreach (var name in new[] { "Stop", "Interrupt", "SubagentStop" })
                {
                    var group = (IDictionary<string, object>)((IList)hooks[name])[0];
                    var hook = (IDictionary<string, object>)((IList)group["hooks"])[0];
                    Assert.That(hook["server"], Is.EqualTo("cua_repl"));
                    Assert.That(hook["tool"], Is.EqualTo("turn_ended"));
                    var input = (IDictionary<string, object>)hook["input"];
                    Assert.That(input["session_id"], Is.EqualTo(name == "SubagentStop" ? "${agent_id}" : "${session_id}"));
                    Assert.That(input["turn_id"], Is.EqualTo("${turn_id}"));
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PluginPackageRejectsUnexpectedMcpOrTamperedSkillFiles(bool tampered)
        {
            var installation = Path.Combine(TestContext.CurrentContext.WorkDirectory, "browser-plugin-" + Guid.NewGuid().ToString("N"));
            var resources = new Dictionary<string, string>
            {
                { ".agents/plugins/marketplace.json", "MyCodex.BrowserUse.PluginMarketplace" },
                { "plugins/browser-use/.codex-plugin/plugin.json", "MyCodex.BrowserUse.PluginManifest" },
                { "plugins/browser-use/skills/browser-use/SKILL.md", "MyCodex.BrowserUse.PluginSkill" }
            };
            foreach (var item in resources)
            {
                var path = Path.Combine(installation, "browser-use", item.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var source = typeof(WindowsBrowserExtensionSetupService).Assembly.GetManifestResourceStream(item.Value))
                using (var destination = File.Create(path)) source.CopyTo(destination);
            }
            Assert.That(BrowserUsePluginSetup.VerifyPackage(installation), Is.EqualTo(Path.Combine(installation, "browser-use")));
            var changed = Path.Combine(installation, "browser-use", "plugins", "browser-use",
                tampered ? "skills/browser-use/SKILL.md" : ".mcp.json");
            File.WriteAllText(changed, "untrusted fixture");
            Assert.Throws<InvalidDataException>(() => BrowserUsePluginSetup.VerifyPackage(installation));
        }

        [TestCase(false, false, false, BrowserAutomationToolState.Missing)]
        [TestCase(true, false, false, BrowserAutomationToolState.Disabled)]
        [TestCase(true, true, false, BrowserAutomationToolState.Configured)]
        [TestCase(false, false, true, BrowserAutomationToolState.Ready)]
        public void SetupStateDoesNotConfuseInstalledNativeBridgeWithConversationTools(bool installed, bool enabled, bool tools, BrowserAutomationToolState expected)
        {
            var plugins = new List<AppServerPluginSummary>();
            if (installed) plugins.Add(new AppServerPluginSummary { PluginId = BrowserUsePluginSetup.PluginId, Installed = true, Enabled = enabled });
            var servers = new List<AppServerMcpServerStatus>();
            if (tools) servers.Add(new AppServerMcpServerStatus { ServerName = "cua_repl", ToolCount = 3 });
            Assert.That(BrowserUsePluginSetup.State(plugins, servers), Is.EqualTo(expected));
        }

        [Test]
        public void FailedOrDisabledMcpServerDoesNotReportReady()
        {
            var plugins = new[] { new AppServerPluginSummary { PluginId = "chrome@openai-bundled", Installed = true, Enabled = true } };
            var server = new AppServerMcpServerStatus { ServerName = "cua_repl", ToolCount = 3, ErrorText = "fixture failure" };
            Assert.That(BrowserUsePluginSetup.State(plugins, new[] { server }), Is.EqualTo(BrowserAutomationToolState.Configured));
            server.ErrorText = null;
            server.Enabled = false;
            Assert.That(BrowserUsePluginSetup.State(plugins, new[] { server }), Is.EqualTo(BrowserAutomationToolState.Configured));
        }

        [Test]
        public void InstalledOwnedPluginIsRecognizedByTheActualThreadConfigurationPlanner()
        {
            var config = new Dictionary<string, object>();
            Assert.That(BrowserUseConfigurationPlanner.IsRequested(config, null), Is.False);
            config["plugins"] = new Dictionary<string, object>
            {
                { BrowserUsePluginSetup.PluginId, new Dictionary<string, object> { { "enabled", true } } }
            };
            Assert.That(BrowserUseConfigurationPlanner.IsRequested(config, null), Is.True);
            Assert.That(BrowserUsePluginSetup.EnableKey, Is.EqualTo("plugins.\"browser-use@mycodex-browser-use\".enabled"));
        }
    }
}
