using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ComputerUseConfigurationPlannerTests
    {
        private const string Pipe = @"\\.\pipe\mycodex-cua-test";
        private const string Home = @"C:\users\test\.codex";

        [Test]
        public void Plan_PreservesBrowserOtherServersAndThreadOverridesWithoutMutatingInputs()
        {
            var config = Json(@"{ 'mcp_servers': { 'node_repl': { 'env': {
                'NODE_REPL_TRUSTED_CODE_PATHS': 'C:/plugins',
                'NODE_REPL_UNTRUSTED_ENV_ALLOWLIST': 'BROWSER_SURFACE,OTHER_SURFACE',
                'NODE_REPL_INSTRUCTIONS_USE_CASE_CHROME': 'existing instructions', 'BROWSER_PORT': '123'
                }, 'tool_timeout_sec': 120 }, 'other': { 'command': 'other.exe' } } }");
            Child(Child(Child(config, "mcp_servers"), "node_repl"), "env")["NODE_REPL_TRUSTED_SERVICES"] = "{\"browser\":\"C:/plugins/browser.mjs\"}";
            var overrides = Json(@"{ 'projects': { 'E:/repo': { 'trust_level': 'trusted' } },
                'model_reasoning_effort': 'high', 'mcp_servers': { 'separate': { 'enabled': false } } }");
            var before = Serialize(config);
            var overrideBefore = Serialize(overrides);
            var plan = Plan(config, null, overrides);
            Assert.That(plan.IsAvailable, Is.True, plan.DetailCode);
            var node = Node(plan);
            var env = Child(node, "env");
            var services = Json((string)env["NODE_REPL_TRUSTED_SERVICES"]);
            Assert.That(services["browser"], Is.EqualTo("C:/plugins/browser.mjs"));
            Assert.That(services["sky"], Is.EqualTo("@oai/sky/service"));
            Assert.That(env["NODE_REPL_TRUSTED_CODE_PATHS"], Is.EqualTo(@"C:/plugins;C:\cua\bin\node_modules"));
            Assert.That(env["NODE_REPL_UNTRUSTED_ENV_ALLOWLIST"], Is.EqualTo("BROWSER_SURFACE,OTHER_SURFACE,CODEX_WINDOWS_REGISTERED_CORE"));
            Assert.That(env["BROWSER_PORT"], Is.EqualTo("123"));
            Assert.That(node["tool_timeout_sec"], Is.EqualTo(120));
            Assert.That(env["CODEX_CLI_PATH"], Is.EqualTo(@"D:\selected\codex.exe"));
            Assert.That(env["CODEX_HOME"], Is.EqualTo(Home));
            Assert.That(env["SKY_CUA_NATIVE_PIPE_DIRECTORY"], Is.EqualTo(Pipe));
            Assert.That(plan.ConfigOverrides.ContainsKey("projects"), Is.True);
            Assert.That(Child(plan.ConfigOverrides, "mcp_servers").ContainsKey("separate"), Is.True);
            // Other effective MCP servers are inherited, not replaced by generated overrides.
            Assert.That(Child(plan.ConfigOverrides, "mcp_servers").ContainsKey("other"), Is.False);
            Assert.That(Serialize(config), Is.EqualTo(before));
            Assert.That(Serialize(overrides), Is.EqualTo(overrideBefore));
        }

        [Test]
        public void Plan_CopiesDenyPoliciesAndRemovesOnlyAbsentOptionalNulls()
        {
            var config = Json(@"{ 'computer_use': { 'default_app_access': 'deny', 'macos': null,
                'windows': { 'aumids': { 'Blocked.App': 'deny' }, 'exes': [
                    { 'publisher_name': 'Publisher', 'product_name': 'App', 'binary_name': null, 'access': 'deny' } ] } } }");
            var plan = Plan(config);
            Assert.That(plan.IsAvailable, Is.True);
            Assert.That(plan.HelperPolicy["default_app_access"], Is.EqualTo("deny"));
            Assert.That(plan.HelperPolicy.ContainsKey("macos"), Is.False);
            Assert.That(Child(Child(plan.HelperPolicy, "windows"), "aumids")["Blocked.App"], Is.EqualTo("deny"));
            var exe = (IDictionary<string, object>)((object[])Child(plan.HelperPolicy, "windows")["exes"])[0];
            Assert.That(exe["access"], Is.EqualTo("deny"));
            Assert.That(exe.ContainsKey("binary_name"), Is.False);
            Assert.That(Child(config, "computer_use").ContainsKey("macos"), Is.True);
        }

        [TestCase("{'allowBrowserAndComputerUse':false}", "PolicyDenied")]
        [TestCase("{'computerUse':{'allowPersistentApproval':false}}", "PolicyUnsupported")]
        [TestCase("{'computerUse':{'defaultAppAccess':'deny'}}", "PolicyUnsupported")]
        [TestCase("{'futureComputerPolicy':{'enabled':true}}", "PolicyUnsupported")]
        [TestCase("{'featureRequirements':{'computer_use':false}}", "PolicyDenied")]
        [TestCase("{'network':{'allowedDomains':['example.com']}}", "PolicyUnsupported")]
        public void Plan_RefusesManagedPoliciesItCannotPropagate(string requirements, string expected)
        {
            var plan = Plan(Json("{}"), Json(requirements));
            Assert.That(plan.IsAvailable, Is.False);
            Assert.That(plan.DetailCode, Is.EqualTo(expected));
            Assert.That(plan.ConfigOverrides, Is.Empty);
        }

        [Test]
        public void Plan_AcceptsNullOptionalRequirementsAndUnrelatedModelDefaults()
        {
            Assert.That(Plan(Json("{}"), Json(@"{'computerUse':{'windows':null,'defaultAppAccess':null},
                'allowBrowserAndComputerUse':null,'models':{'newThread':{'model':'example'}}}")).IsAvailable, Is.True);
        }

        [TestCase("{'command':'C:/custom/node_repl.exe'}")]
        [TestCase("{'args':['--disable-sandbox']}")]
        [TestCase("{'url':'https://example.com/mcp'}")]
        [TestCase("{'env':{'NODE_REPL_TRUSTED_SERVICES':'invalid'}}")]
        [TestCase("{'env':{'NODE_REPL_NODE_MODULE_DIRS':'C:/unknown/modules'}}")]
        [TestCase(@"{'env':{'SKY_CUA_NATIVE_PIPE_DIRECTORY':'\\\\.\\pipe\\thirdparty'}}")]
        [TestCase("{'env':{'CODEX_HOME':'C:/another/home'}}")]
        [TestCase("{'env':{'codex_home':'C:/another/home'}}")]
        [TestCase("{'env':{'SKY_CUA_NATIVE_PIPE':'0'}}")]
        public void Plan_RejectsConflictingMcpWithoutOverwritingUserConfiguration(string node)
        {
            var config = Json("{'mcp_servers':{'node_repl':" + node + "}}");
            var before = Serialize(config);
            var plan = Plan(config);
            Assert.That(plan.IsAvailable, Is.False);
            Assert.That(plan.DetailCode, Is.EqualTo("NodeReplConflict"));
            Assert.That(Serialize(config), Is.EqualTo(before));
        }

        [Test]
        public void Plan_ReplacesOnlyVerifiedStaleOfficialRuntimePaths()
        {
            var components = Components();
            components.StaleOfficialRuntimeRoots = new[] { @"C:\old-cua" };
            var config = Json(@"{'mcp_servers':{'node_repl':{'command':'C:/old-cua/bin/node_repl.exe','env':{
                'NODE_REPL_NODE_PATH':'C:/old-cua/bin/node.exe',
                'NODE_REPL_NODE_MODULE_DIRS':'C:/old-cua/bin/node_modules',
                'NODE_REPL_TRUSTED_CODE_PATHS':'C:/plugins;C:/old-cua/bin/node_modules',
                'SKY_CUA_NATIVE_PIPE_DIRECTORY':'\\\\.\\pipe\\codex-computer-use-old'
            }}}}");
            var plan = ComputerUseConfigurationPlanner.Plan(config, null, null, components, Pipe, Home);
            Assert.That(plan.IsAvailable, Is.True, plan.DetailCode);
            Assert.That(Node(plan)["command"], Is.EqualTo(components.NodeReplPath));
            Assert.That(Child(Node(plan), "env")["NODE_REPL_TRUSTED_CODE_PATHS"], Is.EqualTo(@"C:/plugins;C:\cua\bin\node_modules"));
            Assert.That(Plan(config).DetailCode, Is.EqualTo("NodeReplConflict"));
        }

        [Test]
        public void Plan_RejectsDifferentSkyService()
        {
            var config = Json("{'mcp_servers':{'node_repl':{'env':{}}}}");
            Child(Child(Child(config, "mcp_servers"), "node_repl"), "env")["NODE_REPL_TRUSTED_SERVICES"] = "{\"sky\":\"C:/custom/sky.mjs\"}";
            Assert.That(Plan(config).DetailCode, Is.EqualTo("NodeReplConflict"));
        }

        [Test]
        public void Plan_DoesNotReenableDisabledToolsOrFeatures()
        {
            Assert.That(Plan(Json("{'mcp_servers':{'node_repl':{'enabled':false}}}")).DetailCode, Is.EqualTo("NodeReplDisabled"));
            Assert.That(Plan(Json("{'features':{'computer_use':false}}")).DetailCode, Is.EqualTo("PolicyDenied"));
            Assert.That(Plan(Json("{'plugins':{'computer-use@openai-bundled':{'enabled':false}}}")).DetailCode, Is.EqualTo("PluginDisabled"));
            Assert.That(Plan(Json("{}"), null, Json("{'plugins':{'computer-use@mycodex-computer-use':{'enabled':false}}}")).DetailCode,
                Is.EqualTo("PluginDisabled"));
        }

        [Test]
        public void Plan_SelectsOwnPluginAndMigratesOnlyOfficialCacheLayoutWithoutReadingIt()
        {
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI", "Codex", "runtimes", "cua_node", "1234567890abcdef", "bin");
            var node = new Dictionary<string, object>
            {
                { "command", Path.Combine(cache, "node_repl.exe") },
                { "env", new Dictionary<string, object>
                    { { "NODE_REPL_NODE_PATH", Path.Combine(cache, "node.exe") },
                      { "NODE_REPL_NODE_MODULE_DIRS", Path.Combine(cache, "node_modules") } } }
            };
            var config = new Dictionary<string, object>
            {
                { "mcp_servers", new Dictionary<string, object> { { "node_repl", node } } }
            };
            var before = Serialize(config);
            var plan = Plan(config);
            Assert.That(plan.IsAvailable, Is.True, plan.DetailCode);
            Assert.That(Node(plan)["command"], Is.EqualTo(Components().NodeReplPath));
            var plugins = Child(plan.ConfigOverrides, "plugins");
            Assert.That(Child(plugins, ComputerUseBundle.PluginId)["enabled"], Is.True);
            Assert.That(Child(plugins, ComputerUseBundle.LegacyPluginId)["enabled"], Is.False);
            Assert.That(Serialize(config), Is.EqualTo(before));
            node["command"] = Path.Combine(cache, "unreviewed", "node_repl.exe");
            Assert.That(Plan(config).DetailCode, Is.EqualTo("NodeReplConflict"));
        }

        [TestCase("OpenAI.Codex_26.1103.7773.0_x64__2p2nqsd0c76g0")]
        [TestCase("OpenAI.Codex_27.1.2.3_arm64__2p2nqsd0c76g0")]
        [TestCase("openai.codex_26.1103.7773.0_X64__2P2NQSD0C76G0")]
        public void Plan_RebindsOfficialWindowsAppsReferencesWithoutChangingSharedConfig(string package)
        {
            var bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WindowsApps", package, "app", "resources", "cua_node", "bin");
            var env = new Dictionary<string, object>
            {
                { "NODE_REPL_NODE_PATH", Path.Combine(bin, "node.exe") },
                { "NODE_REPL_NODE_MODULE_DIRS", Path.Combine(bin, "node_modules") },
                { "NODE_REPL_TRUSTED_CODE_PATHS", Home + ";" + Path.Combine(bin, "node_modules") },
                { "NODE_REPL_TRUSTED_SERVICES", "{\"sky\":\"@oai/sky/service\"}" },
                { "SKY_CUA_NATIVE_PIPE", "1" }, { "SKY_CUA_NATIVE_PIPE_DIRECTORY", @"\\.\pipe\codex-computer-use-old" }
            };
            var config = new Dictionary<string, object>
            {
                { "mcp_servers", new Dictionary<string, object> { { "node_repl", new Dictionary<string, object>
                    { { "command", Path.Combine(bin, "node_repl.exe") }, { "env", env } } } } }
            };
            var before = Serialize(config);
            var plan = Plan(config);
            Assert.That(plan.IsAvailable, Is.True, plan.DetailCode);
            Assert.That(Node(plan)["command"], Is.EqualTo(Components().NodeReplPath));
            var rewritten = Child(Node(plan), "env");
            Assert.That(rewritten["NODE_REPL_NODE_PATH"], Is.EqualTo(Components().NodePath));
            Assert.That(rewritten["NODE_REPL_NODE_MODULE_DIRS"], Is.EqualTo(Components().NodeModulesPath));
            Assert.That(rewritten["NODE_REPL_TRUSTED_CODE_PATHS"], Is.EqualTo(Home + ";" + Components().NodeModulesPath));
            Assert.That(rewritten["SKY_CUA_NATIVE_PIPE_DIRECTORY"], Is.EqualTo(Pipe));
            Assert.That(Serialize(config), Is.EqualTo(before));
        }

        [TestCase("WindowsApps-old/OpenAI.Codex_26.1103.7773.0_x64__2p2nqsd0c76g0")]
        [TestCase("WindowsApps/Other.Codex_26.1103.7773.0_x64__2p2nqsd0c76g0")]
        [TestCase("WindowsApps/OpenAI.Codex_26.1103.7773.0_x64__otherpublisher")]
        [TestCase("WindowsApps/OpenAI.Codex_26.1103.7773_x64__2p2nqsd0c76g0")]
        [TestCase("WindowsApps/OpenAI.Codex_26.1103.7773.0_x64__2p2nqsd0c76g0/extra")]
        [TestCase("WindowsApps/OpenAI.Codex_26.1103.7773.0_x86__2p2nqsd0c76g0")]
        public void Plan_DoesNotTreatWindowsAppsLookalikesAsOfficialReferences(string relative)
        {
            var config = Json("{'mcp_servers':{'node_repl':{}}}");
            Child(Child(config, "mcp_servers"), "node_repl")["command"] = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), relative,
                "app", "resources", "cua_node", "bin", "node_repl.exe");
            Assert.That(Plan(config).DetailCode, Is.EqualTo("NodeReplConflict"));
        }

        [TestCase("OpenAI.Codex_26.1103.7773.0_x64__2p2nqsd0c76g0", "1", true)]
        [TestCase("OpenAI.Codex_26.1103.7773.0_x64__2p2nqsd0c76g0", "0", false)]
        [TestCase("Other.Codex_26.1103.7773.0_x64__2p2nqsd0c76g0", "1", false)]
        public void FallbackSuppressesOnlyRecognizedOfficialDesktopServer(string package, string nativePipe, bool suppressed)
        {
            var node = new Dictionary<string, object>
            {
                { "command", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "WindowsApps", package, "app", "resources", "cua_node", "bin", "node_repl.exe") },
                { "env", new Dictionary<string, object> { { "SKY_CUA_NATIVE_PIPE", nativePipe } } }
            };
            var config = new Dictionary<string, object>
                { { "mcp_servers", new Dictionary<string, object> { { "node_repl", node } } } };
            var overrides = Json("{'model':'keep','mcp_servers':{'other':{'command':'other.exe'}}}");
            var before = Serialize(config);
            var original = Serialize(overrides);
            var fallback = ComputerUseConfigurationPlanner.SuppressOfficialDesktop(config, overrides);
            Assert.That(ReferenceEquals(fallback, overrides), Is.EqualTo(!suppressed));
            Assert.That(fallback["model"], Is.EqualTo("keep"));
            Assert.That(Child(Child(fallback, "mcp_servers"), "other")["command"], Is.EqualTo("other.exe"));
            if (suppressed)
                Assert.That(Child(Child(fallback, "mcp_servers"), "node_repl")["enabled"], Is.False);
            Assert.That(Serialize(config), Is.EqualTo(before));
            Assert.That(Serialize(overrides), Is.EqualTo(original));
            Child(overrides, "mcp_servers")["node_repl"] = Json("{'command':'C:/custom/node_repl.exe'}");
            Assert.That(ComputerUseConfigurationPlanner.SuppressOfficialDesktop(config, overrides), Is.SameAs(overrides));
        }

        [Test]
        public void Plan_DropsAbsentMcpOptionalFieldsBeforeSendingThreadOverrides()
        {
            var config = Json(@"{'mcp_servers':{'node_repl':{'command':'C:/cua/bin/node_repl.exe',
                'args':null,'url':null,'env':null,'tool_timeout_sec':null,'startup_timeout_sec':null,
                'enabled_tools':null,'disabled_tools':null,'bearer_token_env_var':null,'http_headers':null}}}");
            var plan = Plan(config);
            Assert.That(plan.IsAvailable, Is.True, plan.DetailCode);
            var node = Node(plan);
            Assert.That(node.ContainsKey("tool_timeout_sec"), Is.False);
            Assert.That(node.ContainsKey("startup_timeout_sec"), Is.False);
            Assert.That(node.ContainsKey("url"), Is.False);
            Assert.That(node.ContainsKey("args"), Is.False);
            Assert.That(Child(Child(config, "mcp_servers"), "node_repl").ContainsKey("tool_timeout_sec"), Is.True);
        }

        [TestCase("{'computer_use':{'default_app_access':'deny'}}", true)]
        [TestCase("{'mcp_servers':{'node_repl':{'enabled':false}}}", true)]
        [TestCase("{'plugins':{'computer-use@openai-bundled':{'enabled':false}}}", true)]
        [TestCase("{'features':{'computer_use':false}}", true)]
        [TestCase("{'model':'other-model'}", false)]
        public void DisabledLayers_RejectsTrustOverridesThatMayChangeDesktopPolicy(string layerConfig, bool expected)
        {
            var response = Json("{'layers':[{'disabledReason':'Project is not trusted','config':" + layerConfig + "}]}");
            var overrides = Json("{'projects':{'C:/repo':{'trust_level':'trusted'}}}");
            Assert.That(ComputerUseConfigurationPlanner.HasUnsafeDisabledLayers(response, overrides), Is.EqualTo(expected));
            Assert.That(ComputerUseConfigurationPlanner.HasUnsafeDisabledLayers(response, null), Is.False);
            ((IDictionary<string, object>)((object[])response["layers"])[0]).Remove("disabledReason");
            Assert.That(ComputerUseConfigurationPlanner.HasUnsafeDisabledLayers(response, overrides), Is.False);
        }

        [Test]
        public void DisabledLayers_FailsClosedWhenRequestedLayerSnapshotIsMissingOrMalformed()
        {
            var overrides = Json("{'projects':{'C:/repo':{'trust_level':'trusted'}}}");
            Assert.That(ComputerUseConfigurationPlanner.HasUnsafeDisabledLayers(Json("{}"), overrides), Is.True);
            Assert.That(ComputerUseConfigurationPlanner.HasUnsafeDisabledLayers(Json("{'layers':['invalid']}"), overrides), Is.True);
            Assert.That(ComputerUseConfigurationPlanner.HasUnsafeDisabledLayers(Json("{'layers':[]}"), overrides), Is.False);
        }

        private static ComputerUseConfigurationPlan Plan(IDictionary<string, object> config,
            IDictionary<string, object> requirements = null, IDictionary<string, object> overrides = null)
        {
            return ComputerUseConfigurationPlanner.Plan(config, requirements, overrides, Components(), Pipe, Home);
        }

        private static ComputerUseComponents Components()
        {
            return new ComputerUseComponents
            {
                RuntimeRoot = @"C:\cua", NodePath = @"C:\cua\bin\node.exe",
                NodeReplPath = @"C:\cua\bin\node_repl.exe", NodeModulesPath = @"C:\cua\bin\node_modules",
                SelectedCodexPath = @"D:\selected\codex.exe"
            };
        }

        private static IDictionary<string, object> Node(ComputerUseConfigurationPlan plan) { return Child(Child(plan.ConfigOverrides, "mcp_servers"), "node_repl"); }
        private static IDictionary<string, object> Child(IDictionary<string, object> map, string key) { return (IDictionary<string, object>)map[key]; }
        private static IDictionary<string, object> Json(string text) { return (IDictionary<string, object>)new JavaScriptSerializer().DeserializeObject(text.Replace('\'', '"')); }
        private static string Serialize(object value) { return new JavaScriptSerializer().Serialize(value); }
    }
}
