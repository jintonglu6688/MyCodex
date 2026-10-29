using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.BrowserUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class BrowserUseConfigurationPlannerTests
    {
        [Test]
        public void MasterSwitchSuppressesBrowserOnlyAndDoesNotMutateOverrides()
        {
            var original = Json("{'mcp_servers':{'cua_repl':{'enabled':true,'command':'custom'},'other':{'enabled':true}},'model':'keep'}");
            var disabled = BrowserUseConfigurationPlanner.SuppressBrowser(original);
            var servers = (IDictionary<string, object>)disabled["mcp_servers"];
            Assert.That(((IDictionary<string, object>)servers["cua_repl"])["enabled"], Is.False);
            Assert.That(((IDictionary<string, object>)servers["other"])["enabled"], Is.True);
            Assert.That(disabled["model"], Is.EqualTo("keep"));
            Assert.That(BrowserUseConfigurationPlanner.IsRequested(original, null), Is.True);
        }
        private const string Home = @"C:\users\test\.codex";
        private const string Executor = "6a4cb7f4-1625-47d1-b39a-603f026e1d28";
        private const string Pipe = @"\\.\pipe\mycodex-browser-access-6a4cb7f4-1625-47d1-b39a-603f026e1d28";
        private const string Identity = "MCowBQYDK2VwAyEA6ee2t07dHZ8QEG6ftJt6n71hIlS8Kz3q0F0QWxB7Aao=";

        [Test]
        public void Plan_ReplacesForeignCuaBindingWithOwnedHostWithoutChangingNativeOrGlobalConfiguration()
        {
            var config = Json(@"{'mcp_servers':{'cua_repl':{'args':['old.js'],
                'env_vars':['BROWSER_USE_ACCESS_PIPE_PATH','CODEX_WINDOWS_REGISTERED_CORE','OTHER_SETTING'],
                'env':{'BROWSER_USE_ACCESS_EXECUTOR_ID':'foreign','SKY_CUA_NATIVE_PIPE':'1',
                    'SKY_CUA_NATIVE_PIPE_DIRECTORY':'foreign-pipe','CUSTOM_SETTING':'keep'},'tool_timeout_sec':180},
                'node_repl':{'command':'C:/native/node_repl.exe'}},'browser_use':{'allow_history_access':false}}");
            Child(Child(config, "mcp_servers"), "cua_repl")["command"] = OfficialNode();
            var overrides = Json(@"{'mcp_servers':{'node_repl':{'env':{'SKY_CUA_NATIVE_PIPE_DIRECTORY':'owned-native'}}},
                'model_reasoning_effort':'high'}");
            var configBefore = Serialize(config);
            var overridesBefore = Serialize(overrides);
            var plan = Plan(config, null, overrides);
            Assert.That(plan.IsAvailable, Is.True, plan.DetailCode);
            var server = Server(plan);
            var env = Child(server, "env");
            Assert.That(server["command"], Is.EqualTo(Components().NodePath));
            Assert.That((string[])server["args"], Is.EqualTo(new[] { Components().CuaReplPath }));
            Assert.That(env["BROWSER_USE_ACCESS_EXECUTOR_ID"], Is.EqualTo(Executor));
            Assert.That(env["BROWSER_USE_ACCESS_PIPE_PATH"], Is.EqualTo(Pipe));
            Assert.That(env["BROWSER_USE_ACCESS_VERIFYING_IDENTITY"], Is.EqualTo(Identity));
            Assert.That(env["CODEX_HOME"], Is.EqualTo(Home));
            Assert.That(env["CODEX_CLI_PATH"], Is.EqualTo(Components().SelectedCodexPath));
            Assert.That(env["CUA_REPL_ENABLED_SURFACES"], Is.EqualTo("browser"));
            Assert.That(env["BROWSER_USE_AVAILABLE_BACKENDS"], Is.EqualTo("chrome"));
            Assert.That(env["CUSTOM_SETTING"], Is.EqualTo("keep"));
            Assert.That(env.ContainsKey("SKY_CUA_NATIVE_PIPE_DIRECTORY"), Is.False);
            Assert.That(env.ContainsKey("SKY_CUA_NATIVE_PIPE"), Is.False);
            var services = Json((string)env["NODE_REPL_TRUSTED_SERVICES"]);
            Assert.That(services["browser"], Is.EqualTo(Components().BrowserServicePath));
            Assert.That(services.ContainsKey("sky"), Is.False);
            Assert.That(((string[])server["env_vars"]).Contains("BROWSER_USE_ACCESS_PIPE_PATH"), Is.False);
            Assert.That(server["tool_timeout_sec"], Is.EqualTo(180));
            Assert.That(Serialize(Child(Child(plan.ConfigOverrides, "mcp_servers"), "node_repl")),
                Is.EqualTo(Serialize(Child(Child(overrides, "mcp_servers"), "node_repl"))));
            Assert.That(plan.ConfigOverrides["model_reasoning_effort"], Is.EqualTo("high"));
            Assert.That(Serialize(config), Is.EqualTo(configBefore));
            Assert.That(Serialize(overrides), Is.EqualTo(overridesBefore));
        }

        [TestCase("{'mcp_servers':{'cua_repl':{'enabled':false}}}")]
        [TestCase("{'plugins':{'chrome@openai-bundled':{'enabled':false}}}")]
        [TestCase("{'plugins':{'unified-computer-use@openai-bundled':{'enabled':false}}}")]
        [TestCase("{'features':{'browser_use':false}}")]
        [TestCase("{'features':{'browser_use_external':false}}")]
        public void Plan_DoesNotEnableAnExplicitlyDisabledBrowser(string json)
        {
            Assert.That(Plan(Json(json)).DetailCode, Is.EqualTo("BrowserDisabled"));
            Assert.That(Plan(Json("{}"), null, Json(json)).DetailCode, Is.EqualTo("BrowserDisabled"));
        }

        [Test]
        public void Plan_ThreadPreparationDoesNotUndoTheUsersGlobalDisable()
        {
            Assert.That(Plan(Json("{'features':{'browser_use':false}}"), null,
                Json("{'features':{'browser_use':true}} ")).DetailCode, Is.EqualTo("BrowserDisabled"));
        }

        [TestCase("{'mcp_servers':{'cua_repl':{'enabled':true}}}", true)]
        [TestCase("{'plugins':{'chrome@openai-bundled':{'enabled':true}}}", true)]
        [TestCase("{'plugins':{'unified-computer-use@openai-bundled':{'enabled':true}}}", true)]
        [TestCase("{'plugins':{'browser-use@mycodex-browser-use':{'enabled':true}}}", true)]
        [TestCase("{'plugins':{'browser@openai-bundled':{'enabled':true}}}", false)]
        [TestCase("{'features':{'browser_use':true}}", false)]
        [TestCase("{}", false)]
        public void Requested_RequiresAnExplicitExternalBrowserEnable(string config, bool expected)
        {
            Assert.That(BrowserUseConfigurationPlanner.IsRequested(Json(config), null), Is.EqualTo(expected));
            Assert.That(BrowserUseConfigurationPlanner.IsRequested(null, Json(config)), Is.EqualTo(expected));
        }

        [Test]
        public void Requested_RespectsDisablesAndLeavesInputConfigurationUnchanged()
        {
            var enabled = Json("{'mcp_servers':{'cua_repl':{'enabled':true}}}");
            var disabled = Json("{'mcp_servers':{'cua_repl':{'enabled':false}}}");
            var before = Serialize(enabled);
            Assert.That(BrowserUseConfigurationPlanner.IsRequested(enabled, disabled), Is.False);
            Assert.That(BrowserUseConfigurationPlanner.IsRequested(disabled, enabled), Is.False);
            Assert.That(BrowserUseConfigurationPlanner.IsRequested(Json("{'features':{'browser_use':false}}"), enabled), Is.False);
            Assert.That(BrowserUseConfigurationPlanner.IsRequested(Json("{'plugins':'malformed'}"), enabled), Is.False);
            Assert.That(Serialize(enabled), Is.EqualTo(before));
        }

        [Test]
        public void ExternalBrowserDoesNotDependOnTheInAppBrowserPlugin()
        {
            var config = Json("{'plugins':{'browser@openai-bundled':{'enabled':false},'chrome@openai-bundled':{'enabled':true}}}");
            Assert.That(BrowserUseConfigurationPlanner.IsRequested(config, null), Is.True);
            Assert.That(Plan(config).IsAvailable, Is.True);
        }

        [TestCase("{'allowBrowserAndComputerUse':false}")]
        [TestCase("{'featureRequirements':{'browser_use':false}}")]
        [TestCase("{'featureRequirements':{'browser_use_external':false}}")]
        public void Plan_ManagedFeatureDenialCannotBeOverridden(string json)
        {
            Assert.That(Plan(Json("{}"), Json(json)).DetailCode, Is.EqualTo("PolicyDenied"));
        }

        [Test]
        public void Plan_LeavesOriginRequirementsInTheirOwnSchemaForTheOfficialService()
        {
            var config = Json(@"{'browser_use':{'allow_history_access':false,'origins':{'https://example.test':{'access':'deny'}}}}");
            var requirements = Json(@"{'browserUse':{'allowHistoryAccess':false,'defaultOriginPolicy':{
                'access':'deny','accessApprovalLifetime':'turn','persistentApproval':false},
                'extension':{'requestHeaders':[]}}}");
            var configBefore = Serialize(config);
            var requirementsBefore = Serialize(requirements);
            var plan = Plan(config, requirements);
            Assert.That(plan.IsAvailable, Is.True, plan.DetailCode);
            Assert.That(plan.ConfigOverrides.ContainsKey("browser_use"), Is.False);
            Assert.That(Serialize(config), Is.EqualTo(configBefore));
            Assert.That(Serialize(requirements), Is.EqualTo(requirementsBefore));
        }

        [TestCase("{'browserUse':{'futurePermission':true}}")]
        [TestCase("{'browserUse':{'defaultOriginPolicy':{'futurePermission':true}}}")]
        [TestCase("{'browserUse':{'origins':{'https://example.test':{'futurePermission':true}}}}")]
        [TestCase("{'browserUse':{'extension':{'futurePermission':true}}}")]
        [TestCase("{'browserUse':'invalid'}")]
        [TestCase("{'browserUse':{'futurePermission':null}}")]
        [TestCase("{'featureRequirements':{'browser_use':'false'}}")]
        public void Plan_RejectsUnknownOrMalformedManagedBrowserPolicy(string json)
        {
            Assert.That(Plan(Json("{}"), Json(json)).DetailCode, Is.EqualTo("PolicyUnsupported"));
        }

        [Test]
        public void Plan_RejectsForeignPipesAndEscapedRuntimeComponents()
        {
            var binding = Binding();
            binding.PipePath = @"\\.\pipe\codex-browser-use-foreign";
            Assert.That(Plan(Json("{}"), access: binding).DetailCode, Is.EqualTo("ConfigurationUnavailable"));
            var components = Components();
            components.CuaReplPath = @"C:\unverified\cua-repl.mjs";
            Assert.That(Plan(Json("{}"), components: components).IsAvailable, Is.False);
            components = Components();
            components.BrowserServicePath = @"C:\mycodex\browser-runtime\bin\node_modules\..\..\..\outside.mjs";
            Assert.That(Plan(Json("{}"), components: components).IsAvailable, Is.False);
            binding = Binding();
            binding.VerifyingIdentity = "invalid base64";
            Assert.That(Plan(Json("{}"), access: binding).IsAvailable, Is.False);
        }

        [Test]
        public void Plan_DropsNullOptionalFields()
        {
            var config = Json(@"{'mcp_servers':{'cua_repl':{'env':null,'url':null,
                'bearer_token_env_var':null,'http_headers':null,'tool_timeout_sec':null}}}");
            var server = Server(Plan(config));
            Assert.That(server.ContainsKey("url"), Is.False);
            Assert.That(server.ContainsKey("bearer_token_env_var"), Is.False);
            Assert.That(server.ContainsKey("http_headers"), Is.False);
            Assert.That(server.ContainsKey("tool_timeout_sec"), Is.False);
        }

        [TestCase("{'url':'https://custom.test/mcp'}")]
        [TestCase("{'command':'C:/custom/server.exe'}")]
        [TestCase("{'command':'node'}")]
        public void Plan_PreservesCustomCuaServersInsteadOfReplacingTheirRegistration(string entry)
        {
            var config = Json("{'mcp_servers':{'cua_repl':" + entry + "}}");
            var before = Serialize(config);
            var plan = Plan(config);
            Assert.That(plan.IsAvailable, Is.False);
            Assert.That(plan.DetailCode, Is.EqualTo("BrowserServerConflict"));
            Assert.That(plan.ConfigOverrides, Is.Empty);
            Assert.That(Serialize(config), Is.EqualTo(before));
        }

        [Test]
        public void Plan_AcceptsTheVerifiedOwnedNodeButRejectsNearMissesInTheOfficialCache()
        {
            var config = Json("{'mcp_servers':{'cua_repl':{}}}");
            var server = Child(Child(config, "mcp_servers"), "cua_repl");
            server["command"] = Components().NodePath;
            Assert.That(Plan(config).IsAvailable, Is.True);
            server["command"] = OfficialNode().Replace("6ef99b3d04073aa7", "not-a-runtime");
            Assert.That(Plan(config).DetailCode, Is.EqualTo("BrowserServerConflict"));
            server["command"] = OfficialNode().Replace("node.exe", "custom.exe");
            Assert.That(Plan(config).DetailCode, Is.EqualTo("BrowserServerConflict"));
        }

        private static BrowserUseConfigurationPlan Plan(IDictionary<string, object> config,
            IDictionary<string, object> requirements = null, IDictionary<string, object> overrides = null,
            BrowserUseComponents components = null, BrowserUseAccessBinding access = null)
        {
            return BrowserUseConfigurationPlanner.Plan(config, requirements, overrides,
                components ?? Components(), access ?? Binding(), Home);
        }

        private static BrowserUseComponents Components()
        {
            const string root = @"C:\mycodex\browser-runtime";
            const string modules = root + @"\bin\node_modules";
            return new BrowserUseComponents
            {
                RuntimeRoot = root, NodePath = root + @"\bin\node.exe", NodeReplPath = root + @"\bin\node_repl.exe",
                NodeModulesPath = modules, CuaReplPath = modules + @"\@oai\cua-repl\bin\cua-repl.mjs",
                BrowserServicePath = modules + @"\@oai\browser-desktop\scripts\browser-service.mjs",
                BrowserClientPath = modules + @"\@oai\browser-desktop\scripts\browser-client.mjs",
                SelectedCodexPath = @"D:\selected\codex.exe"
            };
        }

        private static BrowserUseAccessBinding Binding()
        {
            return new BrowserUseAccessBinding { ExecutorId = Executor, PipePath = Pipe, VerifyingIdentity = Identity };
        }
        private static string OfficialNode()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI", "Codex", "runtimes", "cua_node", "6ef99b3d04073aa7", "bin", "node.exe");
        }
        private static IDictionary<string, object> Server(BrowserUseConfigurationPlan plan) { return Child(Child(plan.ConfigOverrides, "mcp_servers"), "cua_repl"); }
        private static IDictionary<string, object> Child(IDictionary<string, object> map, string key) { return (IDictionary<string, object>)map[key]; }
        private static IDictionary<string, object> Json(string text) { return (IDictionary<string, object>)new JavaScriptSerializer().DeserializeObject(text.Replace('\'', '"')); }
        private static string Serialize(object value) { return new JavaScriptSerializer().Serialize(value); }
    }
}
